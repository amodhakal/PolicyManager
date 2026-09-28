// Seed/load script: creates policies via POST /api/policies.
//
// Usage (any runtime with fetch + top-level await: Node 18+, Bun, Deno):
//   BASE_URL=http://localhost:8080 LOAD_CONCURRENCY=50 node scripts/addPolicy.ts
//
// Behaviour:
//   - Bounded concurrency (default 50) instead of firing all requests at once.
//   - Counts successes/failures, reports a per-status histogram + error sample.
//   - Sets a non-zero exit code when any request fails (via process.exitCode
//     when available, otherwise throws so the runtime exits non-zero).
//   - BASE_URL defaults to the legacy Azure host; override per environment.
//   - No imports, no package.json required.
//
// NOTE: the payload below mirrors the original ad-hoc script and omits the
// required `type`/`startDate`/`endDate` fields, so a strict API will answer
// 400 and this script will now report that (exit 1) instead of silently
// passing. For valid payloads use scripts/seed/seed.ts (issue #67).
declare const process: { env: Record<string, string | undefined>; exitCode: number } | undefined;

function readEnv(name: string): string | undefined {
  try {
    return (globalThis as { process?: { env?: Record<string, string | undefined> } }).process?.env?.[name];
  } catch {
    return undefined;
  }
}

function failExit(): void {
  const proc = (globalThis as { process?: { exitCode?: number } }).process;
  if (proc) {
    proc.exitCode = 1;
    return;
  }
  throw new Error("load script finished with failures (no process.exitCode available)");
}

const BASE_URL =
  readEnv("BASE_URL") ??
  "https://policymanager-api-fdhafuhzfagyajby.centralus-01.azurewebsites.net";
const TOTAL = Number(readEnv("TOTAL") ?? "10000");
const CONCURRENCY = Number(readEnv("LOAD_CONCURRENCY") ?? "50");
const LOG_EVERY = Number(readEnv("LOG_EVERY") ?? "500");

let succeeded = 0;
let failed = 0;
const statusCounts = new Map<number | string, number>();
const errorSamples: string[] = [];

function record(status: number | string, ok: boolean, sample?: string): void {
  statusCounts.set(status, (statusCounts.get(status) ?? 0) + 1);
  if (ok) {
    succeeded++;
  } else {
    failed++;
    if (sample && errorSamples.length < 20) errorSamples.push(sample);
  }
  const done = succeeded + failed;
  if (done % LOG_EVERY === 0) console.log(`progress: ${done}/${TOTAL} ok=${succeeded} fail=${failed}`);
}

async function addPolicy(index: number): Promise<void> {
  const data = {
    premium: Math.floor(Math.random() * 1_000_000),
    policyHolderId: Math.floor(Math.random() * 7_417),
  };
  try {
    const res = await fetch(`${BASE_URL}/api/policies`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data),
    });
    const text = await res.text().catch((e: unknown) => `<unreadable body: ${String(e)}>`);
    record(res.status, res.ok, res.ok ? undefined : `#${index} HTTP ${res.status}: ${text.slice(0, 300)}`);
    if (!res.ok) console.error(`#${index} FAILED HTTP ${res.status}: ${text.slice(0, 300)}`);
  } catch (err: unknown) {
    const msg = err instanceof Error ? `${err.name}: ${err.message}` : String(err);
    record("network-error", false, `#${index} ${msg}`);
    console.error(`#${index} ERROR ${msg}`);
  }
}

// Bounded-concurrency pool: runs `TOTAL` tasks with at most `CONCURRENCY` in flight.
async function runPool(): Promise<void> {
  let next = 0;
  const workers = Array.from({ length: Math.min(CONCURRENCY, TOTAL) }, async () => {
    while (next < TOTAL) {
      const i = next++;
      await addPolicy(i);
    }
  });
  await Promise.all(workers);
}

await runPool();

console.log("--- summary ---");
console.log(JSON.stringify({ baseUrl: BASE_URL, total: TOTAL, succeeded, failed, statusCounts: Object.fromEntries(statusCounts) }, null, 2));
if (errorSamples.length > 0) {
  console.error("--- error samples (first 20) ---");
  for (const s of errorSamples) console.error(s);
}
if (failed > 0) {
  console.error(`${failed}/${TOTAL} requests failed`);
  failExit();
}
