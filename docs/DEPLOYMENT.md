# Deployment Guide
## VA CMS — USWDS-Compliant Content Management System

## Prerequisites

| Requirement | Minimum | Recommended |
|---|---|---|
| OS | Windows Server 2019 | Windows Server 2022 |
| .NET Runtime | .NET 8 | .NET 8 latest patch |
| Node.js | 20 LTS | 22 LTS |
| SQL Server | 2019 | 2022 |
| IIS | 10 | 10 |
| RAM (Web Tier) | 8 GB | 16 GB |
| RAM (DB Tier) | 16 GB | 32 GB |
| Disk (Web Tier) | 50 GB SSD | 100 GB SSD |
| Disk (DB Tier) | 200 GB SSD | 500 GB SSD (+ backup volume) |

## Quick Start (Development)

```bash
# 1. Clone the repo
git clone https://github.com/jwilson411/va-cms.git
cd va-cms

# 2. Start SQL Server (Docker for local dev)
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=Dev_Password123!" \
  -p 1433:1433 --name va-cms-sqlserver \
  -d mcr.microsoft.com/mssql/server:2022-latest

# 3. Configure connection string
cp src/api/VA.CMS.API/appsettings.Development.json.example \
   src/api/VA.CMS.API/appsettings.Development.json
# Edit: set ConnectionStrings:DefaultConnection

# 4. Migrations run automatically on API startup in Development (Database:MigrateOnStartup defaults
#    to true there). Elsewhere, or to run them by hand: vacms db migrate

# 5. Start API
dotnet run --project VA.CMS.API

# 6. Start admin SPA (new terminal)
cd src/admin
npm install && npm run dev

# 7. Start public site (new terminal)
cd src/public
npm install && npm run dev
```

Default admin: `http://localhost:3000/admin`  
Default public: `http://localhost:3001`  
API: `http://localhost:5100/api/v1`  
Swagger: `http://localhost:5100/swagger`

## Production Deployment (IIS + Windows Server)

### 1. SQL Server Setup

Three identities touch the database, and only the first ever holds DDL rights:

| Identity | Used by | Rights |
|---|---|---|
| Deployment account (DBA / pipeline, e.g. `sa` or a `db_owner` + `securityadmin` login) | `vacms db provision-logins`, `vacms db migrate` | `CREATE DATABASE`, DDL, `CREATE LOGIN` |
| `vacms_app` | the running API (`ConnectionStrings__DefaultConnection`) | `EXECUTE` on `dbo` procedures only; `SELECT/INSERT/UPDATE/DELETE` on tables denied; `UPDATE/DELETE` on `AuditLog` denied twice |
| `vacms_readonly` | reporting / BI | `SELECT` only |

**Step 1 — provision the logins** (once per SQL Server instance; rerun to rotate a password). Secrets come from the
pipeline's secret store, never from a file in the repo. Either:

```bash
# SQLCMD
sqlcmd -S SQLSERVER -d master -E \
  -v VacmsAppPassword="$VACMS_APP_PASSWORD" VacmsReadonlyPassword="$VACMS_READONLY_PASSWORD" DatabaseName="VACMS" \
  -i infra/sql/provision-logins.sql

# or the CLI (same script, no sqlcmd needed)
VACMS_CONNECTION_STRING="Server=SQLSERVER;Database=VACMS;Integrated Security=True;Encrypt=True;" \
  vacms db provision-logins --app-password "$VACMS_APP_PASSWORD" --readonly-password "$VACMS_READONLY_PASSWORD"
```

The logins are created with `CHECK_POLICY = ON`. If the database already exists the script also maps the users and
applies the grants; otherwise `V003__security_model.sql` does that during the next step.

**Step 2 — migrate** as the deployment account. This creates the database on first run and applies every pending
script in `migrations/` (DbUp journal: `dbo.SchemaVersions`):

```bash
vacms db migrate --connection "Server=SQLSERVER;Database=VACMS;Integrated Security=True;Encrypt=True;"
vacms db migrate --check    --connection "…"    # exit 0 = current, 2 = pending (use as a deploy gate)
vacms db migrate --dry-run  --connection "…"    # list what would run
```

**Step 3 — run the API as `vacms_app`.** `ConnectionStrings__DefaultConnection` uses `User Id=vacms_app`. At startup
the API does **not** migrate; it calls `usp_Migrations_ListApplied`, compares with the scripts shipped beside the
binaries, and refuses to start (exit 1, listing the pending scripts) if the database is behind. Set
`Database__MigrateOnStartup=true` only in Development, where the connection is a DDL-capable dev login.

Full-Text Search must be installed on the instance (`SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')` = 1);
the catalog is created by the migrations.

### 2. Build and Publish

```bash
# Build admin SPA
cd src/admin
npm ci && npm run build
# Output: src/admin/dist/

# Build public site
cd src/public
npm ci && npm run build
# Output: src/public/.next/

# Publish API
cd src/api
dotnet publish VA.CMS.API -c Release -o /publish/api
# Copy to web server: C:\inetpub\vacms\api\
```

### 3. IIS Configuration

Create three IIS applications under a single site:

```
Site: VA CMS (port 443, HTTPS)
├── /          → C:\inetpub\vacms\public\   (node.js via iisnode or reverse proxy to port 3001)
├── /admin     → C:\inetpub\vacms\admin\    (static React build, SPA routing via URL Rewrite)
└── /api       → C:\inetpub\vacms\api\      (ASP.NET Core via AspNetCoreModule)
```

**web.config for /api:** `dotnet publish` writes the AspNetCoreModule stanza. Add explicit request limits
(#167) — IIS enforces `maxAllowedContentLength` *before* the API's own per-request limit, so it must be at
least `media.maxUploadBytes` (default 100 MB) plus a little headroom, while the API keeps every non-upload
request at `api.maxRequestBodyBytes` (default 1 MB):

```xml
<system.webServer>
  <security>
    <requestFiltering>
      <requestLimits maxAllowedContentLength="110000000" maxUrl="4096" maxQueryString="8192" />
    </requestFiltering>
  </security>
</system.webServer>
```

Rate limiting (`api.rateLimits.*`, docs/SETTINGS.md) keys anonymous requests by the client address the API
sees, so `ForwardedHeaders__KnownProxies`/`KnownNetworks` must name the ARR / load-balancer hops — otherwise
every visitor shares the proxy's bucket. IIS's own dynamic IP restrictions can stay on as an outer layer.

**web.config for /admin:** `vite build` writes `dist/web.config` (SPA fallback rule plus the security
headers and the admin Content-Security-Policy from `src/security/csp.ts`, #162). Deploy the `dist/` folder
as-is; do not hand-edit the file — the build regenerates it and refuses to complete if `index.html` ever
gains an inline script the CSP would block.

### 4. Environment Variables

Set on the IIS application pool or via Windows environment:

```
# API (VA.CMS.API)
ASPNETCORE_ENVIRONMENT=Production
AllowedHosts=cms.va.gov;cms-admin.va.gov          # required outside Development; "*" refuses to start (#162)
ForwardedHeaders__KnownProxies__0=10.1.2.3       # IIS ARR / load balancer addresses whose X-Forwarded-* is trusted
ForwardedHeaders__KnownNetworks__0=10.1.0.0/16   # (CIDR); leave both empty when the API terminates TLS itself
# Cors__AllowedOrigins__0=https://www.va.gov     # only if the public site or SPA lives on a different origin
ConnectionStrings__DefaultConnection=Server=SQLSERVER;Database=VACMS;User Id=vacms_app;Password=<pw>;Encrypt=True;TrustServerCertificate=False;
Auth__Mode=WindowsAuth                           # on-prem default: IIS Windows Authentication (Kerberos)
# Alternative — AD FS OpenID Connect (Auth__Mode=AzureAd; see "Identity provider" below):
# AzureAd__Instance=https://adfs.va.gov/
# AzureAd__TenantId=adfs
# AzureAd__ClientId=<adfs_application_group_client_id>
# AzureAd__ClientCredentials__0__SourceType=StoreWithThumbprint …  (certificate in the Windows store)
Storage__Backend=local                           # or unc (Storage__UncRootPath=\\files\va-cms); no cloud backends
Storage__LocalRootPath=D:\vacms-uploads
Media__Scanner__Mode=Icap                        # or ClamAv; Disabled is refused in Production
Media__Scanner__Host=avscan.va.gov
Media__Scanner__Port=1344
Media__Scanner__ServicePath=/avscan              # the engine's RESPMOD service (vendor-specific)
Media__Scanner__FailClosed=true                  # unreachable engine ⇒ upload rejected (503), nothing stored
Email__Smtp__Host=mail.va.gov                    # sender address/name and the on/off switch are site settings
Email__Smtp__Port=587
Email__Smtp__Security=StartTls                   # None is refused outside Development (#173)
Jwt__SigningKey=<256-bit-random-key>             # `openssl rand -base64 48`; 32+ bytes, never the example value (#173)
Logging__Sinks__File__Enabled=true               # structured JSON logs (#166; see docs/LOGGING.md for every sink)
Logging__Sinks__File__Path=D:\logs\vacms\api-.json
Logging__Sinks__Splunk__Enabled=true             # on-prem Splunk HTTP Event Collector
Logging__Sinks__Splunk__HecUrl=https://splunk-hec.va.gov:8088
Logging__Sinks__Splunk__Token=<hec token>
DataProtection__KeysPath=\\files\va-cms\dp-keys          # key ring that encrypts webhook secrets; required outside Development (#168)
# DataProtection__DpapiNgDescriptor=SID=S-1-5-21-…        # optional: lock the key files to the app-pool gMSA (Windows CNG DPAPI-NG)

# Public site (Next.js)
NEXT_PUBLIC_API_URL=https://cms.youragency.va.gov   # API origin only — the site appends /api/v1/… itself
CSP_REPORT_ONLY=true                             # report-only phase; set false to enforce once the report log is quiet
```

#### Security headers (#162)

Every API response carries `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` /
`frame-ancestors 'none'`, `Referrer-Policy`, a minimal `Permissions-Policy`, `Cross-Origin-Opener-Policy` and,
on HTTPS outside Development, `Strict-Transport-Security` (1 year, includeSubDomains; `security.hstsPreload`
adds `preload`). The API's Content-Security-Policy is `default-src 'none'` and is sent as **Report-Only**
while the `security.cspReportOnly` site setting is on; violations from all three front ends arrive at
`POST /api/v1/security/csp-report` and are logged as warnings. The admin SPA's policy (`script-src 'self'`,
no inline scripts) is in the generated `web.config`; the public site's is nonce-based per request
(`src/public/proxy.ts`) and switches from Report-Only to enforced with `CSP_REPORT_ONLY=false`.
Swagger (`/swagger`) outside Development needs the `features.swaggerUi` setting **and** a bearer token with
the Developer role. The three front ends are expected on the same origin; set `Cors__AllowedOrigins`
only when that is not the case.

#### Identity provider (on-prem only)

This deployment has no cloud identity service. Two on-prem options:

**Windows Integrated Authentication (recommended)** — `Auth__Mode=WindowsAuth`. Enable *Windows
Authentication* (Negotiate/Kerberos, NTLM fallback) on the IIS site and register an SPN for the
app-pool identity (`setspn -S HTTP/cms.va.gov VA\svc-vacms`). Login flow: `GET /api/auth/login` →
302 `/api/auth/windows-login?returnUrl=…` (the browser completes the Kerberos exchange there) → the
`cms_rt` refresh cookie is set → 302 back into the SPA, which obtains the JWT through `POST /api/auth/refresh`.
No token ever appears in a URL or response body. Group-mapped roles come from the identity's group
SIDs (mappings may be keyed by SID or `DOMAIN\Group`; see Admin → Settings → AD Group Mappings).

**AD FS OpenID Connect** — `Auth__Mode=AzureAd` (the mode name is historical; it is the
Microsoft.Identity.Web OIDC handler, which supports AD FS 2016+). Set `AzureAd__Instance=https://<adfs-host>/`
and `AzureAd__TenantId=adfs`; create an AD FS *Application Group* (Server application + Web API) with:

| Setting | Value |
|---|---|
| Redirect URI | `https://<host>/signin-oidc` — the OIDC handler's `AzureAd:CallbackPath` (leave unset to use this default; a value under `/api/` is rejected at startup) |
| Post-logout redirect URI | `https://<host>/login` — where `GET /api/auth/signout` returns the browser after the end-session round trip |
| Issued claims | `upn` (or `preferred_username`), `name`, and a stable id claim (`oid`/`sub`); add group claims if AD-group → role mappings are used |

Login flow: `GET /api/auth/login` → AD FS → `/signin-oidc` (OIDC handler, sets the `cms_aad` session cookie) →
`GET /api/auth/callback` (issues the `cms_rt` refresh cookie, then 302 into the SPA).
Logout: `POST /api/auth/logout` revokes the refresh session and, when the `auth.azureAdSignOut` site setting is
on (default), returns `{ "signOutUrl": "/api/auth/signout" }`, which the SPA navigates to.

#### Session policy (VA Handbook 6500 — #163/#164)

- **System-use notification (AC-8).** `GET /api/auth/login` only starts a sign-in with `ack=1`, which the admin
  SPA adds after the user has agreed to the `auth.systemUseNotice` text on `/login`; a bookmarked
  `/api/auth/login` is sent back to that page. The `Logon` audit row records `systemUseAcknowledged`.
- **Refresh tokens live in `[RefreshToken]`** (SHA-256 only), rotate on every `POST /api/auth/refresh`, and share
  sessions across a web garden or farm — a deploy no longer signs everyone out. A replayed token revokes its whole
  chain (`RefreshReplay` audit row). Register `infra/sql-agent-jobs/job_Maint_PurgeRefreshTokens.sql` with the
  other Agent jobs (nightly; deletes dead rows after 30 days).
- **Idle and absolute limits** are the `auth.idleTimeoutMinutes` / `auth.absoluteSessionHours` site settings,
  enforced by the API and mirrored by the SPA's inactivity dialog.
- **Deactivation and role changes end open sessions**: `usp_RefreshToken_RevokeAllForUser` revokes the user's
  tokens and bumps `User.SessionVersion`; the JWT's `sv` claim must match, so existing access tokens are refused
  within `auth.revocationCheckSeconds` on every node.
- **Cookies.** `cms_rt` is `HttpOnly; Secure; SameSite=Strict; Path=/api/auth` everywhere except the Development
  environment, so Staging/UAT must be served over HTTPS (or behind a trusted TLS-terminating proxy — the API refuses
  to start when its own Kestrel bindings are http-only and no `ForwardedHeaders` trust is configured).
- **CSRF (FR-SECURITY-05).** The access token is held in SPA memory and sent as a bearer header; the only ambient
  credential is the `SameSite=Strict`, `/api/auth`-scoped refresh cookie, whose endpoints are POST and only return
  a token to same-origin script. No anti-forgery token is required.
- **`Auth__Mode=DevBypass`, `WINDOWS_AUTH_FAKE_NEGOTIATE` and `AZUREAD_FAKE_OIDC` are refused unless
  `ASPNETCORE_ENVIRONMENT=Development`**, and DevBypass refuses an empty `Auth__DevBypassAllowedUsers`.

#### Audit trail (NIST AU-2/AU-3 — #165)

Every mutating stored procedure writes its own `AuditLog` row inside the same transaction, and the API adds logon,
logoff, refresh, refresh-failure/replay, session-revocation and policy-denial (`AuthorizationDenied`, 403) events.
Rows carry `Outcome` (Success/Failure), `IpAddress`, `UserAgent` and `CorrelationId` (an inbound `X-Correlation-Id`
from IIS ARR / the load balancer, else the request trace id — echoed on every response). Set
`ForwardedHeaders__KnownProxies`/`KnownNetworks` so the recorded address is the client's, not the proxy's. The
admin viewer (`/admin/audit`) and its CSV export filter by outcome and IP; the event catalogue is in
`docs/DATABASE_LAYER.md` §4.9.

The `AzureAd` section binds from the **top-level** `AzureAd__*` variables (not `Auth__AzureAd__*`, which
does not bind).

**First sign-in.** `auth.autoProvisionUsers` defaults to off, so a fresh deployment rejects every identity
until a user row exists. Bootstrap the first administrator with SQL before go-live
(`INSERT INTO [User] (ExternalId, Email, DisplayName, IsActive)` — ExternalId is the UPN in WindowsAuth mode
or the issued id claim for OIDC — then `INSERT INTO UserRole` for SystemAdmin), or temporarily set the site
setting to `true`, sign in, assign the role, and switch it back. Signed-in users with no CMS role get 403
from every `/api/v1` endpoint and a "no access" page in the admin SPA.

**Client credential (OIDC only).** Use a certificate from the Windows certificate store rather than a
plaintext `AzureAd__ClientSecret`: `AzureAd__ClientCredentials__0__SourceType=StoreWithThumbprint`,
`AzureAd__ClientCredentials__0__CertificateStorePath=LocalMachine/My`,
`AzureAd__ClientCredentials__0__CertificateThumbprint=<thumbprint>` (the app-pool identity needs read
access to the private key). If a secret must be used, inject it from the deployment tool's secret store
at start-up; never commit it to `appsettings*.json`.

**Secrets on the host (#173).** All other secrets (`Jwt__SigningKey`, `ConnectionStrings__DefaultConnection`,
SMTP credentials, `REVALIDATE_SECRET`, the Splunk HEC token) never live in the repository or in a checked-in
`appsettings*.json`. Two on-prem options, in order of preference:

1. **Environment variables on the IIS application pool**, injected by the deployment tool (Jenkins credential
   binding, Ansible vault, or the VA's approved secret store) at deploy time. `web.config`
   `<environmentVariables>` is acceptable only when the file is written by the deployment tool and ACL'd to
   the app-pool identity and administrators.
2. **A DPAPI-protected `appsettings.Production.json`** outside the web root, encrypted with
   `System.Security.Cryptography.ProtectedData` under the app-pool identity's user scope, and decrypted by a
   small configuration provider at start-up. The plaintext file must never be written to disk on the host.

No cloud vault is involved. Certificates (TLS, the AD FS client credential) live in the Windows certificate
store (`LocalMachine\My`) with private-key read access granted to the app-pool identity only.

**Token signing.** The API signs access and preview tokens with HS256 today, so `Jwt__SigningKey` has to be
present on every node. For a multi-node production deployment consider RS256 with a certificate from the VA
PKI: the private key stays in the certificate store of the signing host and only the public key is
distributed. This is a planned change, not a configuration switch.

#### Startup validation (#173)

The API evaluates its configuration before registering a single service and refuses to start with **one
numbered list of every problem** (`StartupValidation.cs`; `ASPNETCORE_ENVIRONMENT` decides which rules
apply). The rules:

| Rule | Development | Everywhere else |
|---|---|---|
| `ConnectionStrings__DefaultConnection` present | required | required |
| `TrustServerCertificate=True`, `Encrypt=False` or `User Id=sa` in the connection string | allowed | **refused in Production** |
| `Jwt__SigningKey` ≥ 32 bytes, not an example placeholder, not trivially low-entropy | empty ⇒ random per-process key | required |
| `Auth__Mode=DevBypass`, `WINDOWS_AUTH_FAKE_NEGOTIATE`, `AZUREAD_FAKE_OIDC` | allowed (DevBypass needs an allow-list) | refused |
| `AzureAd__CallbackPath` under `/api/` | refused | refused |
| `AllowedHosts=*` or empty | allowed | refused |
| Kestrel bound to `http://` only with no trusted proxy | allowed | refused (Secure cookies would never return) |
| `Storage__Backend` other than `local`/`unc`; UNC root not a UNC path; local root inside the web root | backend/root checks | all checks |
| `Media__Scanner__Mode=Disabled` | allowed | **refused in Production**; engines need `Host` |
| `Email__Smtp__Security=None`; half-configured credentials; bad port | port/credential checks | all checks |
| `DataProtection__KeysPath` set (key ring for webhook secrets, #168); `DpapiNgDescriptor` only on Windows | optional | required |
| DataAnnotations on every options class (`[Range]`, `[Required]`) | checked | checked |

Every options class is also registered with `AddOptions<T>().Bind().ValidateDataAnnotations().ValidateOnStart()`,
so `IOptions<T>` consumers and the host's own start-up validation share one source of truth. The one rule that
cannot be a startup rule — `notifications.adminBaseUrl`, which is embedded in every workflow email — lives in the
database instead: the Settings screen refuses a non-`https://` value outside Development, and `/health/ready`
reports the setting until it has been changed from its `http://localhost:5173` default.

# Migrations are NOT applied by the API. Run them as the deployment account before starting the app pool:
vacms db migrate --connection "<deployment-account connection string>"
# The API verifies the schema at startup and exits 1 with the list of pending scripts if it is behind.

#### Data Protection key ring and webhook egress (#168)

Webhook signing secrets are stored in `[Webhook].[Secret]` as ASP.NET Data Protection payloads
(`dp1:…`), never in clear text, and the reporting login `vacms_readonly` is denied the column
(`dbo.vw_Webhook` is the projection without it). The key ring that encrypts them must outlive an
app-pool recycle and be readable by every node, so `DataProtection__KeysPath` is **required outside
Development**: a local directory on a single-node host, a UNC share (`\\files\va-cms\dp-keys`) for a farm.
The directory is created if missing; grant the app-pool identity Modify on it and nobody else Read. On
Windows the key files are additionally wrapped with DPAPI (machine scope) or, when
`DataProtection__DpapiNgDescriptor` names the app-pool gMSA (`SID=S-1-5-21-…`), with CNG DPAPI-NG so the
same key files decrypt on every domain-joined node. There is no cloud key vault option, per the on-prem
constraint. Rows written before V046 are re-keyed automatically on the first start after the upgrade.

Webhook deliveries are outbound HTTP from inside the network, so the destination is policed at
registration and again on every delivery (`WebhookDestinationPolicy`):

| Control | Where |
|---|---|
| `https://` only outside Development; URLs with credentials refused | registration + delivery |
| Host on the `webhooks.allowedHosts` site setting (`"www.va.gov"`, `"*.va.gov"`); empty = **no deliveries** outside Development | registration + delivery |
| Every resolved A/AAAA (and any literal IP) must be routable unicast: loopback, link-local (`169.254.169.254`), multicast, unspecified always refused; RFC 1918 / CGNAT / ULA only with `webhooks.allowPrivateNetworks` | literal at registration; resolved inside the socket connect callback, so the address checked is the address connected to |
| Redirects never followed (`AllowAutoRedirect=false`), no proxy inheritance, no cookies, TLS 1.2+, response body capped at 64 KB, 10 s connect timeout | `WebhookHttpHandler` |

A refused delivery is written to the delivery log with `Refused: …` and is not retried. Operators can
inspect the log and redeliver from **Admin → Webhooks**. For an on-prem public site (an RFC 1918 host), add
its name to `webhooks.allowedHosts` *and* turn on `webhooks.allowPrivateNetworks`; loopback stays refused.

### 5. Malware scanning (NIST SI-3)

Every upload is streamed to the configured engine before it is recorded. `Media__Scanner__Mode=Icap` speaks
ICAP RESPMOD (Trend Micro, McAfee/Trellix, Symantec Protection Engine and similar enterprise scanners expose
this; ask the AV team for the host, port and service path). `ClamAv` uses clamd's INSTREAM command and is what
local development and CI use (`docker compose --profile clamav up -d`, then `Media__Scanner__Mode=ClamAv`).
An infected file is deleted from storage, kept as an `IsVirusScanPassed = 0` tombstone row, and written to the
audit log (`VirusDetected`); an unreachable engine with `FailClosed=true` rejects the upload with 503 and audits
`VirusScanUnavailable`. Run `CLAMAV_HOST=localhost dotnet test --filter Eicar` to prove the wiring end to end.

### 6. SSL / TLS

- Use a VA-issued certificate (DigiCert Federal PKI or equivalent)
- Bind to port 443 in IIS
- Redirect HTTP (port 80) to HTTPS via IIS URL Rewrite
- Minimum TLS 1.2 (configure via IIS Crypto or registry)
- Disable TLS 1.0 and 1.1

### 7. Health Check Endpoints (#166)

| Endpoint | Returns |
|---|---|
| `GET /health`, `GET /health/live` | 200 `{"status":"Healthy"}` while the process serves requests — no dependencies; use for the load balancer |
| `GET /health/ready` | 200 `{"status":"Healthy"}` / `"Degraded"` when SQL Server, the storage root, the settings snapshot and (if enabled) the SMTP relay check out; 503 `{"status":"Unhealthy"}` otherwise |

The same routes exist under `/api/health`. Both are anonymous; the readiness body only lists the individual
checks (name, duration, failure text) for a caller with the Developer role. Configure VA monitoring tools to
poll `/health/ready` every 60 seconds and alert on 503. Logging, correlation ids and the full health-check
description are in `docs/LOGGING.md`.

Database health (index fragmentation, table sizes, queries running longer than 5 s) is a separate, authenticated
view: `GET /api/v1/admin/health/db` (Developer or SystemAdmin), rendered in the admin SPA at
`/admin/settings/health` (#172). The public Next.js site serves no `/admin/*` routes; its `/theme` component
gallery is `next dev` only and answers 404 in a production build.

### 8. Topologies and multi-node behaviour (NFR-OPS-04, #171)

The API has no in-process state that a second node would contradict: sessions are bearer tokens with
database-backed refresh tokens (#163), site settings are read from `[SiteSetting]` on every node (#141),
Data Protection keys live on the shared `DataProtection__KeysPath` (#168), media is on the shared storage
root, and since #171 every deferred job — webhook deliveries, workflow emails, the publish/expire
scheduler — is coordinated through SQL Server. The supported layouts:

| Topology | Notes |
|---|---|
| **Single IIS site** (one app pool, one worker process) | The default. Everything below still applies to the nightly app-pool recycle. |
| **Web garden** (one site, `Maximum Worker Processes` > 1) | Each worker process is a node. Nothing to configure. |
| **2+ node farm behind IIS ARR / a hardware load balancer** | Point every node at the same SQL Server, storage root and `DataProtection__KeysPath` (UNC). Set `ForwardedHeaders__KnownProxies` to the ARR hops (#162/#167). Health-check `/health/live` per node. |

**Sticky sessions are not required.** Access tokens are self-contained JWTs, the refresh cookie is
validated against `[RefreshToken]` on whichever node receives it, and nothing is cached per node that a
request depends on. Round-robin, least-connections and health-based routing all work.

**What runs on every node, and how they avoid stepping on each other:**

| Component | Coordination |
|---|---|
| `ScheduledPublishWorker` (publish / expire due content) | Each sweep is one call to `usp_ContentEntry_ClaimScheduledForPublish` / `_ClaimScheduledForExpiry`, which updates due rows read `WITH (UPDLOCK, READPAST)` in one transaction and audits + queues their webhooks there. Two nodes sweeping at once split the rows; a due entry is published exactly once and its `content.published` webhook fires exactly once. |
| `OutboxDispatcherWorker` (webhook deliveries, workflow emails) | Claims batches of `[OutboundEvent]` rows with `usp_OutboundEvent_Claim` (`UPDLOCK, READPAST`, one `UPDATE … OUTPUT`), so every row is delivered by one node. A claim is a lease (`outbox.leaseSeconds`, default 5 min): a node that is recycled mid-delivery loses the row to another node when the lease expires, and the attempt it started counts toward `webhooks.maxAttempts` / `notifications.emailMaxAttempts`. |
| `SiteSettingsService` (settings snapshot) | The node that handles an admin write reloads at once; every other node polls the table's change stamp every 5 s and reloads when it moves, with a full reload every 60 s as the backstop. A change is live cluster-wide within seconds; the `settings.updated` webhook still tells the public site. |
| `WebhookSecretRekeyService` (one-time clear-text → Data Protection re-key) | Idempotent per row (`usp_Webhook_ListSecretsForRekey`); running on several nodes at once is harmless. |

**App-pool recycle / process restart.** IIS recycles the pool nightly by default (and on config
changes, idle timeout, memory limits). Because every deferred job is a database row, a recycle loses
nothing: a webhook or email that was queued but not yet sent is delivered by the next poll on any node
(within `outbox.pollSeconds`, default 5 s, plus the lease if the recycled node had already claimed it);
a scheduled publish that was due is picked up by the next sweep. In-flight HTTP requests are drained by
IIS's overlapped recycle as usual. There is no need to disable the recycle, set `Disable Overlapped
Recycle`, or pin a single node for background work. The only per-node caches are the redirect resolve
cache (`redirects.cacheSeconds`, #169) and the rate-limit buckets (#167), both of which are safe to
lose and to hold independently per node.

**What to watch.** `/health/ready` reports **Degraded** when the oldest due outbox row has waited
longer than `outbox.staleAfterSeconds` (default 10 min) — that means no node is delivering
(all pools stopped, or every poll is failing; check the `OutboxDispatcherWorker` log lines). Failed
rows (`Status = 'Failed'`) keep their `LastError`; webhook failures are also visible per attempt under
**Admin → Webhooks → deliveries**, from where an operator can redeliver. Completed rows are purged after
`outbox.retentionDays`.

**Zero-downtime updates.** Migrations are additive and run before the new build is deployed (§ Updating
below); the previous build keeps running against the migrated schema until the swap. Roll nodes one at a
time: take a node out of the load balancer, deploy, wait for `/health/ready` = 200, put it back. The
outbox and the scheduler need no drain step — whatever a node held is picked up by the others.

## Backup and Recovery

- SQL Server: Full backup nightly, differential every 4 hours, transaction log every 15 minutes. Retain 30 days.
- File storage: Include media upload directory in enterprise backup rotation
- AuditLog: Treat as critical data — separate backup retention policy (7 years recommended for VA compliance)

## Updating

```bash
# 1. Put app in maintenance mode (IIS → stop site or swap to maintenance page)
# 2. Backup DB
# 3. Deploy new API build to staging directory
# 4. Run migrations as the deployment account: vacms db migrate --connection "…"  (then: vacms db migrate --check)
# 5. If migrations succeed: swap staging to production (xcopy or IIS virtual directory swap)
# 6. Deploy new admin/public builds
# 7. Restart app pool
# 8. Remove maintenance mode
# 9. Smoke test: /health/ready, /admin, /
```
