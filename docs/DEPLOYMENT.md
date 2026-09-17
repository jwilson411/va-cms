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

**web.config for /admin (SPA fallback routing):**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="SPA Fallback" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
          </conditions>
          <action type="Rewrite" url="/admin/index.html" />
        </rule>
      </rules>
    </rewrite>
    <staticContent>
      <mimeMap fileExtension=".webmanifest" mimeType="application/manifest+json" />
    </staticContent>
  </system.webServer>
</configuration>
```

### 4. Environment Variables

Set on the IIS application pool or via Windows environment:

```
# API (VA.CMS.API)
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=Server=SQLSERVER;Database=VACMS;User Id=vacms_app;Password=<pw>;TrustServerCertificate=False;
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
Email__SmtpHost=mail.va.gov
Email__SmtpPort=587
Email__FromAddress=noreply-cms@va.gov
Jwt__SigningKey=<256-bit-random-key>

# Public site (Next.js)
NEXT_PUBLIC_API_URL=https://cms.youragency.va.gov/api/v1
```

#### Identity provider (on-prem only)

This deployment has no cloud identity service. Two on-prem options:

**Windows Integrated Authentication (recommended)** — `Auth__Mode=WindowsAuth`. Enable *Windows
Authentication* (Negotiate/Kerberos, NTLM fallback) on the IIS site and register an SPN for the
app-pool identity (`setspn -S HTTP/cms.va.gov VA\svc-vacms`). Login flow: `GET /api/auth/login` →
302 `/api/auth/windows-login?returnUrl=…` (the browser completes the Kerberos exchange there) → the
`cms_rt` refresh cookie is set → 302 back into the SPA, which obtains the JWT through `GET /api/auth/refresh`.
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

**Secrets on the host.** All other secrets (`Jwt__SigningKey`, `ConnectionStrings__DefaultConnection`,
SMTP credentials, `REVALIDATE_SECRET`) are environment variables set on the IIS application pool by the
deployment tool, or `dotnet user-secrets`-style files protected with DPAPI — no cloud vault is involved.

# Migrations are NOT applied by the API. Run them as the deployment account before starting the app pool:
vacms db migrate --connection "<deployment-account connection string>"
# The API verifies the schema at startup and exits 1 with the list of pending scripts if it is behind.

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

### 7. Health Check Endpoints

| Endpoint | Returns |
|---|---|
| `GET /api/health` | 200 OK `{"status":"healthy","db":"ok","storage":"ok"}` |
| `GET /api/health/live` | 200 OK (just "alive" — no dependencies) |
| `GET /api/health/ready` | 200 OK when DB is reachable |

Configure VA monitoring tools to poll `/api/health/ready` every 60 seconds.

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
# 9. Smoke test: /api/health, /admin, /
```
