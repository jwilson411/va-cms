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

**Same pipeline in live preview:** `POST /api/v1/preview/render` accepts Markdown, returns HTML. The admin SPA calls this on a debounced 500ms interval. The preview panel always matches what publish produces — no drift.

**Admin WYSIWYG editor:** Content owners use a formatted rich text surface and never write raw Markdown. Buttons in the toolbar produce Markdown under the hood transparently.

```typescript
// src/admin/src/features/contentTypes/fields/MarkdownField.tsx
// Using Milkdown (ProseMirror-based Markdown WYSIWYG)
import { Editor } from '@milkdown/react';
import { commonmark } from '@milkdown/preset-commonmark';

export function MarkdownField({ value, onChange, fieldDef }: CustomFieldProps) {
  return (
    <div className="usa-form-group">
      <label className="usa-label" htmlFor={fieldDef.name}>
        {fieldDef.label}
        {fieldDef.required && <abbr title="required" className="usa-required"> *</abbr>}
      </label>
      {/* WYSIWYG surface — user sees formatting, storage is Markdown */}
      <Editor defaultValue={value} onChange={onChange} plugins={[commonmark]} />
    </div>
  );
}
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
     - API validates httpOnly cookie
     - Issues new JWT
8. If AD account is disabled: next refresh → 401 → SPA clears state → login redirect
```

**Dev setup (no AD):** Set `Auth:Mode=DevBypass` in `appsettings.Development.json`. The API accepts a `X-Dev-User: alice@va.gov` header and issues a JWT for that UPN without AD. Never ship this mode in Production.

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
| ORM | Entity Framework Core 8 | https://docs.microsoft.com/ef/core |
| GraphQL | Hot Chocolate | https://chillicream.com/docs/hotchocolate |
| Admin UI | React 18 + TypeScript | https://react.dev |
| Design System | USWDS 3.x | https://designsystem.digital.gov |
| Admin State | TanStack Query v5 | https://tanstack.com/query |
| Rich Text | TipTap | https://tiptap.dev |
| Public Site | Next.js 14 (App Router) | https://nextjs.org/docs |
| Database | SQL Server 2019+ / EF Core | |
| Auth | Microsoft.Identity.Web | |

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

### 3. Run migrations (if schema changes affect computed columns or FTS)

```bash
dotnet ef migrations add AddNewsArticleType --project VA.CMS.Infrastructure --startup-project VA.CMS.API
dotnet ef database update
```

Content types are stored in the `ContentType` table — no migration needed for the type definition itself. EF migrations are only needed for structural DB changes.

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

POST   /api/v1/webhooks                          Register webhook
GET    /api/v1/webhooks                          List webhooks
DELETE /api/v1/webhooks/{id}                     Remove webhook
```

Full OpenAPI spec: `/swagger` when running in Development, or exported to `docs/openapi.json`.

## GraphQL

Endpoint: `/api/graphql`  
Playground: `/api/graphql/ui` (Development only)

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

## CLI Tool

```bash
# Install
dotnet tool install --global VA.CMS.CLI

# Commands
vacms db migrate              # Run pending migrations
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
- **USWDS:** Import from `@uswds/uswds`. Use CSS custom properties for any overrides. Never override with `!important`.
- **TypeScript:** `strict: true`. No `any` except in test utilities. Explicit return types on all exported functions.
- **SQL:** All queries via EF Core LINQ or raw `SqlQuery<>` with parameterized inputs. No string interpolation in SQL.
- **Secrets:** Never log secrets, connection strings, or tokens. Use `IOptionsMonitor<>` with validation.
