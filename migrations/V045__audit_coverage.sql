-- V045__audit_coverage.sql
-- Issue #165 (epic #152) — audit log coverage, part 2: every mutating stored
-- procedure writes its own AuditLog row inside the same transaction (NIST AU-2),
-- the pattern usp_Workflow_Transition and usp_ContentEntry_Archive already used.
--
-- Actor resolution: SPs that already receive the acting user (OwnerId, AuthorId,
-- GrantedById, UploadedById, CreatedById) pass it explicitly. The rest take a new
-- trailing @ActorId parameter that defaults to NULL, in which case
-- usp_AuditLog_Write reads the actor (and source IP / user agent / correlation
-- id) from SESSION_CONTEXT, set by the API for every authenticated request.
-- Existing positional EXEC calls therefore keep working unchanged.
--
-- Role changes also end the target user's open sessions (#163): explicit
-- assignments feed the JWT role claims, so a change must not wait for the
-- access token to expire.
--
-- Action strings written here are listed in docs/DATABASE_LAYER.md §4.9 and
-- asserted by Issue165AcceptanceTests.

-- ============================================================
-- §4.4 Users
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_User_Upsert]
    @ExternalId  NVARCHAR(200),
    @Email       NVARCHAR(500),
    @DisplayName NVARCHAR(500),
    @UserId      BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

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

        -- First-time provisioning: the new user is the actor of their own creation.
        DECLARE @Diff NVARCHAR(MAX) = (SELECT @ExternalId AS externalId, @Email AS email FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        EXEC [dbo].[usp_AuditLog_Write] @UserId, 'User', @UserId, 'Provision', @Diff;
    END
    ELSE
        SELECT @UserId = [Id] FROM [dbo].[User] WHERE [ExternalId] = @ExternalId;

    COMMIT TRANSACTION;
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
    SET XACT_ABORT ON;

    IF EXISTS (
        SELECT 1 FROM [dbo].[UserRole]
        WHERE [UserId] = @UserId AND [RoleId] = @RoleId
          AND ([SectionId] = @SectionId OR ([SectionId] IS NULL AND @SectionId IS NULL))
    )
        RETURN;   -- idempotent: nothing changed, nothing to audit

    BEGIN TRANSACTION;
    INSERT INTO [dbo].[UserRole] ([UserId], [RoleId], [SectionId], [GrantedById], [CreatedAt])
    VALUES (@UserId, @RoleId, @SectionId, @GrantedById, SYSUTCDATETIME());

    DECLARE @RoleName NVARCHAR(100);
    SELECT @RoleName = [Name] FROM [dbo].[Role] WHERE [Id] = @RoleId;
    DECLARE @Diff NVARCHAR(MAX) = (SELECT @RoleId AS roleId, @RoleName AS role, @SectionId AS sectionId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    EXEC [dbo].[usp_AuditLog_Write] @GrantedById, 'User', @UserId, 'AssignRole', @Diff;

    EXEC [dbo].[usp_RefreshToken_RevokeAllForUser] @UserId = @UserId, @ActorId = @GrantedById, @Reason = 'RoleChange';
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_User_RevokeRole]
    @UserId    BIGINT,
    @RoleId    BIGINT,
    @SectionId BIGINT = NULL,
    @ActorId   BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DELETE FROM [dbo].[UserRole]
    WHERE [UserId]    = @UserId
      AND [RoleId]    = @RoleId
      AND ([SectionId] = @SectionId OR ([SectionId] IS NULL AND @SectionId IS NULL));

    IF @@ROWCOUNT > 0
    BEGIN
        DECLARE @RoleName NVARCHAR(100);
        SELECT @RoleName = [Name] FROM [dbo].[Role] WHERE [Id] = @RoleId;
        DECLARE @Diff NVARCHAR(MAX) = (SELECT @RoleId AS roleId, @RoleName AS role, @SectionId AS sectionId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
        EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'User', @UserId, 'RevokeRole', @Diff;
        EXEC [dbo].[usp_RefreshToken_RevokeAllForUser] @UserId = @UserId, @ActorId = @ActorId, @Reason = 'RoleChange';
    END;

    COMMIT TRANSACTION;
END;
GO

-- ============================================================
-- §4.1 / §4.2 Content
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_Create]
    @ContentTypeId BIGINT,
    @Slug          NVARCHAR(500),
    @Locale        NVARCHAR(10),
    @OwnerId       BIGINT,
    @NewId         BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    INSERT INTO [dbo].[ContentEntry]
        ([ContentTypeId], [Slug], [Locale], [Status], [OwnerId], [CreatedAt], [UpdatedAt])
    VALUES
        (@ContentTypeId, @Slug, @Locale, 'Draft', @OwnerId, SYSUTCDATETIME(), SYSUTCDATETIME());
    SET @NewId = SCOPE_IDENTITY();

    DECLARE @Diff NVARCHAR(MAX) = (SELECT @ContentTypeId AS contentTypeId, @Slug AS slug, @Locale AS locale FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXEC [dbo].[usp_AuditLog_Write] @OwnerId, 'ContentEntry', @NewId, 'Create', @Diff;

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_UpdateStatus]
    @Id                 BIGINT,
    @Status             NVARCHAR(20),
    @PublishedVersionId BIGINT = NULL,
    @ActorId            BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @From NVARCHAR(20);
    SELECT @From = [Status] FROM [dbo].[ContentEntry] WHERE [Id] = @Id;

    UPDATE [dbo].[ContentEntry]
    SET    [Status]             = @Status,
           [PublishedVersionId] = COALESCE(@PublishedVersionId, [PublishedVersionId]),
           [UpdatedAt]          = SYSUTCDATETIME()
    WHERE  [Id] = @Id;

    IF @@ROWCOUNT > 0
    BEGIN
        DECLARE @Diff NVARCHAR(MAX) = (SELECT @From AS fromStatus, @Status AS toStatus, @PublishedVersionId AS publishedVersionId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
        EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'ContentEntry', @Id, 'UpdateStatus', @Diff;
    END;

    COMMIT TRANSACTION;
END;
GO

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
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

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

    -- The field payload itself is on the ContentVersion row; the audit row records
    -- that a new version was written, by whom, and the note they attached.
    DECLARE @Diff NVARCHAR(MAX) = (SELECT @ContentEntryId AS contentEntryId, @NextVersion AS versionNumber, @Status AS status, @ChangeNote AS changeNote FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    EXEC [dbo].[usp_AuditLog_Write] @AuthorId, 'ContentVersion', @NewId, 'Create', @Diff;

    COMMIT TRANSACTION;
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
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    INSERT INTO [dbo].[MediaAsset]
        ([FileName], [StoragePath], [StorageBackend], [MimeType], [FileSizeBytes],
         [Width], [Height], [UploadedById], [IsVirusScanPassed], [CreatedAt], [UpdatedAt])
    VALUES
        (@FileName, @StoragePath, @StorageBackend, @MimeType, @FileSizeBytes,
         @Width, @Height, @UploadedById, NULL, SYSUTCDATETIME(), SYSUTCDATETIME());
    SET @NewId = SCOPE_IDENTITY();

    DECLARE @Diff NVARCHAR(MAX) = (SELECT @FileName AS fileName, @MimeType AS mimeType, @FileSizeBytes AS fileSizeBytes FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXEC [dbo].[usp_AuditLog_Write] @UploadedById, 'MediaAsset', @NewId, 'Create', @Diff;

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_UpdateMetadata]
    @Id          BIGINT,
    @AltText     NVARCHAR(500)  = NULL,
    @Title       NVARCHAR(500)  = NULL,
    @Description NVARCHAR(2000) = NULL,
    @Tags        NVARCHAR(MAX)  = NULL,
    @ActorId     BIGINT         = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    UPDATE [dbo].[MediaAsset]
    SET    [AltText]     = COALESCE(@AltText, [AltText]),
           [Title]       = COALESCE(@Title, [Title]),
           [Description] = COALESCE(@Description, [Description]),
           [Tags]        = COALESCE(@Tags, [Tags]),
           [UpdatedAt]   = SYSUTCDATETIME()
    WHERE  [Id] = @Id;

    IF @@ROWCOUNT > 0
    BEGIN
        -- Which fields were supplied (values live on the row; alt text is short enough to keep)
        DECLARE @Diff NVARCHAR(MAX) = (SELECT @AltText AS altText, @Title AS title,
                                              CASE WHEN @Description IS NULL THEN 0 ELSE 1 END AS descriptionChanged,
                                              CASE WHEN @Tags IS NULL THEN 0 ELSE 1 END AS tagsChanged
                                       FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
        EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'MediaAsset', @Id, 'UpdateMetadata', @Diff;
    END;

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_SafeDelete]
    @Id      BIGINT,
    @Result  INT OUTPUT,
    @ActorId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM [dbo].[MediaUsage] WHERE [MediaAssetId] = @Id)
    BEGIN
        SET @Result = 1; -- blocked
        RETURN;
    END;

    BEGIN TRANSACTION;
    DECLARE @FileName NVARCHAR(500);
    SELECT @FileName = [FileName] FROM [dbo].[MediaAsset] WHERE [Id] = @Id;

    DELETE FROM [dbo].[MediaAsset] WHERE [Id] = @Id;
    DECLARE @Rows INT = @@ROWCOUNT;
    SET @Result = 0; -- deleted

    IF @Rows > 0
    BEGIN
        DECLARE @Diff NVARCHAR(MAX) = (SELECT @FileName AS fileName FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'MediaAsset', @Id, 'Delete', @Diff;
    END;
    COMMIT TRANSACTION;
END;
GO

-- ============================================================
-- §4.6 Navigation
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_CreateMenu]
    @Name    NVARCHAR(200),
    @Handle  NVARCHAR(100),
    @NewId   BIGINT OUTPUT,
    @ActorId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    INSERT INTO [NavigationMenu] ([Name], [Handle], [CreatedAt], [UpdatedAt])
    VALUES (@Name, @Handle, SYSUTCDATETIME(), SYSUTCDATETIME());
    SET @NewId = SCOPE_IDENTITY();

    DECLARE @Diff NVARCHAR(MAX) = (SELECT @Name AS name, @Handle AS handle FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'NavigationMenu', @NewId, 'Create', @Diff;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_UpdateMenu]
    @Id      BIGINT,
    @Name    NVARCHAR(200),
    @ActorId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    UPDATE [NavigationMenu]
    SET [Name] = @Name, [UpdatedAt] = SYSUTCDATETIME()
    WHERE [Id] = @Id;

    IF @@ROWCOUNT > 0
    BEGIN
        DECLARE @Diff NVARCHAR(MAX) = (SELECT @Name AS name FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'NavigationMenu', @Id, 'Update', @Diff;
    END;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_DeleteMenu]
    @Id      BIGINT,
    @ActorId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DECLARE @Handle NVARCHAR(100);
    SELECT @Handle = [Handle] FROM [NavigationMenu] WHERE [Id] = @Id;

    -- Cascade: delete all items in this menu first
    DELETE FROM [NavigationItem] WHERE [MenuId] = @Id;
    DECLARE @Items INT = @@ROWCOUNT;
    DELETE FROM [NavigationMenu] WHERE [Id] = @Id;

    IF @@ROWCOUNT > 0
    BEGIN
        DECLARE @Diff NVARCHAR(MAX) = (SELECT @Handle AS handle, @Items AS itemsDeleted FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'NavigationMenu', @Id, 'Delete', @Diff;
    END;
    COMMIT TRANSACTION;
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
    @NewId          BIGINT        OUTPUT,
    @ActorId        BIGINT        = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @Action NVARCHAR(50);
    IF @Id IS NOT NULL
    BEGIN
        UPDATE [dbo].[NavigationItem]
        SET [ParentItemId] = @ParentItemId, [Label] = @Label, [Url] = @Url,
            [ContentEntryId] = @ContentEntryId, [Target] = @Target,
            [SortOrder] = @SortOrder, [IsVisible] = @IsVisible
        WHERE [Id] = @Id;
        SET @NewId = @Id;
        SET @Action = 'Update';
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[NavigationItem]
            ([MenuId], [ParentItemId], [Label], [Url], [ContentEntryId], [Target], [SortOrder], [IsVisible])
        VALUES
            (@MenuId, @ParentItemId, @Label, @Url, @ContentEntryId, @Target, @SortOrder, @IsVisible);
        SET @NewId = SCOPE_IDENTITY();
        SET @Action = 'Create';
    END;

    DECLARE @Diff NVARCHAR(MAX) = (SELECT @MenuId AS menuId, @ParentItemId AS parentItemId, @Label AS label, @Url AS url,
                                          @ContentEntryId AS contentEntryId, @Target AS target, @SortOrder AS sortOrder, @IsVisible AS isVisible
                                   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES);
    EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'NavigationItem', @NewId, @Action, @Diff;

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_DeleteItem]
    @Id      BIGINT,
    @ActorId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @MenuId BIGINT, @Label NVARCHAR(500);
    SELECT @MenuId = [MenuId], @Label = [Label] FROM [NavigationItem] WHERE [Id] = @Id;

    -- Recursively delete children first (simple iterative approach for up to 3 levels)
    DELETE FROM [NavigationItem]
    WHERE [ParentItemId] IN (
        SELECT ni2.Id FROM [NavigationItem] ni2
        WHERE ni2.[ParentItemId] IN (
            SELECT ni3.Id FROM [NavigationItem] ni3
            WHERE ni3.[ParentItemId] = @Id
        )
    );
    DELETE FROM [NavigationItem]
    WHERE [ParentItemId] IN (
        SELECT ni2.Id FROM [NavigationItem] ni2
        WHERE ni2.[ParentItemId] = @Id
    );
    DELETE FROM [NavigationItem] WHERE [ParentItemId] = @Id;
    DELETE FROM [NavigationItem] WHERE [Id] = @Id;

    IF @@ROWCOUNT > 0
    BEGIN
        DECLARE @Diff NVARCHAR(MAX) = (SELECT @MenuId AS menuId, @Label AS label FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'NavigationItem', @Id, 'Delete', @Diff;
    END;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_BulkReorder]
    @MenuId      BIGINT,
    @ItemsJson   NVARCHAR(MAX),   -- JSON: [{id, parentItemId, sortOrder}]
    @ActorId     BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    CREATE TABLE #ReorderItems (
        Id           BIGINT,
        ParentItemId BIGINT NULL,
        SortOrder    INT
    );

    INSERT INTO #ReorderItems (Id, ParentItemId, SortOrder)
    SELECT
        CAST(j.[Id]           AS BIGINT),
        CAST(j.[ParentItemId] AS BIGINT),
        CAST(j.[SortOrder]    AS INT)
    FROM OPENJSON(@ItemsJson)
    WITH (
        Id           BIGINT       '$.id',
        ParentItemId BIGINT       '$.parentItemId',
        SortOrder    INT          '$.sortOrder'
    ) j;

    BEGIN TRANSACTION;

    UPDATE ni
    SET ni.[ParentItemId] = r.ParentItemId,
        ni.[SortOrder]    = r.SortOrder
    FROM [NavigationItem] ni
    JOIN #ReorderItems r ON r.Id = ni.Id
    WHERE ni.[MenuId] = @MenuId;
    DECLARE @Updated INT = @@ROWCOUNT;

    UPDATE [NavigationMenu]
    SET [UpdatedAt] = SYSUTCDATETIME()
    WHERE [Id] = @MenuId;

    -- The reorder payload is the diff: it is the complete new arrangement.
    DECLARE @Diff NVARCHAR(MAX) = (SELECT @Updated AS itemsUpdated, JSON_QUERY(@ItemsJson) AS items FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'NavigationMenu', @MenuId, 'Reorder', @Diff;

    COMMIT TRANSACTION;
    DROP TABLE #ReorderItems;
END;
GO

-- ============================================================
-- §4.6 Redirects
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_Redirect_Create]
    @FromPath    NVARCHAR(2000),
    @ToPath      NVARCHAR(2000),
    @StatusCode  INT    = 301,
    @CreatedById BIGINT,
    @NewId       BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    UPDATE [dbo].[Redirect] SET [IsActive] = 0 WHERE [FromPath] = @FromPath;

    INSERT INTO [dbo].[Redirect] ([FromPath], [ToPath], [StatusCode], [IsActive], [CreatedById], [CreatedAt])
    VALUES (@FromPath, @ToPath, @StatusCode, 1, @CreatedById, SYSUTCDATETIME());
    SET @NewId = SCOPE_IDENTITY();

    DECLARE @Diff NVARCHAR(MAX) = (SELECT @FromPath AS fromPath, @ToPath AS toPath, @StatusCode AS statusCode FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXEC [dbo].[usp_AuditLog_Write] @CreatedById, 'Redirect', @NewId, 'Create', @Diff;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Redirect_Update]
    @Id         BIGINT,
    @FromPath   NVARCHAR(2000),
    @ToPath     NVARCHAR(2000),
    @StatusCode INT    = 301,
    @ActorId    BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    UPDATE [Redirect]
    SET    [IsActive] = 0
    WHERE  [FromPath] = @FromPath
      AND  [IsActive] = 1
      AND  [Id] <> @Id;

    UPDATE [Redirect]
    SET    [FromPath]   = @FromPath,
           [ToPath]     = @ToPath,
           [StatusCode] = @StatusCode
    WHERE  [Id] = @Id;

    IF @@ROWCOUNT > 0
    BEGIN
        DECLARE @Diff NVARCHAR(MAX) = (SELECT @FromPath AS fromPath, @ToPath AS toPath, @StatusCode AS statusCode FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'Redirect', @Id, 'Update', @Diff;
    END;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Redirect_Deactivate]
    @Id      BIGINT,
    @ActorId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    UPDATE [Redirect]
    SET    [IsActive] = 0
    WHERE  [Id] = @Id AND [IsActive] = 1;

    IF @@ROWCOUNT > 0
        EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'Redirect', @Id, 'Deactivate', NULL;
    COMMIT TRANSACTION;
END;
GO

-- ============================================================
-- §4.10 Webhooks
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_Webhook_Create]
    @Name        NVARCHAR(200),
    @Url         NVARCHAR(2000),
    @Secret      NVARCHAR(500),
    @EventsJson  NVARCHAR(MAX),
    @CreatedById BIGINT,
    @NewId       BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    INSERT INTO [Webhook] ([Name], [Url], [Secret], [EventsJson], [IsActive], [CreatedById], [CreatedAt])
    VALUES (@Name, @Url, @Secret, @EventsJson, 1, @CreatedById, SYSUTCDATETIME());
    SET @NewId = SCOPE_IDENTITY();

    -- Never the secret.
    DECLARE @Diff NVARCHAR(MAX) = (SELECT @Name AS name, @Url AS url, JSON_QUERY(@EventsJson) AS events FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXEC [dbo].[usp_AuditLog_Write] @CreatedById, 'Webhook', @NewId, 'Create', @Diff;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Webhook_Delete]
    @Id      BIGINT,
    @ActorId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    -- Soft-delete: mark inactive rather than physical delete to preserve delivery history
    UPDATE [Webhook]
    SET    [IsActive] = 0
    WHERE  [Id] = @Id AND [IsActive] = 1;

    IF @@ROWCOUNT > 0
        EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'Webhook', @Id, 'Delete', NULL;
    COMMIT TRANSACTION;
END;
GO

-- ============================================================
-- §4.7 Search pins
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_SearchPin_Create]
    @QueryString    NVARCHAR(500),
    @ContentEntryId BIGINT,
    @CreatedById    BIGINT = NULL,
    @NewId          BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @Action NVARCHAR(50);
    IF EXISTS (SELECT 1 FROM [dbo].[SearchPin] WHERE [QueryString] = @QueryString)
    BEGIN
        UPDATE [dbo].[SearchPin]
        SET    [ContentEntryId] = @ContentEntryId,
               [CreatedById]    = @CreatedById,
               [CreatedAt]      = SYSUTCDATETIME()
        WHERE  [QueryString] = @QueryString;

        SELECT @NewId = [Id] FROM [dbo].[SearchPin] WHERE [QueryString] = @QueryString;
        SET @Action = 'Update';
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[SearchPin] ([QueryString], [ContentEntryId], [CreatedById], [CreatedAt])
        VALUES (@QueryString, @ContentEntryId, @CreatedById, SYSUTCDATETIME());

        SET @NewId = SCOPE_IDENTITY();
        SET @Action = 'Create';
    END;

    DECLARE @Diff NVARCHAR(MAX) = (SELECT @QueryString AS queryString, @ContentEntryId AS contentEntryId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXEC [dbo].[usp_AuditLog_Write] @CreatedById, 'SearchPin', @NewId, @Action, @Diff;

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_SearchPin_Delete]
    @Id      BIGINT,
    @ActorId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DECLARE @QueryString NVARCHAR(500);
    SELECT @QueryString = [QueryString] FROM [dbo].[SearchPin] WHERE [Id] = @Id;

    DELETE FROM [dbo].[SearchPin] WHERE [Id] = @Id;

    IF @@ROWCOUNT > 0
    BEGIN
        DECLARE @Diff NVARCHAR(MAX) = (SELECT @QueryString AS queryString FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'SearchPin', @Id, 'Delete', @Diff;
    END;
    COMMIT TRANSACTION;
END;
GO

-- ============================================================
-- §4.4 AD group → role mappings (audit moves from the controller into the SP)
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_AdGroupMapping_Upsert]
    @AdGroup     NVARCHAR(500),
    @RoleId      BIGINT,
    @CreatedById BIGINT,
    @NewId       BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    SELECT @NewId = Id
    FROM   [AdGroupRoleMapping]
    WHERE  [AdGroup] = @AdGroup AND [RoleId] = @RoleId;

    DECLARE @Action NVARCHAR(50);
    IF @NewId IS NOT NULL
    BEGIN
        -- Already exists — touch UpdatedAt so the audit trail shows admin awareness
        UPDATE [AdGroupRoleMapping]
        SET    [UpdatedAt] = SYSUTCDATETIME()
        WHERE  [Id] = @NewId;
        SET @Action = 'AdGroupMappingUpserted';
    END
    ELSE
    BEGIN
        INSERT INTO [AdGroupRoleMapping] ([AdGroup], [RoleId], [CreatedById], [CreatedAt], [UpdatedAt])
        VALUES (@AdGroup, @RoleId, @CreatedById, SYSUTCDATETIME(), SYSUTCDATETIME());
        SET @NewId = SCOPE_IDENTITY();
        SET @Action = 'AdGroupMappingUpserted';
    END;

    DECLARE @RoleName NVARCHAR(100);
    SELECT @RoleName = [Name] FROM [dbo].[Role] WHERE [Id] = @RoleId;
    DECLARE @Diff NVARCHAR(MAX) = (SELECT @AdGroup AS adGroup, @RoleId AS roleId, @RoleName AS role FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXEC [dbo].[usp_AuditLog_Write] @CreatedById, 'AdGroupRoleMapping', @NewId, @Action, @Diff;

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_AdGroupMapping_Delete]
    @Id      BIGINT,
    @ActorId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DECLARE @AdGroup NVARCHAR(500), @RoleId BIGINT;
    SELECT @AdGroup = [AdGroup], @RoleId = [RoleId] FROM [AdGroupRoleMapping] WHERE [Id] = @Id;

    DELETE FROM [AdGroupRoleMapping] WHERE [Id] = @Id;

    IF @@ROWCOUNT > 0
    BEGIN
        DECLARE @Diff NVARCHAR(MAX) = (SELECT @AdGroup AS adGroup, @RoleId AS roleId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'AdGroupRoleMapping', @Id, 'AdGroupMappingDeleted', @Diff;
    END;
    COMMIT TRANSACTION;
END;
GO
