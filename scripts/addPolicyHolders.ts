// Load script: creates policyholders via POST /api/policyholders.
//
//   BASE_URL=http://localhost:8080 API_TOKEN=<jwt> node scripts/addPolicyHolders.ts
//
// The API is behind bearer authentication, so a token is required. Either supply one with
// API_TOKEN, or set JWT_SIGNING_KEY to the API's Jwt:SigningKey and this script will mint a token
// itself. See scripts/README.md for the full list of environment variables.
//
// Errors are counted and reported, and the process exits non-zero if any request failed, rather
// than a stream of console output and an exit code that says everything worked.
import { runLoadTest } from "./lib/api.ts";

await runLoadTest({
  name: "addPolicyHolders",
  path: "/api/policyholders",
  defaultTotal: 7_417,
  body: () => ({
    email: `${crypto.randomUUID()}@gmail.com`,
    firstName: crypto.randomUUID(),
    lastName: crypto.randomUUID(),
  }),
});
