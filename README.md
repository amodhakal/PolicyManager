# PolicyManager

[![CI](https://github.com/amodhakal/PolicyManager/actions/workflows/ci.yml/badge.svg)](https://github.com/amodhakal/PolicyManager/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/amodhakal/PolicyManager/branch/main/graph/badge.svg?token=UPG0QIKCI6)](https://codecov.io/gh/amodhakal/PolicyManager)

REST API for managing insurance policies, policyholders, and claims.

---

## What It Is

PolicyManager is an ASP.NET Core Web API that models core insurance operations: creating and managing policyholders, issuing and updating policies, filing claims, and adjudicating those claims (approve/deny)

All data is persisted to SQL Server via Entity Framework Core with migration-based schema management.

Every endpoint is authenticated and role-authorized, personal data is encrypted at rest and access-audited, writes carry optimistic concurrency tokens, and a transactional outbox publishes domain events to a message broker. Those are load-bearing, not decoration — each one is explained where it is implemented, including what it costs.

---

## Stack

| Technology              | Role |
|-------------------------|---|
| ASP.NET Core            | Web API framework |
| Entity Framework Core   | ORM with code-first migrations |
| SQL Server (Docker)     | Primary data store |
| MassTransit / RabbitMQ  | Transactional-outbox transport (opt-in) |
| OpenTelemetry           | Distributed tracing and metrics |
| Polly                   | Retry, circuit breakers and timeouts for outbound calls |
| OpenAPI / Swagger       | API documentation and contract |
| GitHub Actions          | CI pipeline (build, test on every push to main) |
| Docker / Docker Compose | Containerized local environment |
| xUnit                   | Unit testing (service and controller layers) |
| Angular                 | Web client for the API (`client/`) |
| Vitest                  | Unit testing (client) |


---

## How to Run

**Prerequisites:** Docker Desktop, .NET SDK, `.env` file in root, similar to `.env.example`

```bash
# 1. Start SQL Server
docker compose up -d

# 2. Apply migrations
dotnet ef database update --project PolicyManager/PolicyManager.csproj

# 3. Run the API
dotnet run --project PolicyManager/PolicyManager.csproj
```

**Migrations are required.** The schema is managed by code-first EF Core migrations and
nothing creates the tables for you — the API will fail to serve any request that touches the
database until migrations have been applied. Step 2 is the command, run from the repository
root.

To create a new migration after changing a model:

```bash
dotnet ef migrations add <MigrationName> --project PolicyManager/PolicyManager.csproj
```

Swagger UI is available at `https://localhost:7100/swagger` (the `https` profile in
`PolicyManager/Properties/launchSettings.json`). The app calls `UseHttpsRedirection()`,
so plain HTTP requests are redirected to HTTPS.

### The Angular client

`client/` is a standalone Angular application covering every endpoint: policyholders, policies,
claims and the three reports, with the writes each role is allowed to perform.

```bash
cd client
npm install
npm start          # http://localhost:4200
```

> **The API must already be running**, and its schema must already exist — see
> [Known Issues](#known-issues): on `main` the migration chain does not apply to an empty
> database, so step 2 of [How to Run](#how-to-run) currently fails.

**It asks for a bearer token on first run.** The API has no login endpoint — it validates
externally minted JWTs and exposes nothing that issues one — so the client accepts a pasted
token and keeps it in `localStorage`. The token is decoded to read the caller's roles so the UI
can hide what would answer 403; nothing is enforced client-side, and the API remains the only
thing that decides. A token carrying none of `Admin`, `Adjuster` or `Agent` is rejected up
front, because such a token would authenticate and then be refused by every policy.

**The dev server proxies to the API** rather than the API enabling CORS. A CORS policy would
let any origin call a service holding encrypted personal data, for the benefit of a local
setup; a same-origin proxy costs the API nothing. The target defaults to
`https://localhost:7080` and is overridden with `POLICYMANAGER_API` for a container or a
remote API:

```bash
POLICYMANAGER_API=http://localhost:8080 npm start
```

```bash
cd client
npm test           # Vitest
npm run build      # production bundle into client/dist/
```

### The app will not start without three secrets

Authentication and PII protection both fail fast rather than falling back to a default, so a
first run needs all three of these supplied:

```bash
# 32 bytes base64 for AES-256:  openssl rand -base64 32
Pii__EncryptionKey=
# at least 32 bytes:             openssl rand -base64 48
Pii__BlindIndexKey=
# at least 32 bytes for HMAC-SHA256
Jwt__SigningKey=
```

Each is explained where it is used: [Encryption at rest](#encryption-at-rest) and
[Configuration](#configuration) respectively. A missing one is named in the startup error.
This is the first thing that will stop you, and it is deliberate — an instance that comes up
healthy while accepting unsigned tokens or storing addresses in the clear is worse than one
that is plainly down.

---

## Environment Variables

Copy `.env.example` to `.env` in the repository root. In the Development environment
`Program.cs` loads `../.env` relative to the project directory via DotNetEnv; the values
are then read from the process environment.

| Variable | Default (in `.env.example`) | Required | Purpose |
|---|---|---|---|
| `DB_PASSWORD` | `xxxxxxx` (placeholder) | Yes | SQL Server `sa` password. Consumed by `compose.yaml` as `MSSQL_SA_PASSWORD`, and by `Program.cs` as the connection string's `Password`. The `xxxxxxx` value is a placeholder — replace it, otherwise the SQL Server container will not accept it. |
| `DB_HOST` | `localhost,14330` | Yes | SQL Server instance for the connection string's `Server`. The default matches the host-side port published by `compose.yaml` (`14330:1433`). Use `mssql-server` when the API runs inside the Compose network. |
| `DB_NAME` | `PolicyManager` | Yes | Database name for the connection string's `Database`. Must already exist for `dotnet ef database update` to target it. |
| `DB_USER` | `SA` | Yes | SQL Server login for the connection string's `User Id`. Must match the account `DB_PASSWORD` belongs to (the Compose service creates `sa`). |

### Broker variables

Set `BROKER_ENABLED=true` in `.env` to have the API publish the transactional outbox to
RabbitMQ instead of the in-process logging stand-in. Leave it `false` — the default — and no
broker is needed for `dotnet test` or for local development. See
[Publishing to a real broker](#publishing-to-a-real-broker) for the full set of `Broker__*`
configuration keys and what each one is for.

| Variable | Default (in `.env.example`) | Required | Purpose |
|---|---|---|---|
| `BROKER_ENABLED` | `false` | No | Publishes the outbox to the broker when `true`. Consumed by `compose.yaml` as `Broker__Enabled`. |
| `BROKER_HOST` | `localhost` | No | Broker hostname. Use `rabbitmq` when the API runs inside the Compose network. |
| `BROKER_PORT` | `5672` | No | AMQP port. |
| `BROKER_USERNAME` | `guest` | No | Broker username. Also sets `RABBITMQ_DEFAULT_USER` on the Compose broker. |
| `BROKER_PASSWORD` | `guest` | No | Broker password. Also sets `RABBITMQ_DEFAULT_PASS`. Change it for anything but a local container. |
| `BROKER_VHOST` | `/` | No | Virtual host. Also sets `RABBITMQ_DEFAULT_VHOST`. |
| `BROKER_QUEUE_NAME` | `policy-manager.outbox` | No | The durable queue outbox messages are published to. |

The connection string lives in configuration under the standard `ConnectionStrings:DefaultConnection`
key. `appsettings.json` holds the template and `appsettings.Development.json` overrides it;
`Program.cs` resolves it with `GetConnectionString("DefaultConnection")` and expands `${NAME}`
placeholders from the four variables above.

`TrustServerCertificate` is part of that connection string, not hardcoded. It is `False` in
`appsettings.json` and `True` only in `appsettings.Development.json`, because the local
SQL Server uses a self-signed certificate. **Production does not trust server certificates
unless you explicitly override `ConnectionStrings__DefaultConnection`.**

If the connection string is missing — or, outside Development, still contains unresolved
`${...}` placeholders — the app throws at startup and names the offending variables rather
than failing later on the first query.

---

## Running in Docker

`compose.yaml` builds the API image and starts both services:

```bash
cp .env.example .env    # then set a strong DB_PASSWORD
docker compose up -d
```

- The API is published on **http://localhost:8080**.
- The `api` service waits on `condition: service_healthy`, so Compose blocks until SQL Server
  actually accepts logins (a `sqlcmd` healthcheck) rather than merely starting. If the
  database never becomes healthy, `docker compose up` appears to hang — that is the gate
  working, and it usually means `DB_PASSWORD` failed the container's password policy.
- The container applies any pending EF migrations before starting the API, retrying for about
  a minute while SQL Server finishes booting, and exits non-zero if they fail. It does not
  start the app in a broken state.
- The `api` service is started with `ASPNETCORE_ENVIRONMENT=Development` so it can reach the
  self-signed local SQL Server certificate, and so Swagger is served in the container.

`compose.yaml` also starts a `rabbitmq` service and wires the `Broker__*` variables into the
`api` service, but the outbox transport is off unless `BROKER_ENABLED=true` is set in `.env` — the
API does not wait on RabbitMQ and does not contact it. The management UI is at
<http://localhost:15672> (`guest` / `guest` by default).

`DB_PASSWORD` must satisfy the SQL Server image's password policy: **at least 8 characters
and at least three of** uppercase, lowercase, digits, and symbols. The `xxxxxxx` placeholder
in `.env.example` is deliberately invalid and will crash-loop the database container.

---

## Known Issues

- **The database is never created for you.** `compose.yaml` provisions the SQL Server
  *instance* but no `PolicyManager` database. `dotnet ef database update` and the container
  entrypoint both connect with `Database=PolicyManager`, so that database must exist first.

- **The migration chain does not apply to an empty database.** Two independent faults, in the
  order you meet them:

  1. `dotnet ef migrations has-pending-model-changes` reports drift, and the CLI turns that
     into a hard error, so `dotnet ef database update` refuses to run at all. The cause is
     `EnforcePiiBlindIndex`: the migration and the snapshot both leave `EmailHash` as
     `NOT NULL` with a unique index, while `AppDbContext` still declares it nullable with a
     plain index — deliberately, per the two-phase rollout described under
     [Protecting Personal Data](#protecting-personal-data). EF derives the snapshot from the
     model, so a model that stays at phase one and a migration that encodes phase two cannot
     both be right. The CI job `migration-drift` fails on this.
  2. Past that, `20260928175037_QueryIndexes` fails with *"an index or statistics with name
     'IX_OutboxMessages_Pending' already exists"*. It creates the replacement index under the
     same name an earlier migration already used, and only drops the old one afterwards. An
     index name is unique per table, so the create can never succeed; the intent stated in its
     own comment — replace without ever leaving a query unindexed — needs the new index to be
     built under a temporary name, the old one dropped, and the new one renamed.

  Together these mean the API cannot be started from a clean database, and the SQL Server
  suite is failing on every test rather than on any real regression. Tracked in issue #126.

- **`scripts/*.ts` are broken against the current API.** They were written before
  authentication, rate limiting and request size limits existed, and all three get in their
  way: every request now needs a bearer token, thousands of `POST`s in parallel will be
  rate-limited, and the scripts hard-code an Azure host rather than taking a base URL. They
  also still ignore every error — a bare `Promise.all` over fire-and-forget fetches, so a
  run that fails entirely looks like a success. Treat them as a sketch of the intended
  load profile, not as working tooling. Tracked in issues #18, #67.

- **There is no token issuer.** Every endpoint needs a bearer token and nothing in this
  repository mints one for a human — `TestTokens` exists but is test-only. Driving the API
  means writing a token first, whether by hand or through the client, which accepts a pasted
  one. Tracked in issue #89.

- **Encrypted email columns only support equality.** The `Email` column is ciphertext, so the
  only comparison available against it is the `EmailHash` blind index — an exact match. A
  "starts with" or range query on a policyholder address is not expressible. Nothing needs
  one today, and reaching for it later means decrypting in the application, not adding a
  database index.

- **`ResilientHttpMessageHandler` protects nothing yet.** The `outbound` named `HttpClient` is
  registered with the pipeline and nothing issues calls through it, so the HTTP resilience
  section is currently configuration without a dependency to apply it to.

- **Controller-level tests do not exercise the SQL Server provider.** Only the
  `SqlServer`-category tests do. A service can satisfy every controller test and still be
  wrong against the real database; that gap is why `PolicyManager.Tests/Integration/` exists,
  but it is not a substitute for a full-stack suite.

---

## Tests

Run the full test suite from the repository root (a `PolicyManager.sln` is present):

```bash
dotnet test
```

The suite is xUnit, with Moq for mocking the service interfaces and the EF Core InMemory
provider for `AppDbContext`, so the default run needs no SQL Server instance and no broker.
Coverage is collected via coverlet.

Both of the opt-in features are off by default in the test host, so `dotnet test` needs
neither RabbitMQ nor a collector. Tests that need a token call `TestTokens`, which mints
**real** signed tokens the production validation pipeline evaluates — see
[Tests](#tests) under Authentication.

### SQL Server tests

The InMemory provider builds its schema from the model and enforces **no unique indexes, no
foreign keys, no column precision, no string length limits and no transactions**. A suite that
only ever runs against it reports green while the application is broken against the database it
actually ships with. `PolicyManager.Tests/Integration/` therefore covers the constraints that
provider hides, against a real SQL Server started per run by
[Testcontainers](https://dotnet.testcontainers.org/):

```bash
# Everything except the container-backed tests (the default)
dotnet test

# Only the SQL Server tests; needs a running Docker daemon
dotnet test --filter "Category=SqlServer"
```

The container image is pinned to the same tag `compose.yaml` uses, and the schema is built by
running the real EF migrations rather than `EnsureCreated`, so these tests also prove the
migration scripts are valid and complete. The container is shared across the collection, so it
starts once per run.

CI splits this into three independent jobs: the InMemory suite with coverage, the
SQL Server suite, and a `dotnet ef migrations has-pending-model-changes` check. That last one
matters because a model change shipped without a matching migration is invisible to both test
suites — the InMemory provider builds from the model, and the SQL Server tests would fail on a
missing table rather than on the drift.

Shared test infrastructure lives in `PolicyManager.Tests/Infrastructure/`:
`InMemoryApiFactory` swaps the SQL Server `AppDbContext` for the InMemory provider and supplies
the JWT and PII configuration the host now requires, `SqlServerApiFactory` repoints the application
at the container and stops the outbox poller so it cannot drain rows a test is asserting on,
`ApiIntegrationTestBase` provides the authenticated client, the lifecycle and the holder/policy/claim
seeding helpers, `TestTokens` mints signed tokens with real role claims, `ServiceTestBase` provides a
per-test `DbContext` and `Dispose` for the service tests, and `SqlServerFixture`/`SqlServerTestBase`
own the container. Add new tests by deriving from those rather than repeating the setup.

CI runs `dotnet test` on every push to `main` and on every pull request targeting `main`,
excluding `Migrations/**` from coverage, and uploads the report to Codecov. A second,
independent job builds the Docker image without running it, so a Dockerfile regression fails
CI without needing a database. Note that CI only runs for pull requests whose **base** is
`main`; a pull request based on another branch does not trigger it.

---

## API Endpoints

### Policyholders
| Method | Route | Description |
|---|---|---|
| `GET` | `/api/policyholders` | List a page of policyholders; supports `?page=`, `?pageSize=`, `?sortBy=`, `?descending=` |
| `POST` | `/api/policyholders` | Create a policyholder; a duplicate email is a 409 |
| `GET` | `/api/policyholders/{id}` | Get by ID |
| `PUT` | `/api/policyholders/{id}` | Update `firstName`, `lastName` or `email`; omitted fields are left alone |
| `DELETE` | `/api/policyholders/{id}` | Soft delete; hides the holder, keeps the row and their policies |
| `PATCH` | `/api/policyholders/{id}/restore` | Undo a soft delete; idempotent |

### Policies
| Method | Route | Description |
|---|---|---|
| `GET` | `/api/policies` | List a page of policies; supports `?status=Active` and the paging/sorting options |
| `POST` | `/api/policies` | Create a policy linked to a policyholder; an unknown holder is a 404 |
| `GET` | `/api/policies/{id}` | Get with policyholder info |
| `PUT` | `/api/policies/{id}` | Update status or premium |
| `DELETE` | `/api/policies/{id}` | Soft delete, sets status to `Cancelled` |

### Claims
| Method | Route | Description |
|---|---|---|
| `GET` | `/api/claims` | List a page of claims; supports the paging/sorting options |
| `POST` | `/api/claims` | File a claim against a policy |
| `GET` | `/api/claims/{id}` | Get claim details |
| `PATCH` | `/api/claims/{id}/status` | Adjudicate, approve or deny the claim |
| `DELETE` | `/api/claims/{id}` | Soft delete a claim awaiting adjudication, hides it and keeps the row |
| `PATCH` | `/api/claims/{id}/restore` | Undo a soft delete |

### Health
| Method | Route | Description |
|---|---|---|
| `GET` | `/health` | Liveness. Runs every registered check |
| `GET` | `/health/ready` | Readiness. Runs only the checks tagged `ready` — `sql-server`, and `message-broker` when the broker is enabled |

Route casing follows ASP.NET Core's default: `[Route("api/[controller]")]` resolves
`PolicyHolders` to `/api/policyholders`, `Policies` to `/api/policies`, and `Claims` to
`/api/claims`. All `{id}` segments are constrained to integers in code
(`[HttpGet("{id:int}")]`).

Every endpoint requires a bearer token — see
[Authentication and Authorization](#authentication-and-authorization). The two health
endpoints are the exception: they are unauthenticated, because an orchestrator probing
liveness has no credential to present and a probe that needs one cannot report an
outage. That is why their failure descriptions are deliberately vague.

### Who may call what

Roles are described in full under
[Roles](#roles); the per-endpoint requirement is:

| Operation | Requirement |
|---|---|
| Read any resource | `Agent`, `Adjuster` or `Admin` |
| Create a policyholder, create or update a policy, file or adjudicate a claim | `Admin` or `Adjuster` |
| Delete a policy | `Admin` only |

An **adjudicated claim cannot be deleted at all**, by any role. The record of who decided
it is why it stays in the book.

---

## Pagination, Filtering and Sorting

All three list endpoints take the same four query-string options and return a `PagedResult<T>`
envelope instead of a bare array:

| Option | Default | Behaviour |
|---|---|---|
| `page` | `1` | One-based page number |
| `pageSize` | `25` | Clamped to `1`..`100` (`PaginationQuery.MaxPageSize`) |
| `sortBy` | resource default | Field name, matched case-insensitively |
| `descending` | `false` | Reverses the order `sortBy` establishes |

The envelope carries `items`, `page`, `pageSize`, `totalCount`, `totalPages`, `hasPrevious`
and `hasNext`. `totalCount` is the size of the whole filtered set, so a client can page
without a second request; `totalPages` is `0` when nothing matches, and a page past the end
comes back with empty `items` and the true `totalCount` rather than a 404.

Out-of-range values are **clamped, not rejected**: `?page=0` is page 1, `?pageSize=0` is one
item, and `?pageSize=100000` is 100. A caller with a bad page size wants the nearest sensible
page, not a 400.

An unrecognised `sortBy` also does not fail. Each service has its own explicit list of sortable
fields and falls back to ordering by identifier, which is unique and therefore the only total
order:

| Resource | Sortable | Default |
|---|---|---|
| Policyholders | `id`, `firstName`, `lastName`, `email` | `id` |
| Policies | `id`, `policyNumber`, `premium`, `status`, `policyHolderId` | `id` |
| Claims | `id`, `claimNumber`, `amount`, `status`, `filedAt`, `policyId` | `id` |

Every ordering appends the identifier as a tie-breaker. Without it, two rows sharing a last name
or a premium could swap between two consecutive requests and the same page number would return
different rows.

The filtering, the `COUNT`, the ordering, the `OFFSET`/`FETCH` and the projection all stay in
the SQL the database receives: the query stays an `IQueryable` and the DTO projection is applied
*after* `Skip`/`Take`, so paging happens in the database rather than over a materialized list.
`?status=` on `/api/policies` still works and composes with paging — `totalCount` counts the
filtered set only.

A page is a snapshot. `totalCount` describes the rows that matched when the page was read, so
rows written afterwards are not reflected in it.

---

## Claim Lifecycle

Adjudication is enforced as a state machine, not a free-text status field.

```
   Pending ──┬──> Approved   (terminal)
             └──> Denied     (terminal)
```

`Pending` is the only status a claim can move out of, and `Approved`/`Denied` are terminal.
`PATCH /api/claims/{id}/status` accepts only these transitions and answers **422** with the
permitted targets in the body otherwise. Previously the endpoint assigned whatever status it was
given, so a **denied claim could be flipped back to approved** and a paid claim silently retracted.
A transition to the status a claim already holds is also rejected rather than treated as a
successful no-op, which would otherwise stamp a fresh decision date on an undecided claim.

The rules live in `PolicyManager/Domain/` as pure functions over a loaded entity, so they can be
read and tested without a database, a clock or a service. `ClaimRules.EnsurePolicyAcceptsClaims`
rejects a claim against a cancelled or expired policy — previously only the policy's *existence*
was checked, so a cancelled policy still accepted claims. `ClaimRules.EnsureWithinCoverage` checks
both a single claim and the running total against `Policy.CoverageLimit`:

- Checking only the single amount would let a caller file four claims that each fit under the limit
  and collectively exceed it, so both are checked.
- A **denied** claim releases its coverage, since the amount was never payable. Pending and approved
  claims both reserve value that could still be paid out; counting only approved claims would allow
  unlimited pending claims against an exhausted policy.
- A `null` `CoverageLimit` means **unlimited**, not zero. Treating a missing limit as zero would
  reject every claim against an unconfigured policy.

`CoverageLimit` is supplied when the policy is issued and is deliberately separate from `Premium`,
which is what the holder pays — conflating them would cap a policy's payout at its price.

Adjudication also records an audit trail: `AdjusterNotes`, `DecisionDate` and `DecidedBy`, all
nullable so "not yet adjudicated" stays distinguishable from "adjudicated with nothing recorded".
`DecisionDate` is set by the server and cannot be backdated by the caller.

A claim against a policy that does not exist is now a **404** rather than a 400; the existence
check moved into the service, which needs the loaded policy to evaluate the coverage rules anyway.

---

## Health Checks

Two unauthenticated endpoints, both registered with `MapHealthChecks` and both emitting JSON:

| Route | Predicate | Use |
|---|---|---|
| `GET /health` | every registered check | **Liveness** — is this process running |
| `GET /health/ready` | checks tagged `ready` | **Readiness** — can this instance serve a request now |

They are separate on purpose. A process whose database is unreachable is still *running*, and
restarting it will not fix the database; only a readiness failure should take the instance out of
rotation. A single combined endpoint would force an orchestrator to choose between restarting a
healthy-but-isolated pod and leaving a broken one in the load balancer.

There are two checks, both tagged `ready`:

- **`sql-server`** (`PolicyManager/Health/SqlServerHealthCheck.cs`) opens a connection through the
  injected `AppDbContext` and runs `SELECT 1` — the same connection string, provider and credentials
  the application itself uses, so a check that passed cannot coexist with a failure on every real
  request. It probes with a query rather than `CanConnectAsync` alone, because a connection validated
  when the process started is routinely dead by the time a probe first asks. A five-second per-check
  timeout bounds how long a hung connection can hold a probe open. It is wrapped in the database
  resilience pipeline, so a database that is genuinely down is rejected quickly instead of every
  probe paying the full timeout.
- **`message-broker`** is registered only when `Broker:Enabled` is true. It reports the bus, and counts
  a `Degraded` bus as a failure so a broker that is still starting takes the instance out of rotation
  rather than admitting requests that would only fail. With the broker disabled the check does not
  exist, and readiness is the database alone.

The body carries the overall status, the per-check status, the published description and the
timings, and the status is `200` for `Healthy` and `Degraded` and `503` for `Unhealthy`.

```json
{
  "status": "Healthy",
  "totalDurationMs": 3.412,
  "checks": {
    "sql-server": { "status": "Healthy", "description": null, "durationMs": 3.412 }
  }
}
```

**The failure description is constant — "The database is not reachable." — for every cause.**
Those two endpoints have no authentication, and a provider exception names the server, the database,
the login and often the path to the credential. The exception is logged instead, so it is reachable
through the `X-Correlation-ID` of the probe rather than to anyone who can reach the port.

For the same reason the built-in `HealthCheckResponseWriter.WriteMinimalPlaintext` is deliberately
*not* used: it publishes `entry.Exception.Message`. The custom `ResponseWriter` in `Program.cs`
emits only the status, the description the check chose to publish, and the timings. The check's
`Data` (the exception's *type name*, never its message) is attached to the `HealthCheckResult` for
the health-check pipeline to consume, but is not serialized to the caller.

Because the app calls `UseHttpsRedirection()`, a plain-HTTP probe against a configured HTTPS port
gets a `307` rather than a verdict. Point probes at the HTTPS endpoint. (With no HTTPS port
configured — the container, which is published on `http://localhost:8080` — the redirection is a
logged no-op and `/health` answers directly.)

### Omitted fields are rejected, not defaulted

Every enum in this domain starts at a meaningful value: `PolicyType.Auto`, `PolicyStatus.Active`
and `ClaimStatus.Pending` are all `0`. A non-nullable property therefore cannot distinguish "the
caller sent the first member" from "the caller sent nothing" — both bind to the same value. So
`CreatePolicyDto.Type` and `UpdateClaimStatusDto.Status` are nullable with `[Required]`, and an
omitted field is a 400 rather than a silent default.

`PUT /api/policies/{id}` applies only the fields supplied. Omitting `status` used to reset a
cancelled policy to `Active`, because a non-nullable enum bound the omission to its zero member;
omitting `premium` used to set it to `0`. A request that supplies neither is rejected as a no-op
rather than reported as a success.

---

## Soft Deletes and Restore

`DELETE` on a policyholder or a claim hides the record and keeps the row. `PATCH .../restore`
brings it back. A restore is idempotent — restoring something that was never deleted succeeds
and changes nothing — so a client that retries is not told it made a mistake.

| Resource | Flag | On delete | On restore |
|---|---|---|---|
| Policyholders | `IsDeleted`, `DeletionDate` | Hidden from every read | Visible again, cache entry evicted |
| Claims | `IsDeleted`, `DeletionDate` | Hidden from every read, and its amount stops reserving coverage | Visible again, and its amount reserves coverage again |
| Policies | — | Status set to `Cancelled` | Not offered; a cancelled policy is a business state, not a deletion |

**Why the row survives.** The foreign key from a policy to its holder cascades, and the one from
a claim to its policy cascades in turn, so removing a holder outright would take their policies
and every claim ever filed against them with it. Keeping the row is what makes the removal
reversible, and it is why a hard delete of a holder with claim history is not available at all.

Two consequences worth knowing:

- **Deleting a holder hides the holder, not their book.** Their policies stay in the policy list
  and in the reports, still named after them; the holder is a 404 only where they are addressed
  directly. This is also why the read filter is applied per query rather than globally on the
  entity: a global filter on `PolicyHolders` would leave the holder's name null on every one of
  those policies.
- **A deleted holder's email address stays taken.** The unique index still covers the row, so
  the address cannot be registered to somebody else while it exists, and a restore can therefore
  never collide.

An **adjudicated** claim still cannot be deleted, soft or otherwise: an approved or denied claim
records who decided it, when, and why, and hiding it would take that record out of the book while
leaving the payout it settled in place. `DELETE` on one answers **409**.

---

## Project Structure

```
PolicyManager/
├── PolicyManager/
│   ├── Controllers/
│   ├── DTOs/
│   ├── Models/
│   │   └── Enums/
│   ├── Domain/            # claim lifecycle and coverage rules, as pure functions
│   ├── Services/
│   ├── Data/              # DbContext, concurrency tokens, outbox transaction
│   ├── Health/
│   ├── Configuration/     # one options class per appsettings section
│   ├── Messaging/
│   ├── Resilience/
│   ├── Telemetry/
│   ├── Middleware/
│   ├── Errors/            # global exception handler, SQL error translation
│   ├── Exceptions/
│   ├── Migrations/
│   ├── Properties/
│   ├── Dockerfile
│   ├── PolicyManager.csproj
│   └── Program.cs
├── PolicyManager.Tests/
│   ├── Controllers/
│   ├── Services/
│   ├── Domain/
│   ├── Errors/
│   ├── Middleware/
│   ├── Messaging/
│   ├── Resilience/
│   ├── Telemetry/
│   ├── Data/
│   ├── Integration/
│   └── Infrastructure/
├── client/                 # Angular client
│   ├── src/app/core/       # API client, auth, list state, problem-details handling
│   ├── src/app/features/   # one folder per resource: list, detail, reports
│   ├── src/app/shared/     # banner, pager, badge
│   └── proxy.conf.json     # dev proxy to the API, so the API needs no CORS policy
├── scripts/
│   ├── addPolicyHolders.ts
│   ├── addPolicy.ts
│   └── addClaim.ts
├── .github/
│   └── workflows/
│       └── ci.yml
├── .env.example
├── PolicyManager.sln
├── compose.yaml
└── README.md
```

The `scripts/*.ts` files are standalone seed/load scripts, not part of the .NET build and
not exercised by CI. Each one fires thousands of `POST` requests with randomly generated
bodies (`7,417` in `addPolicyHolders.ts`, `10,000` in `addPolicy.ts` and `addClaim.ts`) at
a hard-coded Azure host, using `fetch` and top-level `await`. There is no `package.json`
or `tsconfig.json` in `scripts/`, so they need a runtime that supports both.

**They will not work against the API as it now stands.** Two changes made them obsolete, and
neither has been addressed — see [Known Issues](#known-issues).

---

## Caching

Single policyholder reads (`GET /api/policyholders/{id}`) are served from an in-process
`IMemoryCache`. Keys are centralised in `PolicyManager/Models/CacheKeys.cs` rather than being
built as string literals at each call site. The cache is registered with a 10 MiB `SizeLimit`,
and **every** entry declares an explicit `Size` — once a `SizeLimit` is configured,
`MemoryCache` throws if any entry omits it. Entries are held at `CacheItemPriority.High`.

**A write generation is part of every per-holder key.** Evicting a key on write is not enough on
its own: a read that starts before the write and finishes after it has already read the old row,
and putting that row back under the key the write just evicted resurrects it for the whole cache
lifetime — an update that reports success and is then invisible, or a `DELETE` that still answers
`GET`. So the key is `policyholders:{id}:g{generation}`, and every write advances that holder's
generation in `PolicyHolderWriteGenerations` *after* its transaction commits. A late reader then
writes to a key no reader will ask for again — wasted memory rather than a stale answer — and a
reader that starts after the bump reads a generation whose commit is already visible. The counter
is a process-wide singleton and deliberately not held in `IMemoryCache`: an evicted generation
would restart at zero and make a long-dead key reachable again.

**The list endpoint is not cached, and the former `policyholders:all` entry is gone.** A
paginated read cannot use a cached full table: serving one page out of it still holds every row
in memory — the unbounded growth the `SizeLimit` exists to prevent — and a table large enough
to exceed that limit evicts the entry, turning every page into a full rebuild. A single
unkeyed entry also cannot serve more than one sort order, and the caller still needs
`totalCount` and its slice, neither of which the cache could supply without the full
materialization it existed to avoid. An indexed `ORDER BY` / `OFFSET` / `FETCH` returns a
bounded result instead.

Retiring the entry also closes a correctness hole rather than trading one for another: writes
made **outside** the service (direct `AppDbContext` use, a future bulk import) never invalidated
it, so they stayed invisible to every list reader until the entry expired.


---

## Protecting Personal Data

Policyholder email addresses are the only personal data this service holds. They get three
protections, because any one of them alone leaves the obvious gap.

```json
"Pii": {
  "AuditAccess": true,
  "BackfillOnStartup": true
}
```

The keys are **not** in `appsettings.json`. Supply `Pii__EncryptionKey` (base64, exactly 32 bytes for
AES-256 — `openssl rand -base64 32`) and `Pii__BlindIndexKey` (at least 32 bytes) from a secret
store. The process **refuses to start** if either is missing or too short. A short AES key is not
padded up to strength; accepting one would mean believing the data is protected when it is not.

### Encryption at rest

`Email` is stored as AES-256-GCM ciphertext through an EF value converter, so nothing above the
persistence layer knows the value is encrypted. GCM is *authenticated*: a tampered row fails to
decrypt rather than returning corrupted personal data, and `PiiProtectionTests` asserts that by
flipping a bit.

Ciphertext is **randomised** — a fresh nonce per write — so two holders with the same address do not
produce the same bytes. That is also why the address column cannot be indexed at all, and why
`QueryIndexTests` asserts that it is not.

### The blind index

A randomised column cannot be compared, so the unique constraint on the address has to move to
something deterministic. `EmailHash` is an **HMAC-SHA256** of the normalised address, keyed with a
key the database never sees.

HMAC rather than a plain hash: an unkeyed hash of an email address is trivially reversible by brute
force, because addresses are low-entropy and a wordlist of them is easy to obtain. That would put
every address back in the clear in a column that looks harmless. HMAC makes the index safe to store
beside the ciphertext it protects.

The index is stamped in `AppDbContext.SaveChanges`, **not** by a value converter. A converter applies
to both sides of a comparison, so a query for `EmailHash == x` would search for the hash *of* `x` and
find nothing — silently disabling the duplicate-address check. Stamping on save is also what makes it
correct on every write path, including ones that do not go through a service.

### Access control

| | Sees the address |
| --- | --- |
| `Admin`, `Adjuster` | In the clear |
| `Agent` | Masked: `j***@***.com` |
| Unauthenticated | 403 — no token at all |

Authorization already stops anonymous callers; what it does not do is narrow *reads*. An agent who
files a claim on someone's behalf has no need for their address, and returning it anyway means a
compromised agent account discloses contact details for the whole book. An adjuster contacts holders
about claims, so they keep the address.

The mask keeps the first character of the local part and the domain's public suffix, because a mask
nobody can recognise is useless in a support conversation and gets read out over the telephone anyway.
Everything that identifies a person is gone, and the length does not reveal the original.

**The mask is applied per response, never to the cached value.** The single-holder cache entry is
shared, so masking the cached copy would serve a disclosed address to an agent who is only entitled
to the masked form.

### Audit trail

`PiiAccessAudits` records every read of a holder: who, with which roles, when, from which path, under
which correlation ID, and **whether the address was disclosed or masked**. "Read the record" and
"read the contact details" are different acts, and a disclosure review needs to know which happened.

The row is written in the request's own context, so it commits with the read that caused it. Writing it
afterwards would leave a window where a successful disclosure had no record at all — which is exactly
the window an auditor would ask about.

The log **never copies the data it audits**. An access log containing the personal data is one more
store to redact rather than a control.

For the same reason the address is **absent from the `PolicyHolderCreated` outbox message**. An outbox
message is a broadcast to whatever consumes it, and its retention is not this service's to bound;
publishing the address there would copy personal data out of the one store that protects it.
Consumers that need it read the holder, which is access-controlled and audited. The message carries
`HasEmail` instead.

### Rolling this out: two phases, deliberately

The data cannot be converted in T-SQL. AES-GCM has no SQL Server equivalent, and the blind index is an
HMAC precisely because a plain hash would be reversible. So the rows have to be read and written by
the application, and the constraint has to wait for them.

**Phase 1** — `PiiProtection` migration. Adds `EmailHash` as **nullable**, widens `Email`, and creates
the audit table. Everything is additive and the old index is left alone, so this migration on its own
still behaves. `PiiBackfillService` runs on start-up and converts rows with no blind index, in
committed batches so progress is durable and the table stays usable throughout. It is idempotent and
resumable, and a converted table makes its first pass return nothing.

The duplicate-address **409 is preserved in the meantime** by an explicit check in
`PolicyHoldersService.Create` against the blind index. Without it the guarantee would silently vanish
for the length of the rollout.

**Phase 2** — applied by hand, once the logs say the backfill is complete:

```bash
dotnet ef database update PiiProtection
# watch for: "The PII backfill is complete after N passes."
dotnet ef database update EnforcePiiBlindIndex
```

`EnforcePiiBlindIndex` makes the column `NOT NULL` and the index unique. It `THROW`s if any row
still has no index, because an address left in plaintext with a NULL index is precisely the state this
feature exists to end, and quietly skipping those rows would leave them there permanently.

Applying it early either fails outright or succeeds over rows whose index was computed under a key
about to change — and the database cannot tell the difference, which is why the ordering is a
documented operator step rather than something the migration chain can enforce.

## Authentication and Authorization

Every endpoint requires a bearer token. There is no anonymous read access: a leaked identifier is
still a disclosure, and a policyholder's email address is personal data.

```http
Authorization: Bearer <jwt>
```

### Configuration

```json
"Jwt": {
  "Issuer": "policy-manager",
  "Audience": "policy-manager-api",
  "ClockSkewSeconds": 30,
  "RequireKnownRole": true
}
```

The signing key is **not** in `appsettings.json`. Supply it as `Jwt__SigningKey` (or
`Jwt:SigningKey`) from a secret store. Two things happen if you do not:

- **The process refuses to start**, with a message naming the missing settings. A deployment that
  comes up healthy while accepting unsigned tokens is worse than one that is plainly down.
- **A key shorter than 32 bytes is also refused.** HMAC-SHA256 does not fail on a short key, it
  quietly makes forged signatures cheaper — the failure nobody notices until it matters.

### Roles

| Role | May |
| --- | --- |
| `Agent` | Read everything. File claims. |
| `Adjuster` | Everything an agent may, plus create and update policies, adjudicate claims, register policyholders. |
| `Admin` | Everything, plus cancel a policy. |

Two boundaries are deliberate rather than incidental:

- **An agent may not adjudicate a claim.** The point of an adjuster being a different person is that
  whoever filed the claim does not also approve it.
- **Cancelling a policy is admin-only.** It ends cover and may have to be honoured retroactively, so
  it is a commercial decision, not an operational one.

### 401 versus 403

The two are kept distinct because the remedy is different. **401** means *prove who you are* — obtain
a token. **403** means *we know who you are and the answer is no*. A client that conflates them
either gives up or retries forever.

### Token validation

Issuer, audience, lifetime and signature are all validated. A token signed with the right key but
minted by a different service, or for a different service, is rejected — so a partner that shares the
key cannot impersonate this API.

A valid signature proves the token was minted here, not that it may do anything. A token carrying a
role this service has never heard of is refused with **403** rather than authenticating as a
role-less user, which would otherwise be refused by every policy and read as a permissions bug rather
than a configuration one.

**The claim types are left at the framework defaults.** Overriding `NameClaimType` to the raw `sub`
looks tidier and silently breaks: inbound claim mapping renames `sub` on the way in, the identity then
finds no claim of the configured type, `Identity.Name` comes back null, and every authenticated write
lands in the audit columns as `system`. A token minted by any standard library works without this
service publishing a convention that whoever issues tokens has to know about.

### Audit and authorization agree

`ICurrentUser` reads the same token that the authorization policies evaluated, so who was permitted
and who is recorded in `UpdatedBy` cannot drift apart.

### Tests

`TestTokens` mints **real** signed tokens using the key the host was configured with, and the
production validation pipeline evaluates them. A stubbed authentication scheme would keep the suite
green if the signature check, the issuer check, the audience check, the lifetime check or the role
claim mapping were all wrong — which is every part of this feature that is easy to get wrong.

`AuthorizationTests` covers the whole matrix, plus the token-forgery cases: wrong key, wrong issuer,
wrong audience, expired, and an unrecognised role.

## API Versioning

Every controller is tagged `[ApiVersion("1.0")]` and reachable at two URLs:

```
/api/policies             ← unversioned, means 1.0
/api/v1.0/policies        ← explicitly versioned
/api/policies?api-version=1.0
```

### The unversioned route is kept, not replaced

This is the important part. The versioned template is *added alongside* the existing route rather
than substituted for it, and `AssumeDefaultVersionWhenUnspecified` makes a request with no version
resolve to 1.0. A client that never sends a version keeps working, unchanged — introducing a version
is not a breaking change. Replacing `/api/policies` with `/api/v1/policies` would have been a
breaking change dressed up as a version introduction.

### Discovery

`ReportApiVersions` puts `api-supported-versions: 1.0` on every response, so a client learns what a
deployment supports from a single call rather than from documentation it has to trust to be current.

Swagger is one document per version (`/swagger/v1.0/swagger.json`), so a diff between two of them is
exactly the surface change in that version and nothing else.

### Rejecting a version that does not exist

| Request | Status | Why |
| --- | --- | --- |
| `/api/v2.0/policies` | **404** | The version segment is part of the route. No version declares it, so no route matches, and the request is indistinguishable from a path that does not exist. |
| `/api/policies?api-version=2.0` | **400** | The route matched. The resource was found and the only fault is the parameter. |
| `/api/policies?api-version=2.0` when 1.0 is the only version | **400** | Never silently downgraded. Serving 1.0 to a caller that asked for 2.0 would hand it a contract it is not expecting with no way to tell. |

### Adding a version

1. Add the constant to `ApiVersions`.
2. Add a second `[ApiVersion]` attribute to the controllers that support it, and a `SwaggerDoc` for
   it in `Program.cs`.
3. Add a second route template with a distinct prefix, or a distinct controller — do not change the
   meaning of the 1.0 template.

`ApiVersioningTests` asserts that every controller declares a version, because a controller that
forgets is not a compile error: it is a set of routes that silently stop being reachable through the
versioned template.

### Known limitation

Controllers are shared across versions, so a v2 that needs a different response shape cannot get one
without introducing versioned DTOs. That is the right trade at this size — one set of DTOs and one
set of tests — but it is a constraint, and it is the thing to revisit first when a v2 actually needs
to differ.

## Rate Limiting and Request Size Limits

Both are configured under `RateLimiting` in `appsettings.json` and can be overridden per environment
without a rebuild — the right quota for a staging load test is rarely the right one in production.

```json
"RateLimiting": {
  "Enabled": true,
  "PermitLimit": 100,
  "Window": "00:01:00",
  "Cooldown": "00:00:10",
  "QueueLimit": 20,
  "MaxRequestBodySizeBytes": 65536,
  "RejectionStatusCode": 429
}
```

### What a caller is limited against

The correlation ID, falling back to the remote address. A limit keyed on the address punishes everyone
behind a shared egress — a corporate NAT, a mobile carrier, a CI runner — for one caller's
misbehaviour, and gives an attacker a single address to rotate. The correlation ID is already echoed
on every response and is already sanitised to log-safe characters.

It is **not** an authenticated identity. A caller can mint a fresh correlation ID per request and get
a fresh quota, which is why the address stays as the fallback: the limiter degrades to per-connection
rather than to per-nobody. Keying on the authenticated subject is the obvious next step and is a
one-line change in `RateLimiting.PartitionKey`.

### Rejection

A caller over quota gets **429** with `Retry-After` set to the cooldown. A bare 429 tells a client
only that it was too fast, not for how long to slow down, so it either retries immediately — turning a
burst into sustained load — or gives up.

The cooldown is clamped to the window, so a caller that waits exactly as long as `Retry-After` says is
always let through. A 429 is deliberately **not** a ProblemDetails body: it is a statement about the
caller's pacing, not a failure of this request.

`QueueLimit` is non-zero so a brief burst is smoothed rather than rejected, and queued callers still
get a 429 once the queue fills, so a genuine flood is still shed — just not punished for arriving a
few hundred milliseconds early.

### Request size

Two limits, on purpose:

- **Kestrel's `MaxRequestBodySize`** is the real defence. It stops reading while the payload is still
  arriving, so the bytes never accumulate in memory.
- **`RequestSizeLimitMiddleware`** runs in front of it to turn the same condition into the same
  RFC 9457 ProblemDetails, carrying the correlation ID, rather than Kestrel's bare connection reset.
  It also runs identically under `TestServer`, where no Kestrel limit applies and the behaviour would
  otherwise be untestable.

The middleware checks `Content-Length`, so it is a fast rejection rather than a defence: a chunked
request that declares no length passes it and is caught by the server limit. That division is
deliberate — measuring a stream to discover it is too long means reading all of it, which is the
thing being prevented.

64 KiB is generous by two orders of magnitude: the largest payload any endpoint here can legitimately
produce is a claim description, and that is capped at 1000 characters.

### Pipeline position

```csharp
app.UseMiddleware<CorrelationIdMiddleware>();   // supplies the partition key
app.UseMiddleware<RequestSizeLimitMiddleware>();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRouting();                               // so per-endpoint overrides are honoured
app.UseRateLimiter();                           // before authentication
app.UseAuthorization();
```

`UseRateLimiter` sits **before** `UseAuthorization` on purpose. A JWT is a signed blob that costs
real CPU to check; verifying one per rejected request would let an unauthenticated caller spend the
very CPU the limiter exists to protect.

When `Enabled` is false the middleware still runs, with a no-op limiter, so the pipeline shape and
these ordering constraints hold identically whether or not the feature is on.

### A note on reading the configuration

The runtime limits are read from `IOptions<RateLimitOptions>` through the request's own services, not
captured into a closure at startup. `IOptions<T>` resolves its section lazily against the *finished*
configuration, so a source added after `Program.cs` read the section still applies. Reading eagerly
instead silently applies the built-in defaults and the limit appears to do nothing at all — which is
how this was found.

## Indexes

Indexes are shaped by the queries the application actually issues, not by which columns happen to
look filterable. Two rules run through all of it:

- **Every list endpoint appends `Id` to its `ORDER BY` as a tie-breaker**, so a page number always
  identifies the same rows. A single-column index cannot supply that tie-breaker, so the index has to
  include it or the engine sorts the candidates before it can page them.
- **A filtered index is better than a general one whenever a column has a value the queries never
  read.** Denied claims and delivered outbox messages are the majority of their tables over time and
  no hot query touches them; excluding them keeps the structure from growing with history.

| Index | Serves |
| --- | --- |
| `IX_Policies_PolicyNumber` (unique) | Business-number lookup, and the duplicate-number 409 |
| `IX_PolicyHolder_Email` (unique) | Duplicate-email 409, and email ordering |
| `IX_Policies_Status_Id` | The only filter the policies list supports, plus the `Id` tie-breaker |
| `IX_Policies_PolicyHolderId_Id` | Holder-scoped listing and the `policyHolderId` sort |
| `IX_PolicyHolders_LastName_Id` | The name ordering holders are actually listed by |
| `IX_Claims_Coverage` — filtered `[Status] <> 2` | The coverage sum on every claim create |
| `IX_Claims_FiledAt_Id` | The recency listing, newest first |
| `IX_OutboxMessages_Pending` — filtered on the two unprocessed predicates | The dispatcher's poll, keyed on the `CreatedAt` it orders by |
| `IX_OutboxMessages_Claimed` — filtered `[LockToken] IS NOT NULL` | The dispatcher's read-back of the rows it just won |

`IX_Claims_Coverage` is the one that matters most. Every claim is created after summing what its
policy has already paid out, so it is the hottest read in the system, and `Amount` is in the index so
the sum needs no row lookups at all.

### What was removed, and why

Four indexes were dropped because a composite or filtered index now serves the same query more
narrowly, while costing a write on every insert:

- `IX_Policies_Status` and `IX_Policies_PolicyHolderId` — subsumed by the composites. Their
  declarations were also removed from the model, so EF's foreign-key convention recognises the
  composite as covering the relationship and does not recreate them.
- `IX_Claims_PolicyId` — subsumed by `IX_Claims_Coverage`, which leads on the same column.
- `IX_OutboxMessages_ProcessedAt` — the poll always requires `ProcessedAt IS NULL`, which the
  filtered index already guarantees, so a general index on the column served nothing.

`IX_OutboxMessages_Pending` was also **re-keyed** from `(ProcessedAt, NextAttemptAt)` to
`(CreatedAt)`. Within a filtered index `ProcessedAt` is constant, so it contributed no ordering at
all: the dispatcher asks for `ORDER BY CreatedAt` and the engine had to sort the live rows before it
could take a batch. The two "due and unclaimed" predicates are deliberately *not* keys — they are
OR-ed against `NULL`, which is not sargable, so they are applied as a residual filter over the live
rows instead.

`IX_OutboxMessages_Claimed` is new and fixes a genuine gap: `LockToken` was not indexed at all, so
every dispatch pass that claimed anything finished by scanning the whole outbox table to collect the
rows it had just won.

### Migration ordering

`QueryIndexes` creates the replacements before dropping the originals, so a query running while the
migration is in flight never finds itself with no supporting index.

The filtered predicates are written with the enum's numeric value (`[Status] <> 2`), not its member
name. Interpolating `ClaimStatus.Denied` produces `[Status] <> Denied`, which SQL Server cannot
resolve — and the failure surfaces only when the migration runs against a real server, long after the
mistake. `QueryIndexTests` asserts the predicate literally to keep that mistake from coming back.

## Business Numbers

Policy and claim numbers are sequential and human-readable rather than GUIDs:

```
POL-2026-000001
CLM-2026-000042
```

Nobody can quote a GUID down a telephone, spot a transposition in it, or tell from it which of two
policies was issued first. The year makes an issue traceable to a period without a lookup, and
numbering restarts each year so the number stays short enough to read out and write on a form.

### How they are allocated

`BusinessNumberSequence` is a counter row per (kind, year). Allocation is a compare-and-swap on the
counter's `RowVersion`: read the row, add one, write it with the version that was read. If another
instance got there first the write matches no rows, raises `DbUpdateConcurrencyException`, and the
number is retried — contention, not failure, so it is retried rather than surfaced.

Deriving "the highest number issued so far" from the policy rows themselves would need a read
followed by a write, and two concurrent creates would both read the same maximum and produce the same
number. The unique index on `PolicyNumber`/`ClaimNumber` would then turn a race into a spurious
**409**, which is a far worse outcome than the number having a gap in it.

Numbers therefore contain gaps whenever a reservation is not followed by a successful create. That
is the deliberate trade: a number only has to be unique and increasing within its year, never dense.

The reservation is *not* rolled back with a failed create, and is not part of the outbox transaction
— it is taken in the request's own unit of work, before the entity insert. It is committed first so
the number lands in the same write as the row it identifies.

### Migration

`BusinessNumberSequences` creates the counter table. Existing rows keep the GUID numbers they already
have: the new format applies to policies and claims created from the migration onwards. Rewriting
existing numbers would break references held by customers and printed on paper, so it is not done.

## Auditing and Optimistic Concurrency

Every `PolicyHolder`, `Policy` and `Claim` row carries `CreatedAt`, `CreatedBy`, `UpdatedAt`,
`UpdatedBy` and a `RowVersion` concurrency token.

### Who made a change

The audit columns are stamped by `AppDbContext.SaveChanges` for every added or modified entity, not
by the services. A new write path is therefore audited by default instead of only if its author
remembered to pass an actor down through the call chain. The actor comes from `ICurrentUser`, which
reads the ambient `ClaimsPrincipal`; with nothing authenticated it records the fixed token `system`,
the same value a background worker such as the outbox processor produces.

`UpdatedAt`/`UpdatedBy` are marked `IsModified` explicitly. Without that, re-saving a row with an
unchanged value produces no EF change for those properties, and the timestamp of the last real edit
would be silently dropped.

### Losing updates

`RowVersion` is a SQL Server `rowversion` column, so the database maintains it and a client can only
ever echo back a value the database issued. EF appends it to the `WHERE` clause of every update; if
the row moved on in the meantime, zero rows are affected and the write is rejected.

The token is returned as a base64 `rowVersion` field on every read of a policy, claim or policyholder:

```http
GET /api/policies/1
{ "id": 1, "policyNumber": "POL-2026-000001", "premium": 500.00, "rowVersion": "AAAAAAAAB9E=" }

PUT /api/policies/1
{ "premium": 750.00, "rowVersion": "AAAAAAAAB9E=" }
```

A mismatched token is reported as **409 Conflict** with `"rule": "stale-row-version"`; the record is
left untouched. The token is optional on every write — omitting it writes unconditionally, which
keeps older clients working — and a value that is not valid base64 is rejected rather than
silently dropped, because a caller who believes they have concurrency protection and does not is
worse off than one who was never offered it.

Supported on `PUT /api/policies/{id}`, `DELETE /api/policies/{id}` (as a `?rowVersion=` query
parameter, since a DELETE carries no body) and `PATCH /api/claims/{id}/status`.

### Migration

`PolicyAuditAndConcurrency` adds the columns and backfills them. `Policies.CreatedAt` and
`Claims.CreatedAt` are added nullable, backfilled from `StartDate` and `FiledAt` respectively, then
tightened to `NOT NULL` — adding them straight away as `NOT NULL` would stamp every pre-existing row
with the CLR default of `0001-01-01`, which reads as a real date in an audit report.

## Errors

Every failure leaves the API as [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) ProblemDetails
with `Content-Type: application/problem+json`, and every response carries an
`X-Correlation-ID` header.

### Correlation IDs

`CorrelationIdMiddleware` assigns each request an identifier, taken from an inbound
`X-Correlation-ID` when the caller supplies one and minted from the connection's trace identifier
otherwise, so a trace started upstream survives the hop into this service. The identifier is pushed
into the logging scope, so every log line for that request carries it without any call site passing
it along.

Two details are deliberate:

- **The header is registered through `OnStarting`, not assigned eagerly.** The exception handler
  calls `Response.Clear()` before writing its body, and that wipes the header collection — so setting
  it eagerly loses it on precisely the responses that matter. The ProblemDetails body still carries
  the ID either way, which is what makes this easy to miss.
- **The inbound value is capped at 128 characters and restricted to characters safe in a log.** It is
  attacker-controlled and reaches the log for every line of the request. An unusable value is
  discarded in favour of the trace identifier rather than sanitised into something that looks
  legitimate but does not match what the caller sent.

### Status mapping

`GlobalExceptionHandler` maps each exception to the status that actually describes it, so a caller is
never told "server error" for something they can fix:

| Exception | Status |
|---|---|
| `NotFoundException` | 404 |
| `ConflictException` | 409 |
| `BusinessRuleException` | 422, with the rule id in the body |
| `BadHttpRequestException` | the status the framework assigned |
| `DbUpdateException` — unique index/constraint | 409, naming the constraint |
| `DbUpdateException` — foreign key | 409, naming the constraint |
| `DbUpdateException` — value too large for its column | 400 |
| `DbUpdateException` — anything else | 500 |
| `OperationCanceledException` (server-side) | 499 |
| anything else | 500 |

Database faults are translated rather than passed through, because EF Core wraps provider exceptions
in an opaque `DbUpdateException`. Without that, a duplicate email is an indistinguishable 500.

An unrecognised `DbUpdateException` is deliberately a 500 rather than a 409: a deadlock, a command
timeout or a dropped connection is not something the caller caused, and reporting it as a conflict
would keep it at Warning severity where it never trips the alerting a real server fault should.

**Exception text is never echoed to the caller for server faults.** It routinely carries column
names, values and SQL fragments. It stays in the logs, reachable by correlation ID, and the body
tells the caller to quote that ID when reporting the problem.

A request aborted by the client is logged and dropped without writing a response — there is nobody
left to read it, and writing to an aborted response body throws.

The three domain exception types (`NotFoundException`, `ConflictException`, `BusinessRuleException`)
are the vocabulary for these failures, and **the services do throw them.** A write against an entity
that does not exist raises `NotFoundException` rather than returning `false` for the controller to
translate, so every write reports a missing entity the same way instead of each controller deciding
separately. Previously the services returned `null` or completed silently, and `PUT`, `DELETE` and
`PATCH` answered **200 for a resource that was never there** — contradicting their own documented 404
and leaving a client unable to tell a successful update from a no-op.

A duplicate email is a 409 both ways: the unique index and its error translation catch the race
between the check and the insert, and `PolicyHoldersService` checks the blind index explicitly so the
guarantee does not depend on a database error surfacing at all.

---

## Transactional Outbox

Policy creation, policy updates and cancellation, claim filing and claim adjudication each
write a row to an `OutboxMessages` table in the **same database transaction** as the entity
change. `OutboxProcessorBackgroundService` polls that table and dispatches anything not yet
processed, so a domain change and the notification it produces can never diverge: either both
rows commit or neither does.

The write path is centralised in `PolicyManager/Data/OutboxTransaction.cs`:

- `AddWithOutboxAsync` stages a new entity, and `SaveWithOutboxAsync` flushes changes to an
  already tracked one.
- Both open an explicit transaction, `SaveChangesAsync` first so the database assigns the
  generated identifier, **then** serialise the payload, write the message, and commit.

The ordering matters. Serialising before the insert produces a message whose `Id` is always
`0`, because the key is only assigned by the database on insert. Flushing first and
serialising second is what makes the published payload carry a real identifier, and the
explicit transaction is what preserves atomicity across the two writes.

Messages carry a `Type` discriminator (`PolicyCreated`, `PolicyUpdated`, `PolicyCancelled`,
`PolicyHolderCreated`, `ClaimCreated`, `ClaimStatusUpdated`) and a JSON `Content` payload.

### Delivery

`OutboxDispatcher` claims a batch, publishes it, and records the outcome of each message. The
claim is a read narrowed to plausible rows followed by a **conditional** `UPDATE` that only
touches rows no other processor currently holds, so when the API is scaled horizontally each
message is claimed by exactly one instance. The claim is a lease (`LockedUntil`), not a
permanent lock, so an instance that dies mid-batch releases its work instead of stranding it.

Failures are retried on an exponential backoff (`BaseRetryDelay * 2^(attempt-1)`, capped at
`MaxRetryDelay`) recorded in `NextAttemptAt`, and `AttemptCount`/`Error` track the history — the
`Error` column was previously written but never acted on, so a failing message was retried
forever at a fixed five-second cadence. After `MaxAttempts` the message is **dead-lettered**:
`DeadLetteredAt` is stamped and the row leaves the poll set. That is deliberately distinct from
`ProcessedAt`, because a dead-lettered message was never delivered and treating it as processed
would make the table look drained when it is not.

The poll interval adapts to the backlog — `BusyDelay` after a pass that delivered something,
exponential backoff up to `MaxIdleDelay` while idle — so a busy outbox drains promptly without an
idle instance querying the table several times a second.

Everything above is configured under the `Outbox` section of `appsettings.json`:
`BatchSize`, `LockDuration`, `BusyDelay`, `MinIdleDelay`, `MaxIdleDelay`, `BaseRetryDelay`,
`MaxRetryDelay`, `MaxAttempts`.

`IOutboxPublisher` is the transport seam. The registered `LoggingOutboxPublisher` records each
message and returns success — with no broker configured the outbox still drains correctly, but
**the messages are discarded**. See [Publishing to a real broker](#publishing-to-a-real-broker)
below for what replaces it.

### Publishing to a real broker

The transport is [MassTransit](https://masstransit.io) over AMQP, and it is **opt-in**. With
`Broker:Enabled` false — the default — nothing in the composition root starts a bus, no broker is
contacted, and the logging stand-in stays registered. That is a deliberate constraint, not a
convenience: making the transport mandatory would mean `dotnet test` needed a running broker, and
CI would grow a service dependency for a path none of the tests are about.

The claiming, retry and dead-lettering policy in `OutboxDispatcher` is **unchanged**. The
dispatcher only ever sees the `IOutboxPublisher` contract — publish, or throw — so swapping the
implementation is the whole of the change. A transport failure still surfaces as an exception and
the existing policy decides between a scheduled retry and a dead letter.

Enabling it publishes an `OutboxEnvelope` — a wire contract carrying the outbox row's identifier,
its `Type` discriminator, its JSON `Content` payload, when it was created and how many attempts had
already failed. The row identifier is reused as the broker message identifier, so a redelivery is
recognisable as the same message rather than a new one, and the discriminator travels as a header
so a consumer can filter on it without deserialising the payload. The queue named by
`Broker:QueueName` is declared and bound to the publish exchange at bus start, because nothing in
this application consumes these messages and an unbound queue would mean they reached an exchange
and were thrown away.

| Key | Default | Purpose |
|---|---|---|
| `Broker:Enabled` | `false` | Registers the MassTransit publisher and starts a RabbitMQ bus. `false` keeps the logging stand-in. |
| `Broker:Host` | `localhost` | Broker hostname. `rabbitmq` when the API runs inside the Compose network. |
| `Broker:Port` | `5672` | AMQP port. `5671` for AMQP over TLS. |
| `Broker:VirtualHost` | `/` | Virtual host the bus connects to. |
| `Broker:Username` | `guest` | Broker username. |
| `Broker:Password` | `guest` | Broker password. Supply this from a secret store or `Broker__Password` rather than committing it. |
| `Broker:UseSsl` | `false` | Negotiate TLS with the broker. |
| `Broker:QueueName` | `policy-manager.outbox` | The durable queue outbox messages are published to. |
| `Broker:Durable` | `true` | Survive a broker restart. `false` would lose exactly the messages the outbox exists to guarantee. |
| `Broker:RequestedHeartbeat` | `00:00:30` | AMQP heartbeat, so a silently dropped connection is detected rather than waited out. |
| `Broker:RequestedConnectionTimeout` | `00:00:10` | How long to wait for the initial connection before giving up. Also bounds the broker health check. |
| `Broker:PublishTimeout` | `00:00:30` | Deadline for a single publish, so it cannot outlive the outbox claim lease and be picked up by a second processor. |

Override any of them with the `__` environment form, as `compose.yaml` does:

```bash
Broker__Enabled=true
Broker__Host=localhost
Broker__Port=5672
Broker__Username=guest
Broker__Password=guest
Broker__QueueName=policy-manager.outbox
```

A broker that is enabled but not usable — no host, no queue, a port outside 1–65535, a zero publish
timeout — **fails at startup** with a message naming the offending key, rather than coming up
healthy and silently discarding events.

With the broker enabled, a `message-broker` health check is registered and tagged `ready`, so
`/health/ready` reports the broker's state while `/health` still reports only that the process is
up. It counts a `Degraded` bus as a failure, so a broker that is still starting takes the instance
out of rotation instead of admitting requests that would only fail.

`compose.yaml` includes a `rabbitmq` service (`rabbitmq:3.13-management-alpine`, AMQP on `5672`
and the management UI on <http://localhost:15672>). It is started by `docker compose up -d` but the
API does not depend on it: the `Broker__*` variables default to `Broker__Enabled=false`, so the
local path is unchanged. To publish from the containerised API, set `BROKER_ENABLED=true` in `.env`.

`PolicyManager.Tests/Messaging/` covers the wiring without a broker: that the disabled default
registers the stand-in and no bus, that enabling replaces it, that configuration binds either way,
that an invalid configuration is rejected at registration, and — against MassTransit's in-memory
harness — that the outbox row reaches the publish pipeline with its identifier and header intact
and that a transport failure propagates rather than being swallowed.

The publish itself runs inside a Polly pipeline, described under
[Resilience](#resilience-circuit-breakers-and-retry).

---

## Observability

### Telemetry

The API is instrumented with [OpenTelemetry](https://opentelemetry.io): ASP.NET Core for incoming
requests, Entity Framework Core for database commands, `HttpClient` for outbound calls, the runtime
for process metrics, and MassTransit's activity source for broker publishes. Traces and metrics are
collected and tagged with the correlation ID that `CorrelationIdMiddleware` already puts on the
current activity, so a support conversation that starts with "here is my request ID" ends at a trace
rather than at a log search.

**Nothing is exported unless you configure a collector.** Collection is on by default; export is not.
That separation is deliberate — it is what makes turning on OTLP a configuration change rather than a
code change — and it is also why the default local-dev and CI paths ship no telemetry anywhere,
without anyone having to remember to switch it off.

| Key | Default | Purpose |
|---|---|---|
| `Telemetry:ServiceName` | `policy-manager-api` | The `service.name` resource attribute every span and metric is attributed to. |
| `Telemetry:ServiceVersion` | `1.0.0` | The `service.version` resource attribute. |
| `Telemetry:Environment` | host environment | `deployment.environment`. Falls back to `ASPNETCORE_ENVIRONMENT`. |
| `Telemetry:EnableTracing` | `true` | Collect distributed traces. |
| `Telemetry:EnableMetrics` | `true` | Collect metrics. |
| `Telemetry:OtlpEndpoint` | *(unset)* | Collector endpoint, e.g. `http://localhost:4317`. **Unset means nothing is exported.** |
| `Telemetry:OtlpProtocol` | `grpc` | `grpc` or `http/protobuf`. |
| `Telemetry:TraceSampleRatio` | `1.0` | Head-sampling ratio. The decision is made at the root of a trace and carried on the trace, so a downstream service does not decide differently and produce a trace with a hole in the middle. |

Override with the `__` environment form, as with every other section:

```bash
Telemetry__OtlpEndpoint=http://localhost:4317
Telemetry__ServiceName=policy-manager-api-canary
Telemetry__TraceSampleRatio=0.1
```

Pointing at a collector with [docker compose](https://github.com/open-telemetry/opentelemetry-collector) locally
and then restarting is the whole of the setup. An unrecognised `OtlpProtocol`, a sample ratio outside
0..1, or an empty `ServiceName` all fail at startup rather than being silently corrected — a sampler
quietly clamped to a range would quietly change how much telemetry a deployment produces.

EF Core **query parameters are not recorded**. A parameter can carry a policyholder's email, and a
trace backend is a far wider audience than the database the row lives in. The statement text is
recorded, which is enough to see which query is slow.

### Resilience: circuit breakers and retry

Every outbound dependency gets a [Polly](https://www.nuget.org/packages/Polly) pipeline built from
the `Resilience` section: an **exponential-backoff retry**, a **circuit breaker**, and a **timeout**.
Three dependencies, three independent budgets, because a database and a broker fail for different
reasons on different timescales and treating them the same would be wrong in both directions.

The strategies are nested breaker → retry → timeout, in that order. The breaker is outermost so a
dependency that is down is rejected immediately rather than after the caller has sat through the
backoff chain; the timeout is innermost so a single hung attempt is bounded and a timeout counts as
a retryable failure rather than stalling the whole chain.

`MaxRetryAttempts: 0` means the retry strategy is **omitted**, not configured with zero attempts —
Polly treats a retry budget of zero as a misconfiguration, so a dependency that should fail fast has
no retry in its pipeline at all. Cancellations are never retried: a cancelled token is the host
shutting down or the caller giving up, and replaying it spends the budget and then throws anyway.

| Key | Default | Purpose |
|---|---|---|
| `MaxRetryAttempts` | `2` (`Database`: 3) | Retries after the first attempt. `0` disables the retry strategy. |
| `Delay` | `200ms` | First backoff. Each retry doubles it. |
| `MaxDelay` | `2s` (database/broker: `5s`) | Ceiling for the doubling. |
| `UseJitter` | `true` | Without it every caller that failed at the same instant retries at the same instant, which is how a recovering dependency gets knocked over again. |
| `BreakDuration` | `10s` (database: `15s`, broker: `30s`) | How long the circuit stays open. |
| `FailureRatio` | `0.5` | Share of failures over the sampling window at which the circuit opens. |
| `MinimumThroughput` | `10` (database/broker: `5`) | Calls required in the window before the ratio counts at all. A floor of 1 would open the circuit on a single unlucky request. |
| `Timeout` | `10s` (database: `5s`, broker: `30s`) | Deadline for a single attempt. |

A budget that would silently disable the protection it describes — a zero timeout, a `MaxDelay` below
its own `Delay`, a `MinimumThroughput` below 2, a `FailureRatio` outside 0..1 — is rejected at
startup naming the offending key.

#### Where the pipelines are applied

- **Broker.** The outbox publish runs inside the broker pipeline. It absorbs a blip within one
  attempt while the dispatcher's own policy handles the long game across passes, eventually
  dead-lettering; an open circuit rejects immediately, so a broker that has been down for a while is
  not asked again until it has had a chance to recover. The two compose rather than duplicate.
- **Database.** The SQL Server readiness probe. It is the one database call made on a timer, and the
  one most likely to be repeated while the database is down — an orchestrator polls readiness every
  few seconds precisely when things are worst. Without a breaker, each poll pays the full timeout,
  and with a retry budget underneath it pays that several times over, so the check meant to detect an
  outage becomes a contributor to it.
- **HTTP.** The `outbound` named client, via `ResilientHttpMessageHandler` in its handler stack. A
  handler rather than a call at each site, so a new outbound call is protected the moment it is
  written — a policy applied by hand is a policy the next contributor forgets.

#### Why `EnableRetryOnFailure` is deliberately off

EF Core's retrying execution strategy refuses user-initiated transactions, and the outbox write path
in `Data/OutboxTransaction.cs` opens one. Turning it on would turn every create and update into
`The configured execution strategy does not support user-initiated transactions`. Making it work
would mean re-entrancy work in `OutboxTransaction` — a replayed `SaveChangesAsync` on a
change tracker that has already marked the entity `Unchanged` would insert nothing and write an
outbox message with `Id = 0` — which is a change to the outbox's correctness story, not a resilience
setting. Database retry therefore stays where it can be applied safely. Do not add
`EnableRetryOnFailure` without that work.

`PolicyManager.Tests/Resilience/` and `PolicyManager.Tests/Telemetry/` cover the behaviour from the
outside: that a transient failure is retried and eventually succeeds, that the retry budget is
exponential, capped and jittered, that retries actually wait, that a cancellation is not replayed,
that the circuit opens and stops calling through, that a single failure does not open it, that the
readiness probe is short-circuited by it, and that nothing is exported without a collector endpoint.

The retry and breaker *shapes* are asserted against `ResilienceRegistration.BuildRetry` and
`BuildCircuitBreaker` rather than inferred from elapsed time. A "the second wait was longer than the
first" assertion measures the machine as much as the policy, and fails on a busy one.
