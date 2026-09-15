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
 │  2019+        │ │  Local / UNC   │ │  Exchange / EXO      │
 │  (Primary DB) │ │  / Azure Blob  │ │                      │
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
│   │   │   ├── Data/                 # EF Core DbContext + migrations
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
├── migrations/                       # EF Core database migrations
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

## Authentication Flow

```
User → IIS → Admin SPA
                │
                ├─► POST /api/auth/login
                │     │
                │     ├─► Redirect to AAD OIDC endpoint
                │     ├─► AAD validates VA credentials (MFA if required)
                │     └─► Return to /api/auth/callback with code
                │           │
                │           └─► Exchange code for tokens
                │                 │
                │                 └─► Issue JWT access token (15min) +
                │                     refresh token (httpOnly cookie, 8hr)
                │
                └─► Subsequent API calls: Bearer {access_token}
                    │
                    └─► Token expiry: silent refresh via /api/auth/refresh
```

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
| ORM | Entity Framework Core 8 | Code-first migrations, LINQ, strong typing on MSSQL |
| GraphQL Server | Hot Chocolate | Best .NET GraphQL library, DataLoader support |
| Admin state management | TanStack Query (React Query) | Server state management, caching, background sync |
| Admin routing | React Router v6 | Stable, well-understood in VA dev community |
| Rich text editor | TipTap | ProseMirror-based, extensible, can enforce USWDS HTML output |
| Auth library | Microsoft.Identity.Web | Official MS library for AAD OIDC in ASP.NET Core |
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
                    │    /health   → Health check endpoint
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
                    │  (UNC Share or        │
                    │   Azure Blob)         │
                    └───────────────────────┘
```
