-- V003__security_model.sql
-- Application SQL Server logins, users, and permission model.
-- The SA password placeholders here are documentation only —
-- the actual passwords must be injected at deploy time via SQLCMD variables
-- (vacms_app password) or set out-of-band.
-- For local dev, the sa login is used directly and this script is a no-op
-- if the logins already exist.
--
-- See DATABASE_LAYER.md §2

-- Application login (password injected by deployment pipeline as $(VacmsAppPassword))
-- In local dev you may use the sa account via the connection string in appsettings.Development.json.
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE [name] = N'vacms_app')
BEGIN
    -- NOTE: $(VacmsAppPassword) is a SQLCMD variable — set with -v or via deployment secret.
    -- For local dev this block is skipped (login already exists or sa is used instead).
    PRINT 'SKIP: vacms_app login must be created by the deployment pipeline with a deployment-managed password.';
END;
GO

-- Read-only reporting login (optional, for analytics exports)
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE [name] = N'vacms_readonly')
BEGIN
    PRINT 'SKIP: vacms_readonly login must be created by the deployment pipeline.';
END;
GO

-- ============================================================
-- Note on permissions:
-- Once the logins are created, the deployment pipeline should run:
--
--   CREATE USER [vacms_app] FOR LOGIN [vacms_app];
--   GRANT EXECUTE ON SCHEMA::dbo TO [vacms_app];
--   DENY  SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [vacms_app];
--
--   CREATE USER [vacms_readonly] FOR LOGIN [vacms_readonly];
--   GRANT SELECT ON SCHEMA::dbo TO [vacms_readonly];
--   DENY  INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [vacms_readonly];
--
-- This is separated from this migration so the SA password never appears
-- in source control and the migration can run in dev without a vacms_app login.
-- ============================================================
