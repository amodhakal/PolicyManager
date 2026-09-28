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
| `GET` | `/api/policyholders` | List all policyholders |
| `POST` | `/api/policyholders` | Create a policyholder |
| `GET` | `/api/policyholders/{id}` | Get by ID |

### Policies
| Method | Route | Description |
|---|---|---|
| `GET` | `/api/policies` | List all; supports `?status=Active` filter |
| `POST` | `/api/policies` | Create a policy linked to a policyholder |
| `GET` | `/api/policies/{id}` | Get with policyholder info |
| `PUT` | `/api/policies/{id}` | Update status or premium |
| `DELETE` | `/api/policies/{id}` | Soft delete, sets status to `Cancelled` |

### Claims
| Method | Route | Description |
|---|---|---|
| `GET` | `/api/claims` | List all claims |
| `POST` | `/api/claims` | File a claim against a policy |
| `GET` | `/api/claims/{id}` | Get claim details |
| `PATCH` | `/api/claims/{id}/status` | Adjudicate, approve or deny the claim |

Route casing follows ASP.NET Core's default: `[Route("api/[controller]")]` resolves
`PolicyHolders` to `/api/policyholders`, `Policies` to `/api/policies`, and `Claims` to
`/api/claims`. All `{id}` segments are constrained to integers in code
(`[HttpGet("{id:int}")]`).

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

Policyholder reads are served from an in-process `IMemoryCache`. Keys are centralised in
`PolicyManager/Models/CacheKeys.cs` rather than being built as string literals at each call
site. The cache is registered with a 10 MiB `SizeLimit`, and **every** entry declares an
explicit `Size` — once a `SizeLimit` is configured, `MemoryCache` throws if any entry omits
it. Priorities are set so the large "all policyholders" collection is evicted before hot
single-holder entries, and `Create` invalidates both keys explicitly.

Be aware that the collection entry is a cache-invalidation seam: writes made **outside** the
service (direct `AppDbContext` use, a future bulk import) do not invalidate it and will be
masked until the entry expires.


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

Messages currently carry a `Type` discriminator (`PolicyCreated`, `PolicyUpdated`,
`PolicyCancelled`, `PolicyHolderCreated`, `ClaimCreated`, `ClaimStatusUpdated`) and a JSON
`Content` payload. The processor marks each row with `ProcessedAt` when it drains it.
