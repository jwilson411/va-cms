# Architecture Overview
## VA CMS — USWDS-Compliant Content Management System

## System Components

```
┌──────────────────────────────────────────────────────────────────────┐
│                        Clients                                        │
│  ┌─────────────┐   ┌──────────────┐   ┌────────────────────────┐   │
│  │  Admin SPA  │   │ Public Site  │   │  External API Consumer  │   │
│  │  (React 18) │   │  (Next.js)   │   │  (Published-only when  │   │
│  │             │   │              │   │   anonymous; user JWT   │   │
│  │             │   │              │   │   for anything else)    │   │
│  └──────┬──────┘   └──────┬───────┘   └────────────┬───────────┘   │
└─────────┼─────────────────┼────────────────────────┼───────────────┘
          │                 │                         │
          └─────────────────┼─────────────────────────┘
                            │  HTTPS (REST / GraphQL)
                   ┌────────▼──────────┐
                   │   IIS (Reverse    │
                   │   Proxy + TLS     │
                   │   Termination)    │
                   └────────┬──────────┘
                            │
                   ┌────────▼──────────────────────────────┐
                   │         ASP.NET Core 8 Web API         │
                   │                                        │
                   │  ┌──────────┐  ┌──────────────────┐  │
                   │  │  REST    │  │    GraphQL        │  │
                   │  │  API     │  │    (Hot Chocolate) │  │
                   │  └──────────┘  └──────────────────┘  │
                   │                                        │
                   │  ┌─────────┐  ┌──────────────────┐   │
                   │  │  Auth   │  │  Hosted workers   │   │
                   │  │  WIA /  │  │  scheduler,       │   │
                   │  │  AD FS  │  │  outbox, settings │   │
                   │  └─────────┘  └──────────────────┘   │
                   │                                        │
                   │  ┌─────────────────────────────────┐  │
                   │  │       Domain Services            │  │
                   │  │  Content  │ Media  │ Workflow    │  │
                   │  │  Search   │ Users  │ Audit       │  │
                   │  └─────────────────────────────────┘  │
                   └────────┬───────────────────────────────┘
                            │
          ┌─────────────────┼─────────────────────┐
          │                 │                       │
 ┌────────▼──────┐ ┌───────▼────────┐ ┌──────────▼──────────┐
 │  SQL Server   │ │  File Storage  │ │  Email (SMTP)        │
 │  2019+        │ │  Local / UNC   │ │  Exchange (on-prem)  │
 │  (Primary DB) │ │  (on-prem)     │ │                      │
 └───────────────┘ └────────────────┘ └─────────────────────┘
```

## Project Structure

```
va-cms/
├── src/
│   ├── api/                          # .NET 8 solution (VA.CMS.sln, central package management)
│   │   ├── VA.CMS.API/               # ASP.NET Core 8 Web API
│   │   │   ├── Controllers/          # REST endpoints (Admin/ for the admin-only surface)
│   │   │   ├── GraphQL/              # Hot Chocolate schema, resolvers, GraphQLAudience (#156)
│   │   │   ├── Auth/                 # JWT, refresh tokens, RBAC policies, WIA/AD FS glue, session revocation
│   │   │   ├── Middleware/           # Correlation id, security headers, audit context, header redaction
│   │   │   ├── RateLimiting/         # Per-client policies from api.rateLimits.* (#167)
│   │   │   ├── Webhooks/             # Outbox consumer, destination policy, HMAC signing, secret protector
│   │   │   ├── Observability/        # Serilog sinks, health checks (#166)
│   │   │   ├── StartupValidation.cs  # Fail-fast configuration rules (#173)
│   │   │   └── Program.cs
│   │   ├── VA.CMS.Infrastructure/    # PetaPoco data access, POCOs, settings, storage, search, email, outbox
│   │   │   ├── Data/                 # CmsDatabase (SP-only), repositories, session-context audit plumbing
│   │   │   ├── Settings/             # SiteSettingDefinitions — every runtime toggle/limit (#141)
│   │   │   ├── Storage/              # Local/UNC backends, MIME sniffer, SVG sanitiser, ICAP/ClamAV scanners
│   │   │   ├── Migration/SharePoint/ # Export package reader + validation + inventory (epic #13, docs/MIGRATION.md)
│   │   │   └── Search/ Email/ Notifications/ Outbox/ Markdown/ ContentTypes/ Services/
│   │   ├── VA.CMS.CLI/               # `vacms` — db migrate / provision-logins / seed (#157), migrate sharepoint (#191)
│   │   └── VA.CMS.Tests/             # xUnit; IssueNNNAcceptanceTests per story, SQL Server 2022 via Testcontainers
│   │
│   ├── admin/                        # React 18 + TypeScript + Vite admin SPA (dist/web.config carries the CSP)
│   │   └── src/{components,features,pages,api,security,...}
│   ├── public/                       # Next.js public site (App Router; proxy.ts = nonce CSP + redirect resolver)
│   └── theme/uswds/                  # VA USWDS theme tokens shared by both front ends
│
├── migrations/                       # Plain SQL scripts V001…V049, forward-only, run by DbUp (vacms db migrate)
├── infra/
│   ├── sql/provision-logins.sql      # vacms_app / vacms_readonly logins from pipeline secrets
│   ├── sql-agent-jobs/               # Archive audit log, purge refresh tokens/deliveries, roll up search logs, index maintenance
│   └── sharepoint/                   # Export-VacmsSharePoint.ps1 — runs on the SP2016 farm, writes the migration package (#191)
├── tests/accessibility/              # Playwright + axe suite (CI job)
├── docs/                             # This folder — see README "Quick Links"
├── docker-compose.yml                # SQL Server 2022 (14333), optional Mailpit and ClamAV profiles
├── SECURITY.md
└── .github/workflows/
    ├── ci.yml                        # build, tests + coverage, OpenAPI drift, SPA/public builds, a11y
    └── security.yml                  # CodeQL, Semgrep (opt), dependency advisories, gitleaks, CycloneDX SBOMs
```

Not present, although the original plan listed them: a `Plugins/` extension host, a separate `VA.CMS.Domain`
project (POCOs live in `VA.CMS.Infrastructure/Data/Pocos`), `tests/load` (k6), and a `deploy.yml` workflow —
deployment is a manual, documented procedure (`docs/DEPLOYMENT.md`) because VA change management owns it.

## Auth Flow: AD → JWT

Two on-prem identity paths, both ending in the same CMS-issued JWT (`docs/DEPLOYMENT.md` § Identity provider):

```
User opens Admin SPA → /login shows the system-use notice (auth.systemUseNotice, AC-8)
        │  user accepts
        ▼
GET /api/auth/login?ack=1&returnUrl=…
        │
        ├── Auth:Mode=WindowsAuth ──► 302 /api/auth/windows-login
        │                              IIS Negotiate (Kerberos, NTLM fallback) authenticates the browser
        │
        └── Auth:Mode=AzureAd ───────► 302 AD FS (OpenID Connect via Microsoft.Identity.Web)
                                       AD FS validates VA credentials / PIV / MFA per AD policy
                                       → /signin-oidc → GET /api/auth/callback
        │
        ▼
  ├── Look up User row by ExternalId (UPN or oid/sub); create it only if auth.autoProvisionUsers is on
  ├── Resolve AD group → CMS role mappings (SIDs or DOMAIN\Group) + explicit UserRole rows
  ├── Refuse with 403 if the identity ends up with no CMS role (default deny, #155)
  ├── Audit Logon (mode, upn, systemUseAcknowledged, IP, user agent, correlation id)
  └── Set refresh cookie cms_rt (HttpOnly; Secure; SameSite=Strict; Path=/api/auth) → 302 into the SPA
        │
        ▼
SPA calls POST /api/auth/refresh → JWT access token (HS256, lifetime auth.accessTokenMinutes, default 15)
  stored in memory only (NOT localStorage); every API call: Authorization: Bearer {jwt}
  No token ever appears in a URL or in a redirect response body.

Silent refresh: before expiry, SPA calls POST /api/auth/refresh
  → validates the httpOnly refresh cookie against [RefreshToken] (SHA-256 hash only)
  → rotates it: old token revoked, replacement set in the cookie (#163)
  → issues new JWT (carries User.SessionVersion as "sv")
  → no user interaction required

Replayed (already rotated) refresh token → whole session chain revoked, audited RefreshReplay
Idle > auth.idleTimeoutMinutes (15), or login + auth.absoluteSessionHours (8) reached → 401, sign in again
AD account disabled / user deactivated / role changed → sessions revoked, SessionVersion
  bumped → existing access tokens refused within auth.revocationCheckSeconds, refresh returns 401
Logout: POST /api/auth/logout revokes the chain; with auth.azureAdSignOut the SPA then visits
  GET /api/auth/signout to end the AD FS session
Development only: Auth:Mode=DevBypass (X-Dev-User header) — refused under any other environment
```

**Key principles:**
- No CMS password ever created or stored
- AD is the single source of identity truth
- JWT is stateless and the refresh token lives in SQL Server — the API tier scales out without sticky sessions (#171)
- Refresh token in httpOnly cookie — not accessible to JavaScript (XSS-safe)
- AD group → role mapping eliminates manual per-user role assignment at scale

## Content Rendering Pipeline

```
Published Entry in MSSQL
        │
        ▼
GET /api/v1/content/{slug}  ← Next.js Server Component (App Router, revalidated by the content.* webhooks)
        │
        ▼
API returns: ContentEntry JSON
  { type, fields, version, publishedAt, template }
        │
        ▼
Template Resolver
  ContentType.templateId → registered React component
        │
        ▼
USWDS React Template renders:
  Banner → Header → Breadcrumb → [Content Body] → Footer → Identifier
        │
        ▼
HTML streamed to browser (SSR) or served from ISR cache
```

## Key Technology Decisions

| Decision | Choice | Rationale |
|---|---|---|
| ORM | PetaPoco | Thin micro-ORM — write real SQL, get typed results. No magic, no migration drama. |
| Migrations | DbUp | Plain SQL scripts in `/migrations/`. Versioned, forward-only. Applied by `vacms db migrate` as the deployment account; the API only verifies the journal at startup (Development may still auto-migrate) (#157). |
| GraphQL Server | Hot Chocolate | Best .NET GraphQL library, DataLoader support |
| Admin state management | TanStack Query (React Query) | Server state management, caching, background sync |
| Admin routing | React Router v6 | Stable, well-understood in VA dev community |
| Rich text editor | TipTap | ProseMirror-based, extensible, can enforce USWDS HTML output |
| Auth | IIS Windows Authentication or Microsoft.Identity.Web OIDC against AD FS (AD → JWT) | On-prem only; AD authenticates, CMS issues its own short-lived JWT and DB-backed refresh token. The `AzureAd` configuration name is historical — no cloud tenant is involved |
| Markdown renderer | Markdig | Fast, extensible CommonMark renderer for .NET; same pipeline in live preview and publish |
| WYSIWYG editor | TipTap + tiptap-markdown | Decision in #69: true WYSIWYG (the editor is the preview); content owners see formatting, storage is Markdown serialised by tiptap-markdown |
| Testing (API) | xUnit + TestContainers (MSSQL) | Real DB in CI, no mocks for data layer |
| Testing (React) | Vitest + React Testing Library | Fast, co-located with components |
| Accessibility testing | axe-core + Playwright | Automated a11y CI gate |
| Load testing | k6 (planned) | Not yet in the repository; run before the first production rollout to size `api.rateLimits.*` and the IIS recycle limits |

## Deployment Topology (On-Premises)

```
                    ┌─────────────────────┐
                    │  Windows Server 2022 │
                    │  (Web Tier)          │
                    │                      │
                    │  IIS 10              │
                    │    /         → Next.js (node)
                    │    /admin    → React SPA (static)
                    │    /api      → ASP.NET Core 8
                    │    /health   → liveness; /health/ready → readiness (#166)
                    └──────────────────────┘
                            │
                    ┌───────▼──────────────┐
                    │  Windows Server 2019+ │
                    │  (Database Tier)      │
                    │                       │
                    │  SQL Server 2019+     │
                    │  (Always On AG        │
                    │   recommended)        │
                    └───────────────────────┘
                            │
                    ┌───────▼──────────────┐
                    │  File Storage         │
                    │  (local disk or       │
                    │   UNC share, on-prem) │
                    └───────────────────────┘
```

The web tier scales out to N nodes behind IIS ARR or a load balancer without sticky sessions
(NFR-OPS-04, #171): every node runs the same hosted services and coordinates through SQL Server —
the publish/expire scheduler claims due rows `WITH (UPDLOCK, READPAST)` in one transaction, webhook
deliveries and workflow emails are `[OutboundEvent]` outbox rows that any node claims under a lease,
and the settings snapshot on each node reloads when the table's change stamp moves. Supported
topologies and app-pool recycle behaviour are in `docs/DEPLOYMENT.md` § 8.

## Requirements that were dropped or narrowed (annotations for the BRD)

| BRD item | Original | What was built | Why |
|---|---|---|---|
| FR-MEDIA-07 | local / UNC / **Azure Blob** storage | `local` and `unc` only; any other `Storage:Backend` refuses to start (#170) | On-prem constraint — no cloud resources. The `IStorageBackend` seam remains if a future hosting decision changes |
| FR-USERS-01 | **Azure AD** OIDC | Windows Integrated Authentication (IIS Kerberos) or **AD FS** OIDC through the same Microsoft.Identity.Web handler | Same constraint; the `AzureAd` config section name is kept for the library |
| FR-USERS-01a | 15-minute JWT, 8-hour session as constants | `auth.accessTokenMinutes`, `auth.refreshTokenHours`, `auth.idleTimeoutMinutes`, `auth.absoluteSessionHours` site settings with those defaults (#146/#164) | Settings rule (#141): every limit is an admin-editable `SiteSetting` |
| FR-SEARCH-05 | SQL FTS **or Elasticsearch** | SQL Server Full-Text Search only (`LIKE` fallback when FTS is unavailable, V038) | No Elasticsearch service on-prem; not built |
| FR-MEDIA-04 | virus-scan "integration point" | ICAP RESPMOD or ClamAV `INSTREAM`, fail-closed, refused if disabled in Production (#159) | Implemented, not stubbed |
| BRD §7 integrations | "API keys" for headless consumers | None. Anonymous callers get the Published-only REST/GraphQL surface; anything else needs a user JWT (#156) | No service-account credential type exists; if one is needed it is a new story, not an API-key header |
| Architecture (plan) | Plugin/extension host, SAML | Neither built; extensibility is custom content types + custom field types (admin UI) and the webhook/outbox events | Not required by any BRD row |
| NFR-OPS-03 | migrations "run automatically on deployment" | Run by `vacms db migrate` as a deploy step; the API refuses to start against a stale schema (#157) | The app login must not hold DDL rights (AC-6) |

