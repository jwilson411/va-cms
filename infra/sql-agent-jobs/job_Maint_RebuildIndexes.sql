-- job_Maint_RebuildIndexes.sql
-- SQL Server Agent job definition: Index Maintenance
-- Schedule: Weekly Sunday at 01:00 local time
-- Procedure: usp_Maint_RebuildIndexes
--
-- Run this script as a sysadmin to register the Agent job.
-- Idempotent: drops and recreates the job if it already exists.

USE [msdb];
GO

-- ============================================================
-- Remove existing job if present (allows re-run)
-- ============================================================
IF EXISTS (
    SELECT job_id FROM msdb.dbo.sysjobs WHERE name = N'job_Maint_RebuildIndexes'
)
BEGIN
    EXEC msdb.dbo.sp_delete_job @job_name = N'job_Maint_RebuildIndexes', @delete_unused_schedule = 1;
END;
GO

-- ============================================================
-- Create job
-- ============================================================
DECLARE @job_id   UNIQUEIDENTIFIER;
DECLARE @schedule_id INT;

EXEC msdb.dbo.sp_add_job
    @job_name        = N'job_Maint_RebuildIndexes',
    @description     = N'Rebuilds (>30% frag) or reorganizes (10-30% frag) indexes on all VA CMS tables. Runs off-peak Sunday.',
    @category_name   = N'Database Maintenance',
    @owner_login_name= N'sa',
    @job_id          = @job_id OUTPUT;

-- Step 1: Execute the maintenance SP
EXEC msdb.dbo.sp_add_jobstep
    @job_id              = @job_id,
    @step_name           = N'Rebuild / Reorganize Indexes',
    @step_id             = 1,
    @subsystem           = N'TSQL',
    @command             = N'EXEC [VACMS].[dbo].[usp_Maint_RebuildIndexes];',
    @database_name       = N'VACMS',
    @on_success_action   = 1,  -- Quit with success
    @on_fail_action      = 2;  -- Quit with failure

-- Schedule: Weekly, Sunday at 01:00
-- freq_type=8 (weekly), freq_interval=1 (Sunday=1 in bitmask)
EXEC msdb.dbo.sp_add_schedule
    @schedule_name       = N'sched_Maint_RebuildIndexes_WeeklySun0100',
    @freq_type           = 8,      -- Weekly
    @freq_interval       = 1,      -- Sunday (bitmask: Sun=1)
    @freq_recurrence_factor = 1,   -- Every 1 week
    @active_start_time   = 10000,  -- 01:00:00 (HHMMSS)
    @schedule_id         = @schedule_id OUTPUT;

EXEC msdb.dbo.sp_attach_schedule
    @job_id      = @job_id,
    @schedule_id = @schedule_id;

-- Target: local server
EXEC msdb.dbo.sp_add_jobserver
    @job_id      = @job_id,
    @server_name = N'(LOCAL)';

PRINT N'job_Maint_RebuildIndexes created successfully.';
GO
