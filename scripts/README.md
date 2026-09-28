# scripts

Standalone seed and load tooling. **Nothing here is part of the .NET build and nothing here is
exercised by CI.** The TypeScript files are runtime-agnostic: no `package.json`, no `tsconfig.json`,
no imports of anything outside `scripts/`. They need a runtime that strips types and provides
`fetch` and Web Crypto — **Node 22.6+, Bun or Deno**.

## Authentication first, because everything here needs it

Every `/api` endpoint requires a bearer token, including the read-only ones. Nothing in this
directory issues tokens — the API does not have a token endpoint — so a tool run from a shell has to
be handed one. Two ways:

| Variable | Effect |
|---|---|
| `API_TOKEN` | Use this token verbatim. The right answer when a deployment issues tokens from somewhere else. |
| `JWT_SIGNING_KEY` | Mint an HS256 token from the API's `Jwt:SigningKey`. Convenient locally; useless if the deployment signs tokens somewhere you cannot reach. |

When neither is set, the scripts say so on stderr and every request comes back **401** — which the
`add*.ts` scripts then report as failures, because that is what they are.

`scripts/mint-token.ts` prints a token on stdout, for tools that take one as an argument:

```bash
API_TOKEN="$(node scripts/mint-token.ts)" k6 run ...
```

Mint-time settings, all defaulting to the values in `appsettings.json`:

| Variable | Default |
|---|---|
| `JWT_ISSUER` | `policy-manager` |
| `JWT_AUDIENCE` | `policy-manager-api` |
| `JWT_ROLE` | `Admin` (comma-separated for several) |
| `JWT_SUBJECT` | `load-script` (also the audit actor) |
| `JWT_TTL_SECONDS` | `3600` |

`API_TOKEN` takes precedence over `JWT_SIGNING_KEY`: when both are set, the supplied token is used.

## Rate limiting will end your run early unless you account for it

The API sheds callers over `RateLimiting__PermitLimit` (100/minute by default) with a **429** and a
`Retry-After`. It counts the quota against the `X-Correlation-ID` when one is supplied and against
the connection address otherwise, so:

- `k6/load.js` sends a distinct correlation ID per VU, which means each VU has its own quota. Fifty
  VUs still exceed 100/minute between them, so raise `RateLimiting__PermitLimit` for the run.
- `bombardier/read.sh` sends no correlation ID, so the whole run shares the caller's address bucket.
- A 429 counts as a failed request in every threshold and summary here. A load run against the
  default quota is measuring the limiter, not the API.

## Ad-hoc load scripts

The original fire-and-forget scripts, hardened in #18. Bodies are randomly generated; the point is
volume, not validity.

| Script | Default requests | Endpoint |
|---|---|---|
| `addPolicyHolders.ts` | 7,417 | `POST /api/policyholders` |
| `addPolicy.ts` | 10,000 | `POST /api/policies` |
| `addClaim.ts` | 10,000 | `POST /api/claims` |

```bash
BASE_URL=http://localhost:8080 node scripts/addPolicyHolders.ts
```

| Variable | Default | Meaning |
|---|---|---|
| `BASE_URL` | `http://localhost:8080` | API to hit |
| `TOTAL` | per script | Request count |
| `LOAD_CONCURRENCY` | `50` | Maximum requests in flight |
| `LOG_EVERY` | `500` | Progress line cadence |
| `HOLDER_ID_MIN` / `HOLDER_ID_MAX` | `1` / `7417` | Range `addPolicy.ts` draws `policyHolderId` from |
| `POLICY_ID_MIN` / `POLICY_ID_MAX` | `1` / `5000` | Range `addClaim.ts` draws `policyId` from |

The ID ranges matter: they are random, so a **404** means no such row exists. Run the seed or
`addPolicyHolders.ts` first, or narrow the range. `addClaim.ts` can also return **422** when the
policy is not `Active` or the claim exceeds its remaining coverage — `addPolicy.ts` sets
`coverageLimit` to `null` (unlimited) precisely so this does not happen by accident.

Each run prints a per-failure line, a JSON summary (`succeeded` / `failed` / per-status histogram /
how it authenticated) and **exits non-zero if anything failed**.

## Deterministic seed

`seed/seed.ts` inserts a small fixed set in dependency order — 3 holders → 3 policies → 2 claims —
with valid bodies, so a demo or a manual test starts from a known state.

```bash
BASE_URL=http://localhost:8080 SEED_TAG=demo node scripts/seed/seed.ts
```

Rerunning with the same `SEED_TAG` hits the unique email index and reports 409s (exit non-zero); use
a fresh `SEED_TAG` for a second copy. Policy dates are relative to the day it runs, because a fixed
window would put every seeded policy outside its coverage period the day after it was written.

## k6 suites

Install: <https://grafana.io/docs/get-started/installation/>

```bash
# smoke: 1 VU, 1 iteration, holder -> policy -> claim -> adjudicate -> reads.
# A post-deploy or bring-up gate. Writes rows: point it at a scratch database.
k6 run -e BASE_URL=http://localhost:8080 \
       -e API_TOKEN="$(node ../mint-token.ts)" smoke.js

# load: ramping read-heavy mix (~90% paged list reads, ~10% holder writes).
# Thresholds: p95 < 800ms, errors < 1%.
k6 run -e BASE_URL=http://localhost:8080 \
       -e API_TOKEN="$(node ../mint-token.ts)" load.js

# tune without editing: -e VUS_MAX=20 -e VUS_WRITE_MAX=2 -e HOLD=2m
```

`common.js` holds the base URL, the token and the headers. Without `API_TOKEN` it mints one from
`JWT_SIGNING_KEY` using `k6/crypto`, so a local run needs nothing but that key.

The smoke suite's whole lifecycle is in one script, so it needs a role that may register a
policyholder and adjudicate a claim: **Admin or Adjuster**. An Agent token is refused on the first
write.

## bombardier examples

Install: `go install github.com/codesenberg/bombardier@latest`

```bash
BASE_URL=http://localhost:8080 API_TOKEN="$(node ../mint-token.ts)" bash read.sh
BASE_URL=http://localhost:8080 API_TOKEN="$(node ../mint-token.ts)" bash write-policyholders.sh
```

`read.sh` hits the three paged list endpoints. `write-policyholders.sh` reuses one body file, so
repeat requests return **409** (duplicate email): it measures raw throughput, not valid intake. Use
`addPolicyHolders.ts` or the k6 load suite for varied valid writes.
