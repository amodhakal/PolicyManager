// Prints a bearer token on stdout, for the tools that need one as a command-line argument.
//
//   API_TOKEN="$(node scripts/mint-token.ts)" k6 run ...
//
// The API is behind bearer authentication and issues no tokens itself, so a load-test tool run
// from a shell has to be handed one. Prefer API_TOKEN when a deployment issues tokens somewhere
// else. Otherwise this mints one from JWT_SIGNING_KEY — the same symmetric key the API validates
// with — so a local run against a local API needs nothing but that key:
//
//   JWT_SIGNING_KEY=<the API's Jwt:SigningKey> node scripts/mint-token.ts
//
// Everything is overridable: JWT_ISSUER, JWT_AUDIENCE, JWT_ROLE (comma-separated), JWT_SUBJECT
// and JWT_TTL_SECONDS, defaulting to the values in appsettings.json.
//
// The token is written to stdout and nothing else, so it can be captured with $(...); progress
// and warnings go to stderr and never end up in the captured value.
import { failExit, resolveAuth } from "./lib/api.ts";

declare const process: { stdout: { write(chunk: string): void } };

const auth = await resolveAuth();

if (!auth) {
  console.error(
    "Set JWT_SIGNING_KEY to the API's Jwt:SigningKey (at least 32 bytes) to mint a token here, " +
      "or API_TOKEN to print a token you already have.",
  );
  failExit();
} else {
  process.stdout.write(`${auth.token}\n`);
}
