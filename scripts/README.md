# scripts

Standalone seed/load tooling. Nothing here is part of the .NET build and
nothing here is exercised by CI. All scripts are runtime-agnostic
(Node 18+, Bun, Deno) with no `package.json` — except the k6 suites, which
need the `k6` binary.

Point every script at the API with `BASE_URL` (default is the legacy Azure
host for `add*.ts`, `http://localhost:8080` for the seed script):

```bash
BASE_URL=http://localhost:8080 node scripts/addPolicyHolders.ts
```

## Ad-hoc load scripts (`add*.ts`)

The original fire-and-forget scripts, hardened by #18:

| Script | Requests | Body |
|---|---|---|
| `addPolicyHolders.ts` | 7,417 `POST /api/policyholders` | random holder |
| `addPolicy.ts` | 10,000 `POST /api/policies` | random premium + holder id (**omits required `type`/`startDate`/`endDate** — expect 400s) |
| `addClaim.ts` | 10,000 `POST /api/claims` | random amount + policy id |

Common behaviour / env knobs:

| Variable | Default | Meaning |
|---|---|---|
| `BASE_URL` | legacy Azure host | API to hit |
| `TOTAL` | per-script default above | request count |
| `LOAD_CONCURRENCY` | `50` | max in-flight requests |
| `LOG_EVERY` | `500` | progress line cadence |

Each run prints per-failure lines, a final JSON summary
(`succeeded`/`failed`/per-status histogram + first 20 error samples) and
exits non-zero when anything fails.

## Deterministic seed (`seed/seed.ts`)

Small fixed data set (3 holders → 3 policies → 2 claims) with valid
payloads, inserted in dependency order for demos and manual testing:

```bash
BASE_URL=http://localhost:8080 SEED_TAG=demo node scripts/seed/seed.ts
```

Rerunning with the same `SEED_TAG` hits the unique email index (409s are
reported, exit non-zero); pass a fresh `SEED_TAG` for a second copy.

## k6 suites (`k6/`)

Install: https://k6.io/docs/get-started/installation/

```bash
# smoke: 1-VU full lifecycle (holder -> policy -> claim -> adjudicate),
# suitable as a post-deploy gate against a scratch database
k6 run -e BASE_URL=http://localhost:8080 scripts/k6/smoke.js

# load: ramping read-heavy mix (~90% paged list reads, ~10% holder writes),
# thresholds p95<800ms and errors<1%
k6 run -e BASE_URL=http://localhost:8080 scripts/k6/load.js

# tune without editing: -e VUS_MAX=20 -e VUS_WRITE_MAX=2 -e HOLD=2m
```

The k6 scripts are plain JavaScript (k6 cannot run TypeScript without a
bundling step) and expect a mutable database — do not aim them at
production.

## bombardier examples (`bombardier/`)

Install: `go install github.com/codesenberg/bombardier@latest`

```bash
BASE_URL=http://localhost:8080 bash scripts/bombardier/read.sh
BASE_URL=http://localhost:8080 bash scripts/bombardier/write-policyholders.sh
```

`read.sh` hammers the three paged list endpoints. The write example reuses
a single body file, so repeat runs return 409 (duplicate email) — it
measures raw throughput, not valid intake.
