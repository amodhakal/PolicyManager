# PolicyManager

[![CI](https://github.com/amodhakal/PolicyManager/actions/workflows/ci.yml/badge.svg)](https://github.com/amodhakal/PolicyManager/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/amodhakal/PolicyManager/branch/main/graph/badge.svg?token=UPG0QIKCI6)](https://codecov.io/gh/amodhakal/PolicyManager)

REST API for managing insurance policies, policyholders, and claims.

---

## What It Is

PolicyManager is an ASP.NET Core Web API that models core insurance operations: creating and managing policyholders, issuing and updating policies, filing claims, and adjudicating those claims (approve/deny)

All data is persisted to SQL Server via Entity Framework Core with migration-based schema management. Includes caching for performance.

---

## Stack

| Technology              | Role |
|-------------------------|---|
| ASP.NET Core            | Web API framework |
| Entity Framework Core   | ORM with code-first migrations |
| SQL Server (Docker)     | Primary data store |
| OpenAPI / Swagger       | API documentation and contract |
| GitHub Actions          | CI pipeline (build, test on every push to main) |
| Docker / Docker Compose | Containerized local environment |
| xUnit                   | Unit testing (service and controller layers) |


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

`DB_PASSWORD` must satisfy the SQL Server image's password policy: **at least 8 characters
and at least three of** uppercase, lowercase, digits, and symbols. The `xxxxxxx` placeholder
in `.env.example` is deliberately invalid and will crash-loop the database container.

---

## Known Issues

- **The database is never created for you.** `compose.yaml` provisions the SQL Server
  *instance* but no `PolicyManager` database. `dotnet ef database update` and the container
  entrypoint both connect with `Database=PolicyManager`, so that database must exist first.

---

## Tests

Run the full test suite from the repository root (a `PolicyManager.sln` is present):

```bash
dotnet test
```

The suite is xUnit, with Moq for mocking the service interfaces and the EF Core InMemory
provider for `AppDbContext`, so the default run needs no SQL Server instance. Coverage is
collected via coverlet.

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
`InMemoryApiFactory` swaps the SQL Server `AppDbContext` for the InMemory provider,
`SqlServerApiFactory` repoints the application at the container and stops the outbox poller so it
cannot drain rows a test is asserting on, `ApiIntegrationTestBase` provides the client, lifecycle
and holder/policy/claim seeding helpers, `ServiceTestBase` provides a per-test `DbContext` and
`Dispose` for the service tests, and `SqlServerFixture`/`SqlServerTestBase` own the container.
Add new tests by deriving from those rather than repeating the setup.

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
| `DELETE` | `/api/policyholders/{id}` | Delete a holder who owns no policies; a holder with policies is a 409 |

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

### Health
| Method | Route | Description |
|---|---|---|
| `GET` | `/health` | Liveness. Runs every registered check |
| `GET` | `/health/ready` | Readiness. Runs only the checks tagged `ready` (currently `sql-server`) |

Route casing follows ASP.NET Core's default: `[Route("api/[controller]")]` resolves
`PolicyHolders` to `/api/policyholders`, `Policies` to `/api/policies`, and `Claims` to
`/api/claims`. All `{id}` segments are constrained to integers in code
(`[HttpGet("{id:int}")]`).

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

There is one check today, `sql-server` (`PolicyManager/Health/SqlServerHealthCheck.cs`), tagged
`ready`. It opens a connection through the injected `AppDbContext` and runs `SELECT 1` — the same
connection string, provider and credentials the application itself uses, so a check that passed
cannot coexist with a failure on every real request. It probes with a query rather than
`CanConnectAsync` alone, because a connection validated when the process started is routinely dead
by the time a probe first asks. A five-second per-check timeout bounds how long a hung connection
can hold a probe open.

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
That endpoint has no authentication, and a provider exception names the server, the database, the
login and often the path to the credential. The exception is logged instead, so it is reachable
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

## Project Structure

```
PolicyManager/
├── PolicyManager/
│   ├── Controllers/
│   ├── DTOs/
│   ├── Models/
│   │   └── Enums/
│   ├── Services/
│   ├── Data/
│   ├── Health/
│   ├── Migrations/
│   ├── Properties/
│   ├── Dockerfile
│   ├── PolicyManager.csproj
│   └── Program.cs
├── PolicyManager.Tests/
│   ├── Controllers/
│   ├── Services/
│   └── Infrastructure/
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
are the vocabulary for these failures and are mapped above, but **the services do not throw them
yet** — they return null or complete silently and the controllers choose the status. A duplicate
email already returns 409 today, via the unique-index mapping rather than an explicit check.

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
message and returns success — there is no broker on the other end yet, so **messages are drained
and discarded**. Replacing it with a real transport means registering a different
`IOutboxPublisher`; the claiming, retry and dead-lettering policy is unaffected.
