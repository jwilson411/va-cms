# Runtime Settings & Feature Flags

Epic [#141](https://github.com/jwilson411/va-cms/issues/141). Everything an operator might want
to change without a build — feature switches, limits, lifetimes, cadences, public-site chrome —
lives in the `[SiteSetting]` table and is edited from **Admin → Settings → Site Settings**.
A change is applied by the running API as soon as it is saved and reaches the admin SPA and the
public site without a deploy.

## What stays in `appsettings.json` / environment (by design)

| Value | Why it cannot be a database setting |
|---|---|
| `ConnectionStrings:DefaultConnection` | Needed to reach the database at all |
| `Auth:Mode`, `AzureAd:*`, `Auth:DevBypassAllowedUsers` | Chooses the auth pipeline at startup; secrets |
| `Jwt:SigningKey`, `Jwt:Issuer`, `Jwt:Audience` | Secret; validation parameters are built at startup |
| `Storage:*` (backend, paths, Azure connection string) | Switching backends at runtime would orphan files; secrets |
| `Media:Scanner:*` (`Mode` = Disabled \| Icap \| ClamAv, `Host`, `Port`, `ServicePath`, `TimeoutSeconds`, `FailClosed`) | Malware-engine wiring (#159). `Disabled` is refused in Production; `FailClosed` defaults to true outside Development so an unreachable engine rejects uploads with 503 instead of storing unscanned files |
| `Email:Smtp:*` (host, port, security, username, password) | SMTP relay address and credentials; secrets bound to the deployment (issue #39). The switch, sender and link origin are `notifications.*` settings below. |
| `Database:MigrateOnStartup` | Whether the API applies migrations itself at startup (default: Development only). Off, it only verifies nothing is pending and exits 1 otherwise — deployments run `vacms db migrate` with an elevated login |
| `AllowedHosts`, `ForwardedHeaders:*`, `Cors:AllowedOrigins` | Host-header allow-list, trusted proxies and cross-origin front ends — evaluated before any request is served (#162) |
| Next.js: `CSP_REPORT_ONLY` | The public site's proxy sets headers per request without a settings lookup, so its report-only switch is an env var |
| `SKIP_MIGRATIONS`, `WINDOWS_AUTH_FAKE_NEGOTIATE`, `AZUREAD_FAKE_OIDC` | Test/host bootstrap switches |
| Next.js: `NEXT_PUBLIC_API_URL`, `CMS_API_URL`, `REVALIDATE_SECRET`, `NEXT_PUBLIC_SITE_URL` | Needed to find the API / verify webhooks before settings can be read |

Everything else is a site setting.

## How it works

```
SiteSettingDefinitions.cs        ← single source of truth: key, type, default, category, scope, description
        │  (startup sync: usp_SiteSetting_EnsureDefinition — inserts missing rows, refreshes
        │   metadata/default, never touches an admin-edited Value)
        ▼
[SiteSetting] table              ← Value (admin-set, NULL = use DefaultValue), DefaultValue, DataType,
        │                           Category, Scope, UpdatedById/At
        ▼
ISiteSettingsService (singleton) ← in-memory snapshot; refreshed every 60 s and immediately after an
        │                           admin write; code defaults until the first load / if the DB is down
        ├── API services & controllers read GetBool/GetInt/GetLong/GetString/GetJson at request time
        ├── GET /api/v1/settings/client  (any role)   → Admin + Public scope, for the admin SPA
        └── GET /api/v1/settings/public  (anonymous)  → Public scope, for the Next.js site (ISR tag
                                                        cms-site-settings, dropped by the settings.updated webhook)
```

Reads never block on the database. Startup, tests and request handling all proceed with code
defaults until the snapshot loads.

### Adding a setting

1. Add a constant to `SiteSettingKeys` and a definition to `SiteSettingDefinitions.All` in
   `src/api/VA.CMS.Infrastructure/Settings/SiteSettingDefinitions.cs`. Pick the **scope**:
   `Server` (API only), `Admin` (also returned to the admin SPA), `Public` (also returned
   anonymously to the public site).
2. Read it where it is used: inject `ISiteSettingsService` and call the typed getter **at the
   point of use**, not in a constructor or at startup — otherwise the value is frozen.
3. If the admin SPA or public site reads it, add the key and its default to
   `src/admin/src/features/siteSettings/useClientSettings.ts` or
   `src/public/lib/cms/settings.ts`.
4. No migration. The row is created the next time the API starts.
5. Stored-procedure logic can read flags with `dbo.fn_SiteSetting_GetBool('key', @fallback)`
   (see `usp_Workflow_Transition` in V040).

### Management API (SystemAdmin)

| Method | Path | Notes |
|---|---|---|
| `GET` | `/api/v1/admin/settings` | Every setting: value, default, effective value, type, category, scope, who changed it |
| `PUT` | `/api/v1/admin/settings/{key}` | Body `{ "value": "..." }`. Validated against DataType: `bool` → `true`/`false`; `int` → non-negative whole number; `json` → parseable JSON |
| `POST` | `/api/v1/admin/settings/{key}/reset` | Restore the code default |

Every write is audited (`AuditLog` action `SiteSettingUpdated` / `SiteSettingReset` with
`{key, from, to}`) and fires the `settings.updated` webhook event.

## Settings reference

Defaults are the values that were previously hard-coded.

### Site (Public)
| Key | Default | Used by |
|---|---|---|
| `site.title` | Department of Veterans Affairs | Public header masthead; `<title>` suffix |
| `site.metaTitle` | VA CMS Public Site | Home `<title>` |
| `site.metaDescription` | USWDS-compliant VA content management system public site | `<meta description>` |
| `site.agencyName` | Department of Veterans Affairs | Footer, identifier, article JSON-LD publisher |
| `site.agencyShortName` | VA | Identifier |
| `site.agencyHref` | https://www.va.gov | Footer, identifier |
| `site.agencyLogoSrc` | *(blank)* | Footer/identifier logo when set |
| `site.bannerLang` | en | Government banner language (`en`/`es`) |

### Analytics (Public)
| Key | Default | Used by |
|---|---|---|
| `analytics.dapEnabled` | false | `DapScript` master switch |
| `analytics.dapAgency` | *(blank)* | DAP `agency` |
| `analytics.dapSubagency` | *(blank)* | DAP `subagency` |

### Features
| Key | Default | Scope | Effect when off |
|---|---|---|---|
| `features.graphql` | **false** | Server | `/api/graphql` → 404 (on: anonymous = Published only, CanRead JWT = full surface — see DEVELOPER_GUIDE) |
| `features.swaggerUi` | false | Server | `/swagger` → 404 outside Development (always on in Development) |
| `features.webhooks` | true | Server | `IWebhookBackgroundDispatcher.Enqueue` is a no-op |
| `features.scheduledPublishing` | true | Server | `ScheduledPublishWorker` skips its sweep |
| `features.notifications` | true | Admin | Workflow notifications not recorded; admin bell hidden |
| `features.webpVariants` | true | Server | Uploads skip WebP generation |
| `features.searchAnalytics` | true | Admin | Search queries not logged; Search Analytics nav item hidden |
| `features.mediaUpload` | true | Admin | Upload endpoint → 403; upload form replaced with a notice |
| `features.publicSearch` | true | Public | Header search box hidden; `/search` shows "unavailable" |

### Media
| Key | Default | Scope |
|---|---|---|
| `media.maxUploadBytes` | 104857600 (100 MB) | Admin — request body limit and service check for `POST /api/v1/media/upload` |
| `media.allowedMimeTypes` | the BRD FR-SECURITY-06 list (JSON array) — `image/svg+xml` is **not** in it (#158); add it only if SVG is required: uploads are then sanitized (scripts, event handlers, foreignObject, external references stripped) and served with `Content-Security-Policy: sandbox; default-src 'none'`. Every upload's bytes must also match the declared type and extension. | Admin |
| `media.imageMaxWidthPx` | 1920 | Server |
| `media.webpQuality` | 80 | Server |

### Auth (Server)
| Key | Default |
|---|---|
| `auth.accessTokenMinutes` | 15 |
| `auth.refreshTokenHours` | 8 (also the `cms_rt` cookie Max-Age) |
| `auth.previewTokenMinutes` | 60 |
| `auth.azureAdSignOut` | true — logout also sends the browser to the Azure AD end-session endpoint (`GET /api/auth/signout`); off clears only the CMS cookies |
| `auth.autoProvisionUsers` | false — unknown AzureAd/WindowsAuth identities are rejected (`/login?error=not_provisioned` / 403) until an administrator creates the user row; DevBypass is governed by `DevBypassAllowedUsers` instead |

### Security (Server)
| Key | Default |
|---|---|
| `security.cspReportOnly` | true — the API's Content-Security-Policy is sent as Report-Only (violations logged via `POST /api/v1/security/csp-report`); off enforces it |
| `security.hstsPreload` | false — adds `preload` to Strict-Transport-Security once the host is submitted to hstspreload.org |

### Navigation (Server)
| Key | Default |
|---|---|
| `redirects.allowedExternalHosts` | `[]` — host names (exact, or `*.example.gov`) a redirect `ToPath` may point at over https; empty means site-relative targets only |

### Workflow
| Key | Default | Scope |
|---|---|---|
| `workflow.scheduledPublishPollSeconds` | 60 (minimum 5) | Server |
| `workflow.requireReturnComment` | true — enforced inside `usp_Workflow_Transition` | Admin |

### Webhooks (Server)
| Key | Default |
|---|---|
| `webhooks.maxAttempts` | 3 |
| `webhooks.retryDelaysSeconds` | `[5, 25]` (last value repeats) |
| `webhooks.timeoutSeconds` | 15 |

### Search / API
| Key | Default | Scope |
|---|---|---|
| `search.publicPageSize` | 10 | Public |
| `search.defaultPageSize` | 25 | Server |
| `search.maxPageSize` | 100 | Server |
| `api.maxPageSize` | 200 — clamp for admin list endpoints and GraphQL | Server |

### Notifications / Admin (Admin)
| Key | Default |
|---|---|
| `notifications.pollIntervalSeconds` | 30 |
| `notifications.panelLimit` | 20 |

### Notifications — email (Server; issue #39)
| Key | Default | Effect |
|---|---|---|
| `notifications.emailEnabled` | true | Off: workflow events still fill the bell, no email is queued. Delivery also needs `Email:Smtp:Host` in the environment. |
| `notifications.emailFromAddress` | cms-noreply@va.gov | Sender address; must be accepted by the SMTP connector. |
| `notifications.emailFromName` | VA CMS | Sender display name. |
| `notifications.adminBaseUrl` | http://localhost:5173 | Admin site origin; emails link to `{origin}/admin/content/{id}/edit`. |
| `admin.autoSaveIntervalSeconds` | 60 (0 disables) |
| `admin.contentListPageSize` | 25 |
| `admin.auditLogPageSize` | 50 |
| `admin.searchAnalyticsPageSize` | 50 |

## Multi-node deployments

Each API node holds its own snapshot. The node that handled the admin write refreshes
immediately; the others pick the change up within the 60-second refresh interval
(`SiteSettingsService.DefaultRefreshInterval`). The admin SPA caches `/settings/client` for five
minutes per session and invalidates it on every save from the settings screen. The public site
drops its ISR cache on the `settings.updated` webhook (register `settings.updated` alongside the
content events — see README step 5).
