# VA CMS — USWDS-Compliant Content Management System

A modern, self-hosted content management system built on the **U.S. Web Design System (USWDS)** for VA and federal agency web properties. Built with **React** (frontend) and **Microsoft SQL Server** (backend), designed to replace SharePoint 2016 on-premises deployments.

## Why This Exists

SharePoint 2016 on-prem is aging out. Drupal 11 (the only cleanly TRM-authorized CMS) requires significant PHP/Linux expertise most VA dev teams don't carry. This project gives VA teams a purpose-built CMS that:

- Ships 100% USWDS-compliant UI out of the box
- Runs on the Windows/.NET/MSSQL stack most VA teams already own and operate
- Lets **content owners** publish and manage content without a developer
- Gives **dev teams** full extensibility to build custom content types, workflows, and embedded applications
- Meets VA TRM, Section 508, and FedRAMP requirements by design

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | React 18 + TypeScript |
| Design System | USWDS 3.x (Web Components + CSS tokens) |
| API | ASP.NET Core 8 Web API |
| Database | Microsoft SQL Server 2019+ |
| Auth | Azure AD / Windows Auth (SAML/OIDC) |
| Search | SQL Full-Text Search (+ optional Elasticsearch) |
| File Storage | Network share / Azure Blob (configurable) |
| Hosting | IIS / Windows Server or containerized |

## Quick Links

- [Business Requirements Document](docs/BRD.md)
- [Database Layer: SPs, Indexes & Hygiene](docs/DATABASE_LAYER.md)
- [Claude Design Prompt](docs/CLAUDE_DESIGN_PROMPT.md)
- [Architecture Overview](docs/ARCHITECTURE.md)
- [Data Model](docs/DATA_MODEL.md)
- [API Reference](docs/API_REFERENCE.md)
- [Deployment Guide](docs/DEPLOYMENT.md)
- [Content Owner Guide](docs/CONTENT_OWNER_GUIDE.md)
- [Developer Guide](docs/DEVELOPER_GUIDE.md)

## Local Development Setup

Three processes run locally, all on fixed ports:

| Process | Command | URL |
|---|---|---|
| API (ASP.NET Core) | `cd src/api && dotnet run --project VA.CMS.API` | http://localhost:5100 (Swagger at `/swagger`) |
| Admin SPA (Vite) | `cd src/admin && npm install && npm run dev` | http://localhost:5173 (proxies `/api` → 5100) |
| Public site (Next.js) | `cd src/public && npm install && npm run dev` | http://localhost:3000 |

The API listens on **5100** rather than 5000 because macOS AirPlay Receiver binds port 5000
and silently answers 403 to anything proxied there.

### Prerequisites

- **.NET 8 runtime/SDK.** The projects target `net8.0`; a newer SDK can *build* them but
  cannot *run* them (`dotnet run` / `dotnet test` fail with "You must install or update .NET",
  and `DOTNET_ROLL_FORWARD` is not a safe workaround). Check `dotnet --list-runtimes` for
  `Microsoft.NETCore.App 8.x`. On a Mac with Homebrew's .NET 10 shadowing a
  `/usr/local/share/dotnet` install that has 8.x, prefix commands with:

  ```bash
  export DOTNET_ROOT=/usr/local/share/dotnet PATH="/usr/local/share/dotnet:$PATH"
  ```
- **Node 20+** and **Docker**.

### Authentication: DevBypass mode (no Azure AD required)

Developers can run the full API locally without configuring an Azure AD app registration.

**1. Copy the example settings file:**

```bash
cp src/api/VA.CMS.API/appsettings.Development.json.example \
   src/api/VA.CMS.API/appsettings.Development.json
```

The example file is pre-configured with `Auth:Mode=DevBypass` and two sample UPNs
(`alice@va.gov`, `bob@va.gov`). Update `Jwt:SigningKey` with any random 32+ character string
before starting the API. The connection string points to the local Docker SQL Server on port 14333.

**2. Start the local SQL Server container (if not already running):**

```bash
docker compose up -d        # port 14333, SA password VaCms_Dev!2026
```

or equivalently:

```bash
docker run -d --name va-cms-sqlserver \
  -e ACCEPT_EULA=Y \
  -e MSSQL_SA_PASSWORD=VaCms_Dev!2026 \
  -p 14333:1433 \
  mcr.microsoft.com/mssql/server:2022-latest
```

If your container uses a different SA password, change only the `Password=` in your local
`appsettings.Development.json` (it is gitignored). Note the integration tests that need a
database use Testcontainers and spin up their own SQL Server, so they don't depend on this one.

**3. Run the API:**

```bash
cd src/api
dotnet run --project VA.CMS.API
```

DbUp migrations run automatically on startup (scripts are discovered from the repo-root
`migrations/` folder).

**4. Sign in to the admin SPA:**

Start the admin app (`cd src/admin && npm run dev`) and open http://localhost:5173. When the
API is in DevBypass mode the login page shows a **Development sign-in** panel listing the
`DevBypassAllowedUsers`; pick one and you land on the dashboard. Dev users are issued the
`SystemAdmin` + `Developer` roles so every admin screen is usable. (`GET /api/auth/login`
redirects to `/login` in DevBypass mode instead of challenging Azure AD.)

**5. Or authenticate from the command line using the DevBypass header:**

```bash
# Obtain a JWT for a sample user
curl -s -X POST http://localhost:5100/api/auth/dev-login \
  -H "X-Dev-User: alice@va.gov" | jq .

# Use the returned accessToken on protected endpoints
curl -s http://localhost:5100/api/v1/admin/health/db \
  -H "Authorization: Bearer <accessToken>"
```

You can also pass `X-Dev-User` directly on any API call — the DevBypass middleware
auto-injects a JWT so you can skip the explicit dev-login step:

```bash
curl -s http://localhost:5100/api/v1/admin/health/db \
  -H "X-Dev-User: alice@va.gov"
```

**Security note:** `Auth:Mode=DevBypass` is hard-blocked in Production — the API refuses to
start if `ASPNETCORE_ENVIRONMENT=Production` and DevBypass is configured. Never set DevBypass
in production appsettings.

**CI integration tests:** The CI pipeline passes a fixed `X-Dev-User` header in integration
test requests. Set `Auth:Mode=DevBypass` and `Auth:DevBypassAllowedUsers` in the test host
configuration (see `Issue68AcceptanceTests.cs` for the pattern used in this repo's tests).


## Project Status

🟡 **Pre-development** — BRD and backlog complete. Ready for agent build.

## License

MIT
