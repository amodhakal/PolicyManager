// Load script: creates policies via POST /api/policies.
//
//   BASE_URL=http://localhost:8080 API_TOKEN=<jwt> node scripts/addPolicy.ts
//
// The API is behind bearer authentication, so a token is required. Either supply one with
// API_TOKEN, or set JWT_SIGNING_KEY to the API's Jwt:SigningKey and this script will mint a token
// itself. See scripts/README.md for the full list of environment variables.
//
// The body carries every field the API now requires (`type`, `startDate`, `endDate`). The original
// script omitted them, which the API answers with 400 — invisible before, because this script
// discarded the status and reported success either way.
//
// `policyHolderId` is picked at random from a range, so a 404 means no such policyholder exists.
// Point HOLDER_ID_MIN/MAX at a range that is actually populated (run addPolicyHolders.ts, or
// scripts/seed/seed.ts) before treating a failure count as a defect in the API.
import { envNumber, runLoadTest } from "./lib/api.ts";

const holderIdMin = envNumber("HOLDER_ID_MIN", 1);
const holderIdMax = envNumber("HOLDER_ID_MAX", 7_417);

await runLoadTest({
  name: "addPolicy",
  path: "/api/policies",
  defaultTotal: 10_000,
  body: () => ({
    premium: Math.floor(Math.random() * 1_000_000),
    policyHolderId: holderIdMin + Math.floor(Math.random() * (holderIdMax - holderIdMin + 1)),
    type: ["Auto", "Home", "Life"][Math.floor(Math.random() * 3)],
    // null means "no stated limit", which is the only value that lets the claim load script below
    // file claims against these policies without exhausting coverage.
    coverageLimit: null,
    startDate: "2026-01-01T00:00:00Z",
    endDate: "2027-01-01T00:00:00Z",
  }),
});
