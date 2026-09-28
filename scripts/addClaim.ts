// Load script: files claims via POST /api/claims.
//
//   BASE_URL=http://localhost:8080 API_TOKEN=<jwt> node scripts/addClaim.ts
//
// The API is behind bearer authentication, so a token is required. Either supply one with
// API_TOKEN, or set JWT_SIGNING_KEY to the API's Jwt:SigningKey and this script will mint a token
// itself. See scripts/README.md for the full list of environment variables.
//
// `policyId` is picked at random from a range and `description` is required by the column, both of
// which the original script got wrong or omitted. A 404 means no such policy exists; a 422 means
// the policy is not Active or the claim exceeds its remaining coverage — point POLICY_ID_MIN/MAX at
// policies created by addPolicy.ts (whose `coverageLimit` is null, i.e. unlimited) before treating a
// failure count as a defect in the API.
import { envNumber, runLoadTest } from "./lib/api.ts";

const policyIdMin = envNumber("POLICY_ID_MIN", 1);
const policyIdMax = envNumber("POLICY_ID_MAX", 5_000);

await runLoadTest({
  name: "addClaim",
  path: "/api/claims",
  defaultTotal: 10_000,
  body: (index) => ({
    amount: Math.floor(Math.random() * 1_000_000),
    policyId: policyIdMin + Math.floor(Math.random() * (policyIdMax - policyIdMin + 1)),
    description: `load claim ${index}`,
  }),
});
