-- job_Maint_RollupSearchLogs.sql
-- SQL Server Agent job definition: Search Log Rollup
-- Schedule: Daily at 00:15 local time
-- Procedure: usp_Maint_RollupSearchLogs
--
-- Retention of the raw SearchQueryLog / SearchResultClick rows is the site setting
-- search.analytics.retentionDays (default 90), read by the procedure itself (#175);
-- nothing here needs editing to change it.
--
-- Run this script as a sysadmin to register the Agent job.
-- Idempotent: drops and recreates the job if it already exists.

USE [msdb];
GO

-- ============================================================
-- Remove existing job if present (allows re-run)
-- ============================================================
IF EXISTS (
    SELECT job_id FROM msdb.dbo.sysjobs WHERE name = N'job_Maint_RollupSearchLogs'
)
BEGIN
    EXEC msdb.dbo.sp_delete_job @job_name = N'job_Maint_RollupSearchLogs', @delete_unused_schedule = 1;
END;
GO

-- ============================================================
-- Create job
-- ============================================================
DECLARE @job_id   UNIQUEIDENTIFIER;
DECLARE @schedule_id INT;

EXEC msdb.dbo.sp_add_job
    @job_name        = N'job_Maint_RollupSearchLogs',
    @description     = N'Aggregates completed days of SearchQueryLog into SearchQuerySummary; purges raw query and click rows older than search.analytics.retentionDays (site setting, default 90).',
    @category_name   = N'Database Maintenance',
    @owner_login_name= N'sa',
    @job_id          = @job_id OUTPUT;

-- Step 1: Execute the maintenance SP
EXEC msdb.dbo.sp_add_jobstep
    @job_id              = @job_id,
    @step_name           = N'Roll Up Search Logs',
    @step_id             = 1,
    @subsystem           = N'TSQL',
    @command             = N'EXEC [VACMS].[dbo].[usp_Maint_RollupSearchLogs];',
    @database_name       = N'VACMS',
    @on_success_action   = 1,  -- Quit with success
    @on_fail_action      = 2;  -- Quit with failure

-- Schedule: Daily at 00:15
EXEC msdb.dbo.sp_add_schedule
    @schedule_name       = N'sched_Maint_RollupSearchLogs_Daily0015',
    @freq_type           = 4,      -- Daily
    @freq_interval       = 1,      -- Every 1 day
    @active_start_time   = 1500,   -- 00:15:00 (HHMMSS)
    @schedule_id         = @schedule_id OUTPUT;

EXEC msdb.dbo.sp_attach_schedule
    @job_id      = @job_id,
    @schedule_id = @schedule_id;

-- Target: local server
EXEC msdb.dbo.sp_add_jobserver
    @job_id      = @job_id,
    @server_name = N'(LOCAL)';

PRINT N'job_Maint_RollupSearchLogs created successfully.';
GO
