# Security Controls Mapping
## VA CMS — NIST SP 800-53 Rev. 5 / VA Handbook 6500

This document maps the security controls a VA ISSO will ask about to **the code, stored procedure or setting
that implements each one**, and to the acceptance test that proves it. It is written to be pasted into the
System Security Plan (SSP) control-implementation statements and walked with an assessor; every row names a
file that can be opened and a test that can be run. Where a control is inherited from the hosting environment
(Active Directory, IIS, SQL Server, the VA network) the row says so, and where the application has a known gap
the row links the issue that closes it.

**Status date:** 2026-09-18 — epic #152 (enterprise readiness). Rows reference the epic child that
implemented them so the git history is the change record.

| Legend | Meaning |
|---|---|
| **App** | Implemented in this repository |
| **Host** | Inherited from IIS / Windows Server / SQL Server configuration — `docs/DEPLOYMENT.md` says how |
| **AD** | Inherited from Active Directory / AD FS (the identity provider) |
| **Gap** | Not implemented; issue linked |

Conventions: paths are repository-relative; `usp_*` names are stored procedures in `migrations/`; `xxx.yyy`
names in code font are `SiteSetting` keys (`docs/SETTINGS.md`) editable at **Admin → Settings** and live
cluster-wide within seconds; `Xxx__Yyy` names are environment variables bound at startup
(`docs/DEPLOYMENT.md` § 4).

---

## 1. System boundary

Three deployable units on the same origin behind IIS, one SQL Server database, one file share, all on-prem
(on-prem only by constraint: no cloud identity, storage, vault or scanner is used or supported).

| Unit | Trust | Authentication |
|---|---|---|
| **API** `src/api/VA.CMS.API` (ASP.NET Core 8) | the only component with database and storage credentials | Bearer JWT issued by the API after Windows Integrated Authentication or AD FS OIDC; anonymous only where `[AllowAnonymous]` is explicit |
| **Admin SPA** `src/admin` (React, static files) | untrusted browser code | holds the access token in memory only; refresh cookie is `HttpOnly` |
| **Public site** `src/public` (Next.js) | untrusted for content; calls the API anonymously | none — sees Published content only |
| **CLI** `src/api/VA.CMS.CLI` (`vacms`) | operator tool, runs with the deployment account | SQL credentials from the pipeline secret store |

Data flows and topology: `docs/ARCHITECTURE.md`; deployment controls: `docs/DEPLOYMENT.md`.

---

## 2. Access Control (AC)

| Control | Impl. | Where | Evidence |
|---|---|---|---|
| **AC-2** Account management | **App** + **AD** | No CMS password exists; a `[User]` row is created only for an identity AD has authenticated. `auth.autoProvisionUsers` (default **off**) decides whether an unknown tenant identity gets a row on first login; when off, unknown identities are refused (#155). Role grants/revocations: `usp_User_AssignRole` / `usp_User_RevokeRole`, `src/api/VA.CMS.API/Controllers/Admin/UserRoleController.cs` (SystemAdmin only). Deactivation: `usp_User_Deactivate`. Every one of these writes an `AuditLog` row and ends the user's open sessions (`usp_RefreshToken_RevokeAllForUser`). AD-group → role mapping (`AdGroupRoleResolver.cs`, `usp_AdGroupMapping_*`) lets role membership follow AD group membership | `Issue155AcceptanceTests`, `Issue163AcceptanceTests`, `Issue165AcceptanceTests` |
| **AC-3** Access enforcement | **App** | Default-deny authorization: `CmsAuthorizationExtensions.cs` sets both `DefaultPolicy` and `FallbackPolicy` to *authenticated with at least one CMS role*, so an endpoint with no attribute, or bare `[Authorize]`, refuses a role-less principal; anonymous endpoints must opt out with `[AllowAnonymous]`. Named policies `cms:read/write/publish/manage_site/develop/admin_system` (`CmsRoles.cs`) gate every controller. Section scoping for Content Owners: `RbacService.cs` (denies when the section prefix is missing). GraphQL: anonymous callers see the Published-only surface; a `cms:read` bearer sees the full one (`GraphQL/GraphQLAudience.cs`, #156). Media: anonymous serve only for assets referenced by Published content (`MediaUsage`). Database: `vacms_app` holds `EXECUTE` on `dbo` only; `SELECT/INSERT/UPDATE/DELETE` on every table is `DENY`ed (`migrations/V003__security_model.sql`) | `Issue155AcceptanceTests`, `Issue156AcceptanceTests`, `Issue158AcceptanceTests` |
| **AC-6** Least privilege | **App** + **Host** | Six roles (`CmsRoles.cs`: ContentOwner, Editor, SiteAdmin, Developer, SystemAdmin, ReadOnly) with cumulative policies; Swagger UI outside Development needs `features.swaggerUi` **and** the Developer role (`Middleware/SwaggerAccessMiddleware.cs`); `GET /api/v1/admin/health/db` needs Developer or SystemAdmin; search analytics (`GET /api/v1/admin/search/analytics*`) needs SiteAdmin or SystemAdmin and the SPA hides the card and nav item below that (#175). Database identities: deployment account (DDL) ≠ `vacms_app` (EXECUTE only) ≠ `vacms_readonly` (SELECT only, denied the webhook secret column via `vw_Webhook` and the raw `SearchQueryLog` / `SearchResultClick` tables, `V049`) — `docs/DEPLOYMENT.md` § 1. Migrations never run under the application login outside Development (`vacms db migrate`, #157). IIS app pools run as a gMSA (**Host**, `docs/DEPLOYMENT.md` § 6.2) | `MigrationRunnerTests`, `StartupValidation` tests in `Issue173AcceptanceTests`, `Issue175AcceptanceTests` |
| **AC-7** Unsuccessful logon attempts | **AD** + **App** | Credential verification and lockout are Active Directory's (Kerberos / AD FS lockout policy) — the CMS never sees a password. The API adds a fixed-window throttle on `/api/auth/*` (`api.rateLimits.authPerMinute`, default 30/min per client IP — `RateLimiting/RateLimitPolicies.cs`, #167) and audits every `LogonFailure` / `RefreshFailure` / `RefreshReplay` with the client IP (#165) | `Issue167AcceptanceTests`, `Issue165AcceptanceTests` |
| **AC-8** System use notification | **App** | `GET /api/auth/login` starts a sign-in only with `ack=1`, which the SPA's `/login` page adds after the user accepts the `auth.systemUseNotice` text (default is the VA standard warning banner; `Auth/…`, `Controllers/AuthController.cs`, `src/admin/src/pages/LoginPage.tsx`, #164). The `Logon` audit row records `systemUseAcknowledged` | `Issue164AcceptanceTests`, `LoginPage.test.tsx` |
| **AC-11** Device lock / session lock | **App** | Idle timeout `auth.idleTimeoutMinutes` (default 15) enforced by the API on refresh and mirrored by the SPA's inactivity dialog (`src/admin/src/components/IdleSessionGuard.tsx`). VA 6500's 15-minute inactivity requirement is the default value (#164) | `Issue164AcceptanceTests`, `IdleSessionGuard.test.tsx` |
| **AC-12** Session termination | **App** | Absolute session cap `auth.absoluteSessionHours` (default 8): refresh stops at the cap and the user re-authenticates. Explicit logout `POST /api/auth/logout` revokes the refresh chain and, when `auth.azureAdSignOut` is on, ends the AD FS session (`GET /api/auth/signout`). Administrative termination: deactivation and role changes call `usp_RefreshToken_RevokeAllForUser`, which bumps `User.SessionVersion`; `Auth/SessionRevocationGuard.cs` compares the JWT `sv` claim with the row on every request (cached `auth.revocationCheckSeconds`, default 30), so revoked access tokens die within 30 s on every node (#163) | `Issue163AcceptanceTests` |
| **AC-14** Permitted actions without identification | **App** | Anonymous surface is enumerable: `GET /api/v1/content/*` (Published content, `PublicContentController.cs`), search, navigation, redirects, media serve for Published-referenced assets, GraphQL Published-only view, `/health`, CSP report intake. Everything else is default-deny (AC-3). `features.graphql` / `features.publicSearch` can switch surfaces off (404) | `Issue156AcceptanceTests`, `Issue169AcceptanceTests` |
| **AC-17** Remote access | **Host** | HTTPS only; IIS binding and redirect in `docs/DEPLOYMENT.md` § 6.1. No management interface is exposed by the application beyond the authenticated admin API | — |

---

## 3. Audit and Accountability (AU)

| Control | Impl. | Where | Evidence |
|---|---|---|---|
| **AU-2** Event logging | **App** | `AuditLog` table; every mutating stored procedure writes its own row inside its transaction (`usp_AuditLog_Write`), the API adds `Logon`, `LogonFailure`, `Logoff`, `Refresh`, `RefreshFailure`, `RefreshReplay`, `SessionsRevoked`, `AuthorizationDenied` (one per 403, `Auth/AuditingAuthorizationResultHandler.cs`), `VirusDetected`, `VirusScanUnavailable`, `SiteSettingUpdated/Reset`. The full catalogue — entity, action, actor, diff — is `docs/DATABASE_LAYER.md` § 4.9 (#165) | `Issue165AcceptanceTests.Mutating_Procedure_Writes_Its_Audit_Row` executes every audited procedure |
| **AU-3** Content of audit records | **App** | Each row: `ActorId`, `EntityType`, `EntityId`, `Action`, `DiffJson` (what changed, never a secret), `Outcome` (Success/Failure), `IpAddress`, `UserAgent`, `CorrelationId`, `CreatedAt` (UTC). Actor and request context reach the stored procedures through `sp_set_session_context` set by `CmsDatabase` on every connection open; the client IP is the forwarded-header-resolved address (`ForwardedHeaders__KnownProxies`). Correlation id: `Middleware/CorrelationIdMiddleware.cs` (`X-Correlation-Id` in, echoed out, present in every log line and ProblemDetails) | `Issue165AcceptanceTests`, `Issue166AcceptanceTests` |
| **AU-4 / AU-5** Storage capacity, processing failures | **App** + **Host** | Audit rows are written in the same transaction as the change — a full log volume fails the change, it does not drop the record. The SQL log-shipping / free-space alerts are the DBA's (**Host**). Application log sinks (`docs/LOGGING.md`) are asynchronous and never fail a request | `Issue165AcceptanceTests` (transactional write) |
| **AU-6** Audit review, analysis, reporting | **App** | Admin viewer `/admin/audit` (`Controllers/Admin/AuditLogController.cs`, SystemAdmin only) with filters for user, action, entity type, date range, outcome and IP (`usp_AuditLog_ListPaged`), and CSV export (`usp_AuditLog_ExportCsv`). Security-relevant application events are also emitted as structured log lines to Splunk / Event Log for SIEM correlation (`docs/LOGGING.md`) | `Issue165AcceptanceTests`, admin `AuditLogPage` tests |
| **AU-8** Time stamps | **App** + **Host** | `SYSUTCDATETIME()` in every procedure, UTC ISO-8601 in logs; hosts sync to the VA NTP source (**Host**) | — |
| **AU-9** Protection of audit information | **App** + **Host** | The application login cannot alter history: `DENY UPDATE, DELETE ON dbo.AuditLog TO vacms_app` on top of the schema-wide DML deny (`V003__security_model.sql`); no stored procedure updates or deletes audit rows; `vacms_readonly` is `SELECT` only. Bearer tokens are redacted from logs before any sink sees them (`Middleware/AuthHeaderRedactionMiddleware.cs`); the log directory is write-only for the app-pool identity (`docs/LOGGING.md` § 4). Archive: `usp_Maint_ArchiveAuditLog` *moves* rows to `AuditLogArchive`; nothing purges either table | `AuthHeaderRedactionMiddlewareTests`, `Issue165AcceptanceTests` |
| **AU-11** Audit record retention | **App** + **Host** | Online 2 years, then `AuditLogArchive` indefinitely (`infra/sql-agent-jobs/job_Maint_ArchiveAuditLog.sql`); backups per `docs/DEPLOYMENT.md` § Backup, retention and recovery; log records per RCS 10-1 (`docs/LOGGING.md` § 4) | — |
| **AU-12** Audit generation | **App** | Generation is inside the data layer (stored procedures), so every code path — REST, GraphQL, CLI seed, scheduler — produces the same records; the API cannot mutate a table without going through a procedure (AC-3) | `Issue165AcceptanceTests`, `Issue171AcceptanceTests` (scheduler audits inside the claim transaction) |

---

## 4. Identification and Authentication (IA)

| Control | Impl. | Where | Evidence |
|---|---|---|---|
| **IA-2** Identification and authentication (organizational users) | **AD** + **App** | Users are authenticated by Active Directory: **Windows Integrated Authentication** (Kerberos via IIS, `Controllers/WindowsAuthController.cs`, `Auth__Mode=WindowsAuth`) or **AD FS OpenID Connect** (Microsoft.Identity.Web, `Auth__Mode=AzureAd`, `Controllers/AuthController.cs`). PIV/CAC and MFA (IA-2(1)/(2)) are enforced by AD / AD FS policy, not by the CMS. The CMS then issues its own short-lived access token (`Auth/JwtService.cs`); no other credential exists. `DevBypass` and the fake handlers are refused outside the Development environment (`StartupValidation.cs`, #164/#173) | `Issue153`, `Issue154`, `Issue164`, `Issue173AcceptanceTests`, `AuthControllerTests` |
| **IA-3** Device identification | **Host** | Not an application control; network/device posture is the VA enclave's | — |
| **IA-4** Identifier management | **App** + **AD** | `User.ExternalId` is the AD UPN (WindowsAuth) or the IdP's stable `oid`/`sub` claim (OIDC); identifiers are never reused because they are AD's. Deactivated users keep their row for audit attribution | `Issue155AcceptanceTests` |
| **IA-5** Authenticator management | **App** + **AD** + **Host** | No user passwords are created or stored (IA-5(1) is AD's). Application secrets: `Jwt__SigningKey` must be ≥ 32 bytes, not an example value, not low-entropy (`StartupValidation.ValidateSigningKey`, #173); refresh tokens are stored as SHA-256 hashes only, rotate on every use, and a replayed token revokes its whole chain (`Auth/RefreshTokenService.cs`, `usp_RefreshToken_*`, #163); preview tokens are HMAC-SHA256 with a derived sub-key and constant-time compare (`Auth/PreviewTokenService.cs`); SQL logins are created with `CHECK_POLICY = ON` from pipeline-supplied passwords (`infra/sql/provision-logins.sql`, #157); webhook secrets are write-only and encrypted at rest (SC-28). Rotation procedures for every secret: `docs/DEPLOYMENT.md` § Key and secret rotation | `Issue163AcceptanceTests`, `Issue173AcceptanceTests`, `Issue168AcceptanceTests` |
| **IA-6** Authentication feedback | **App** | Failed logon responses carry a generic reason; the identity attempted is in the audit row, not the response or the log line (`docs/LOGGING.md` § 3) | `Issue165AcceptanceTests` |
| **IA-8** Non-organizational users | **App** | Public-site visitors are anonymous by design and get the Published-only surface (AC-14); there is no public-user account type | — |
| **IA-11** Re-authentication | **App** | Absolute session cap (`auth.absoluteSessionHours`) forces a fresh AD authentication; role changes end sessions (AC-12) | `Issue163AcceptanceTests` |

---

## 5. System and Communications Protection (SC)

| Control | Impl. | Where | Evidence |
|---|---|---|---|
| **SC-5** Denial-of-service protection | **App** + **Host** | Per-client rate limits on every endpoint by convention — `public-read`, `auth`, `analytics-write`, `admin` policies from `api.rateLimits.*` (`RateLimiting/RateLimitPolicies.cs`, #167); request body cap `api.maxRequestBodyBytes` (default 1 MB) except uploads (`media.maxUploadBytes`); page-size clamps (`api.maxPageSize`, `search.maxPageSize`, `search.maxQueryLength`); webhook egress capped at 64 KB / 10 s (`Webhooks/WebhookHttpHandler.cs`). IIS request filtering and dynamic IP restrictions are the outer layer (**Host**, `docs/DEPLOYMENT.md` § 3/§ 6.2) | `Issue167AcceptanceTests` |
| **SC-7** Boundary protection | **App** + **Host** | Outbound: webhook destinations are policed at registration and inside the socket connect (`Webhooks/WebhookDestinationPolicy.cs` — `https://` only, host allow-list `webhooks.allowedHosts`, RFC 1918/link-local/loopback refused unless `webhooks.allowPrivateNetworks`, no redirects, no proxy), SMTP to the configured relay only, malware scanner to the configured host only. Inbound: `AllowedHosts` required (no `*`) and `ForwardedHeaders__KnownProxies/KnownNetworks` limit which proxies may assert the client address (#162). Storage root must be outside the web root (`StartupValidation`). Network segmentation is the enclave's (**Host**) | `Issue168AcceptanceTests`, `Issue162AcceptanceTests`, `Issue173AcceptanceTests` |
| **SC-8** Transmission confidentiality and integrity | **App** + **Host** | HTTPS only: `Strict-Transport-Security` (1 year, includeSubDomains, optional preload) on every HTTPS response outside Development (`Middleware/SecurityHeadersMiddleware.cs`); startup refuses an http-only Kestrel binding with no trusted proxy; cookies are `Secure` (#162/#164). SQL: `Encrypt=True` required and `TrustServerCertificate=True` refused in Production (#173). SMTP: `Email__Smtp__Security=None` refused outside Development. Webhooks: TLS 1.2+ only. TLS termination, protocol and cipher policy at IIS: `docs/DEPLOYMENT.md` § 6.1 (**Host**) | `Issue162AcceptanceTests`, `Issue173AcceptanceTests` |
| **SC-12 / SC-17** Key management, PKI certificates | **App** + **Host** | Symmetric signing key from the secret store (`Jwt__SigningKey`); ASP.NET Data Protection key ring on `DataProtection__KeysPath` with automatic 90-day rotation, DPAPI / DPAPI-NG wrapped on Windows (`KeyRingOptions.cs`, #168); TLS, AD FS client and TDE certificates from VA PKI in the Windows certificate store (`docs/DEPLOYMENT.md` § 4, § 6.3). No key material is in the repository; the example signing key is rejected at startup | `Issue168AcceptanceTests`, `Issue173AcceptanceTests` |
| **SC-13** Cryptographic protection | **App** | JWT access tokens: HMAC-SHA256 (`Auth/JwtService.cs`); refresh tokens: 256-bit random, SHA-256 at rest; preview tokens: HMAC-SHA256 sub-key; webhook signatures: HMAC-SHA256 over the body in `X-CMS-Signature` (`Webhooks/WebhookDispatcher.cs`); webhook secrets at rest: ASP.NET Data Protection (AES-256-CBC + HMAC-SHA256); all via .NET's FIPS-capable providers (enable the Windows FIPS policy on the host, `docs/DEPLOYMENT.md` § 6.1). Migration to RS256 with a VA PKI certificate is documented as a planned change, not a switch (`docs/DEPLOYMENT.md` § 4 "Token signing") | `Issue163`, `Issue168AcceptanceTests`, `Issue34AcceptanceTests` |
| **SC-18** Mobile code | **App** | Content is stored as Markdown and rendered with `DisableHtml()` (Markdig) — no raw HTML from authors reaches a page; uploaded SVG is off the allow-list by default and, if enabled, is sanitised (`Storage/SvgSanitizer.cs`) and served under `Content-Security-Policy: sandbox; default-src 'none'` (`Controllers/MediaResponsePolicy.cs`, #158). CSP on every unit: API `default-src 'none'`, admin SPA `script-src 'self'` (no inline scripts, build-verified), public site nonce-per-request (`src/public/proxy.ts`); `security.cspReportOnly` switches enforcement; reports arrive at `POST /api/v1/security/csp-report` (`Controllers/SecurityReportController.cs`, #162) | `Issue158AcceptanceTests`, `Issue162AcceptanceTests`, admin `build` CSP check |
| **SC-23** Session authenticity | **App** | Refresh cookie `cms_rt`: `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`; access token in SPA memory only, sent as a bearer header, so no ambient credential reaches a state-changing endpoint (BRD FR-SECURITY-05 — CSRF); refresh rotation with replay detection (#163) | `Issue163AcceptanceTests`, `Issue164AcceptanceTests` |
| **SC-28** Protection of information at rest | **App** + **Host** | Application: webhook secrets encrypted (`Webhooks/WebhookSecretProtector.cs`, `[Webhook].[Secret]` = `dp1:…`), refresh tokens hashed, no passwords stored, uploads outside the web root. Database and backup encryption: SQL Server TDE with a VA PKI certificate (`docs/DEPLOYMENT.md` § 6.3, **Host**); media share encryption: BitLocker / the storage array's encryption (**Host**) | `Issue168AcceptanceTests` |

---

## 6. System and Information Integrity (SI)

| Control | Impl. | Where | Evidence |
|---|---|---|---|
| **SI-2** Flaw remediation | **App** (CI) | `.github/workflows/security.yml`: `dotnet list package --vulnerable` (any severity fails), `npm audit --audit-level=high` on all three npm roots, NuGet audit on restore in `ci.yml`; Dependabot-style upgrades land through PRs with the full test suite (#160/#161). Patch cadence for the OS, IIS and SQL Server is the host baseline's (**Host**) | Workflow runs on every PR |
| **SI-3** Malicious code protection | **App** + **Host** | Every upload is streamed to the configured engine before it is recorded: ICAP RESPMOD (`Storage/IcapVirusScanService.cs`, enterprise scanners) or clamd INSTREAM (`Storage/ClamAvVirusScanService.cs`); `Media__Scanner__Mode=Disabled` is refused in Production; `FailClosed=true` rejects the upload (503) when the engine is unreachable; an infected file is deleted, tombstoned (`IsVirusScanPassed = 0`) and audited `VirusDetected`; an unreachable engine audits `VirusScanUnavailable` (#159). Host anti-malware on the web and file tiers is the VA endpoint standard (**Host**) | `Issue159AcceptanceTests`, EICAR end-to-end test (`CLAMAV_HOST=… dotnet test --filter Eicar`) |
| **SI-4** System monitoring | **App** + **Host** | Structured JSON logs with correlation ids to file / Windows Event Log / Splunk HEC (`Observability/SerilogSetup.cs`, `docs/LOGGING.md`, #166); `/health/live` and `/health/ready` for VA monitoring (`Observability/HealthChecks.cs`), readiness degrading when the outbox stalls or SQL/storage/SMTP fail; security-relevant events (failed logons, 403s, CSP violations, refused webhook destinations, scanner rejections) at Warning for SIEM rules | `Issue166AcceptanceTests` |
| **SI-5** Security alerts and advisories | **App** (CI) | Dependency advisories fail CI (SI-2); `SECURITY.md` gives the disclosure route | — |
| **SI-7** Software, firmware and information integrity | **App** (CI) | CycloneDX SBOMs for the API and each npm root are produced on every build (`security.yml`, NFR-SEC-02); `npm ci` / locked NuGet restore; CodeQL for C# and JavaScript/TypeScript; gitleaks secret scan; webhook payloads are HMAC-signed so consumers can verify integrity | Workflow artifacts (`sbom`, `semgrep-sarif`) |
| **SI-10** Information input validation | **App** | Model validation with DataAnnotations on every request and options class (`ValidateDataAnnotations().ValidateOnStart()`, #173); uploads: MIME allow-list `media.allowedMimeTypes` checked against the **bytes** (`Storage/MediaContentSniffer.cs`), not the client's `Content-Type`; images are decoded and re-encoded for the resized/WebP variants (`Storage/ImageProcessingService.cs`); SVG sanitiser; redirect `ToPath` limited to site-relative paths or `redirects.allowedExternalHosts` and loop/chain rules (`Controllers/RedirectAdminController.cs`, `usp_Redirect_*`, #155/#169); `returnUrl` on login must pass `Url.IsLocalUrl` (`AuthController.SafeLocal`, #154); webhook URL policy (SC-7); slug and locale formats validated in the procedures; page-size and query-length clamps; **all SQL is stored procedures with parameters** (`EXEC usp_* @0…`) and the full-text query is escaped/wrapped (BRD FR-SECURITY-04) | `Issue158`, `Issue155`, `Issue169`, `Issue167`, `Issue154AcceptanceTests` |
| **SI-11** Error handling | **App** | Unhandled errors become RFC 7807 ProblemDetails with the correlation id; stack traces and exception text are included only in Development (`Program.cs` `AddProblemDetails`, #166) | `Issue166AcceptanceTests` |
| **SI-12** Information management and retention | **App** + **Host** | Retention table in `docs/DEPLOYMENT.md` § Backup, retention and recovery. Data categories, retention and PIA status for the two stores that hold what people typed or did: § 6.1 below. Search analytics: query text is redacted before it is stored (`Infrastructure/Search/SearchQueryRedactor.cs`, `search.analytics.redactionPatterns`; `fn_Search_LooksLikeIdentifier` inside the procedures as the last line), raw rows are purged after `search.analytics.retentionDays` (default 90) by `usp_Maint_RollupSearchLogs`, no IP address or session key is ever written (#175) | `Issue175AcceptanceTests` (regex set, queue, endpoints, procedure parameters, rollup, reporting login) |
| **SI-16** Memory protection | **Host** | .NET 8 runtime / Windows DEP and ASLR — inherited | — |

### 6.1 Data categories, retention and privacy assessment status

The CMS stores content, configuration and the two record sets below that describe *people* rather than
pages. Both are listed here so the PTA/PIA can cite one place; the Privacy Threshold Analysis and any
Privacy Impact Assessment are VA Privacy Service documents that this repository informs but does not contain.

| Store | What it holds | Who it is about | Minimisation in code | Retention | Readers | PTA / PIA |
|---|---|---|---|---|---|---|
| `SearchQueryLog`, `SearchResultClick` (raw) and `SearchQuerySummary` (aggregate) | Search box text, result count, clicked slug, rank, UTC timestamp | Anonymous public-site visitors (Veterans, families, staff). `UserId` is always `NULL` from the public endpoints | Every query passes `SearchQueryRedactor` before it is queued: SSN in any spacing, 9-digit runs, VA file/claim numbers (`C…`, `CSS…`), phone numbers, e-mail addresses become `[redacted]` (`search.analytics.redactionPatterns`, operator-extendable). The procedures scrub bare SSN / 9-digit shapes again. **No client IP, user agent, cookie or session key is stored** — click-through joins on query text alone (`Issue175DatabaseTests.Raw_Tables_Store_No_Ip_Address_Or_Session_Key`). The whole feature is a switch: `features.searchAnalytics` | Raw rows: `search.analytics.retentionDays` (default **90**) then purged by `usp_Maint_RollupSearchLogs` (`job_Maint_RollupSearchLogs.sql`, nightly). Aggregate rows: per day + redacted query, kept while the site needs trend history; contain nothing the raw rows did not already pass | API: SiteAdmin, SystemAdmin (`cms:manage_site`). Database: `vacms_readonly` is **denied** the raw tables and may read the aggregate; `vacms_app` executes procedures only | **PTA required** before the analytics switch is on in production; the redaction, retention and reader restrictions above are the minimisation the PTA records. If the PTA concludes a PIA is needed, the Privacy Service's PIA supersedes this row |
| `AuditLog`, `AuditLogArchive` | Actor id, action, entity, diff of what changed, outcome, client IP, user agent, correlation id, UTC timestamp | CMS users (VA staff authenticated by AD); the IP of anyone whose logon is refused | `DiffJson` never contains a secret (webhook secrets, tokens are excluded at the procedure); failed logon rows carry the attempted identity, the response does not (IA-6) | Online 2 years, `AuditLogArchive` indefinitely, no purge (AU-11) | API: SystemAdmin (`/admin/audit`). Database: `vacms_app` cannot update or delete; `vacms_readonly` may read | **System of records: covered by the VA staff/system-access SORN the ISSO designates in the SSP** (the rows are about employees' use of a government system, a category VA already holds under existing SORNs). A PTA is still required at ATO; it is expected to conclude no new PIA because no new category of information about the public is collected |

---

## 7. Other families the assessor will ask about

| Control | Impl. | Where |
|---|---|---|
| **CM-6 / CM-7** Configuration settings, least functionality | **App** | Fail-fast startup validation with one numbered list of every misconfiguration (`StartupValidation.cs`, table in `docs/DEPLOYMENT.md` § Startup validation, #173); feature switches (`features.*`) turn GraphQL, Swagger, webhooks, uploads, public search off at runtime; production refuses DevBypass, `AllowedHosts=*`, `sa`, disabled scanning, plaintext SMTP. IIS role/feature minimisation: `docs/DEPLOYMENT.md` § 6.2 (**Host**) |
| **CM-8** System component inventory | **App** (CI) | CycloneDX SBOM per build (`security.yml`) |
| **CP-9 / CP-10** Backup and recovery | **Host** | `docs/DEPLOYMENT.md` § Backup, retention and recovery — including the Data Protection key ring and TDE certificate, without which encrypted data is unrecoverable |
| **RA-5** Vulnerability monitoring and scanning | **App** (CI) + **Host** | SAST (CodeQL; Semgrep when `SAST_ENGINE=semgrep`), dependency advisories, secret scanning on every change (`security.yml`, #161). Infrastructure scanning (Nessus/ACAS) and the pre-production penetration test (NFR-SEC-01) are VA OIS activities; this repository provides the SBOM, SARIF and this mapping as inputs |
| **SA-11** Developer testing | **App** (CI) | `ci.yml`: build with warnings as errors, 790+ .NET tests against SQL Server 2022 (Testcontainers), 400+ admin SPA tests, public-site build, Playwright + axe accessibility suite (`tests/accessibility`), OpenAPI snapshot drift check (`docs/openapi.json`) |
| **SA-15 / SR-3** Supply chain | **App** (CI) | Locked dependency manifests, NuGet audit on restore, `npm audit`, SBOM; no build-time network fetch outside the package registries |
| **PL-4 / AC-8** Rules of behavior | **App** | System-use notice text is a site setting (`auth.systemUseNotice`) so the ISSO can set the approved wording without a release |

---

## 8. Known gaps and planned changes

Listed so the SSP can carry them as POA&M items rather than discover them in assessment.

| Gap | Control(s) | Status |
|---|---|---|
| Search analytics redaction is pattern-based (`search.analytics.redactionPatterns`): an identifier in a shape the set does not describe (a foreign phone format, a name) is stored as typed for `search.analytics.retentionDays` | SI-12 | **Closed — #175** for the enumerated shapes; residual risk accepted by keeping the raw window short, readers to SiteAdmin/SystemAdmin and the reporting login off the raw tables. Extend the pattern list at **Admin → Settings** without a release; the PTA (§ 6.1) records the decision |
| Access tokens are HS256 with one shared symmetric key on every node; rotation has no dual-key window (one 401 + silent refresh during a roll) | SC-12, SC-13 | Documented planned change to RS256 with a VA PKI certificate (`docs/DEPLOYMENT.md` § 4 "Token signing"); not scheduled |
| Account lockout (AC-7) and MFA/PIV (IA-2(1)) are inherited from AD / AD FS; the CMS only throttles and audits | AC-7, IA-2 | By design (no CMS credential); record the AD policy in the SSP |
| Audit review is a manual admin screen plus SIEM forwarding; no in-app alerting rules (AU-6(1)) | AU-6 | Splunk / SIEM correlation rules are the operating environment's |
| Migrations are forward-only; a failed migration is rolled back by DbUp's transaction but there is no automated *down* script (NFR-OPS-04 rollback = redeploy the previous build against the additive schema) | CM-3 | By design; `docs/DEPLOYMENT.md` § Blue-green update |
| No penetration test has been performed | NFR-SEC-01 | VA OIS activity before production; inputs are this document, the SBOM and the CodeQL/Semgrep SARIF |

---

## 9. How to keep this document true

- Every epic-#152 child added an `IssueNNNAcceptanceTests.cs` that fails if the control regresses; CI runs
  them on every PR. When a control's implementation moves, update the **Where** cell and the test name here in
  the same PR.
- `docs/openapi.json` is snapshot-tested (`OpenApiSnapshotTests`), so the anonymous vs. authenticated surface
  in AC-14 can be diffed release to release.
- `docs/SETTINGS.md` is the source of truth for every `xxx.yyy` key named above; `docs/DEPLOYMENT.md` § 4 for
  every `Xxx__Yyy` variable.
