# Data Model
## VA CMS — USWDS-Compliant Content Management System

All tables use SQL Server conventions: `PascalCase` names, `Id` as primary key (BIGINT IDENTITY or UNIQUEIDENTIFIER), `CreatedAt` / `UpdatedAt` timestamps on all mutable tables.

---

## Core Tables

### ContentType
Defines a content schema — a named type with an ordered list of field definitions.

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| Name | NVARCHAR(100) | Unique. Machine name e.g. "standard_page" |
| DisplayName | NVARCHAR(200) | Human name e.g. "Standard Page" |
| Description | NVARCHAR(1000) | Shown in "Create new" picker |
| TemplateId | NVARCHAR(200) | React component identifier for public rendering |
| IsSystemType | BIT | Protected from deletion by UI |
| AllowWorkflow | BIT | Whether content of this type goes through approval |
| FieldSchemaJson | NVARCHAR(MAX) | JSON — ordered array of field definitions |
| CreatedAt | DATETIME2 | |
| UpdatedAt | DATETIME2 | |

### ContentEntry
A single piece of content. Immutable after creation — all edits produce a new ContentVersion.

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| ContentTypeId | BIGINT | FK → ContentType |
| Slug | NVARCHAR(500) | URL path segment. Unique per locale. |
| Locale | NVARCHAR(10) | e.g. "en-US", "es" |
| Status | NVARCHAR(20) | ENUM: Draft, InReview, Approved, Published, Archived |
| PublishedVersionId | BIGINT | FK → ContentVersion (nullable) |
| ScheduledPublishAt | DATETIME2 | Nullable — scheduled future publish |
| ScheduledExpireAt | DATETIME2 | Nullable — scheduled future unpublish |
| OwnerId | BIGINT | FK → User — original creator |
| CreatedAt | DATETIME2 | |
| UpdatedAt | DATETIME2 | |

**Indexes:** `IX_ContentEntry_Slug_Locale` (unique), `IX_ContentEntry_Status`, `IX_ContentEntry_ContentTypeId`

### ContentVersion
A point-in-time snapshot of a content entry's fields.

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| ContentEntryId | BIGINT | FK → ContentEntry |
| VersionNumber | INT | Auto-increment per entry |
| FieldsJson | NVARCHAR(MAX) | Full field payload as JSON. Rich text fields stored as CommonMark Markdown strings — never raw HTML. |
| RenderedFieldsJson | NVARCHAR(MAX) | Cached rendered HTML for each Markdown field, generated at publish time. Invalidated and regenerated on restore or re-publish. |
| Status | NVARCHAR(20) | Status at time of snapshot |
| AuthorId | BIGINT | FK → User — who created this version |
| ChangeNote | NVARCHAR(1000) | Optional description of changes |
| CreatedAt | DATETIME2 | |

**Markdown storage note:** `FieldsJson` stores rich text fields as raw Markdown (CommonMark). The API exposes both `markdownBody` (raw, for headless consumers) and `renderedBody` (server-rendered HTML via Markdig, USWDS-safe). The same Markdig pipeline is used in the live preview, eliminating preview/publish drift.

**Indexes:** `IX_ContentVersion_EntryId_Version` (unique composite)

### MediaAsset
Uploaded file metadata. The file itself is stored externally; this table stores the reference.

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| FileName | NVARCHAR(500) | Original uploaded filename |
| StoragePath | NVARCHAR(2000) | Relative path in storage backend |
| StorageBackend | NVARCHAR(50) | "local", "unc" (on-prem only) |
| MimeType | NVARCHAR(200) | e.g. "image/jpeg" |
| FileSizeBytes | BIGINT | |
| AltText | NVARCHAR(500) | Required for images before asset can be published |
| Title | NVARCHAR(500) | Optional display title |
| Description | NVARCHAR(2000) | Optional long description |
| Width | INT | Nullable — pixels, images only |
| Height | INT | Nullable — pixels, images only |
| Tags | NVARCHAR(MAX) | JSON array of tag strings |
| UploadedById | BIGINT | FK → User |
| IsVirusScanPassed | BIT | NULL = not scanned, 1 = clean, 0 = flagged |
| CreatedAt | DATETIME2 | |
| UpdatedAt | DATETIME2 | |

### MediaUsage
Tracks which content entries reference each media asset.

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| MediaAssetId | BIGINT | FK → MediaAsset |
| ContentEntryId | BIGINT | FK → ContentEntry |
| FieldName | NVARCHAR(100) | Which field references this asset |
| CreatedAt | DATETIME2 | |

---

## Users and Access Control

### User
Synced from Azure AD. Never stores passwords.

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| ExternalId | NVARCHAR(200) | AAD Object ID / UPN |
| Email | NVARCHAR(500) | Unique |
| DisplayName | NVARCHAR(500) | |
| IsActive | BIT | Set to 0 to soft-deactivate |
| LastLoginAt | DATETIME2 | |
| CreatedAt | DATETIME2 | |
| UpdatedAt | DATETIME2 | |

### Role
Built-in and custom roles.

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| Name | NVARCHAR(100) | Unique. e.g. "ContentOwner", "Editor" |
| DisplayName | NVARCHAR(200) | |
| IsSystemRole | BIT | Protected from deletion |

### UserRole
Many-to-many: users to roles, optionally scoped to a content section.

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| UserId | BIGINT | FK → User |
| RoleId | BIGINT | FK → Role |
| SectionId | BIGINT | FK → ContentSection (nullable — null = global) |
| GrantedById | BIGINT | FK → User |
| CreatedAt | DATETIME2 | |

### ContentSection
Named groupings of content (e.g., "HR", "Benefits", "News") for scoped permissions.

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| Name | NVARCHAR(200) | |
| SlugPrefix | NVARCHAR(500) | URL prefix that defines this section |
| ParentSectionId | BIGINT | FK → ContentSection (nullable) |

---

## Navigation

### NavigationMenu
Named navigation structures (e.g., "Primary Nav", "Footer Nav").

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| Name | NVARCHAR(200) | |
| Handle | NVARCHAR(100) | Machine name used in templates |
| CreatedAt | DATETIME2 | |
| UpdatedAt | DATETIME2 | |

### NavigationItem
A single item within a navigation menu (recursive for nested menus).

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| MenuId | BIGINT | FK → NavigationMenu |
| ParentItemId | BIGINT | FK → NavigationItem (nullable) |
| Label | NVARCHAR(500) | Display text |
| Url | NVARCHAR(2000) | Absolute or relative URL |
| ContentEntryId | BIGINT | FK → ContentEntry (nullable — dynamic link) |
| Target | NVARCHAR(10) | "_self" or "_blank" |
| SortOrder | INT | Within parent |
| IsVisible | BIT | |

### Redirect

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| FromPath | NVARCHAR(2000) | Source URL path |
| ToPath | NVARCHAR(2000) | Destination URL |
| StatusCode | INT | 301 or 302 |
| IsActive | BIT | |
| CreatedById | BIGINT | FK → User |
| CreatedAt | DATETIME2 | |

---

## Taxonomy

### Taxonomy
A named vocabulary of terms (e.g., "Topics", "Policy Areas").

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| Name | NVARCHAR(200) | |
| Handle | NVARCHAR(100) | Machine name |
| IsHierarchical | BIT | Whether terms can have parents |

### TaxonomyTerm

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| TaxonomyId | BIGINT | FK → Taxonomy |
| ParentTermId | BIGINT | FK → TaxonomyTerm (nullable) |
| Name | NVARCHAR(500) | |
| Slug | NVARCHAR(500) | |
| SortOrder | INT | |

### ContentEntryTerm
Many-to-many: content entries to taxonomy terms.

| Column | Type | Notes |
|---|---|---|
| ContentEntryId | BIGINT | FK → ContentEntry |
| TaxonomyTermId | BIGINT | FK → TaxonomyTerm |

---

## Workflow and Audit

### WorkflowTransition
Tracks each status change on a content entry.

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| ContentEntryId | BIGINT | FK → ContentEntry |
| ContentVersionId | BIGINT | FK → ContentVersion |
| FromStatus | NVARCHAR(20) | |
| ToStatus | NVARCHAR(20) | |
| ActorId | BIGINT | FK → User |
| Comment | NVARCHAR(2000) | Required when returning to draft |
| CreatedAt | DATETIME2 | |

### Notification
In-app inbox row for a workflow event (issue #38, FR-WORKFLOW-02/03). Written by
`usp_Notification_CreateForWorkflowEvent` right after a successful transition; one row per recipient.
The procedure also returns the rows it created joined to the recipient's `Email` and `DisplayName`,
and the API sends one email per row (issue #39) — inbox and email always agree on who was told.

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| RecipientUserId | BIGINT | FK → User |
| EventType | NVARCHAR(50) | `ReviewRequested` (→ publish-capable users, global or section-matching), `ContentApproved` / `ContentReturned` / `ContentPublished` (→ entry owner). The actor is never notified. |
| ContentEntryId | BIGINT | FK → ContentEntry — the panel links to `/admin/content/{id}/edit` |
| ContentTitle | NVARCHAR(500) | Snapshot of the latest version's `$.title` (falls back to slug) |
| Message | NVARCHAR(1000) | Rendered description, e.g. `alice submitted "Page" for review` |
| ActorId | BIGINT | FK → User (nullable; NULL for a scheduled publish by the system actor) |
| Comment | NVARCHAR(2000) | Reviewer's return comment |
| IsRead / ReadAt | BIT / DATETIME2 | Set by `usp_Notification_MarkRead` / `MarkAllRead` (own rows only) |
| CreatedAt | DATETIME2 | Index `(RecipientUserId, IsRead, CreatedAt DESC)` |

### AuditLog
Immutable log of all system mutations.

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| ActorId | BIGINT | FK → User (nullable for system actions) |
| ActorEmail | NVARCHAR(500) | Denormalized — preserved if user deleted |
| EntityType | NVARCHAR(100) | e.g. "ContentEntry", "MediaAsset", "User" |
| EntityId | NVARCHAR(100) | String PK of affected entity |
| Action | NVARCHAR(50) | e.g. "Create", "Update", "Delete", "Publish" |
| DiffJson | NVARCHAR(MAX) | JSON patch of what changed (null for creates/deletes) |
| IpAddress | NVARCHAR(50) | |
| UserAgent | NVARCHAR(500) | |
| CreatedAt | DATETIME2 | Clustered index |

**Note:** AuditLog has no UPDATE or DELETE permissions for the app service account. Write-only. Retention managed by DBA policy.

---

## Developer Extensibility

### Webhook
Registered webhook endpoints.

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| Name | NVARCHAR(200) | |
| Url | NVARCHAR(2000) | HTTPS endpoint |
| Secret | NVARCHAR(500) | HMAC signing key (encrypted at rest) |
| EventsJson | NVARCHAR(MAX) | JSON array of subscribed event names |
| IsActive | BIT | |
| CreatedById | BIGINT | FK → User |
| CreatedAt | DATETIME2 | |

### WebhookDelivery
Log of webhook delivery attempts.

| Column | Type | Notes |
|---|---|---|
| Id | BIGINT IDENTITY | PK |
| WebhookId | BIGINT | FK → Webhook |
| EventName | NVARCHAR(100) | |
| PayloadJson | NVARCHAR(MAX) | |
| ResponseStatusCode | INT | Nullable |
| AttemptNumber | INT | |
| DeliveredAt | DATETIME2 | |
| ErrorMessage | NVARCHAR(2000) | Nullable |

---

## Search (Full-Text)

SQL Server Full-Text indexes on:
- `ContentVersion.FieldsJson` (indexed via a computed column extracting plain text)
- `MediaAsset.AltText`, `MediaAsset.Title`, `MediaAsset.Description`
- `TaxonomyTerm.Name`

Full-text catalog: `CMS_FTC`  
Change tracking: `AUTO`
