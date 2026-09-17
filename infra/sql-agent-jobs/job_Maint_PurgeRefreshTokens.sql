-- job_Maint_PurgeRefreshTokens.sql
-- SQL Server Agent job definition: Refresh Token Purge (#163)
-- Schedule: Daily at 02:30 local time
-- Procedure: usp_Maint_PurgeRefreshTokens
--
-- Deletes revoked or expired [RefreshToken] rows older than 30 days. Live rows
-- are never touched; the security-relevant events (logon, refresh, replay,
-- revocation) are already in [AuditLog], so the token rows are only kept long
-- enough to investigate a replay report.
--
-- Run this script as a sysadmin to register the Agent job.
-- Idempotent: drops and recreates the job if it already exists.

USE [msdb];
GO

-- ============================================================
-- Remove existing job if present (allows re-run)
-- ============================================================
IF EXISTS (
    SELECT job_id FROM msdb.dbo.sysjobs WHERE name = N'job_Maint_PurgeRefreshTokens'
)
BEGIN
    EXEC msdb.dbo.sp_delete_job @job_name = N'job_Maint_PurgeRefreshTokens', @delete_unused_schedule = 1;
END;
GO

-- ============================================================
-- Create job
-- ============================================================
DECLARE @job_id   UNIQUEIDENTIFIER;
DECLARE @schedule_id INT;

EXEC msdb.dbo.sp_add_job
    @job_name        = N'job_Maint_PurgeRefreshTokens',
    @description     = N'Purges revoked/expired RefreshToken rows older than 30 days. Runs nightly.',
    @category_name   = N'Database Maintenance',
    @owner_login_name= N'sa',
    @job_id          = @job_id OUTPUT;

-- Step 1: Execute the maintenance SP
EXEC msdb.dbo.sp_add_jobstep
    @job_id              = @job_id,
    @step_name           = N'Purge Old Refresh Tokens',
    @step_id             = 1,
    @subsystem           = N'TSQL',
    @command             = N'EXEC [VACMS].[dbo].[usp_Maint_PurgeRefreshTokens] @RetentionDays = 30;',
    @database_name       = N'VACMS',
    @on_success_action   = 1,  -- Quit with success
    @on_fail_action      = 2;  -- Quit with failure

-- Schedule: Daily at 02:30
EXEC msdb.dbo.sp_add_schedule
    @schedule_name          = N'sched_Maint_PurgeRefreshTokens_Daily0230',
    @freq_type              = 4,      -- Daily
    @freq_interval          = 1,      -- Every 1 day
    @active_start_time      = 23000,  -- 02:30:00 (HHMMSS)
    @schedule_id            = @schedule_id OUTPUT;

EXEC msdb.dbo.sp_attach_schedule
    @job_id      = @job_id,
    @schedule_id = @schedule_id;

-- Target: local server
EXEC msdb.dbo.sp_add_jobserver
    @job_id      = @job_id,
    @server_name = N'(LOCAL)';

PRINT N'job_Maint_PurgeRefreshTokens created successfully.';
GO
