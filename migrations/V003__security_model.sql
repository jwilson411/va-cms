-- V003__security_model.sql
-- Database users and permission model for the application logins.
-- BRD NFR-DB-01, NFR-DB-04. Issue #157: no login or password material lives in
-- the migration set — infra/sql/provision-logins.sql creates the server logins
-- with pipeline-supplied secrets (CHECK_POLICY = ON). This script only maps
-- those logins to database users and applies the grants, and is a no-op for a
-- login that has not been provisioned yet (re-run provision-logins.sql
-- afterwards; it applies the same grants).
--
-- vacms_app      : application service account — EXECUTE on stored procedures only.
--                  Direct SELECT/INSERT/UPDATE/DELETE on tables is explicitly denied.
-- vacms_readonly : read-only reporting account — SELECT only.

-- ============================================================
-- 1. Application user: vacms_app
-- ============================================================
IF EXISTS (SELECT 1 FROM sys.server_principals WHERE [name] = N'vacms_app')
   AND NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE [name] = N'vacms_app')
BEGIN
    CREATE USER [vacms_app] FOR LOGIN [vacms_app];
END;
GO

IF EXISTS (SELECT 1 FROM sys.database_principals WHERE [name] = N'vacms_app')
BEGIN
    -- EXECUTE on every stored procedure in dbo (covers procedures added by later migrations)
    GRANT EXECUTE ON SCHEMA::dbo TO [vacms_app];

    -- Explicitly deny all direct table DML — the app must go through SPs
    DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [vacms_app];

    -- AuditLog: extra-defensive deny so the app login can never UPDATE or DELETE
    -- audit rows even if schema-level grants are accidentally broadened later.
    DENY UPDATE, DELETE ON dbo.AuditLog TO [vacms_app];
END
ELSE
    PRINT N'V003: login vacms_app not provisioned — skipping user mapping (run infra/sql/provision-logins.sql).';
GO

-- ============================================================
-- 2. Read-only reporting user: vacms_readonly
-- ============================================================
IF EXISTS (SELECT 1 FROM sys.server_principals WHERE [name] = N'vacms_readonly')
   AND NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE [name] = N'vacms_readonly')
BEGIN
    CREATE USER [vacms_readonly] FOR LOGIN [vacms_readonly];
END;
GO

IF EXISTS (SELECT 1 FROM sys.database_principals WHERE [name] = N'vacms_readonly')
BEGIN
    GRANT SELECT ON SCHEMA::dbo TO [vacms_readonly];
    DENY INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [vacms_readonly];
END
ELSE
    PRINT N'V003: login vacms_readonly not provisioned — skipping user mapping (run infra/sql/provision-logins.sql).';
GO
