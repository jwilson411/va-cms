-- job_Maint_ArchiveAuditLog.sql
-- SQL Server Agent job definition: Audit Log Archival
-- Schedule: Monthly, first Sunday at 00:00 local time
-- Procedure: usp_Maint_ArchiveAuditLog
--
-- Run this script as a sysadmin to register the Agent job.
-- Idempotent: drops and recreates the job if it already exists.

USE [msdb];
GO

-- ============================================================
-- Remove existing job if present (allows re-run)
-- ============================================================
IF EXISTS (
    SELECT job_id FROM msdb.dbo.sysjobs WHERE name = N'job_Maint_ArchiveAuditLog'
)
BEGIN
    EXEC msdb.dbo.sp_delete_job @job_name = N'job_Maint_ArchiveAuditLog', @delete_unused_schedule = 1;
END;
GO

-- ============================================================
-- Create job
-- ============================================================
DECLARE @job_id   UNIQUEIDENTIFIER;
DECLARE @schedule_id INT;

EXEC msdb.dbo.sp_add_job
    @job_name        = N'job_Maint_ArchiveAuditLog',
    @description     = N'Moves AuditLog rows older than 730 days to AuditLogArchive in 5000-row batches. Runs monthly on first Sunday.',
    @category_name   = N'Database Maintenance',
    @owner_login_name= N'sa',
    @job_id          = @job_id OUTPUT;

-- Step 1: Execute the maintenance SP
EXEC msdb.dbo.sp_add_jobstep
    @job_id              = @job_id,
    @step_name           = N'Archive Old Audit Rows',
    @step_id             = 1,
    @subsystem           = N'TSQL',
    @command             = N'EXEC [VACMS].[dbo].[usp_Maint_ArchiveAuditLog];',
    @database_name       = N'VACMS',
    @on_success_action   = 1,  -- Quit with success
    @on_fail_action      = 2;  -- Quit with failure

-- Schedule: Monthly, first Sunday at 00:00
-- freq_type=32 (monthly, relative), freq_interval=1 (Sunday), freq_relative_interval=1 (first)
EXEC msdb.dbo.sp_add_schedule
    @schedule_name          = N'sched_Maint_ArchiveAuditLog_MonthlyFirstSun0000',
    @freq_type              = 32,    -- Monthly relative
    @freq_interval          = 1,     -- Sunday
    @freq_relative_interval = 1,     -- First (occurrence)
    @freq_recurrence_factor = 1,     -- Every 1 month
    @active_start_time      = 0,     -- 00:00:00 (HHMMSS)
    @schedule_id            = @schedule_id OUTPUT;

EXEC msdb.dbo.sp_attach_schedule
    @job_id      = @job_id,
    @schedule_id = @schedule_id;

-- Target: local server
EXEC msdb.dbo.sp_add_jobserver
    @job_id      = @job_id,
    @server_name = N'(LOCAL)';

PRINT N'job_Maint_ArchiveAuditLog created successfully.';
GO
