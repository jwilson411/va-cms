-- V040__site_settings.sql
-- Issue #142 (epic #141) — Runtime configuration and feature flags live in the database.
--
-- Creates:
--   SiteSetting table                 — one row per key; Value is what admins edit, DefaultValue
--                                       is the code default the row can be reset to.
--   usp_SiteSetting_List              — every row, for the cache loader and the admin screen
--   usp_SiteSetting_EnsureDefinition  — called at API startup for every C#-declared setting:
--                                       inserts a missing row, refreshes metadata/default on an
--                                       existing one, never touches an admin-edited Value.
--   usp_SiteSetting_SetValue          — admin write (audit is written by the API)
--   usp_SiteSetting_Reset             — Value := DefaultValue
--   fn_SiteSetting_GetBool            — scalar lookup for use inside other stored procedures
--
-- Adding a new setting never needs a migration: declare it in
-- VA.CMS.Infrastructure/Settings/SiteSettingDefinitions.cs and the startup sync creates the row.
--
-- Also re-creates usp_Workflow_Transition so the "comment required when returning to Draft"
-- rule is governed by the workflow.requireReturnComment setting.

-- ── 1. Table ─────────────────────────────────────────────────────────────────

CREATE TABLE [dbo].[SiteSetting] (
    [Id]           BIGINT IDENTITY(1,1) NOT NULL,
    [Key]          NVARCHAR(200)        NOT NULL,   -- dotted, e.g. 'features.webhooks'
    [Value]        NVARCHAR(MAX)        NULL,       -- current value (NULL = use DefaultValue)
    [DefaultValue] NVARCHAR(MAX)        NULL,       -- code default, refreshed at startup
    [DataType]     NVARCHAR(20)         NOT NULL,   -- string | int | bool | json
    [Category]     NVARCHAR(50)         NOT NULL,   -- grouping for the admin screen
    [Scope]        NVARCHAR(20)         NOT NULL,   -- Server | Admin | Public (who may read it)
    [Description]  NVARCHAR(1000)       NULL,
    [SortOrder]    INT                  NOT NULL CONSTRAINT [DF_SiteSetting_SortOrder] DEFAULT 0,
    [UpdatedById]  BIGINT               NULL,
    [UpdatedAt]    DATETIME2            NOT NULL CONSTRAINT [DF_SiteSetting_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_SiteSetting] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_SiteSetting_UpdatedBy] FOREIGN KEY ([UpdatedById]) REFERENCES [dbo].[User] ([Id]),
    CONSTRAINT [CK_SiteSetting_DataType] CHECK ([DataType] IN ('string', 'int', 'bool', 'json')),
    CONSTRAINT [CK_SiteSetting_Scope]    CHECK ([Scope]    IN ('Server', 'Admin', 'Public'))
);
CREATE UNIQUE INDEX [UX_SiteSetting_Key] ON [dbo].[SiteSetting] ([Key]);
GO

-- ── 2. usp_SiteSetting_List ──────────────────────────────────────────────────

CREATE OR ALTER PROCEDURE [dbo].[usp_SiteSetting_List]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.[Id], s.[Key], s.[Value], s.[DefaultValue], s.[DataType], s.[Category], s.[Scope],
           s.[Description], s.[SortOrder], s.[UpdatedById], s.[UpdatedAt],
           u.[DisplayName] AS UpdatedByName
    FROM   [dbo].[SiteSetting] s
    LEFT   JOIN [dbo].[User] u ON u.[Id] = s.[UpdatedById]
    ORDER  BY s.[Category], s.[SortOrder], s.[Key];
END;
GO

-- ── 3. usp_SiteSetting_EnsureDefinition ──────────────────────────────────────
-- Idempotent. Metadata and DefaultValue follow the code; Value is preserved.

CREATE OR ALTER PROCEDURE [dbo].[usp_SiteSetting_EnsureDefinition]
    @Key          NVARCHAR(200),
    @DefaultValue NVARCHAR(MAX),
    @DataType     NVARCHAR(20),
    @Category     NVARCHAR(50),
    @Scope        NVARCHAR(20),
    @Description  NVARCHAR(1000) = NULL,
    @SortOrder    INT            = 0
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM [dbo].[SiteSetting] WHERE [Key] = @Key)
    BEGIN
        UPDATE [dbo].[SiteSetting]
        SET    [DefaultValue] = @DefaultValue,
               [DataType]     = @DataType,
               [Category]     = @Category,
               [Scope]        = @Scope,
               [Description]  = @Description,
               [SortOrder]    = @SortOrder
        WHERE  [Key] = @Key;
        RETURN;
    END;

    INSERT INTO [dbo].[SiteSetting]
        ([Key], [Value], [DefaultValue], [DataType], [Category], [Scope], [Description], [SortOrder])
    VALUES
        (@Key, NULL, @DefaultValue, @DataType, @Category, @Scope, @Description, @SortOrder);
END;
GO

-- ── 4. usp_SiteSetting_SetValue ──────────────────────────────────────────────
-- @Success = 0 when the key is unknown (the API only writes declared keys).

CREATE OR ALTER PROCEDURE [dbo].[usp_SiteSetting_SetValue]
    @Key         NVARCHAR(200),
    @Value       NVARCHAR(MAX),
    @UpdatedById BIGINT,
    @Success     BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Actor 0 / unknown ids (automation, tests) are recorded as NULL rather than violating the FK.
    IF NOT EXISTS (SELECT 1 FROM [dbo].[User] WHERE [Id] = @UpdatedById) SET @UpdatedById = NULL;

    UPDATE [dbo].[SiteSetting]
    SET    [Value]       = @Value,
           [UpdatedById] = @UpdatedById,
           [UpdatedAt]   = SYSUTCDATETIME()
    WHERE  [Key] = @Key;

    SET @Success = CASE WHEN @@ROWCOUNT = 1 THEN 1 ELSE 0 END;
END;
GO

-- ── 5. usp_SiteSetting_Reset ─────────────────────────────────────────────────

CREATE OR ALTER PROCEDURE [dbo].[usp_SiteSetting_Reset]
    @Key         NVARCHAR(200),
    @UpdatedById BIGINT,
    @Success     BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM [dbo].[User] WHERE [Id] = @UpdatedById) SET @UpdatedById = NULL;

    UPDATE [dbo].[SiteSetting]
    SET    [Value]       = NULL,
           [UpdatedById] = @UpdatedById,
           [UpdatedAt]   = SYSUTCDATETIME()
    WHERE  [Key] = @Key;

    SET @Success = CASE WHEN @@ROWCOUNT = 1 THEN 1 ELSE 0 END;
END;
GO

-- ── 6. fn_SiteSetting_GetBool ────────────────────────────────────────────────
-- For stored procedures that need a flag. Falls back to DefaultValue, then @Fallback.

CREATE OR ALTER FUNCTION [dbo].[fn_SiteSetting_GetBool]
(
    @Key      NVARCHAR(200),
    @Fallback BIT
)
RETURNS BIT
AS
BEGIN
    DECLARE @Raw NVARCHAR(MAX);

    SELECT @Raw = COALESCE([Value], [DefaultValue])
    FROM   [dbo].[SiteSetting]
    WHERE  [Key] = @Key;

    IF @Raw IS NULL RETURN @Fallback;

    RETURN CASE
        WHEN LOWER(LTRIM(RTRIM(@Raw))) IN ('true', '1', 'yes', 'on')  THEN 1
        WHEN LOWER(LTRIM(RTRIM(@Raw))) IN ('false', '0', 'no', 'off') THEN 0
        ELSE @Fallback
    END;
END;
GO

-- ── 7. usp_Workflow_Transition — return-comment rule becomes a setting ───────
-- Identical to V008 except the InReview → Draft comment check consults
-- workflow.requireReturnComment (default on).

CREATE OR ALTER PROCEDURE [dbo].[usp_Workflow_Transition]
    @ContentEntryId   BIGINT,
    @ContentVersionId BIGINT,
    @FromStatus       NVARCHAR(20),
    @ToStatus         NVARCHAR(20),
    @ActorId          BIGINT,
    @Comment          NVARCHAR(2000) = NULL,
    @Success          BIT            OUTPUT,
    @ErrorMessage     NVARCHAR(500)  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Success      = 0;
    SET @ErrorMessage = NULL;

    IF NOT EXISTS (SELECT 1 FROM [dbo].[ContentEntry] WHERE [Id] = @ContentEntryId AND [Status] = @FromStatus)
    BEGIN
        SET @ErrorMessage = 'Content is not in expected status ' + @FromStatus;
        RETURN;
    END;

    IF NOT EXISTS (
        SELECT 1 FROM (VALUES
            ('Draft',    'InReview'),
            ('InReview', 'Approved'),
            ('InReview', 'Draft'),
            ('Approved', 'Published'),
            ('Approved', 'Draft'),
            ('Published','Archived'),
            ('Published','Approved'),
            ('Draft',    'Published'),
            ('Approved', 'Archived')
        ) AS AllowedTransitions(FromS, ToS)
        WHERE FromS = @FromStatus AND ToS = @ToStatus
    )
    BEGIN
        SET @ErrorMessage = 'Transition from ' + @FromStatus + ' to ' + @ToStatus + ' is not permitted';
        RETURN;
    END;

    IF @ToStatus = 'Draft' AND @FromStatus = 'InReview'
       AND [dbo].[fn_SiteSetting_GetBool]('workflow.requireReturnComment', 1) = 1
       AND (@Comment IS NULL OR LEN(TRIM(@Comment)) = 0)
    BEGIN
        SET @ErrorMessage = 'A comment is required when returning content to Draft';
        RETURN;
    END;

    UPDATE [dbo].[ContentEntry]
    SET    [Status]    = @ToStatus,
           [UpdatedAt] = SYSUTCDATETIME()
    WHERE  [Id] = @ContentEntryId;

    INSERT INTO [dbo].[WorkflowTransition]
        ([ContentEntryId], [ContentVersionId], [FromStatus], [ToStatus], [ActorId], [Comment], [CreatedAt])
    VALUES
        (@ContentEntryId, @ContentVersionId, @FromStatus, @ToStatus, @ActorId, @Comment, SYSUTCDATETIME());

    DECLARE @AuditAction NVARCHAR(100) = 'WorkflowTransition_' + @ToStatus;
    EXEC [dbo].[usp_AuditLog_Write]
        @ActorId, 'ContentEntry', @ContentEntryId,
        @AuditAction, NULL;

    SET @Success = 1;
END;
GO
