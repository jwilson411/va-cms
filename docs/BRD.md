# Business Requirements Document (BRD)
## VA CMS — USWDS-Compliant Content Management System

**Version:** 1.0  
**Status:** Draft  
**Owner:** Justin Wilson  
**Last Updated:** 2026-09-14  

---

## 1. Executive Summary

The VA CMS is a purpose-built content management system designed to replace aging SharePoint 2016 on-premises deployments across VA web properties. It provides a modern, accessible, Section 508-compliant authoring experience based on the **U.S. Web Design System (USWDS)**, while giving development teams the full extensibility needed to build custom content types, embedded applications, and advanced integrations.

The system is built on a React + ASP.NET Core + Microsoft SQL Server stack — technology already authorized and operated by most VA development teams — eliminating the Linux/PHP expertise gap that makes Drupal difficult to staff and maintain in VA environments.

---

## 2. Background and Problem Statement

### 2.1 Current State
- Many VA offices run SharePoint 2016 on-premises, which reached end-of-support in July 2026
- SharePoint migration to SharePoint Online is blocked for many offices due to data classification or network policy
- Umbraco, DotNetNuke, and similar older platforms are either not TRM-authorized or carry POA&M requirements on all current versions
- Drupal 11 is TRM authorized but requires PHP/Linux expertise and a Drupal-specific development model most VA .NET teams do not have

### 2.2 Gap
No TRM-authorized, self-hosted CMS exists that:
- Runs on the Windows/.NET/MSSQL stack VA teams already operate
- Ships USWDS-compliant UI without custom theming work
- Separates content-owner editing from developer extensibility cleanly

### 2.3 Solution
Build a self-hosted CMS on a modern, VA-familiar stack. File for VA TRM inclusion during or after MVP build.

---

## 3. Goals and Non-Goals

### 3.1 Goals
- Allow non-technical content owners to create, edit, publish, schedule, and archive content without developer involvement
- Allow developers to define custom content types, field schemas, workflows, and embedded React components
- Deliver 100% USWDS 3.x compliant UI for all public-facing pages rendered by the system
- Achieve Section 508 WCAG 2.1 AA conformance across admin and public interfaces
- Support Windows-integrated authentication (Azure AD / SAML / OIDC) — no separate user database required for VA deployments
- Run on IIS + Windows Server or containerized on Windows containers
- Support on-premises deployment with no mandatory cloud dependencies
- Provide REST + GraphQL APIs for headless/decoupled use cases

### 3.2 Non-Goals (MVP)
- Real-time collaborative editing (post-MVP)
- Native mobile app (web-responsive only)
- Full document management / records management (not a SharePoint Files replacement)
- Workflow approval chains beyond 2-level (author → reviewer → publish)
- Multi-tenancy across separate VA organizations (post-MVP)

---

## 4. Stakeholders

| Role | Responsibilities |
|---|---|
| Content Owners | Create, edit, schedule, and publish content pages and structured entries |
| Reviewers / Editors | Review submitted content and approve or return for revision |
| Site Administrators | Manage users, roles, navigation, and site settings |
| Developers | Define content types, build custom field types, embed React components, extend APIs |
| System Administrators | Deploy, configure, patch, and monitor the application |
| VA IT Security | Authorize the technology via TRM process, review ATO artifacts |

---

## 5. Functional Requirements

### 5.1 Content Authoring (FR-AUTH)

**FR-AUTH-01** — Content owners shall be able to create new content entries using a guided, USWDS-styled form interface without writing HTML or code.

**FR-AUTH-02** — The rich text editor shall produce semantic, accessible HTML compliant with USWDS typography standards. Inline style overrides that break accessibility shall be blocked.

**FR-AUTH-03** — Content owners shall be able to upload images and documents, with automatic metadata capture (alt text required before publish).

**FR-AUTH-04** — Content shall support scheduled publish and expiry dates. The system shall automatically publish and unpublish content on schedule without manual intervention.

**FR-AUTH-05** — All content shall be versioned. Content owners shall be able to preview any historical version and promote a prior version to current.

**FR-AUTH-06** — Content owners shall be able to save drafts without publishing. A draft shall not affect the live site.

**FR-AUTH-07** — Content owners shall be able to duplicate an existing entry as a starting point for a new one.

**FR-AUTH-08** — The authoring interface shall provide a live preview of how the content will render on the public site before publishing.

**FR-AUTH-09** — The WYSIWYG editor shall enforce USWDS heading hierarchy (H1 is page title only; editor starts at H2) to prevent structural accessibility violations.

### 5.2 Content Types and Schema (FR-SCHEMA)

**FR-SCHEMA-01** — Developers shall be able to define content types with structured field schemas using a code-first TypeScript/C# definition approach.

**FR-SCHEMA-02** — The system shall support the following built-in field types: Short Text, Long Text / Rich Text, Number, Boolean, Date/Time, URL, Email, File/Media, Single Reference (relation to another content entry), Multi-Reference, Repeating Field Group, and JSON (raw structured data for advanced use).

**FR-SCHEMA-03** — Content types shall support inheritance — a base "Page" type that specialized types extend.

**FR-SCHEMA-04** — Developers shall be able to embed custom React components as field types (e.g., a custom map picker, a data visualization widget) without modifying core CMS code.

**FR-SCHEMA-05** — Schema changes shall be managed through versioned migrations, not destructive in-place edits.

**FR-SCHEMA-06** — The admin UI shall display a visual content model diagram showing all types and their relationships.

### 5.3 Navigation and Site Structure (FR-NAV)

**FR-NAV-01** — Site administrators shall be able to manage primary, secondary, and footer navigation menus through the admin interface without code changes.

**FR-NAV-02** — The system shall support hierarchical navigation up to 3 levels deep.

**FR-NAV-03** — Navigation items shall support manual ordering via drag-and-drop.

**FR-NAV-04** — Navigation changes shall preview before publishing.

**FR-NAV-05** — URLs (slugs) shall be human-readable, auto-generated from the content title, and editable. Slug changes shall create 301 redirects from the old URL automatically.

**FR-NAV-06** — The system shall maintain a redirect management table where administrators can manually create, edit, and delete 301/302 redirects.

### 5.4 Media Management (FR-MEDIA)

**FR-MEDIA-01** — The system shall provide a central media library shared across all content types.

**FR-MEDIA-02** — Image uploads shall support automatic resizing and format conversion (WebP output) for performance.

**FR-MEDIA-03** — The media library shall support tagging and search to find assets across a large collection.

**FR-MEDIA-04** — Uploaded files shall be virus-scanned before storage (integration point with VA endpoint security tools).

**FR-MEDIA-05** — Alt text shall be stored as a property of the media asset and shall be required before the asset can be used in published content.

**FR-MEDIA-06** — The system shall track which content entries use each media asset (usage tracking) so administrators can safely delete unused assets.

**FR-MEDIA-07** — Storage backend shall be configurable: local filesystem (on-prem), network share (UNC path), or Azure Blob Storage.

### 5.5 User Management and Access Control (FR-USERS)

**FR-USERS-01** — The system shall integrate with Azure Active Directory via OIDC/SAML for authentication. No separate CMS password shall be required for VA users.

**FR-USERS-02** — The system shall support Windows-integrated authentication for intranet deployments where AAD federation is not available.

**FR-USERS-03** — The system shall implement role-based access control with the following built-in roles: Content Owner, Editor/Reviewer, Site Administrator, Developer/Admin, System Administrator, Read-Only.

**FR-USERS-04** — Roles shall be assignable at the site level and optionally scoped to specific content sections (e.g., a content owner who can only edit the "HR" section).

**FR-USERS-05** — All user actions (create, edit, publish, delete, role change) shall be written to an immutable audit log.

**FR-USERS-06** — Administrators shall be able to view the audit log filtered by user, action type, content type, and date range.

**FR-USERS-07** — The system shall support content ownership — an entry is "owned" by its creator. Ownership can be reassigned by an administrator.

### 5.6 Publishing Workflow (FR-WORKFLOW)

**FR-WORKFLOW-01** — Content shall move through states: Draft → In Review → Approved → Published → Archived.

**FR-WORKFLOW-02** — Content owners shall be able to submit content for review. Reviewers shall be notified (email + in-app) when content is awaiting review.

**FR-WORKFLOW-03** — Reviewers shall be able to approve content (advancing to Published) or return it to Draft with a required comment explaining the reason.

**FR-WORKFLOW-04** — Site Administrators shall be able to bypass the review workflow and publish directly (for urgent corrections).

**FR-WORKFLOW-05** — Workflow state transitions shall be recorded in the version history with the acting user and timestamp.

**FR-WORKFLOW-06** — The system shall support a configurable "auto-approve" mode per content type for sites where workflow is not required.

### 5.7 Search (FR-SEARCH)

**FR-SEARCH-01** — The public site shall expose a USWDS-compliant search interface using the USWDS Search component.

**FR-SEARCH-02** — Full-text search shall index all published content, including structured field text and document attachments (PDF, DOCX).

**FR-SEARCH-03** — Search shall support filtering by content type, date range, and tags/categories.

**FR-SEARCH-04** — Search results shall be ranked by relevance. Administrators shall be able to pin specific results to the top for key queries.

**FR-SEARCH-05** — Search shall be powered by SQL Server Full-Text Search by default. The architecture shall support swapping in Elasticsearch for larger deployments.

**FR-SEARCH-06** — The admin interface shall surface search analytics: top queries, zero-result queries, and click-through rates.

### 5.8 Taxonomy and Categorization (FR-TAXONOMY)

**FR-TAXONOMY-01** — The system shall support flat tag lists and hierarchical category trees (up to 5 levels deep).

**FR-TAXONOMY-02** — Tags and categories shall be managed through the admin interface.

**FR-TAXONOMY-03** — Content types shall be configurable to use specific taxonomies (e.g., "News" uses "Topics" taxonomy; "Policies" uses "Policy Areas" taxonomy).

**FR-TAXONOMY-04** — Tag and category pages shall be auto-generated, listing all content associated with that term.

### 5.9 Multilingual / Translation (FR-I18N)

**FR-I18N-01** — The system architecture shall support content entries in multiple languages (one row per locale per entry).

**FR-I18N-02** — The USWDS Language Selector component shall be used for language switching on public pages.

**FR-I18N-03** — Translation workflow: a content entry in the default locale (en-US) can be sent for translation. Translators see the source alongside the target field.

**FR-I18N-04** — Untranslated pages shall fall back to the default locale with an accessible "This page is not available in your selected language" alert (USWDS Alert component).

*Note: FR-I18N-03 and FR-I18N-04 are post-MVP.*

### 5.10 Developer Extensibility (FR-DEV)

**FR-DEV-01** — All CMS data shall be accessible via a versioned REST API (OpenAPI 3.0 spec auto-generated).

**FR-DEV-02** — The system shall expose a GraphQL endpoint for flexible, efficient content queries.

**FR-DEV-03** — Developers shall be able to define custom page templates as React components. The CMS maps a content type to a template at render time.

**FR-DEV-04** — The system shall support server-side rendering (SSR) via Next.js or equivalent for SEO and performance on public-facing pages.

**FR-DEV-05** — The system shall provide a plugin/extension architecture allowing developers to add custom admin UI pages, field type renderers, and API endpoints without forking core.

**FR-DEV-06** — A CLI tool shall be provided for scaffolding new content types, running migrations, and seeding demo data.

**FR-DEV-07** — The API shall support webhook subscriptions — developers can register endpoints to be notified when content is published, updated, or deleted.

### 5.11 Analytics and Reporting (FR-ANALYTICS)

**FR-ANALYTICS-01** — The admin dashboard shall display a summary of content activity: total published entries, drafts pending review, recently published, scheduled for future publish.

**FR-ANALYTICS-02** — The system shall integrate with the Digital Analytics Program (DAP) / Google Analytics 4 via the standard government analytics script tag.

**FR-ANALYTICS-03** — Administrators shall be able to generate reports on: content by author, content by type, content by publish date range, and media storage usage.

**FR-ANALYTICS-04** — The system shall track and display page view counts sourced from the analytics integration.

### 5.12 Security (FR-SECURITY)

**FR-SECURITY-01** — All communications shall be HTTPS-only. HTTP requests shall redirect to HTTPS.

**FR-SECURITY-02** — The API shall enforce JWT-based authentication with short-lived access tokens and secure refresh token rotation.

**FR-SECURITY-03** — The system shall implement Content Security Policy (CSP) headers on all responses.

**FR-SECURITY-04** — SQL access shall use parameterized queries / ORMs only. No dynamic SQL string concatenation.

**FR-SECURITY-05** — CSRF protection shall be enforced on all state-changing endpoints.

**FR-SECURITY-06** — File uploads shall be validated against an allow-list of permitted MIME types. Files shall be stored outside the web root.

**FR-SECURITY-07** — The system shall support STIG hardening guidelines for the IIS hosting configuration.

**FR-SECURITY-08** — Secrets (connection strings, API keys) shall be stored in environment variables or a secrets manager, never in source code or config files checked into version control.

---

## 6. Non-Functional Requirements

| ID | Requirement |
|---|---|
| NFR-PERF-01 | Public page loads shall achieve a Google Lighthouse Performance score ≥ 90 on a standard desktop connection |
| NFR-PERF-02 | API responses for content retrieval shall complete in < 200ms at the 95th percentile under normal load |
| NFR-PERF-03 | The admin authoring interface shall load in < 3 seconds on the VA network |
| NFR-A11Y-01 | All admin and public UI shall conform to WCAG 2.1 Level AA |
| NFR-A11Y-02 | All USWDS components used shall be consumed from the official USWDS package, not rewritten |
| NFR-A11Y-03 | The system shall pass automated accessibility testing (axe-core) with zero critical violations |
| NFR-SEC-01 | The application shall undergo a VA-standard security assessment (penetration test + SAST) prior to production deployment |
| NFR-SEC-02 | All dependencies shall be tracked via a software bill of materials (SBOM) and scanned for CVEs in the CI pipeline |
| NFR-OPS-01 | The system shall expose health-check endpoints compatible with standard VA monitoring tools |
| NFR-OPS-02 | Structured JSON logs shall be written to a configurable log sink (file, Windows Event Log, Splunk HTTP Event Collector) |
| NFR-OPS-03 | Database migrations shall be idempotent and run automatically on deployment |
| NFR-OPS-04 | The system shall support blue-green deployments with zero-downtime database migrations |
| NFR-SCALE-01 | The system shall support horizontal scaling of the web tier behind a load balancer with session affinity or stateless JWT auth |
| NFR-SCALE-02 | The system shall support content volumes of up to 500,000 entries without degraded search or browse performance |

---

## 7. User Stories

### Content Owner
- As a content owner, I can log in with my VA network credentials so I don't need a separate password.
- As a content owner, I can create a new page by filling out a simple form so I don't need to write HTML.
- As a content owner, I can upload an image, set its alt text, and insert it into my content so the page is accessible.
- As a content owner, I can save my work as a draft and come back later so I don't lose progress.
- As a content owner, I can schedule content to publish on a specific date and time so I can coordinate with announcements.
- As a content owner, I can submit my content for review and see its status in the review queue.
- As a content owner, I can preview my content exactly as it will appear on the live site before publishing.

### Editor / Reviewer
- As an editor, I receive a notification when content is submitted for my review.
- As an editor, I can approve content and it publishes immediately, or return it to the author with comments.
- As an editor, I can see the full version history of a piece of content to understand how it evolved.

### Site Administrator
- As a site admin, I can manage the navigation menu through a visual drag-and-drop interface without a code deploy.
- As a site admin, I can add and remove user accounts and assign roles.
- As a site admin, I can create redirect rules when content moves to a new URL.
- As a site admin, I can view the audit log to see who changed what and when.

### Developer
- As a developer, I can define a new content type in TypeScript/C# and run a migration to make it available in the admin.
- As a developer, I can register a React component as a custom field type renderer.
- As a developer, I can consume the REST or GraphQL API to build a decoupled frontend.
- As a developer, I can register a webhook to trigger a downstream process when content is published.
- As a developer, I can embed a fully custom React application inside a CMS-managed page without fighting the template system.

---

## 8. System Architecture Overview

See [ARCHITECTURE.md](ARCHITECTURE.md) for full diagrams.

```
┌─────────────────────────────────────────────────────────┐
│                    Public Internet / VA LAN              │
└───────────────────────────┬─────────────────────────────┘
                            │ HTTPS
                    ┌───────▼────────┐
                    │  IIS / Reverse  │
                    │  Proxy (HTTPS)  │
                    └───────┬────────┘
              ┌─────────────┼──────────────┐
              │             │              │
     ┌────────▼──────┐  ┌───▼────────┐  ┌─▼──────────────┐
     │  React SPA    │  │ Next.js    │  │  ASP.NET Core  │
     │  (Admin UI)   │  │ (Public    │  │  Web API        │
     │               │  │  Site SSR) │  │                │
     └───────────────┘  └────────────┘  └────────┬───────┘
                                                  │
                                        ┌─────────▼───────┐
                                        │  SQL Server      │
                                        │  (MSSQL 2019+)   │
                                        └─────────────────┘
```

**Key architectural decisions:**
- Admin UI is a separate React SPA — served from the same IIS site under `/admin`
- Public site uses Next.js for SSR/SSG to achieve Lighthouse ≥ 90 scores
- API is the single source of truth — both admin and public site consume it
- Auth token flows through the API — no session state on the API tier
- MSSQL stores all content, versions, users, media metadata, and audit logs
- Files stored on configurable backend (local/UNC/Blob), paths in MSSQL

---

## 9. Data Model Overview

See [DATA_MODEL.md](DATA_MODEL.md) for full schema.

**Core entities:**
- `ContentType` — schema definition (fields, relations, template mapping)
- `ContentEntry` — a single piece of content (belongs to a type, has a locale, has versions)
- `ContentVersion` — snapshot of an entry at a point in time (stores full JSON payload)
- `MediaAsset` — uploaded file metadata (path, MIME type, alt text, dimensions, usages)
- `User` — synced from AAD; stores role assignments and preferences
- `Role` + `Permission` — RBAC matrix
- `NavigationMenu` + `NavigationItem` — site navigation trees
- `Taxonomy` + `TaxonomyTerm` — tags and categories
- `Redirect` — URL redirect rules
- `AuditLog` — immutable log of all mutations
- `Webhook` — registered webhook endpoints and their event subscriptions

---

## 10. Integration Requirements

| Integration | Requirement |
|---|---|
| Azure Active Directory | OIDC/SAML SSO. Groups can map to CMS roles. |
| Digital Analytics Program (DAP) | Standard government analytics script injected into public page `<head>` |
| VA Endpoint Security / AV | File upload scan hook — configurable plugin point |
| Email (SMTP) | Workflow notification emails. Supports Exchange on-prem and Exchange Online |
| Elasticsearch (optional) | Drop-in replacement for SQL Server Full-Text Search at scale |
| Azure Blob Storage (optional) | Media storage backend for cloud-hybrid deployments |
| Splunk (optional) | Structured log forwarding via HTTP Event Collector |
| CI/CD | GitHub Actions or Azure DevOps pipelines. SBOM generated on each build. |

---

## 11. Migration Path from SharePoint 2016

The system shall include a migration toolset:

**MIG-01** — A CLI command (`vacms migrate sharepoint`) that accepts a SharePoint 2016 content export and maps pages to CMS content entries.

**MIG-02** — Page content (HTML body) shall be cleaned and normalized to USWDS-safe HTML during import.

**MIG-03** — SharePoint document library files shall be importable into the CMS media library.

**MIG-04** — SharePoint user accounts shall be mapped to CMS users by UPN (email), preserving content ownership.

**MIG-05** — A migration report shall be generated showing: total pages found, successfully imported, failed with reasons, media assets imported/skipped.

---

## 12. Acceptance Criteria (MVP)

The MVP is shippable when all of the following are true:

- [ ] A content owner with no technical training can create, edit, preview, and publish a USWDS-compliant page using only the admin UI
- [ ] The review workflow (Draft → In Review → Approved → Published) functions end-to-end
- [ ] Azure AD SSO is working for all user roles
- [ ] RBAC correctly prevents content owners from publishing without review (when workflow is enabled)
- [ ] All admin UI pages pass axe-core automated accessibility scan with zero critical violations
- [ ] REST API returns content in < 200ms at p95 under 50 concurrent users
- [ ] Public pages rendered by Next.js score ≥ 90 on Lighthouse Performance
- [ ] Media upload, virus scan hook, and alt text enforcement work end-to-end
- [ ] Full-text search returns relevant results within 500ms
- [ ] Version history and rollback work for all content types
- [ ] Audit log captures all mutations
- [ ] IIS deployment guide successfully deploys the system on a fresh Windows Server 2022 VM
- [ ] Section 508 self-assessment completed with no critical failures

---

## 13. Out of Scope (Explicitly)

- Real-time collaborative editing (Google Docs style)
- Records management / legal holds
- E-forms / online application processing
- SharePoint calendar or task list replacement
- Video hosting or transcoding
- Native iOS/Android app
- Multi-tenancy (single-tenant MVP only)
- Comment / discussion features on public pages
- Paid/gated content
