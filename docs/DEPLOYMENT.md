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

# 4. Migrations run automatically on API startup (DbUp)
# No separate migration command needed. On first run, all SQL scripts in /migrations/ execute.

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

```sql
-- Create database
CREATE DATABASE [VACMS]
  COLLATE SQL_Latin1_General_CP1_CI_AS;

-- Create application login (principle of least privilege)
CREATE LOGIN [vacms_app] WITH PASSWORD = N'<strong_password>';
USE [VACMS];
CREATE USER [vacms_app] FOR LOGIN [vacms_app];

-- Grant required permissions (no DDL after migrations run)
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [vacms_app];

-- AuditLog is write-only for the app
DENY UPDATE, DELETE ON dbo.AuditLog TO [vacms_app];

-- Enable Full-Text Search
EXEC sp_fulltext_database 'enable';
```

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
Auth__AzureAd__TenantId=<aad_tenant_id>
Auth__AzureAd__ClientId=<app_registration_client_id>
Auth__AzureAd__ClientSecret=<client_secret>
Storage__Backend=local
Storage__LocalPath=D:\vacms-uploads
Email__SmtpHost=mail.va.gov
Email__SmtpPort=587
Email__FromAddress=noreply-cms@va.gov
Jwt__SigningKey=<256-bit-random-key>

# Public site (Next.js)
NEXT_PUBLIC_API_URL=https://cms.youragency.va.gov/api/v1
```

# 4. Run migrations in production (DbUp runs automatically on startup)
# Migrations run automatically when the API starts.
# Verify the startup logs show "Successfully upgraded" before marking deployment complete.
# To run manually (dry-run check):
dotnet VA.CMS.API.dll --check-migrations

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
# 4. Run migrations: dotnet VA.CMS.API.dll migrate
# 5. If migrations succeed: swap staging to production (xcopy or IIS virtual directory swap)
# 6. Deploy new admin/public builds
# 7. Restart app pool
# 8. Remove maintenance mode
# 9. Smoke test: /api/health, /admin, /
```
