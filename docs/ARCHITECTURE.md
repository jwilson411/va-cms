# Architecture Overview
## VA CMS — USWDS-Compliant Content Management System

## System Components

```
┌──────────────────────────────────────────────────────────────────────┐
│                        Clients                                        │
│  ┌─────────────┐   ┌──────────────┐   ┌────────────────────────┐   │
│  │  Admin SPA  │   │ Public Site  │   │  External API Consumer  │   │
│  │  (React 18) │   │  (Next.js)   │   │  (Headless frontend,   │   │
│  │             │   │              │   │   integrations, etc.)   │   │
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
                   │  │  Auth   │  │  Plugin/Extension │   │
                   │  │  OIDC/  │  │  Host             │   │
                   │  │  SAML   │  │                   │   │
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

## Project Structure (Planned Repository Layout)

```
va-cms/
├── src/
│   ├── api/                          # ASP.NET Core 8 Web API
│   │   ├── VA.CMS.API/
│   │   │   ├── Controllers/          # REST endpoints
│   │   │   ├── GraphQL/              # Hot Chocolate schema + resolvers
│   │   │   ├── Middleware/           # Auth, logging, CSP headers
│   │   │   ├── Plugins/              # Extension host
│   │   │   └── Program.cs
│   │   ├── VA.CMS.Domain/            # Domain models and interfaces
│   │   │   ├── Content/
│   │   │   ├── Media/
│   │   │   ├── Users/
│   │   │   ├── Workflow/
│   │   │   └── Audit/
│   │   ├── VA.CMS.Infrastructure/    # Data access, external services
│   │   │   ├── Data/                 # PetaPoco DB + repository implementations
│   │   │   ├── Migrations/           # DbUp migration runner (loads from /migrations/)
│   │   │   ├── Storage/              # File storage adapters
│   │   │   ├── Search/               # Full-text search impl
│   │   │   └── Email/
│   │   └── VA.CMS.Tests/
│   │
│   ├── admin/                        # React 18 + TypeScript admin SPA
│   │   ├── src/
│   │   │   ├── components/           # USWDS-based shared components
│   │   │   ├── features/             # Feature slices (content, media, users...)
│   │   │   ├── pages/                # Route-level page components
│   │   │   ├── api/                  # API client (React Query)
│   │   │   ├── hooks/
│   │   │   └── styles/               # USWDS theme overrides + global SCSS
│   │   └── package.json
│   │
│   └── public/                       # Next.js public-facing site
│       ├── app/                      # App Router pages
│       ├── components/               # USWDS page templates
│       ├── lib/                      # CMS API client
│       └── package.json
│
├── cli/                              # vacms CLI (dotnet tool)
│   └── VA.CMS.CLI/
│
├── migrations/                       # Plain SQL migration scripts (run by DbUp)
│   ├── V001__initial_schema.sql
│   ├── V002__add_fts_catalog.sql
│   └── ...                           # Each file is a forward-only, idempotent SQL script
├── docs/                             # Documentation (this folder)
├── infra/                            # IIS config, Dockerfiles, deployment scripts
├── tests/
│   ├── integration/
│   ├── accessibility/                # axe-core test suite
│   └── load/                         # k6 load tests
└── .github/
    └── workflows/
        ├── ci.yml
        ├── sbom.yml
        └── deploy.yml
```

## Auth Flow: AD → JWT

```
User opens Admin SPA
        │
        ▼
GET /api/auth/login
        │
        ▼
Redirect → Azure AD (OIDC)
  AD validates VA credentials + MFA
        │
        ▼
Callback → /api/auth/callback
  Microsoft.Identity.Web validates token
        │
        ├── Sync User row (upsert by UPN)
        ├── Resolve AD group → CMS role mappings
        │
        └── Issue: JWT access token (15 min, signed HS256)
                  + Refresh token (httpOnly cookie, 8 hr)
        │
        ▼
Admin SPA receives JWT — stored in memory (NOT localStorage)
All API calls: Authorization: Bearer {jwt}

Silent refresh: before expiry, SPA calls POST /api/auth/refresh
  → validates the httpOnly refresh cookie against [RefreshToken] (hash only)
  → rotates it: old token revoked, replacement set in the cookie (#163)
  → issues new JWT (carries User.SessionVersion as "sv")
  → no user interaction required

Replayed (already rotated) refresh token → whole session chain revoked, audited
Idle > auth.idleTimeoutMinutes, or login + auth.absoluteSessionHours reached → 401
AD account disabled / user deactivated / role changed → sessions revoked, SessionVersion
  bumped → existing access tokens refused within auth.revocationCheckSeconds, refresh returns 401
```

**Key principles:**
- No CMS password ever created or stored
- AD is the single source of identity truth
- JWT is stateless — API tier is horizontally scalable without shared session state
- Refresh token in httpOnly cookie — not accessible to JavaScript (XSS-safe)
- AD group → role mapping eliminates manual per-user role assignment at scale

## Content Rendering Pipeline

```
Published Entry in MSSQL
        │
        ▼
GET /api/v1/content/{slug}  ← Next.js Server Component (getStaticProps / ISR)
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
| Migrations | DbUp | Plain SQL scripts in `/migrations/`. Versioned, idempotent, run on startup. |
| GraphQL Server | Hot Chocolate | Best .NET GraphQL library, DataLoader support |
| Admin state management | TanStack Query (React Query) | Server state management, caching, background sync |
| Admin routing | React Router v6 | Stable, well-understood in VA dev community |
| Rich text editor | TipTap | ProseMirror-based, extensible, can enforce USWDS HTML output |
| Auth | Microsoft.Identity.Web (AD → JWT) | Official MS library for AAD OIDC in ASP.NET Core; AD authenticates, CMS issues JWT |
| Markdown renderer | Markdig | Fast, extensible CommonMark renderer for .NET; same pipeline in live preview and publish |
| WYSIWYG editor | TipTap + tiptap-markdown | Decision in #69: true WYSIWYG (the editor is the preview); content owners see formatting, storage is Markdown serialised by tiptap-markdown |
| Testing (API) | xUnit + TestContainers (MSSQL) | Real DB in CI, no mocks for data layer |
| Testing (React) | Vitest + React Testing Library | Fast, co-located with components |
| Accessibility testing | axe-core + Playwright | Automated a11y CI gate |
| Load testing | k6 | Scriptable, CI-friendly |

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
