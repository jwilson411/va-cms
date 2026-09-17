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
- [Runtime Settings & Feature Flags](docs/SETTINGS.md)

## Local Development Setup

Three processes run locally, all on fixed ports:

| Process | Command | URL |
|---|---|---|
| API (ASP.NET Core) | `cd src/api && dotnet run --project VA.CMS.API` | http://localhost:5100 (Swagger at `/swagger`) |
| Admin SPA (Vite) | `cd src/admin && npm install && npm run dev` | http://localhost:5173 (proxies `/api` → 5100) |
| Public site (Next.js) | `cd src/public && npm install && npm run dev` | http://localhost:3000 |

The API listens on **5100** rather than 5000 because macOS AirPlay Receiver binds port 5000
and silently answers 403 to anything proxied there.

Both front ends compile USWDS 3.x from Sass with the VA theme tokens (VA Blue `#003e73`,
VA Gold `#f9c642`, Public Sans) defined once in `src/theme/uswds/_va-settings.scss`.
`npm run dev` / `npm run build` first copy the USWDS fonts and images into each app's
gitignored `public/uswds/` (`npm run uswds:assets`). Smoke-test pages that render themed
USWDS components live at http://localhost:5173/admin/theme and http://localhost:3000/theme.

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

Those dev roles live only in the JWT. Workflow notifications (the bell in the admin top bar,
issue #38) resolve reviewers from `UserRole` rows, so to see "submitted for review"
notifications locally, grant the reviewing dev user a real Editor/SiteAdmin role first —
sign in as `alice@va.gov`, open **Users → bob**, assign *Editor* — then submit content as
alice and sign in as bob. Approve/return notifications go to the entry's owner and need no
extra setup.

Running a second checkout against an API on another port? `VITE_API_PROXY=http://localhost:5101 npm run dev -- --port 5174`
overrides the Vite `/api` proxy target.

**5. Seed demo content and connect the public site (optional but recommended):**

```bash
# Demo pages, news articles, users and taxonomy (idempotent; --reset drops and re-seeds)
cd src/api
VACMS_CONNECTION_STRING="Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True" \
  dotnet run --project VA.CMS.CLI -- db seed --demo

# Let publishes/unpublishes/nav changes invalidate the Next.js page cache (issue #54 webhooks).
# Use the same secret as REVALIDATE_SECRET in src/public/.env.local (unset = unsigned, dev only).
curl -s -X POST http://localhost:5100/api/v1/webhooks -H "X-Dev-User: alice@va.gov" \
  -H "Content-Type: application/json" \
  -d '{"name":"public-site","url":"http://localhost:3000/api/revalidate","secret":"dev-secret",
       "events":["content.published","content.unpublished","content.archived","navigation.updated","settings.updated"]}'
```

Runtime configuration — feature flags, upload limits, token lifetimes, the public site's agency
name and DAP codes, polling intervals — is **not** in appsettings. It lives in the `SiteSetting`
table and is edited at http://localhost:5173/admin/settings; the running API applies a change
immediately, no rebuild. See [docs/SETTINGS.md](docs/SETTINGS.md) for the full list and for what
deliberately stays in appsettings (connection string, auth mode, signing key, storage backend).

Uploaded media is written to `Storage:LocalRootPath` (`src/api/VA.CMS.API/.uploads`, gitignored, from
the example settings) and served by `GET /api/v1/media/serve/{id}`.

**6. Or authenticate from the command line using the DevBypass header:**

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

### Workflow email notifications (SMTP)

Every workflow event that lands in the in-app bell also goes out as an email (issue #39,
BRD FR-WORKFLOW-02): *submitted for review* → the reviewers of that section, *returned* /
*approved* / *published* → the entry's owner. The person who took the action is never
emailed. Each message has a plain-language subject (`Review requested: <title>`), the same
one-line description the bell shows, the reviewer's comment when there is one, and a link to
`{AdminBaseUrl}/admin/content/{id}/edit`.

Email is **off until `Email:Smtp:Host` is set** — without it the API logs
`Email not sent (Email:Smtp:Host is not configured)` and the bell keeps working. Everything is
configured through the `Email` section, so in a deployment set environment variables:

| Variable | Exchange Online | Exchange on-prem | Notes |
|---|---|---|---|
| `Email__From` | `cms-noreply@va.gov` | `cms-noreply@va.gov` | Required. Must be a mailbox/sender the connector allows. |
| `Email__FromName` | `VA CMS` | `VA CMS` | Optional display name. |
| `Email__AdminBaseUrl` | `https://cms.va.gov` | `https://cms.va.gov` | Required. Public origin of the admin SPA, used for links. |
| `Email__Smtp__Host` | `smtp.office365.com` | `mail.example.va.gov` | Turning this on turns email on. |
| `Email__Smtp__Port` | `587` | `587` (STARTTLS) or `25` (relay) | Default 587. |
| `Email__Smtp__Security` | `StartTls` | `StartTls` (or `None` for an internal relay) | `StartTls` \| `SslOnConnect` \| `Auto` \| `None`. Default `StartTls`; connection fails if the server can't upgrade. |
| `Email__Smtp__Username` / `Email__Smtp__Password` | the sending mailbox (SMTP AUTH must be enabled on it) | leave empty for an IP-allow-listed receive connector | Set both or neither. |
| `Email__Smtp__TimeoutSeconds` | `30` | `30` | Connect/command timeout. |

The API validates this at startup and refuses to start on a half-configured mailer (host
without `From`/`AdminBaseUrl`, username without password). Delivery runs off the request
thread; a dead relay is logged and never fails the workflow action itself.

To see the emails locally, run the Mailpit sink and point the API at it:

```bash
docker compose --profile mail up -d mailpit        # SMTP on 1025, inbox UI on http://localhost:8025
cd src/api
Email__Smtp__Host=localhost Email__Smtp__Port=1025 Email__Smtp__Security=None \
  dotnet run --project VA.CMS.API
```

(`Email:From` and `Email:AdminBaseUrl` come from `appsettings.Development.json`; the example
file already sets them.) Then submit content for review as one dev user with another holding
an Editor role — see step 4 above — and open Mailpit.


## Project Status

🟡 **Pre-development** — BRD and backlog complete. Ready for agent build.

## License

MIT
