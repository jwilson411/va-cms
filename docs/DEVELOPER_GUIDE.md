# Developer Guide
## VA CMS — USWDS-Compliant Content Management System

## Rich Text Fields: Markdown Storage

Rich text fields store **CommonMark Markdown** — not HTML. The API serves both forms:

```json
{
  "fields": {
    "body": {
      "markdownBody": "## Overview\n\nThis is the **content**...",
      "renderedBody": "<h2>Overview</h2>\n<p>This is the <strong>content</strong>...</p>"
    }
  }
}
```

**Backend rendering (Markdig .NET):**

```csharp
// src/api/VA.CMS.Infrastructure/Markdown/UswdsMarkdownRenderer.cs
using Markdig;

public class UswdsMarkdownRenderer : IMarkdownRenderer
{
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()           // blocks raw HTML injection — DB stores clean Markdown only
        .Build();

    public string Render(string markdown)
    {
        // DisableHtml means no <script>, no inline styles, no raw HTML passthrough
        return Markdown.ToHtml(markdown, _pipeline);
    }
}
```

**Admin WYSIWYG editor (issue #115):** Content owners use a formatted rich text surface and never write raw Markdown. The editor *is* the live preview — there is no split pane or preview toggle. The RichText field renders TipTap with a USWDS toolbar; on every change the document is serialised to Markdown with `tiptap-markdown` (`html: false`, so nothing the editor emits is raw HTML) and that Markdown string is what gets stored. Markdig renders it on the public site with the same `DisableHtml()` pipeline. `POST /api/v1/preview/render` remains available for server-side rendering of arbitrary Markdown.

```typescript
// src/admin/src/features/contentEntries/RichTextEditor.tsx (abridged)
import { useEditor, EditorContent } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import { Markdown } from 'tiptap-markdown';

const editor = useEditor({
  extensions: [
    StarterKit.configure({ heading: false /* H2-H4 added explicitly; H1 is the page title */ }),
    Heading.configure({ levels: [2, 3, 4] }),
    Link, Image,
    Markdown.configure({ html: false, tightLists: true, bulletListMarker: '-' }),
  ],
  content: value,                       // Markdown string in…
  onUpdate: ({ editor }) =>
    onChange(editor.storage.markdown.getMarkdown()),   // …Markdown string out
});
```

---

## Authentication: AD → JWT

The CMS uses AD as the identity provider. The API issues a JWT — it never stores passwords.

```
1. Admin SPA loads → no JWT in memory → redirect to /api/auth/login
2. /api/auth/login → redirect to Azure AD (OIDC)
3. AD validates user + MFA → callback to /api/auth/callback
4. API validates the AD token, upserts User row, resolves AD group → role mapping
5. API issues:
     - JWT access token (15 min, HS256, stored in-memory in SPA — not localStorage)
     - Refresh token (httpOnly cookie, 8 hr)
6. All API requests: Authorization: Bearer {jwt}
7. Before JWT expires, SPA silently POSTs to /api/auth/refresh
     - API validates the httpOnly cookie against [RefreshToken] and rotates it (#163)
     - Issues new JWT
8. If AD account is disabled, the user is deactivated or their roles change: sessions are
   revoked → next refresh → 401 → SPA clears state → login redirect
9. Idle for auth.idleTimeoutMinutes: the SPA warns, then signs out; the API refuses the
   refresh regardless (#164). The /login page shows the system-use notice, which must be
   acknowledged (ack=1) before /api/auth/login starts a sign-in.
```

**Dev setup (no AD):** Set `Auth:Mode=DevBypass` and a non-empty `DevBypassAllowedUsers` in `appsettings.Development.json`. The API accepts a `X-Dev-User: alice@va.gov` header and issues a JWT for that UPN without AD. The mode refuses to start outside `ASPNETCORE_ENVIRONMENT=Development` (#164).

```json
// appsettings.Development.json (excerpt)
{
  "Auth": {
    "Mode": "DevBypass",
    "DevBypassAllowedUsers": ["alice@va.gov", "bob@va.gov"]
  }
}
```

---

## Stack Quick Reference
|---|---|---|
| API | ASP.NET Core 8 | https://docs.microsoft.com/aspnet/core |
| ORM | PetaPoco | https://github.com/CollaboratingPlatypus/PetaPoco |
| Migrations | DbUp | https://dbup.readthedocs.io |
| GraphQL | Hot Chocolate | https://chillicream.com/docs/hotchocolate |
| Admin UI | React 18 + TypeScript | https://react.dev |
| Design System | USWDS 3.x | https://designsystem.digital.gov |
| Admin State | TanStack Query v5 | https://tanstack.com/query |
| WYSIWYG | TipTap + tiptap-markdown | https://tiptap.dev |
| Markdown | Markdig | https://github.com/xoofx/markdig |
| Public Site | Next.js 16 (App Router, Turbopack) | https://nextjs.org/docs |
| Database | SQL Server 2019+ | |
| Auth | Microsoft.Identity.Web (AD → JWT) | |

---

## Data Access: PetaPoco

PetaPoco is a thin micro-ORM — you write real SQL, it maps results to typed POCOs. No change tracking, no lazy loading, no abstraction layered on top of SQL Server that fights you when you need a CTE.

### Setup

```csharp
// src/api/VA.CMS.Infrastructure/Data/CmsDatabase.cs
using PetaPoco;

public class CmsDatabase : Database
{
    public CmsDatabase(string connectionString)
        : base(connectionString, DatabaseType.SqlServer2012,
               System.Data.SqlClient.SqlClientFactory.Instance)
    { }
}

// Program.cs
builder.Services.AddScoped<CmsDatabase>(_ =>
    new CmsDatabase(configuration.GetConnectionString("DefaultConnection")!));
```

### POCO mapping

PetaPoco maps column names to property names by convention (case-insensitive). Use `[Column]` and `[TableName]` attributes when names differ:

```csharp
[TableName("ContentEntry")]
[PrimaryKey("Id", AutoIncrement = true)]
public class ContentEntry
{
    public long Id { get; set; }
    public long ContentTypeId { get; set; }
    public string Slug { get; set; } = "";
    public string Locale { get; set; } = "en-US";
    public string Status { get; set; } = "Draft";
    public long? PublishedVersionId { get; set; }
    public long OwnerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### Repository pattern

All queries live in repository classes. No raw DB calls in controllers or domain services.

```csharp
// src/api/VA.CMS.Infrastructure/Data/Repositories/ContentEntryRepository.cs
public class ContentEntryRepository : IContentEntryRepository
{
    private readonly CmsDatabase _db;
    public ContentEntryRepository(CmsDatabase db) => _db = db;

    public async Task<ContentEntry?> GetBySlugAsync(string slug, string locale) =>
        await _db.FirstOrDefaultAsync<ContentEntry>(
            "WHERE [Slug] = @0 AND [Locale] = @1 AND [Status] = 'Published'",
            slug, locale);

    public async Task<Page<ContentEntry>> ListAsync(int page, int pageSize, string? status = null)
    {
        var sql = Sql.Builder.Append("SELECT * FROM [ContentEntry] WHERE 1=1");
        if (status is not null)
            sql.Append("AND [Status] = @0", status);
        sql.Append("ORDER BY [UpdatedAt] DESC");
        return await _db.PageAsync<ContentEntry>(page, pageSize, sql);
    }

    public async Task<long> CreateAsync(ContentEntry entry) =>
        (long)await _db.InsertAsync(entry);

    public async Task UpdateAsync(ContentEntry entry) =>
        await _db.UpdateAsync(entry);
}
```

### Raw SQL when you need it — just write it

```csharp
// Full-text search: SQL Server CONTAINSTABLE — not something EF can express
public async Task<IEnumerable<SearchResult>> SearchAsync(string query, int page, int pageSize)
{
    return await _db.QueryAsync<SearchResult>(@"
        SELECT e.Id, e.Slug, e.ContentTypeId, v.FieldsJson, kt.RANK
        FROM   [ContentEntry]  e
        JOIN   [ContentVersion] v  ON v.Id = e.PublishedVersionId
        JOIN   CONTAINSTABLE([ContentVersion], [FieldsJson], @0) kt ON kt.[KEY] = v.Id
        WHERE  e.Status = 'Published'
        ORDER  BY kt.RANK DESC
        OFFSET @1 ROWS FETCH NEXT @2 ROWS ONLY",
        query, (page - 1) * pageSize, pageSize);
}
```

---

## Database Migrations: DbUp

Schema changes are plain `.sql` files in `/migrations/`. DbUp runs them on API startup, in version order, exactly once per environment. No CLI. No generated C# files. No `update-database` to remember.

### File naming convention

```
/migrations/
  V001__initial_schema.sql
  V002__add_fts_catalog.sql
  V003__add_webhook_tables.sql
  V004__add_rendered_fields_column.sql
```

`V{NNN}__{description}.sql` — double underscore separator, sequential, forward-only. Never edit a shipped script. Always add a new one.

### DbUp wired into startup

```csharp
// src/api/VA.CMS.API/Program.cs
var upgrader = DeployChanges.To
    .SqlDatabase(connectionString)
    .WithScriptsFromFileSystem(
        Path.Combine(AppContext.BaseDirectory, "migrations"),
        new FileSystemScriptOptions { IncludeSubDirectories = false })
    .WithTransactionPerScript()
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
if (!result.Successful)
{
    Log.Fatal(result.Error, "Migration failed — aborting startup");
    return 1; // crash fast, don't run on a broken schema
}
```

### Example initial migration

```sql
-- migrations/V001__initial_schema.sql
CREATE TABLE [ContentType] (
    [Id]              BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Name]            NVARCHAR(100)        NOT NULL,
    [DisplayName]     NVARCHAR(200)        NOT NULL,
    [Description]     NVARCHAR(1000)       NULL,
    [TemplateId]      NVARCHAR(200)        NULL,
    [IsSystemType]    BIT                  NOT NULL DEFAULT 0,
    [AllowWorkflow]   BIT                  NOT NULL DEFAULT 1,
    [FieldSchemaJson] NVARCHAR(MAX)        NOT NULL DEFAULT '[]',
    [CreatedAt]       DATETIME2            NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]       DATETIME2            NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE UNIQUE INDEX [UX_ContentType_Name] ON [ContentType] ([Name]);

CREATE TABLE [ContentEntry] (
    [Id]                 BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [ContentTypeId]      BIGINT               NOT NULL REFERENCES [ContentType]([Id]),
    [Slug]               NVARCHAR(500)        NOT NULL,
    [Locale]             NVARCHAR(10)         NOT NULL DEFAULT 'en-US',
    [Status]             NVARCHAR(20)         NOT NULL DEFAULT 'Draft',
    [PublishedVersionId] BIGINT               NULL,
    [ScheduledPublishAt] DATETIME2            NULL,
    [ScheduledExpireAt]  DATETIME2            NULL,
    [OwnerId]            BIGINT               NOT NULL,
    [CreatedAt]          DATETIME2            NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]          DATETIME2            NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE UNIQUE INDEX [UX_ContentEntry_Slug_Locale] ON [ContentEntry] ([Slug], [Locale]);
CREATE INDEX [IX_ContentEntry_Status] ON [ContentEntry] ([Status]);
```

### Adding a new column later

```sql
-- migrations/V004__add_rendered_fields_column.sql
ALTER TABLE [ContentVersion]
    ADD [RenderedFieldsJson] NVARCHAR(MAX) NULL;
```

Deploy → DbUp runs V004 → done. No ORM regeneration, no model sync step.

---

## Defining a New Content Type

### 1. Create the C# domain model

```csharp
// src/api/VA.CMS.Domain/Content/Types/NewsArticleType.cs
public class NewsArticleTypeDefinition : ContentTypeDefinitionBase
{
    public override string Name => "news_article";
    public override string DisplayName => "News Article";
    public override string TemplateId => "NewsArticleTemplate";
    public override bool AllowWorkflow => true;

    public override IReadOnlyList<FieldDefinition> Fields => new[]
    {
        new FieldDefinition("title", FieldType.ShortText, required: true, maxLength: 200),
        new FieldDefinition("summary", FieldType.LongText, required: true, maxLength: 500),
        new FieldDefinition("body", FieldType.RichText, required: true),
        new FieldDefinition("featuredImage", FieldType.MediaReference, required: false),
        new FieldDefinition("publishDate", FieldType.DateTime, required: false),
        new FieldDefinition("topics", FieldType.MultiTaxonomyReference, required: false,
            options: new { TaxonomyHandle = "topics" }),
    };
}
```

### 2. Register the type

```csharp
// In Program.cs or a ContentTypeModule
builder.Services.AddContentType<NewsArticleTypeDefinition>();
```

### 3. Add a migration script for any new columns or indexes

```sql
-- migrations/V010__add_news_article_type.sql
-- ContentType rows are seeded by the API on startup via the type registry.
-- This migration adds any DB-level changes required for the new type.
-- Example: add a computed column for FTS on a new field pattern.
-- If no schema changes are needed, no migration is required.
```

If your content type only uses built-in field types stored in `FieldsJson`, no migration is needed at all. Add a migration only when you need a structural DB change (new table, new column, new index).

## Adding a Custom Field Type

Custom field types let you render a bespoke React component in the admin form editor.

### 1. Register the backend field type

```csharp
// src/api/VA.CMS.Domain/Content/Fields/GeoPointFieldType.cs
public class GeoPointFieldType : ICustomFieldType
{
    public string TypeName => "geo_point";
    public Type StorageType => typeof(string); // stored as JSON string
    
    public ValidationResult Validate(object? value)
    {
        // validate JSON has {lat, lng}
    }
}

// Register
builder.Services.AddCustomFieldType<GeoPointFieldType>();
```

### 2. Register the React renderer (admin SPA)

```typescript
// src/admin/src/features/contentTypes/customFields/GeoPointField.tsx
import { CustomFieldProps } from '@/types/fields';

export function GeoPointField({ value, onChange, fieldDef }: CustomFieldProps) {
  // render your custom map picker UI using USWDS components for wrapper/labels
  return (
    <div className="usa-form-group">
      <label className="usa-label">{fieldDef.label}</label>
      {/* your custom UI */}
    </div>
  );
}

// src/admin/src/features/contentTypes/customFields/registry.ts
import { registerCustomField } from '@/core/fieldRegistry';
import { GeoPointField } from './GeoPointField';

registerCustomField('geo_point', GeoPointField);
```

## Registering a Custom Public Template

```typescript
// src/public/components/templates/NewsArticleTemplate.tsx
import { ContentEntry } from '@/lib/cms/types';
import { UswdsProse, UswdsBreadcrumb } from '@/components/uswds';

interface NewsArticleTemplateProps {
  entry: ContentEntry;
}

export function NewsArticleTemplate({ entry }: NewsArticleTemplateProps) {
  const { title, summary, body, featuredImage } = entry.fields;
  
  return (
    <main id="main-content">
      <UswdsBreadcrumb items={[{ label: 'News', href: '/news' }, { label: title }]} />
      <div className="grid-container">
        <h1>{title}</h1>
        <p className="usa-intro">{summary}</p>
        <UswdsProse dangerouslySetInnerHTML={{ __html: body }} />
      </div>
    </main>
  );
}

// Register in template registry
// src/public/lib/cms/templateRegistry.ts
import { registerTemplate } from './registry';
import { NewsArticleTemplate } from '@/components/templates/NewsArticleTemplate';

registerTemplate('NewsArticleTemplate', NewsArticleTemplate);
```

## REST API Reference (Key Endpoints)

```
GET    /api/v1/content-types                     List all content types
GET    /api/v1/content-types/{name}              Get type definition

GET    /api/v1/content?type=&status=&page=       List entries (paginated)
POST   /api/v1/content                           Create entry
GET    /api/v1/content/{id}                      Get entry (latest version)
PATCH  /api/v1/content/{id}                      Update entry fields
DELETE /api/v1/content/{id}                      Archive entry

POST   /api/v1/content/{id}/submit-review        Transition: Draft → InReview
POST   /api/v1/content/{id}/approve              Transition: InReview → Approved → Published
POST   /api/v1/content/{id}/return               Return to Draft (body: { comment })
POST   /api/v1/content/{id}/publish              Direct publish (Admin role only)
POST   /api/v1/content/{id}/unpublish            Unpublish (sets back to Approved)
POST   /api/v1/content/{id}/archive              Archive

GET    /api/v1/content/{id}/versions             List all versions
GET    /api/v1/content/{id}/versions/{versionId} Get specific version
POST   /api/v1/content/{id}/versions/{versionId}/restore  Restore a version

GET    /api/v1/media                             List media assets
POST   /api/v1/media/upload                      Upload a file (multipart)
PATCH  /api/v1/media/{id}                        Update metadata
DELETE /api/v1/media/{id}                        Delete (fails if in use)

GET    /api/v1/taxonomy                          List taxonomies
GET    /api/v1/taxonomy/{handle}/terms           List terms
POST   /api/v1/taxonomy/{handle}/terms           Create term

GET    /api/v1/search?q=&type=&from=&to=&page=  Full-text search

GET    /api/v1/navigation/{handle}               Get navigation menu tree

POST   /api/v1/webhooks                          Register webhook (secret returned once)
GET    /api/v1/webhooks                          List webhooks
DELETE /api/v1/webhooks/{id}                     Remove webhook
GET    /api/v1/webhooks/{id}/deliveries          Delivery log, newest first (page, pageSize)
POST   /api/v1/webhooks/{id}/deliveries/{d}/redeliver   Resend a logged payload once
```

Full OpenAPI spec: `/swagger` when running in Development, or exported to `docs/openapi.json`.

## Dependencies and advisories

- NuGet versions are pinned once in `src/api/Directory.Packages.props` (central package management);
  `src/api/Directory.Build.props` turns on `NuGetAudit` (all packages, transitive included) and makes
  NU1901–NU1904 build errors, so `dotnet restore` fails on a new advisory. Check by hand with
  `dotnet list package --vulnerable --include-transitive`.
- npm roots: `src/admin`, `src/public`, `tests/accessibility`. Gate with `npm audit --audit-level=high`
  (dev tooling included — vitest 5 / vite 8 cleared the last dev-only advisories).
- `.github/dependabot.yml` opens weekly grouped PRs for nuget, the three npm roots and GitHub Actions.
- Public site: Next.js 16 on Turbopack (`turbopack.root` is `src/` so the shared USWDS theme resolves);
  `params` / `searchParams` are Promises; `revalidateTag(tag, 'max')`; lint is plain `eslint .`.

## CI, security scanning and branch protection

**On-prem.** The workflows are written for GitHub Actions syntax (GitHub Enterprise Server runs them
unchanged). Set the repository variable `CI_RUNNER` to your self-hosted runner label (for example
`self-hosted,linux,docker`) — every job uses it and falls back to `ubuntu-latest` only when it is unset. Runners
need Docker (Testcontainers and the SQL Server service container), .NET 8, Node 22 and a mirror for NuGet, npm
and `mcr.microsoft.com` images. CodeQL requires GitHub Advanced Security; set `SAST_ENGINE=semgrep` to run
Semgrep OSS instead. gitleaks, CycloneDX, `dotnet list package --vulnerable` and `npm audit` are plain CLIs.

`ci.yml` (build/test/package + accessibility) and `security.yml` (CodeQL, advisories, gitleaks,
SBOM) run on pull requests and pushes to `main`; `security.yml` also runs weekly. Both must be
required status checks on `main`, with code-owner review (`.github/CODEOWNERS`). Repository
admins apply that once with:

```bash
gh api -X PUT repos/jwilson411/va-cms/branches/main/protection --input - <<'JSON'
{
  "required_status_checks": { "strict": true,
    "contexts": ["API — build, test, coverage, OpenAPI drift", "Admin SPA — typecheck, test, build",
                 "Public site — typecheck, test, build", "CodeQL (csharp)", "CodeQL (javascript-typescript)",
                 "Dependency advisories (NuGet + npm)", "Secret scan (gitleaks)"] },
  "enforce_admins": true,
  "required_pull_request_reviews": { "require_code_owner_reviews": true, "required_approving_review_count": 1 },
  "restrictions": null
}
JSON
```

Regenerate the OpenAPI snapshot after changing a controller:
`UPDATE_OPENAPI_SNAPSHOT=true dotnet test --filter OpenApiSnapshot` (in `src/api`), then commit `docs/openapi.json`.

## GraphQL

Endpoint: `/api/graphql` — off by default; turn on the `features.graphql` site setting.  
Playground: `/api/graphql/ui` (Development only; introspection is also Development-only)

### Audiences

| Caller | `contentEntries` / `contentEntry(id)` | `mediaAsset(id)` | `mediaAssets` | Hidden fields |
|---|---|---|---|---|
| Anonymous | Published entries only (`status` argument is overridden inside `usp_ContentEntry_List`) | only when a Published entry references the asset (`MediaUsage`) | denied | `ownerId`, `uploadedById`, `storagePath`, `isVirusScanPassed` |
| `Authorization: Bearer <CMS JWT>` with any role (`CanRead`) | all statuses | any asset | allowed | none |

Every request is bounded: max depth 8, Hot Chocolate cost limits (2 000 field / type cost), 10 s execution timeout.
The scheme names, limits and audience logic live in `VA.CMS.API/GraphQL/` (`GraphQLAudience`, `GraphQLLimits`).

```graphql
# Example: fetch the 10 most recent published news articles
query RecentNews {
  contentEntries(
    type: "news_article"
    status: PUBLISHED
    first: 10
    orderBy: { field: PUBLISHED_AT, direction: DESC }
  ) {
    nodes {
      id
      slug
      fields {
        ... on NewsArticleFields {
          title
          summary
          featuredImage {
            storageUrl
            altText
          }
        }
      }
      publishedAt
    }
    pageInfo {
      hasNextPage
      endCursor
    }
  }
}
```

## Webhook Events

| Event | Payload |
|---|---|
| `content.published` | `{ entryId, contentType, slug, publishedAt, actorId }` |
| `content.unpublished` | `{ entryId, contentType, slug, actorId }` |
| `content.archived` | `{ entryId, contentType, slug, actorId }` |
| `content.submitted_review` | `{ entryId, contentType, slug, actorId }` |
| `media.uploaded` | `{ assetId, fileName, mimeType, uploadedById }` |

Webhooks are signed with HMAC-SHA256. Verify the `X-CMS-Signature` header:

```typescript
import crypto from 'crypto';

function verifyWebhook(payload: string, signature: string, secret: string): boolean {
  const expected = crypto
    .createHmac('sha256', secret)
    .update(payload)
    .digest('hex');
  return crypto.timingSafeEqual(
    Buffer.from(signature),
    Buffer.from(`sha256=${expected}`)
  );
}
```

### Destination policy (#168)

The API POSTs from inside the network, so a webhook URL is treated as an egress rule, not a free-text
field. Registration and every delivery run `WebhookDestinationPolicy`:

- `https://` outside Development, no `user:pass@` in the URL.
- The host must match the `webhooks.allowedHosts` site setting (exact name or `*.suffix`). The default
  is empty, which means **nothing is delivered** outside Development until an admin adds the subscriber.
- The address the host resolves to is checked inside the HTTP connect callback (`WebhookHttpHandler`), so
  a DNS answer that changes after registration (rebinding) is refused at the socket: loopback,
  link-local, multicast and unspecified always; RFC 1918 / CGNAT / ULA unless
  `webhooks.allowPrivateNetworks` is on. Redirects are not followed.
- Locally (`ASPNETCORE_ENVIRONMENT=Development`) all of this is relaxed so `http://localhost:3000/api/revalidate`
  keeps working.

Refused deliveries appear in the delivery log (`Refused: …`) and are not retried; **Admin → Webhooks**
shows the log per webhook with a *Redeliver* action. Secrets are stored as Data Protection payloads
(`DataProtection:KeysPath`, see DEPLOYMENT.md) and are unprotected only to sign a delivery.

## CLI Tool

```bash
# Install
dotnet tool install --global VA.CMS.CLI

# Commands
vacms db migrate              # Apply pending migrations (deployment account; --check / --dry-run)
vacms db provision-logins     # Create or rotate vacms_app / vacms_readonly (--app-password, --readonly-password)
vacms db seed --demo          # Seed demo content
vacms content-type scaffold NewsArticle  # Scaffold new type definition
vacms migrate sharepoint --export ./sharepoint-export.zip  # Import from SharePoint
vacms health --url https://cms.va.gov/api  # Check deployment health
```

## Testing

```bash
# Unit + integration tests (API)
cd src/api
dotnet test

# Unit tests (admin SPA)
cd src/admin
npm run test

# Accessibility tests (requires running app)
cd tests/accessibility
npm run test:a11y -- --url http://localhost:3000/admin

# Load tests (requires k6)
cd tests/load
k6 run --vus 50 --duration 60s content-api.js
```

## Code Conventions

- **API:** Follow Microsoft ASP.NET Core conventions. Controllers are thin — logic lives in domain services.
- **React:** Functional components only. Hooks for all state. No class components.
- **USWDS:** Both apps compile USWDS 3.x from Sass with the VA theme (`src/theme/uswds/_va-settings.scss`
  — VA Blue `#003e73` as `primary`, VA Gold `#f9c642` as `accent-warm`, Public Sans for every font role).
  Change tokens there, never in app code. App-specific styles are `.scss` files that
  `@use "<relative path>/theme/uswds/va-settings" as *;` and use `color("primary")`, `units(2)`, etc.
  rather than hard-coded hex values. Never override with `!important`. USWDS fonts/images are served
  from each app's `public/uswds/` (copied by `npm run uswds:assets`, run automatically before dev/build).
- **TypeScript:** `strict: true`. No `any` except in test utilities. Explicit return types on all exported functions.
- **SQL:** All queries via EF Core LINQ or raw `SqlQuery<>` with parameterized inputs. No string interpolation in SQL.
- **Secrets:** Never log secrets, connection strings, or tokens. Use `IOptionsMonitor<>` with validation.
