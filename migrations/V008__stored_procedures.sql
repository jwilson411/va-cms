-- V008__stored_procedures.sql
-- All application stored procedures (usp_*).
-- App layer calls EXEC usp_* only — no direct DML.
-- See DATABASE_LAYER.md §4

-- ============================================================
-- §4.9 Audit Log (referenced by other SPs — defined first)
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[usp_AuditLog_Write]
    @ActorId    BIGINT,
    @EntityType NVARCHAR(100),
    @EntityId   BIGINT,
    @Action     NVARCHAR(50),
    @DiffJson   NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ActorEmail NVARCHAR(500);
    SELECT @ActorEmail = [Email] FROM [dbo].[User] WHERE [Id] = @ActorId;

    INSERT INTO [dbo].[AuditLog]
        ([ActorId], [ActorEmail], [EntityType], [EntityId], [Action], [DiffJson], [CreatedAt])
    VALUES
        (@ActorId, @ActorEmail, @EntityType, CAST(@EntityId AS NVARCHAR(100)), @Action, @DiffJson, SYSUTCDATETIME());
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_AuditLog_List]
    @ActorId    BIGINT        = NULL,
    @EntityType NVARCHAR(100) = NULL,
    @Action     NVARCHAR(50)  = NULL,
    @FromDate   DATETIME2     = NULL,
    @ToDate     DATETIME2     = NULL,
    @Page       INT           = 1,
    @PageSize   INT           = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
    FROM   [dbo].[AuditLog]
    WHERE  (@ActorId    IS NULL OR [ActorId]    = @ActorId)
      AND  (@EntityType IS NULL OR [EntityType] = @EntityType)
      AND  (@Action     IS NULL OR [Action]     = @Action)
      AND  (@FromDate   IS NULL OR [CreatedAt] >= @FromDate)
      AND  (@ToDate     IS NULL OR [CreatedAt] <= @ToDate)
    ORDER  BY [CreatedAt] DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH  NEXT @PageSize ROWS ONLY;
END;
GO

-- ============================================================
-- §4.1 Content Entry
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_GetById]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT e.*, v.[FieldsJson], v.[RenderedFieldsJson], v.[VersionNumber]
    FROM   [dbo].[ContentEntry] e
    LEFT JOIN [dbo].[ContentVersion] v ON v.[Id] = e.[PublishedVersionId]
    WHERE  e.[Id] = @Id;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_GetBySlug]
    @Slug   NVARCHAR(500),
    @Locale NVARCHAR(10) = 'en-US'
AS
BEGIN
    SET NOCOUNT ON;
    SELECT e.*, v.[FieldsJson], v.[RenderedFieldsJson], v.[VersionNumber]
    FROM   [dbo].[ContentEntry] e
    JOIN   [dbo].[ContentVersion] v ON v.[Id] = e.[PublishedVersionId]
    WHERE  e.[Slug]   = @Slug
      AND  e.[Locale] = @Locale
      AND  e.[Status] = 'Published';
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_List]
    @ContentTypeId BIGINT        = NULL,
    @Status        NVARCHAR(20)  = NULL,
    @OwnerId       BIGINT        = NULL,
    @Locale        NVARCHAR(10)  = NULL,
    @SearchTerm    NVARCHAR(200) = NULL,
    @Page          INT           = 1,
    @PageSize      INT           = 25,
    @TotalRows     INT           OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    WITH Filtered AS (
        SELECT e.[Id], e.[Slug], e.[ContentTypeId], e.[Status], e.[OwnerId],
               e.[Locale], e.[UpdatedAt], e.[CreatedAt], e.[ScheduledPublishAt],
               ROW_NUMBER() OVER (ORDER BY e.[UpdatedAt] DESC) AS RowNum
        FROM   [dbo].[ContentEntry] e
        WHERE  (@ContentTypeId IS NULL OR e.[ContentTypeId] = @ContentTypeId)
          AND  (@Status        IS NULL OR e.[Status]        = @Status)
          AND  (@OwnerId       IS NULL OR e.[OwnerId]       = @OwnerId)
          AND  (@Locale        IS NULL OR e.[Locale]        = @Locale)
          AND  (@SearchTerm    IS NULL OR e.[Slug] LIKE '%' + @SearchTerm + '%')
    )
    SELECT @TotalRows = COUNT(*) FROM Filtered;

    WITH Filtered AS (
        SELECT e.*, ROW_NUMBER() OVER (ORDER BY e.[UpdatedAt] DESC) AS RowNum
        FROM   [dbo].[ContentEntry] e
        WHERE  (@ContentTypeId IS NULL OR e.[ContentTypeId] = @ContentTypeId)
          AND  (@Status        IS NULL OR e.[Status]        = @Status)
          AND  (@OwnerId       IS NULL OR e.[OwnerId]       = @OwnerId)
          AND  (@Locale        IS NULL OR e.[Locale]        = @Locale)
          AND  (@SearchTerm    IS NULL OR e.[Slug] LIKE '%' + @SearchTerm + '%')
    )
    SELECT * FROM Filtered
    WHERE  RowNum BETWEEN ((@Page - 1) * @PageSize + 1) AND (@Page * @PageSize);
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_Create]
    @ContentTypeId BIGINT,
    @Slug          NVARCHAR(500),
    @Locale        NVARCHAR(10),
    @OwnerId       BIGINT,
    @NewId         BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[ContentEntry]
        ([ContentTypeId], [Slug], [Locale], [Status], [OwnerId], [CreatedAt], [UpdatedAt])
    VALUES
        (@ContentTypeId, @Slug, @Locale, 'Draft', @OwnerId, SYSUTCDATETIME(), SYSUTCDATETIME());

    SET @NewId = SCOPE_IDENTITY();
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_UpdateStatus]
    @Id                 BIGINT,
    @Status             NVARCHAR(20),
    @PublishedVersionId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [dbo].[ContentEntry]
    SET    [Status]             = @Status,
           [PublishedVersionId] = COALESCE(@PublishedVersionId, [PublishedVersionId]),
           [UpdatedAt]          = SYSUTCDATETIME()
    WHERE  [Id] = @Id;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_Archive]
    @Id      BIGINT,
    @ActorId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [dbo].[ContentEntry]
    SET    [Status]    = 'Archived',
           [UpdatedAt] = SYSUTCDATETIME()
    WHERE  [Id] = @Id;

    EXEC [dbo].[usp_AuditLog_Write]
        @ActorId   = @ActorId,
        @EntityType = 'ContentEntry',
        @EntityId  = @Id,
        @Action    = 'Archive',
        @DiffJson  = NULL;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_GetScheduledForPublish]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], [ContentTypeId], [Slug], [Locale]
    FROM   [dbo].[ContentEntry]
    WHERE  [Status]              = 'Approved'
      AND  [ScheduledPublishAt] IS NOT NULL
      AND  [ScheduledPublishAt] <= SYSUTCDATETIME();
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_GetScheduledForExpiry]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], [ContentTypeId], [Slug], [Locale]
    FROM   [dbo].[ContentEntry]
    WHERE  [Status]             = 'Published'
      AND  [ScheduledExpireAt] IS NOT NULL
      AND  [ScheduledExpireAt] <= SYSUTCDATETIME();
END;
GO

-- ============================================================
-- §4.2 Content Version
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentVersion_Create]
    @ContentEntryId     BIGINT,
    @FieldsJson         NVARCHAR(MAX),
    @RenderedFieldsJson NVARCHAR(MAX) = NULL,
    @Status             NVARCHAR(20),
    @AuthorId           BIGINT,
    @ChangeNote         NVARCHAR(1000) = NULL,
    @NewId              BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NextVersion INT;
    SELECT @NextVersion = ISNULL(MAX([VersionNumber]), 0) + 1
    FROM   [dbo].[ContentVersion]
    WHERE  [ContentEntryId] = @ContentEntryId;

    INSERT INTO [dbo].[ContentVersion]
        ([ContentEntryId], [VersionNumber], [FieldsJson], [RenderedFieldsJson],
         [Status], [AuthorId], [ChangeNote], [CreatedAt])
    VALUES
        (@ContentEntryId, @NextVersion, @FieldsJson, @RenderedFieldsJson,
         @Status, @AuthorId, @ChangeNote, SYSUTCDATETIME());

    SET @NewId = SCOPE_IDENTITY();
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentVersion_List]
    @ContentEntryId BIGINT,
    @Page           INT = 1,
    @PageSize       INT = 25
AS
BEGIN
    SET NOCOUNT ON;
    SELECT v.*, u.[DisplayName] AS AuthorName
    FROM   [dbo].[ContentVersion] v
    JOIN   [dbo].[User] u ON u.[Id] = v.[AuthorId]
    WHERE  v.[ContentEntryId] = @ContentEntryId
    ORDER  BY v.[VersionNumber] DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH  NEXT @PageSize ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentVersion_GetById]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT v.*, u.[DisplayName] AS AuthorName
    FROM   [dbo].[ContentVersion] v
    JOIN   [dbo].[User] u ON u.[Id] = v.[AuthorId]
    WHERE  v.[Id] = @Id;
END;
GO

-- ============================================================
-- §4.3 Media
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_Create]
    @FileName        NVARCHAR(500),
    @StoragePath     NVARCHAR(2000),
    @StorageBackend  NVARCHAR(50),
    @MimeType        NVARCHAR(200),
    @FileSizeBytes   BIGINT,
    @Width           INT    = NULL,
    @Height          INT    = NULL,
    @UploadedById    BIGINT,
    @NewId           BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[MediaAsset]
        ([FileName], [StoragePath], [StorageBackend], [MimeType], [FileSizeBytes],
         [Width], [Height], [UploadedById], [IsVirusScanPassed], [CreatedAt], [UpdatedAt])
    VALUES
        (@FileName, @StoragePath, @StorageBackend, @MimeType, @FileSizeBytes,
         @Width, @Height, @UploadedById, NULL, SYSUTCDATETIME(), SYSUTCDATETIME());
    SET @NewId = SCOPE_IDENTITY();
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_UpdateMetadata]
    @Id          BIGINT,
    @AltText     NVARCHAR(500)  = NULL,
    @Title       NVARCHAR(500)  = NULL,
    @Description NVARCHAR(2000) = NULL,
    @Tags        NVARCHAR(MAX)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [dbo].[MediaAsset]
    SET    [AltText]     = COALESCE(@AltText, [AltText]),
           [Title]       = COALESCE(@Title, [Title]),
           [Description] = COALESCE(@Description, [Description]),
           [Tags]        = COALESCE(@Tags, [Tags]),
           [UpdatedAt]   = SYSUTCDATETIME()
    WHERE  [Id] = @Id;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_SetVirusScanResult]
    @Id     BIGINT,
    @Passed BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [dbo].[MediaAsset]
    SET    [IsVirusScanPassed] = @Passed,
           [UpdatedAt]         = SYSUTCDATETIME()
    WHERE  [Id] = @Id;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_List]
    @MimeTypePrefix NVARCHAR(50)  = NULL,
    @SearchTerm     NVARCHAR(200) = NULL,
    @Page           INT           = 1,
    @PageSize       INT           = 50,
    @TotalRows      INT           OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @TotalRows = COUNT(*)
    FROM   [dbo].[MediaAsset]
    WHERE  ([IsVirusScanPassed] IS NULL OR [IsVirusScanPassed] = 1)
      AND  (@MimeTypePrefix IS NULL OR [MimeType] LIKE @MimeTypePrefix + '%')
      AND  (@SearchTerm     IS NULL OR [FileName] LIKE '%' + @SearchTerm + '%'
                                    OR [AltText]  LIKE '%' + @SearchTerm + '%'
                                    OR [Title]    LIKE '%' + @SearchTerm + '%');

    SELECT *
    FROM   [dbo].[MediaAsset]
    WHERE  ([IsVirusScanPassed] IS NULL OR [IsVirusScanPassed] = 1)
      AND  (@MimeTypePrefix IS NULL OR [MimeType] LIKE @MimeTypePrefix + '%')
      AND  (@SearchTerm     IS NULL OR [FileName] LIKE '%' + @SearchTerm + '%'
                                    OR [AltText]  LIKE '%' + @SearchTerm + '%'
                                    OR [Title]    LIKE '%' + @SearchTerm + '%')
    ORDER  BY [CreatedAt] DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH  NEXT @PageSize ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_GetUsage]
    @MediaAssetId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT mu.[ContentEntryId], mu.[FieldName], e.[Slug], e.[Status], e.[ContentTypeId]
    FROM   [dbo].[MediaUsage] mu
    JOIN   [dbo].[ContentEntry] e ON e.[Id] = mu.[ContentEntryId]
    WHERE  mu.[MediaAssetId] = @MediaAssetId;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_SafeDelete]
    @Id     BIGINT,
    @Result INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM [dbo].[MediaUsage] WHERE [MediaAssetId] = @Id)
    BEGIN
        SET @Result = 1; -- blocked
        RETURN;
    END;
    DELETE FROM [dbo].[MediaAsset] WHERE [Id] = @Id;
    SET @Result = 0; -- deleted
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_MediaUsage_Upsert]
    @MediaAssetId   BIGINT,
    @ContentEntryId BIGINT,
    @FieldName      NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (
        SELECT 1 FROM [dbo].[MediaUsage]
        WHERE [MediaAssetId]   = @MediaAssetId
          AND [ContentEntryId] = @ContentEntryId
          AND [FieldName]      = @FieldName
    )
    INSERT INTO [dbo].[MediaUsage] ([MediaAssetId], [ContentEntryId], [FieldName], [CreatedAt])
    VALUES (@MediaAssetId, @ContentEntryId, @FieldName, SYSUTCDATETIME());
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_MediaUsage_DeleteForEntry]
    @ContentEntryId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [dbo].[MediaUsage] WHERE [ContentEntryId] = @ContentEntryId;
END;
GO

-- ============================================================
-- §4.4 Users & Auth
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[usp_User_Upsert]
    @ExternalId  NVARCHAR(200),
    @Email       NVARCHAR(500),
    @DisplayName NVARCHAR(500),
    @UserId      BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [dbo].[User]
    SET    [Email]       = @Email,
           [DisplayName] = @DisplayName,
           [LastLoginAt] = SYSUTCDATETIME(),
           [UpdatedAt]   = SYSUTCDATETIME()
    WHERE  [ExternalId]  = @ExternalId;

    IF @@ROWCOUNT = 0
    BEGIN
        INSERT INTO [dbo].[User] ([ExternalId], [Email], [DisplayName], [IsActive], [LastLoginAt], [CreatedAt], [UpdatedAt])
        VALUES (@ExternalId, @Email, @DisplayName, 1, SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME());
        SET @UserId = SCOPE_IDENTITY();
    END
    ELSE
        SELECT @UserId = [Id] FROM [dbo].[User] WHERE [ExternalId] = @ExternalId;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_User_GetById]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.*,
           (SELECT r.[Name] FROM [dbo].[Role] r JOIN [dbo].[UserRole] ur ON ur.[RoleId] = r.[Id]
            WHERE ur.[UserId] = u.[Id] AND ur.[SectionId] IS NULL
            FOR JSON PATH) AS GlobalRolesJson
    FROM   [dbo].[User] u
    WHERE  u.[Id] = @Id;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_User_GetRoles]
    @UserId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ur.[RoleId], r.[Name] AS RoleName, ur.[SectionId], s.[SlugPrefix] AS SectionSlugPrefix
    FROM   [dbo].[UserRole] ur
    JOIN   [dbo].[Role] r              ON r.[Id]  = ur.[RoleId]
    LEFT JOIN [dbo].[ContentSection] s ON s.[Id]  = ur.[SectionId]
    WHERE  ur.[UserId] = @UserId;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_User_AssignRole]
    @UserId      BIGINT,
    @RoleId      BIGINT,
    @SectionId   BIGINT = NULL,
    @GrantedById BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (
        SELECT 1 FROM [dbo].[UserRole]
        WHERE [UserId] = @UserId AND [RoleId] = @RoleId
          AND ([SectionId] = @SectionId OR ([SectionId] IS NULL AND @SectionId IS NULL))
    )
    INSERT INTO [dbo].[UserRole] ([UserId], [RoleId], [SectionId], [GrantedById], [CreatedAt])
    VALUES (@UserId, @RoleId, @SectionId, @GrantedById, SYSUTCDATETIME());
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_User_RevokeRole]
    @UserId    BIGINT,
    @RoleId    BIGINT,
    @SectionId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [dbo].[UserRole]
    WHERE [UserId]    = @UserId
      AND [RoleId]    = @RoleId
      AND ([SectionId] = @SectionId OR ([SectionId] IS NULL AND @SectionId IS NULL));
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_User_Deactivate]
    @Id      BIGINT,
    @ActorId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [dbo].[User] SET [IsActive] = 0, [UpdatedAt] = SYSUTCDATETIME() WHERE [Id] = @Id;
    EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'User', @Id, 'Deactivate', NULL;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_User_List]
    @SearchTerm NVARCHAR(200) = NULL,
    @IsActive   BIT           = 1,
    @Page       INT           = 1,
    @PageSize   INT           = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
    FROM   [dbo].[User]
    WHERE  [IsActive] = @IsActive
      AND  (@SearchTerm IS NULL
            OR [DisplayName] LIKE '%' + @SearchTerm + '%'
            OR [Email]       LIKE '%' + @SearchTerm + '%')
    ORDER  BY [DisplayName]
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH  NEXT @PageSize ROWS ONLY;
END;
GO

-- ============================================================
-- §4.5 Workflow
-- ============================================================

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

-- ============================================================
-- §4.6 Navigation
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_GetMenuTree]
    @Handle NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    WITH MenuTree AS (
        SELECT ni.[Id], ni.[ParentItemId], ni.[Label], ni.[Url], ni.[Target],
               ni.[ContentEntryId], ni.[SortOrder], ni.[IsVisible], 0 AS Depth
        FROM   [dbo].[NavigationItem] ni
        JOIN   [dbo].[NavigationMenu] nm ON nm.[Id] = ni.[MenuId]
        WHERE  nm.[Handle] = @Handle
          AND  ni.[ParentItemId] IS NULL
          AND  ni.[IsVisible] = 1

        UNION ALL

        SELECT ni.[Id], ni.[ParentItemId], ni.[Label], ni.[Url], ni.[Target],
               ni.[ContentEntryId], ni.[SortOrder], ni.[IsVisible], mt.Depth + 1
        FROM   [dbo].[NavigationItem] ni
        JOIN   MenuTree mt ON mt.[Id] = ni.[ParentItemId]
        WHERE  ni.[IsVisible] = 1 AND mt.Depth < 3
    )
    SELECT * FROM MenuTree
    ORDER  BY ParentItemId, SortOrder;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_UpsertItem]
    @Id             BIGINT        = NULL,
    @MenuId         BIGINT,
    @ParentItemId   BIGINT        = NULL,
    @Label          NVARCHAR(500),
    @Url            NVARCHAR(2000) = NULL,
    @ContentEntryId BIGINT        = NULL,
    @Target         NVARCHAR(10)  = '_self',
    @SortOrder      INT           = 0,
    @IsVisible      BIT           = 1,
    @NewId          BIGINT        OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Id IS NOT NULL
    BEGIN
        UPDATE [dbo].[NavigationItem]
        SET [ParentItemId] = @ParentItemId, [Label] = @Label, [Url] = @Url,
            [ContentEntryId] = @ContentEntryId, [Target] = @Target,
            [SortOrder] = @SortOrder, [IsVisible] = @IsVisible
        WHERE [Id] = @Id;
        SET @NewId = @Id;
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[NavigationItem]
            ([MenuId], [ParentItemId], [Label], [Url], [ContentEntryId], [Target], [SortOrder], [IsVisible])
        VALUES
            (@MenuId, @ParentItemId, @Label, @Url, @ContentEntryId, @Target, @SortOrder, @IsVisible);
        SET @NewId = SCOPE_IDENTITY();
    END;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Redirect_GetByPath]
    @FromPath NVARCHAR(2000)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 [ToPath], [StatusCode]
    FROM   [dbo].[Redirect]
    WHERE  [FromPath] = @FromPath
      AND  [IsActive] = 1;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Redirect_Create]
    @FromPath    NVARCHAR(2000),
    @ToPath      NVARCHAR(2000),
    @StatusCode  INT    = 301,
    @CreatedById BIGINT,
    @NewId       BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [dbo].[Redirect] SET [IsActive] = 0 WHERE [FromPath] = @FromPath;

    INSERT INTO [dbo].[Redirect] ([FromPath], [ToPath], [StatusCode], [IsActive], [CreatedById], [CreatedAt])
    VALUES (@FromPath, @ToPath, @StatusCode, 1, @CreatedById, SYSUTCDATETIME());
    SET @NewId = SCOPE_IDENTITY();
END;
GO

-- ============================================================
-- §4.7 Search
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[usp_Search_FullText]
    @Query         NVARCHAR(500),
    @ContentTypeId BIGINT       = NULL,
    @Page          INT          = 1,
    @PageSize      INT          = 25,
    @TotalRows     INT          OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    -- This SP requires SQL Server Full-Text Search (FTS) to be installed and V002 to have run.
    -- When FTS is not available, returns an empty result set with TotalRows = 0.
    SET @TotalRows = 0;

    IF CAST(ISNULL(SERVERPROPERTY('IsFullTextInstalled'), 0) AS BIT) = 0
    BEGIN
        PRINT 'usp_Search_FullText: Full-Text Search is not installed. Returning empty result.';
        RETURN;
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID('[dbo].[ContentVersion]')
          AND name = 'FieldsPlainText'
    )
    BEGIN
        PRINT 'usp_Search_FullText: FieldsPlainText column not found. Run V002 migration first.';
        RETURN;
    END;

    -- Execute the actual FTS query via dynamic SQL so the SP body compiles without FTS
    DECLARE @FtsQuery NVARCHAR(600) = '"' + REPLACE(@Query, '"', '') + '"';
    DECLARE @SQL      NVARCHAR(MAX);

    -- Count total
    SET @SQL = N'
        SELECT @TotalRows = COUNT(DISTINCT e.[Id])
        FROM   [dbo].[ContentEntry]  e
        JOIN   [dbo].[ContentVersion] v ON v.[Id] = e.[PublishedVersionId]
        WHERE  e.[Status] = ''Published''
          AND  CONTAINS(v.[FieldsPlainText], @FtsQuery)
          AND  (@ContentTypeId IS NULL OR e.[ContentTypeId] = @ContentTypeId);';
    EXEC sp_executesql @SQL,
        N'@FtsQuery NVARCHAR(600), @ContentTypeId BIGINT, @TotalRows INT OUTPUT',
        @FtsQuery, @ContentTypeId, @TotalRows OUTPUT;

    -- Return page
    SET @SQL = N'
        SELECT e.[Id], e.[Slug], e.[ContentTypeId], e.[Locale], e.[UpdatedAt],
               LEFT(v.[FieldsPlainText], 300) AS Excerpt,
               kt.[RANK]
        FROM   [dbo].[ContentEntry] e
        JOIN   [dbo].[ContentVersion] v ON v.[Id] = e.[PublishedVersionId]
        JOIN   CONTAINSTABLE([dbo].[ContentVersion], [FieldsPlainText], @FtsQuery) kt
                   ON kt.[KEY] = v.[Id]
        WHERE  e.[Status] = ''Published''
          AND  (@ContentTypeId IS NULL OR e.[ContentTypeId] = @ContentTypeId)
        ORDER  BY kt.[RANK] DESC
        OFFSET (@Page - 1) * @PageSize ROWS
        FETCH  NEXT @PageSize ROWS ONLY;';
    EXEC sp_executesql @SQL,
        N'@FtsQuery NVARCHAR(600), @ContentTypeId BIGINT, @Page INT, @PageSize INT',
        @FtsQuery, @ContentTypeId, @Page, @PageSize;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Search_LogQuery]
    @Query       NVARCHAR(500),
    @ResultCount INT,
    @UserId      BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[SearchQueryLog] ([Query], [ResultCount], [UserId], [CreatedAt])
    VALUES (@Query, @ResultCount, @UserId, SYSUTCDATETIME());
END;
GO

-- ============================================================
-- §4.8 Taxonomy
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[usp_Taxonomy_GetTermTree]
    @TaxonomyHandle NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    WITH TermTree AS (
        SELECT t.[Id], t.[ParentTermId], t.[Name], t.[Slug], t.[SortOrder], 0 AS Depth
        FROM   [dbo].[TaxonomyTerm] t
        JOIN   [dbo].[Taxonomy] tx ON tx.[Id] = t.[TaxonomyId]
        WHERE  tx.[Handle] = @TaxonomyHandle AND t.[ParentTermId] IS NULL

        UNION ALL

        SELECT t.[Id], t.[ParentTermId], t.[Name], t.[Slug], t.[SortOrder], tt.Depth + 1
        FROM   [dbo].[TaxonomyTerm] t
        JOIN   TermTree tt ON tt.[Id] = t.[ParentTermId]
        WHERE  tt.Depth < 5
    )
    SELECT * FROM TermTree ORDER BY ParentTermId, SortOrder;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Taxonomy_GetEntriesForTerm]
    @TermId   BIGINT,
    @Page     INT = 1,
    @PageSize INT = 25
AS
BEGIN
    SET NOCOUNT ON;
    SELECT e.[Id], e.[Slug], e.[ContentTypeId], e.[Locale], e.[UpdatedAt]
    FROM   [dbo].[ContentEntryTerm] cet
    JOIN   [dbo].[ContentEntry] e ON e.[Id] = cet.[ContentEntryId]
    WHERE  cet.[TaxonomyTermId] = @TermId
      AND  e.[Status] = 'Published'
    ORDER  BY e.[UpdatedAt] DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH  NEXT @PageSize ROWS ONLY;
END;
GO

-- ============================================================
-- §4.10 Webhooks
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[usp_Webhook_GetActiveForEvent]
    @EventName NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], [Url], [Secret]
    FROM   [dbo].[Webhook]
    WHERE  [IsActive]   = 1
      AND  [EventsJson] LIKE '%"' + @EventName + '"%';
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_WebhookDelivery_Create]
    @WebhookId          BIGINT,
    @EventName          NVARCHAR(100),
    @PayloadJson        NVARCHAR(MAX),
    @ResponseStatusCode INT            = NULL,
    @AttemptNumber      INT            = 1,
    @ErrorMessage       NVARCHAR(2000) = NULL,
    @NewId              BIGINT         OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[WebhookDelivery]
        ([WebhookId], [EventName], [PayloadJson], [ResponseStatusCode],
         [AttemptNumber], [DeliveredAt], [ErrorMessage])
    VALUES
        (@WebhookId, @EventName, @PayloadJson, @ResponseStatusCode,
         @AttemptNumber, SYSUTCDATETIME(), @ErrorMessage);
    SET @NewId = SCOPE_IDENTITY();
END;
GO
