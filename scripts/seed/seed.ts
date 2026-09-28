// Deterministic demo seed for PolicyManager.
//
// Unlike scripts/add*.ts (random bodies, thousands of rows, load-oriented),
// this script inserts a SMALL, FIXED data set in dependency order
// (holders -> policies -> claims) so demos and manual testing start from a
// known state. Rerunning with the same SEED_TAG hits the unique email index
// and reports 409s; use a fresh SEED_TAG for a second copy.
//
// Usage (Node 18+, Bun, Deno — no dependencies, no package.json):
//   BASE_URL=http://localhost:8080 SEED_TAG=demo node scripts/seed/seed.ts
declare const process: { env: Record<string, string | undefined>; exitCode: number } | undefined;

function readEnv(name: string): string | undefined {
  try {
    return (globalThis as { process?: { env?: Record<string, string | undefined> } }).process?.env?.[name];
  } catch {
    return undefined;
  }
}

const BASE_URL = readEnv("BASE_URL") ?? "http://localhost:8080";
const TAG = readEnv("SEED_TAG") ?? "demo";
const H = { "Content-Type": "application/json" };

// --- fixed demo data (edit here to change the demo; ids come from responses) ---
const HOLDERS = [
  { firstName: "Ada", lastName: "Lovelace", emailLocal: "ada.lovelace" },
  { firstName: "Grace", lastName: "Hopper", emailLocal: "grace.hopper" },
  { firstName: "Alan", lastName: "Turing", emailLocal: "alan.turing" },
] as const;

const POLICIES = [
  { holder: 0, premium: 1200.5, type: "Auto", coverageLimit: 50000, startDate: "2026-01-01T00:00:00Z", endDate: "2027-01-01T00:00:00Z" },
  { holder: 1, premium: 850.0, type: "Home", coverageLimit: 250000, startDate: "2026-01-01T00:00:00Z", endDate: "2027-01-01T00:00:00Z" },
  { holder: 2, premium: 300.25, type: "Life", coverageLimit: null, startDate: "2026-01-01T00:00:00Z", endDate: "2027-01-01T00:00:00Z" },
] as const;

const CLAIMS = [
  { policy: 0, amount: 500, description: "Rear bumper repair" },
  { policy: 1, amount: 2500, description: "Storm damage to roof" },
] as const;

let failed = 0;

async function post<T>(label: string, path: string, body: unknown): Promise<T | null> {
  try {
    const res = await fetch(`${BASE_URL}${path}`, {
      method: "POST",
      headers: H,
      body: JSON.stringify(body),
    });
    const text = await res.text().catch((e: unknown) => `<unreadable body: ${String(e)}>`);
    if (!res.ok) {
      failed++;
      console.error(`${label} FAILED HTTP ${res.status}: ${text.slice(0, 300)}`);
      return null;
    }
    console.log(`${label} -> ${res.status} ${text.slice(0, 200)}`);
    return JSON.parse(text) as T;
  } catch (err: unknown) {
    failed++;
    console.error(`${label} ERROR ${err instanceof Error ? `${err.name}: ${err.message}` : String(err)}`);
    return null;
  }
}

const holderIds: number[] = [];
for (const [i, h] of HOLDERS.entries()) {
  const created = await post<{ id: number }>(`holder[${i}] ${h.emailLocal}`, "/api/policyholders", {
    firstName: h.firstName,
    lastName: h.lastName,
    email: `${h.emailLocal}+${TAG}@example.com`,
  });
  if (created) holderIds[i] = created.id;
}

const policyIds: number[] = [];
for (const [i, p] of POLICIES.entries()) {
  const holderId = holderIds[p.holder];
  if (holderId === undefined) {
    failed++;
    console.error(`policy[${i}] SKIPPED: holder[${p.holder}] was not created`);
    continue;
  }
  const created = await post<{ id: number }>(`policy[${i}]`, "/api/policies", {
    premium: p.premium,
    policyHolderId: holderId,
    type: p.type,
    coverageLimit: p.coverageLimit,
    startDate: p.startDate,
    endDate: p.endDate,
  });
  if (created) policyIds[i] = created.id;
}

for (const [i, c] of CLAIMS.entries()) {
  const policyId = policyIds[c.policy];
  if (policyId === undefined) {
    failed++;
    console.error(`claim[${i}] SKIPPED: policy[${c.policy}] was not created`);
    continue;
  }
  await post(`claim[${i}]`, "/api/claims", {
    policyId,
    amount: c.amount,
    description: c.description,
  });
}

console.log(`--- seed done: holders=${holderIds.filter((n) => n !== undefined).length}/${HOLDERS.length} policies=${policyIds.filter((n) => n !== undefined).length}/${POLICIES.length} failed=${failed} ---`);
if (failed > 0) {
  const proc = (globalThis as { process?: { exitCode?: number } }).process;
  if (proc) proc.exitCode = 1;
  else throw new Error("seed finished with failures (no process.exitCode available)");
}
