# PolicyManager

[![CI](https://github.com/amodhakal/PolicyManager/actions/workflows/ci.yml/badge.svg)](https://github.com/amodhakal/PolicyManager/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/amodhakal/PolicyManager/branch/main/graph/badge.svg?token=UPG0QIKCI6)](https://codecov.io/gh/amodhakal/PolicyManager)

REST API for managing insurance policies, policyholders, and claims.

---

## What It Is

PolicyManager is an ASP.NET Core Web API that models core insurance operations: creating and managing policyholders, issuing and updating policies, filing claims, and adjudicating those claims (approve/deny)

All data is persisted to SQL Server via Entity Framework Core with migration-based schema management.

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
| xUnit                   | Unit testing (service layer) |


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

Swagger UI available at: `https://localhost:{port}/swagger`

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
| `POST` | `/api/claims` | File a claim against a policy |
| `GET` | `/api/claims/{id}` | Get claim details |
| `PATCH` | `/api/claims/{id}/status` | Adjudicate, approve or deny the claim |

---

## Project Structure

```
PolicyManager/
├── PolicyManager/
│   ├── Controllers/
│   ├── DTOs/
│   ├── Models/
│   ├── Services/
│   ├── Data/
│   ├── Migrations/
│   ├── Properties/
│   └── Program.cs
├── PolicyManager.Tests/
│   └── Services/
├── .github/
│   └── workflows/
│       └── ci.yml
├── compose.yaml
└── README.md
```