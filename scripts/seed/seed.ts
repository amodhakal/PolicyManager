// Deterministic demo seed for PolicyManager.
//
// Unlike scripts/add*.ts (random bodies, thousands of rows, load-oriented),
// this script inserts a SMALL, FIXED data set in dependency order
// (holders -> policies -> claims) so demos and manual testing start from a
// known state. Rerunning with the same SEED_TAG hits the unique email index
// and reports 409s; use a fresh SEED_TAG for a second copy.
//
//   BASE_URL=http://localhost:8080 SEED_TAG=demo node scripts/seed/seed.ts
//
// Authentication is the same as for the load scripts: API_TOKEN if you have
// a token, otherwise JWT_SIGNING_KEY and this mints one. The roles that
// matter here are Admin and Adjuster, because registering a policyholder and
// issuing a policy are both back-office writes; an Agent token is refused.
//
// The dates are relative to the day the seed runs, not hard-coded, because a
// policy whose coverage window has already ended is rejected for claims by
// the rules in Domain/ClaimRules.cs — so a seed with fixed 2026 dates would
// start producing 422s the day after it was written.
import { baseUrl, failExit, readEnv, resolveAuth } from "../lib/api.ts";

const TAG = readEnv("SEED_TAG") ?? "demo";
const HOLDERS = [
  { firstName: "Ada", lastName: "Lovelace", emailLocal: "ada.lovelace" },
  { firstName: "Grace", lastName: "Hopper", emailLocal: "grace.hopper" },
  { firstName: "Alan", lastName: "Turing", emailLocal: "alan.turing" },
] as const;

const POLICIES = [
  { holder: 0, premium: 1200.5, type: "Auto", coverageLimit: 50000, days: 365 },
  { holder: 1, premium: 850.0, type: "Home", coverageLimit: 250000, days: 365 },
  { holder: 2, premium: 300.25, type: "Life", coverageLimit: null, days: 365 },
] as const;

const CLAIMS = [
  { policy: 0, amount: 500, description: "Rear bumper repair" },
  { policy: 1, amount: 2500, description: "Storm damage to roof" },
] as const;

const BASE_URL = baseUrl();
const auth = await resolveAuth();
const HEADERS: Record<string, string> = {
  "Content-Type": "application/json",
  ...(auth ? { Authorization: `Bearer ${auth.token}` } : {}),
};

if (!auth) {
  console.error(
    "No bearer token, so every write below will be refused with 401. Set API_TOKEN, or " +
      "JWT_SIGNING_KEY to the API's Jwt:SigningKey so this script can mint one.",
  );
}

function isoDaysFromNow(days: number): string {
  const date = new Date(Date.now() + days * 86_400_000);
  return date.toISOString();
}

let failed = 0;

async function post<T>(label: string, path: string, body: unknown): Promise<T | null> {
  try {
    const res = await fetch(`${BASE_URL}${path}`, {
      method: "POST",
      headers: HEADERS,
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
    startDate: isoDaysFromNow(-1),
    endDate: isoDaysFromNow(p.days),
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

console.log(
  `--- seed done: holders=${holderIds.filter((n) => n !== undefined).length}/${HOLDERS.length} ` +
    `policies=${policyIds.filter((n) => n !== undefined).length}/${POLICIES.length} failed=${failed} ---`,
);
if (failed > 0) failExit();
