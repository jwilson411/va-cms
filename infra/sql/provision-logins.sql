-- infra/sql/provision-logins.sql
-- Creates (or rotates) the two SQL Server logins the VA CMS uses, with secrets
-- supplied by the deployment pipeline. Run ONCE per SQL Server instance by an
-- operator with securityadmin rights BEFORE `vacms db migrate`; run again to
-- rotate a password. Idempotent. Issue #157 (epic #152).
--
-- Requires SQLCMD mode:
--   sqlcmd -S <server> -d master -E \
--     -v VacmsAppPassword="<secret>" VacmsReadonlyPassword="<secret>" DatabaseName="VACMS" \
--     -i infra/sql/provision-logins.sql
-- or:  vacms db provision-logins --app-password <secret> --readonly-password <secret>
--
-- The logins are policy-checked (CHECK_POLICY = ON). Passwords never appear in
-- the repo or in the DbUp migration set; V003__security_model.sql only maps the
-- logins to database users and applies the EXECUTE-only / SELECT-only grants.
-- When $(DatabaseName) already exists the same user mapping and grants are
-- applied here too, so the order of provisioning and migrating does not matter.

-- ============================================================
-- 1. Application login: vacms_app (EXECUTE-only service account)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE [name] = N'vacms_app')
    CREATE LOGIN [vacms_app] WITH PASSWORD = N'$(VacmsAppPassword)',
        CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [master];
ELSE
    ALTER LOGIN [vacms_app] WITH PASSWORD = N'$(VacmsAppPassword)', CHECK_POLICY = ON;
GO

-- ============================================================
-- 2. Read-only reporting login: vacms_readonly
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE [name] = N'vacms_readonly')
    CREATE LOGIN [vacms_readonly] WITH PASSWORD = N'$(VacmsReadonlyPassword)',
        CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [master];
ELSE
    ALTER LOGIN [vacms_readonly] WITH PASSWORD = N'$(VacmsReadonlyPassword)', CHECK_POLICY = ON;
GO

-- ============================================================
-- 3. Database users + grants (only when the database already exists;
--    otherwise V003 applies the identical block during `vacms db migrate`)
-- ============================================================
IF DB_ID(N'$(DatabaseName)') IS NOT NULL
BEGIN
    EXEC (N'USE [$(DatabaseName)];
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE [name] = N''vacms_app'')
            CREATE USER [vacms_app] FOR LOGIN [vacms_app];
        GRANT EXECUTE ON SCHEMA::dbo TO [vacms_app];
        DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [vacms_app];
        IF OBJECT_ID(N''dbo.AuditLog'') IS NOT NULL
            DENY UPDATE, DELETE ON dbo.AuditLog TO [vacms_app];

        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE [name] = N''vacms_readonly'')
            CREATE USER [vacms_readonly] FOR LOGIN [vacms_readonly];
        GRANT SELECT ON SCHEMA::dbo TO [vacms_readonly];
        DENY INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [vacms_readonly];');
    PRINT N'Mapped vacms_app / vacms_readonly into $(DatabaseName) and applied grants.';
END
ELSE
    PRINT N'Database $(DatabaseName) does not exist yet — users and grants will be applied by V003 during vacms db migrate.';
GO
