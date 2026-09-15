-- V003__security_model.sql
-- Application SQL Server logins, users, and permission model.
-- BRD NFR-DB-01, NFR-DB-04
--
-- vacms_app  : application service account — EXECUTE on stored procedures only.
--              Direct SELECT/INSERT/UPDATE/DELETE on tables is explicitly denied.
-- vacms_readonly : read-only reporting account — SELECT only.
--
-- Passwords are set via SQLCMD variables ($(VacmsAppPassword), $(VacmsReadonlyPassword))
-- injected by the deployment pipeline.  For local dev and integration tests the
-- container SA connection is used directly, and the logins are created with a
-- well-known dev password so the integration tests can open a second connection
-- as vacms_app and verify permission enforcement.
--
-- IMPORTANT: the SQLCMD variable substitution below falls back to a literal
-- token when SQLCMD variables are not supplied (plain DbUp SQL execution).
-- DbUp runs this as plain SQL, so we cannot use $(VarName) syntax safely.
-- Instead we use a fixed dev-only password and rely on the deployment pipeline
-- to ALTER LOGIN with the real secret AFTER this migration runs.

-- ============================================================
-- 1. Application login: vacms_app
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE [name] = N'vacms_app')
BEGIN
    -- Dev/test password — deployment pipeline must ALTER LOGIN after provisioning
    -- to set the production secret. Never use this password in production.
    -- CHECK_POLICY=OFF is required for the Docker test container which enforces
    -- Windows password policy by default; production deployments should use
    -- CHECK_POLICY=ON with a deployment-managed secret.
    CREATE LOGIN [vacms_app] WITH PASSWORD = N'VaCms_App!Dev2026',
        CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF;
END;
GO

-- Create the database user mapped to the login (idempotent)
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE [name] = N'vacms_app')
BEGIN
    CREATE USER [vacms_app] FOR LOGIN [vacms_app];
END;
GO

-- Grant EXECUTE on all stored procedures in the dbo schema
GRANT EXECUTE ON SCHEMA::dbo TO [vacms_app];
GO

-- Explicitly deny all direct table DML — app must go through SPs
DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [vacms_app];
GO

-- AuditLog: extra-defensive deny so the app login can never UPDATE or DELETE
-- audit rows even if schema-level grants are accidentally broadened later.
DENY UPDATE, DELETE ON dbo.AuditLog TO [vacms_app];
GO

-- ============================================================
-- 2. Read-only reporting login: vacms_readonly
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE [name] = N'vacms_readonly')
BEGIN
    CREATE LOGIN [vacms_readonly] WITH PASSWORD = N'VaCms_Readonly!Dev2026',
        CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE [name] = N'vacms_readonly')
BEGIN
    CREATE USER [vacms_readonly] FOR LOGIN [vacms_readonly];
END;
GO

GRANT SELECT ON SCHEMA::dbo TO [vacms_readonly];
GO

DENY INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [vacms_readonly];
GO
