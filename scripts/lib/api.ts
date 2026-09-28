// Shared plumbing for the load scripts: configuration, bearer-token minting, a bounded-concurrency
// runner, and the report each script prints when it finishes.
//
// Runtime-agnostic on purpose. There is no package.json or tsconfig.json in scripts/, so nothing
// here imports a module or a node: builtin. What it does rely on is available in Node 22.6+ (where
// TypeScript type stripping is on by default), Bun and Deno: global fetch, the Web Crypto API,
// btoa, and top-level await in the callers. That is why the file is .ts with no build step rather
// than .js with a compiler.
declare const process:
  | { env: Record<string, string | undefined>; exitCode: number }
  | undefined;

/** Reads an environment variable without assuming a particular runtime's globals exist. */
export function readEnv(name: string): string | undefined {
  try {
    return (globalThis as { process?: { env?: Record<string, string | undefined> } }).process?.env?.[
      name
    ];
  } catch {
    return undefined;
  }
}

/** Sets a non-zero exit code, or throws on a runtime with no process.exitCode to set. */
export function failExit(): void {
  const proc = (globalThis as { process?: { exitCode?: number } }).process;
  if (proc) {
    proc.exitCode = 1;
    return;
  }
  throw new Error("load script finished with failures (no process.exitCode available)");
}

/** The API a script targets. Local compose by default, overridable for any other deployment. */
export function baseUrl(): string {
  return (readEnv("BASE_URL") ?? "http://localhost:8080").replace(/\/+$/, "");
}

/** A request body, and the path it goes to. */
export interface LoadTestSpec {
  /** Human-readable name used in the report. */
  name: string;
  /** Path appended to the base URL, e.g. "/api/policies". */
  path: string;
  /** Builds the JSON body for request number `index` (zero-based). */
  body: (index: number) => unknown;
  /** Request count used when TOTAL is not set. */
  defaultTotal: number;
}

/** A token and where it came from, so a report can say which mechanism authenticated the run. */
export interface Auth {
  token: string;
  source: "API_TOKEN" | "minted from JWT_SIGNING_KEY";
}

function base64Url(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function base64UrlText(text: string): string {
  return base64Url(new TextEncoder().encode(text));
}

/**
 * Mints an HS256 bearer token from the same signing key the API is configured with.
 *
 * The API validates the signature, issuer, audience, lifetime and role claim, so a token is only
 * useful if it agrees with the server's configuration on all of them. Every one of those is
 * therefore an environment variable with the value from appsettings.json as its default, and
 * JWT_SIGNING_KEY is required because it is deliberately not in the repository.
 */
async function mintToken(): Promise<string> {
  const key = readEnv("JWT_SIGNING_KEY");
  if (!key) {
    throw new Error(
      "JWT_SIGNING_KEY is not set. Supply the API's Jwt:SigningKey (at least 32 bytes) so the " +
        "token this mints is one the API will accept, or set API_TOKEN to a token you already have.",
    );
  }

  const issuedAt = Math.floor(Date.now() / 1000);
  const ttl = Number(readEnv("JWT_TTL_SECONDS") ?? "3600");
  const subject = readEnv("JWT_SUBJECT") ?? "load-script";
  const roles = (readEnv("JWT_ROLE") ?? "Admin")
    .split(",")
    .map((role) => role.trim())
    .filter((role) => role.length > 0);

  const header = { alg: "HS256", typ: "JWT" };

  // "role" and "unique_name" are the short claim names, and are the ones the API's inbound claim
  // mapping expects: it rewrites them to the full ClaimTypes URIs the authorization policies and
  // ICurrentUser read. Emitting the long URIs directly also works, but only because the mapping
  // leaves already-mapped names alone, which is a detail worth not depending on.
  const payload = {
    iss: readEnv("JWT_ISSUER") ?? "policy-manager",
    aud: readEnv("JWT_AUDIENCE") ?? "policy-manager-api",
    sub: subject,
    unique_name: subject,
    role: roles,
    jti: crypto.randomUUID(),
    iat: issuedAt,
    nbf: issuedAt,
    exp: issuedAt + ttl,
  };

  const signingInput = `${base64UrlText(JSON.stringify(header))}.${base64UrlText(
    JSON.stringify(payload),
  )}`;

  const cryptoKey = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(key),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const signature = new Uint8Array(
    await crypto.subtle.sign("HMAC", cryptoKey, new TextEncoder().encode(signingInput)),
  );

  return `${signingInput}.${base64Url(signature)}`;
}

/**
 * Resolves the bearer token the script presents.
 *
 * A pre-issued token wins over minting one: when a deployment issues tokens from somewhere else,
 * that is the only way to obtain a valid one, and guessing at the signing key would produce a token
 * the API rejects for reasons the script cannot see.
 */
export async function resolveAuth(): Promise<Auth | null> {
  const supplied = readEnv("API_TOKEN");
  if (supplied && supplied.trim().length > 0) {
    return { token: supplied.trim(), source: "API_TOKEN" };
  }

  if (readEnv("JWT_SIGNING_KEY")) {
    return { token: await mintToken(), source: "minted from JWT_SIGNING_KEY" };
  }

  return null;
}

/** Per-status tallies plus a bounded sample of failures, so a report is bounded whatever the volume. */
export class Report {
  // Plain fields rather than constructor parameter properties: Node's type stripping rewrites types
  // and erases them, but it does not transform code, so a parameter property would survive as an
  // assignment to `this` that never runs. The scripts are executed, not compiled.
  private readonly name: string;
  private readonly target: string;
  private readonly total: number;
  private readonly concurrency: number;
  private readonly logEvery: number;
  private readonly auth: Auth | null;
  private succeeded = 0;
  private failed = 0;
  private readonly statuses = new Map<number | string, number>();
  private readonly samples: string[] = [];

  constructor(
    name: string,
    target: string,
    total: number,
    concurrency: number,
    logEvery: number,
    auth: Auth | null,
  ) {
    this.name = name;
    this.target = target;
    this.total = total;
    this.concurrency = concurrency;
    this.logEvery = logEvery;
    this.auth = auth;
  }

  /** Records one completed request. `sample` is only kept for failures, and only for the first few. */
  record(status: number | string, ok: boolean, sample?: string): void {
    this.statuses.set(status, (this.statuses.get(status) ?? 0) + 1);

    if (ok) {
      this.succeeded++;
    } else {
      this.failed++;
      if (sample && this.samples.length < 20) this.samples.push(sample);
    }

    const done = this.succeeded + this.failed;
    if (this.logEvery > 0 && done % this.logEvery === 0) {
      console.log(`progress: ${done}/${this.total} ok=${this.succeeded} fail=${this.failed}`);
    }
  }

  /** Prints the summary. Returns true when every request succeeded. */
  finish(): boolean {
    console.log(`--- ${this.name}: summary ---`);
    console.log(
      JSON.stringify(
        {
          script: this.name,
          baseUrl: this.target,
          total: this.total,
          concurrency: this.concurrency,
          authenticatedAs: this.auth ? this.auth.source : "NO TOKEN (every request will be rejected)",
          succeeded: this.succeeded,
          failed: this.failed,
          statusCounts: Object.fromEntries(this.statuses),
        },
        null,
        2,
      ),
    );

    if (this.samples.length > 0) {
      console.error(`--- ${this.name}: first ${this.samples.length} failures ---`);
      for (const sample of this.samples) console.error(sample);
    }

    if (this.failed > 0) {
      console.error(`${this.name}: ${this.failed}/${this.total} requests failed`);
      return false;
    }

    return true;
  }
}

/**
 * Runs `spec` for TOTAL requests with at most LOAD_CONCURRENCY in flight, then reports.
 *
 * Bounded concurrency rather than a bare `Promise.all` over every request: the original scripts
 * opened all of them at once, which is a self-inflicted denial of service against whatever is
 * listening, and hides every failure behind a rejected promise nothing was watching for.
 */
export async function runLoadTest(spec: LoadTestSpec): Promise<void> {
  const total = Number(readEnv("TOTAL") ?? spec.defaultTotal);
  const concurrency = Math.max(1, Number(readEnv("LOAD_CONCURRENCY") ?? "50"));
  const logEvery = Number(readEnv("LOG_EVERY") ?? "500");
  const target = baseUrl();
  const auth = await resolveAuth();

  if (!auth) {
    console.error(
      "No bearer token. Every /api endpoint requires one (see README, Authentication and " +
        "Authorization), so every request below will answer 401. Set API_TOKEN to a token you " +
        "have, or JWT_SIGNING_KEY to the API's Jwt:SigningKey so this script can mint one.",
    );
  }

  const report = new Report(spec.name, target, total, concurrency, logEvery, auth);
  const url = `${target}${spec.path}`;

  async function send(index: number): Promise<void> {
    try {
      const response = await fetch(url, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          ...(auth ? { Authorization: `Bearer ${auth.token}` } : {}),
        },
        body: JSON.stringify(spec.body(index)),
      });

      // The body is drained even on success: an unread response holds the connection open, and
      // the pool this script depends on is exactly what runs dry when bodies are left behind.
      const text = await response.text().catch((error: unknown) => `<unreadable body: ${String(error)}>`);
      const sample = response.ok
        ? undefined
        : `#${index} HTTP ${response.status}: ${text.slice(0, 300)}`;

      report.record(response.status, response.ok, sample);
      if (!response.ok) console.error(`${spec.name} #${index} FAILED HTTP ${response.status}: ${text.slice(0, 300)}`);
    } catch (error: unknown) {
      const message = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
      report.record("network-error", false, `#${index} ${message}`);
      console.error(`${spec.name} #${index} ERROR ${message}`);
    }
  }

  let next = 0;
  const workers = Array.from({ length: Math.min(concurrency, total) }, async () => {
    while (next < total) {
      await send(next++);
    }
  });
  await Promise.all(workers);

  if (!report.finish()) failExit();
}

/** Reads an integer environment variable, falling back when it is absent or unparseable. */
export function envNumber(name: string, fallback: number): number {
  const raw = readEnv(name);
  if (raw === undefined) return fallback;
  const parsed = Number(raw);
  return Number.isFinite(parsed) ? parsed : fallback;
}
