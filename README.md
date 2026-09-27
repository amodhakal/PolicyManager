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
nothing creates the tables for you — the API will fail to serve any request that touches
the database until the migrations have been applied. Step 2 above is the command to run,
from the repository root. It applies every pending migration in
`PolicyManager/Migrations/` to the database named by `DB_NAME` in your `.env` file.

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

The connection string is built in `Program.cs` from these four variables only; there is no
`ConnectionStrings__*` entry in `appsettings.json` and no other environment variable is
consumed by the app.

---

## Tests

Run the full test suite from the repository root (a `PolicyManager.sln` is present):

```bash
dotnet test
```

The suite is xUnit, with Moq for mocking the service interfaces and the EF Core InMemory
provider for `AppDbContext`, so it needs no SQL Server instance. Coverage is collected via
coverlet. CI runs `dotnet test` on every push to `main` and on every pull request targeting
`main`, excluding `Migrations/**` from coverage, and uploads the report to Codecov.

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
│   └── Services/
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
