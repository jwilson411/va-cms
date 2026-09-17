# Database Layer: Stored Procedures, Indexing & Hygiene
## VA CMS — USWDS-Compliant Content Management System

All database operations go through stored procedures. No ad-hoc SQL from the application layer reaches the database — PetaPoco calls `EXEC usp_*` exclusively. This enforces:
- A clean security boundary (app login has `EXECUTE` on SPs only, not `SELECT/INSERT/UPDATE/DELETE` on tables)
- A single place to tune and optimize queries
- Explicit, auditable contracts between app and DB

---

## 1. Naming Conventions

| Object | Convention | Example |
|---|---|---|
| Tables | `PascalCase`, singular | `ContentEntry` |
| Columns | `PascalCase` | `PublishedVersionId` |
| Stored procedures | `usp_{Entity}_{Action}` | `usp_ContentEntry_GetBySlug` |
| Indexes | `IX_{Table}_{Columns}` | `IX_ContentEntry_Status_UpdatedAt` |
| Unique indexes | `UX_{Table}_{Columns}` | `UX_ContentEntry_Slug_Locale` |
| Clustered index | `CX_{Table}_{Column}` | `CX_AuditLog_CreatedAt` |
| FTS catalog | `FTC_{Name}` | `FTC_CmsContent` |
| FTS index | on table, named implicitly | — |
| Maintenance jobs | `job_Maint_{Description}` | `job_Maint_RebuildIndexes` |

---

## 2. SQL Server Login & Permission Model

The application service account (`vacms_app`) has **only** `EXECUTE` permission on stored procedures. It cannot directly `SELECT`, `INSERT`, `UPDATE`, or `DELETE` any table.

Login creation and password material live outside the migration set (#157): `infra/sql/provision-logins.sql` creates
`vacms_app` and `vacms_readonly` with pipeline-supplied SQLCMD variables and `CHECK_POLICY = ON`, and is what
`vacms db provision-logins` runs. `migrations/V003__security_model.sql` only maps the logins to database users and
applies the grants (skipping a login that has not been provisioned yet):

```sql
-- migrations/V003__security_model.sql (users + grants only)
IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'vacms_app')
   AND NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'vacms_app')
    CREATE USER [vacms_app] FOR LOGIN [vacms_app];

GRANT EXECUTE ON SCHEMA::dbo TO [vacms_app];                    -- every SP, including future ones
DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [vacms_app];
DENY UPDATE, DELETE ON dbo.AuditLog TO [vacms_app];             -- belt and braces for audit rows

CREATE USER [vacms_readonly] FOR LOGIN [vacms_readonly];        -- same guard as above
GRANT SELECT ON SCHEMA::dbo TO [vacms_readonly];
DENY INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [vacms_readonly];
```

Because `vacms_app` cannot read `dbo.SchemaVersions`, `usp_Migrations_ListApplied` (V042) exposes the DbUp journal
so the API can verify at startup that no migration is pending. Migrations themselves are applied by
`vacms db migrate` under the deployment account; see DEPLOYMENT.md. `SecurityModelTests` prove the model against a
fresh container: direct `SELECT` on `[User]`, `AuditLog`, `SchemaVersions` and `SiteSetting` is denied, `EXECUTE`
works, and `vacms_readonly` can read but not write.

---

## 3. Indexing Strategy

### 3.1 Clustered Indexes

By default SQL Server clusters on the PK (`Id BIGINT IDENTITY`). The exception is `AuditLog` which is almost always queried by date range, so it clusters on `CreatedAt`.

```sql
-- AuditLog: clustered on CreatedAt for date-range query performance
CREATE CLUSTERED INDEX [CX_AuditLog_CreatedAt]
    ON [AuditLog] ([CreatedAt] DESC);
-- PK becomes a non-clustered unique index automatically
```

### 3.2 Non-Clustered Indexes

```sql
-- ContentEntry: most common filter axes
CREATE INDEX [IX_ContentEntry_Status_UpdatedAt]
    ON [ContentEntry] ([Status], [UpdatedAt] DESC)
    INCLUDE ([ContentTypeId], [OwnerId], [Slug], [Locale]);

CREATE INDEX [IX_ContentEntry_ContentTypeId_Status]
    ON [ContentEntry] ([ContentTypeId], [Status])
    INCLUDE ([Slug], [UpdatedAt]);

CREATE INDEX [IX_ContentEntry_OwnerId]
    ON [ContentEntry] ([OwnerId]);

CREATE INDEX [IX_ContentEntry_ScheduledPublishAt]
    ON [ContentEntry] ([ScheduledPublishAt])
    WHERE [ScheduledPublishAt] IS NOT NULL AND [Status] = 'Approved';
    -- Filtered index: only rows the background scheduler actually cares about

CREATE INDEX [IX_ContentEntry_ScheduledExpireAt]
    ON [ContentEntry] ([ScheduledExpireAt])
    WHERE [ScheduledExpireAt] IS NOT NULL AND [Status] = 'Published';

-- ContentVersion: version history lookups
CREATE INDEX [IX_ContentVersion_EntryId_VersionNumber]
    ON [ContentVersion] ([ContentEntryId], [VersionNumber] DESC)
    INCLUDE ([AuthorId], [Status], [CreatedAt]);

-- MediaAsset: library browser filters
CREATE INDEX [IX_MediaAsset_MimeType_CreatedAt]
    ON [MediaAsset] ([MimeType], [CreatedAt] DESC);

CREATE INDEX [IX_MediaAsset_UploadedById]
    ON [MediaAsset] ([UploadedById]);

-- MediaUsage: "which entries use this asset?" lookup
CREATE INDEX [IX_MediaUsage_AssetId]
    ON [MediaUsage] ([MediaAssetId])
    INCLUDE ([ContentEntryId]);

-- User: login by external ID (AAD Object ID / UPN)
CREATE INDEX [IX_User_ExternalId]
    ON [User] ([ExternalId]);

-- AuditLog: filter by actor and entity
CREATE INDEX [IX_AuditLog_ActorId_CreatedAt]
    ON [AuditLog] ([ActorId], [CreatedAt] DESC);

CREATE INDEX [IX_AuditLog_EntityType_EntityId]
    ON [AuditLog] ([EntityType], [EntityId])
    INCLUDE ([ActorId], [Action], [CreatedAt]);

-- WorkflowTransition: history for a content entry
CREATE INDEX [IX_WorkflowTransition_EntryId_CreatedAt]
    ON [WorkflowTransition] ([ContentEntryId], [CreatedAt] DESC);

-- NavigationItem: menu tree traversal
CREATE INDEX [IX_NavigationItem_MenuId_ParentItemId]
    ON [NavigationItem] ([MenuId], [ParentItemId])
    INCLUDE ([Label], [Url], [SortOrder], [IsVisible]);

-- TaxonomyTerm: hierarchy + taxonomy filter
CREATE INDEX [IX_TaxonomyTerm_TaxonomyId_ParentTermId]
    ON [TaxonomyTerm] ([TaxonomyId], [ParentTermId]);

-- ContentEntryTerm: "all entries for this term"
CREATE INDEX [IX_ContentEntryTerm_TermId]
    ON [ContentEntryTerm] ([TaxonomyTermId])
    INCLUDE ([ContentEntryId]);

-- Webhook: active webhooks by event (delivery fan-out)
CREATE INDEX [IX_Webhook_IsActive]
    ON [Webhook] ([IsActive])
    WHERE [IsActive] = 1;

-- WebhookDelivery: retry queue
CREATE INDEX [IX_WebhookDelivery_WebhookId_AttemptNumber]
    ON [WebhookDelivery] ([WebhookId], [AttemptNumber])
    INCLUDE ([EventName], [DeliveredAt], [ResponseStatusCode]);

-- Redirect: fast lookup on incoming path
CREATE INDEX [IX_Redirect_FromPath_IsActive]
    ON [Redirect] ([FromPath])
    WHERE [IsActive] = 1;
```

### 3.3 Full-Text Search

```sql
-- migrations/V002__add_fts_catalog.sql

-- Full-text catalog
CREATE FULLTEXT CATALOG [FTC_CmsContent] AS DEFAULT;

-- Index on ContentVersion.FieldsJson (plain text extracted via computed column)
ALTER TABLE [ContentVersion]
    ADD [FieldsPlainText] AS (
        -- Computed column strips JSON structure; FTS indexes this
        -- In practice, use a scalar UDF for proper extraction:
        dbo.fn_ExtractPlainText([FieldsJson])
    ) PERSISTED;

CREATE FULLTEXT INDEX ON [ContentVersion]
    ([FieldsPlainText] LANGUAGE 1033)     -- English
    KEY INDEX [PK_ContentVersion]
    ON [FTC_CmsContent]
    WITH CHANGE_TRACKING AUTO;

-- Index on MediaAsset text fields
CREATE FULLTEXT INDEX ON [MediaAsset]
    ([AltText] LANGUAGE 1033, [Title] LANGUAGE 1033, [Description] LANGUAGE 1033)
    KEY INDEX [PK_MediaAsset]
    ON [FTC_CmsContent]
    WITH CHANGE_TRACKING AUTO;

-- Index on TaxonomyTerm names
CREATE FULLTEXT INDEX ON [TaxonomyTerm]
    ([Name] LANGUAGE 1033)
    KEY INDEX [PK_TaxonomyTerm]
    ON [FTC_CmsContent]
    WITH CHANGE_TRACKING AUTO;
```

---

## 4. Stored Procedures

### 4.1 Content Entry

```sql
-- usp_ContentEntry_GetById
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_GetById]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT e.*, v.FieldsJson, v.RenderedFieldsJson, v.VersionNumber
    FROM   [ContentEntry] e
    LEFT JOIN [ContentVersion] v ON v.Id = e.PublishedVersionId
    WHERE  e.Id = @Id;
END;
GO

-- usp_ContentEntry_GetBySlug
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_GetBySlug]
    @Slug   NVARCHAR(500),
    @Locale NVARCHAR(10) = 'en-US'
AS
BEGIN
    SET NOCOUNT ON;
    SELECT e.*, v.FieldsJson, v.RenderedFieldsJson, v.VersionNumber
    FROM   [ContentEntry] e
    JOIN   [ContentVersion] v ON v.Id = e.PublishedVersionId
    WHERE  e.Slug   = @Slug
      AND  e.Locale = @Locale
      AND  e.Status = 'Published';
END;
GO

-- usp_ContentEntry_List
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_List]
    @ContentTypeId BIGINT       = NULL,
    @Status        NVARCHAR(20) = NULL,
    @OwnerId       BIGINT       = NULL,
    @Locale        NVARCHAR(10) = NULL,
    @SearchTerm    NVARCHAR(200)= NULL,
    @Page          INT          = 1,
    @PageSize      INT          = 25,
    @TotalRows     INT          OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Build a filtered CTE then page it
    WITH Filtered AS (
        SELECT e.Id, e.Slug, e.ContentTypeId, e.Status, e.OwnerId,
               e.Locale, e.UpdatedAt, e.CreatedAt, e.ScheduledPublishAt,
               ROW_NUMBER() OVER (ORDER BY e.UpdatedAt DESC) AS RowNum
        FROM   [ContentEntry] e
        WHERE  (@ContentTypeId IS NULL OR e.ContentTypeId = @ContentTypeId)
          AND  (@Status        IS NULL OR e.Status        = @Status)
          AND  (@OwnerId       IS NULL OR e.OwnerId       = @OwnerId)
          AND  (@Locale        IS NULL OR e.Locale        = @Locale)
          AND  (@SearchTerm    IS NULL OR e.Slug LIKE '%' + @SearchTerm + '%')
    )
    SELECT @TotalRows = COUNT(*) FROM Filtered;

    WITH Filtered AS (
        SELECT e.*, ROW_NUMBER() OVER (ORDER BY e.UpdatedAt DESC) AS RowNum
        FROM   [ContentEntry] e
        WHERE  (@ContentTypeId IS NULL OR e.ContentTypeId = @ContentTypeId)
          AND  (@Status        IS NULL OR e.Status        = @Status)
          AND  (@OwnerId       IS NULL OR e.OwnerId       = @OwnerId)
          AND  (@Locale        IS NULL OR e.Locale        = @Locale)
          AND  (@SearchTerm    IS NULL OR e.Slug LIKE '%' + @SearchTerm + '%')
    )
    SELECT * FROM Filtered
    WHERE  RowNum BETWEEN ((@Page - 1) * @PageSize + 1) AND (@Page * @PageSize);
END;
GO

-- usp_ContentEntry_Create
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_Create]
    @ContentTypeId BIGINT,
    @Slug          NVARCHAR(500),
    @Locale        NVARCHAR(10),
    @OwnerId       BIGINT,
    @NewId         BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [ContentEntry]
        ([ContentTypeId], [Slug], [Locale], [Status], [OwnerId], [CreatedAt], [UpdatedAt])
    VALUES
        (@ContentTypeId, @Slug, @Locale, 'Draft', @OwnerId, SYSUTCDATETIME(), SYSUTCDATETIME());

    SET @NewId = SCOPE_IDENTITY();
END;
GO

-- usp_ContentEntry_UpdateStatus
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_UpdateStatus]
    @Id                 BIGINT,
    @Status             NVARCHAR(20),
    @PublishedVersionId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [ContentEntry]
    SET    [Status]             = @Status,
           [PublishedVersionId] = COALESCE(@PublishedVersionId, [PublishedVersionId]),
           [UpdatedAt]          = SYSUTCDATETIME()
    WHERE  [Id] = @Id;
END;
GO

-- usp_ContentEntry_Delete (soft-archive only — no physical deletes)
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_Archive]
    @Id      BIGINT,
    @ActorId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [ContentEntry]
    SET    [Status]    = 'Archived',
           [UpdatedAt] = SYSUTCDATETIME()
    WHERE  [Id] = @Id;

    -- Audit
    EXEC [dbo].[usp_AuditLog_Write]
        @ActorId   = @ActorId,
        @EntityType= 'ContentEntry',
        @EntityId  = @Id,
        @Action    = 'Archive',
        @DiffJson  = NULL;
END;
GO

-- usp_ContentEntry_GetScheduledForPublish
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_GetScheduledForPublish]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, ContentTypeId, Slug, Locale
    FROM   [ContentEntry]
    WHERE  [Status]              = 'Approved'
      AND  [ScheduledPublishAt] IS NOT NULL
      AND  [ScheduledPublishAt] <= SYSUTCDATETIME();
END;
GO

-- usp_ContentEntry_GetScheduledForExpiry
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_GetScheduledForExpiry]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, ContentTypeId, Slug, Locale
    FROM   [ContentEntry]
    WHERE  [Status]             = 'Published'
      AND  [ScheduledExpireAt] IS NOT NULL
      AND  [ScheduledExpireAt] <= SYSUTCDATETIME();
END;
GO
```

### 4.2 Content Version

```sql
-- usp_ContentVersion_Create
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentVersion_Create]
    @ContentEntryId    BIGINT,
    @FieldsJson        NVARCHAR(MAX),
    @RenderedFieldsJson NVARCHAR(MAX) = NULL,
    @Status            NVARCHAR(20),
    @AuthorId          BIGINT,
    @ChangeNote        NVARCHAR(1000) = NULL,
    @NewId             BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NextVersion INT;
    SELECT @NextVersion = ISNULL(MAX([VersionNumber]), 0) + 1
    FROM   [ContentVersion]
    WHERE  [ContentEntryId] = @ContentEntryId;

    INSERT INTO [ContentVersion]
        ([ContentEntryId], [VersionNumber], [FieldsJson], [RenderedFieldsJson],
         [Status], [AuthorId], [ChangeNote], [CreatedAt])
    VALUES
        (@ContentEntryId, @NextVersion, @FieldsJson, @RenderedFieldsJson,
         @Status, @AuthorId, @ChangeNote, SYSUTCDATETIME());

    SET @NewId = SCOPE_IDENTITY();
END;
GO

-- usp_ContentVersion_List
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentVersion_List]
    @ContentEntryId BIGINT,
    @Page           INT = 1,
    @PageSize       INT = 25
AS
BEGIN
    SET NOCOUNT ON;
    SELECT v.*, u.DisplayName AS AuthorName
    FROM   [ContentVersion] v
    JOIN   [User] u ON u.Id = v.AuthorId
    WHERE  v.ContentEntryId = @ContentEntryId
    ORDER  BY v.VersionNumber DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH  NEXT @PageSize ROWS ONLY;
END;
GO

-- usp_ContentVersion_GetById
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentVersion_GetById]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT v.*, u.DisplayName AS AuthorName
    FROM   [ContentVersion] v
    JOIN   [User] u ON u.Id = v.AuthorId
    WHERE  v.Id = @Id;
END;
GO
```

### 4.3 Media

```sql
-- usp_MediaAsset_Create
CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_Create]
    @FileName        NVARCHAR(500),
    @StoragePath     NVARCHAR(2000),
    @StorageBackend  NVARCHAR(50),
    @MimeType        NVARCHAR(200),
    @FileSizeBytes   BIGINT,
    @Width           INT = NULL,
    @Height          INT = NULL,
    @UploadedById    BIGINT,
    @NewId           BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [MediaAsset]
        ([FileName], [StoragePath], [StorageBackend], [MimeType], [FileSizeBytes],
         [Width], [Height], [UploadedById], [IsVirusScanPassed], [CreatedAt], [UpdatedAt])
    VALUES
        (@FileName, @StoragePath, @StorageBackend, @MimeType, @FileSizeBytes,
         @Width, @Height, @UploadedById, NULL, SYSUTCDATETIME(), SYSUTCDATETIME());
    SET @NewId = SCOPE_IDENTITY();
END;
GO

-- usp_MediaAsset_UpdateMetadata
CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_UpdateMetadata]
    @Id          BIGINT,
    @AltText     NVARCHAR(500) = NULL,
    @Title       NVARCHAR(500) = NULL,
    @Description NVARCHAR(2000) = NULL,
    @Tags        NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [MediaAsset]
    SET    [AltText]     = COALESCE(@AltText, [AltText]),
           [Title]       = COALESCE(@Title, [Title]),
           [Description] = COALESCE(@Description, [Description]),
           [Tags]        = COALESCE(@Tags, [Tags]),
           [UpdatedAt]   = SYSUTCDATETIME()
    WHERE  [Id] = @Id;
END;
GO

-- usp_MediaAsset_SetVirusScanResult
CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_SetVirusScanResult]
    @Id     BIGINT,
    @Passed BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [MediaAsset]
    SET    [IsVirusScanPassed] = @Passed,
           [UpdatedAt]         = SYSUTCDATETIME()
    WHERE  [Id] = @Id;
END;
GO

-- usp_MediaAsset_List
CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_List]
    @MimeTypePrefix NVARCHAR(50) = NULL,   -- e.g. 'image/' for all images
    @SearchTerm     NVARCHAR(200) = NULL,
    @Page           INT = 1,
    @PageSize       INT = 50,
    @TotalRows      INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @TotalRows = COUNT(*)
    FROM   [MediaAsset]
    WHERE  ([IsVirusScanPassed] IS NULL OR [IsVirusScanPassed] = 1)
      AND  (@MimeTypePrefix IS NULL OR [MimeType] LIKE @MimeTypePrefix + '%')
      AND  (@SearchTerm     IS NULL OR [FileName] LIKE '%' + @SearchTerm + '%'
                                    OR [AltText]  LIKE '%' + @SearchTerm + '%'
                                    OR [Title]    LIKE '%' + @SearchTerm + '%');

    SELECT *
    FROM   [MediaAsset]
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

-- usp_MediaAsset_GetUsage
CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_GetUsage]
    @MediaAssetId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT mu.ContentEntryId, mu.FieldName, e.Slug, e.Status, e.ContentTypeId
    FROM   [MediaUsage] mu
    JOIN   [ContentEntry] e ON e.Id = mu.ContentEntryId
    WHERE  mu.MediaAssetId = @MediaAssetId;
END;
GO

-- usp_MediaAsset_SafeDelete
-- Returns 0 = deleted, 1 = blocked (still in use)
CREATE OR ALTER PROCEDURE [dbo].[usp_MediaAsset_SafeDelete]
    @Id     BIGINT,
    @Result INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM [MediaUsage] WHERE [MediaAssetId] = @Id)
    BEGIN
        SET @Result = 1; -- blocked
        RETURN;
    END;
    DELETE FROM [MediaAsset] WHERE [Id] = @Id;
    SET @Result = 0; -- deleted
END;
GO

-- usp_MediaUsage_Upsert
CREATE OR ALTER PROCEDURE [dbo].[usp_MediaUsage_Upsert]
    @MediaAssetId   BIGINT,
    @ContentEntryId BIGINT,
    @FieldName      NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (
        SELECT 1 FROM [MediaUsage]
        WHERE [MediaAssetId] = @MediaAssetId
          AND [ContentEntryId] = @ContentEntryId
          AND [FieldName] = @FieldName
    )
    INSERT INTO [MediaUsage] ([MediaAssetId], [ContentEntryId], [FieldName], [CreatedAt])
    VALUES (@MediaAssetId, @ContentEntryId, @FieldName, SYSUTCDATETIME());
END;
GO

-- usp_MediaUsage_DeleteForEntry
CREATE OR ALTER PROCEDURE [dbo].[usp_MediaUsage_DeleteForEntry]
    @ContentEntryId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [MediaUsage] WHERE [ContentEntryId] = @ContentEntryId;
END;
GO
```

### 4.4 Users & Auth

```sql
-- usp_User_Upsert (called on every login — sync from AAD)
CREATE OR ALTER PROCEDURE [dbo].[usp_User_Upsert]
    @ExternalId  NVARCHAR(200),
    @Email       NVARCHAR(500),
    @DisplayName NVARCHAR(500),
    @UserId      BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [User]
    SET    [Email]       = @Email,
           [DisplayName] = @DisplayName,
           [LastLoginAt] = SYSUTCDATETIME(),
           [UpdatedAt]   = SYSUTCDATETIME()
    WHERE  [ExternalId]  = @ExternalId;

    IF @@ROWCOUNT = 0
    BEGIN
        INSERT INTO [User] ([ExternalId], [Email], [DisplayName], [IsActive], [LastLoginAt], [CreatedAt], [UpdatedAt])
        VALUES (@ExternalId, @Email, @DisplayName, 1, SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME());
        SET @UserId = SCOPE_IDENTITY();
    END
    ELSE
        SELECT @UserId = Id FROM [User] WHERE [ExternalId] = @ExternalId;
END;
GO

-- usp_User_GetById
CREATE OR ALTER PROCEDURE [dbo].[usp_User_GetById]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.*, 
           (SELECT r.[Name] FROM [Role] r JOIN [UserRole] ur ON ur.RoleId = r.Id
            WHERE ur.UserId = u.Id AND ur.SectionId IS NULL
            FOR JSON PATH) AS GlobalRolesJson
    FROM   [User] u
    WHERE  u.Id = @Id;
END;
GO

-- usp_User_GetRoles (returns all role assignments for a user, used for JWT claims)
CREATE OR ALTER PROCEDURE [dbo].[usp_User_GetRoles]
    @UserId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ur.RoleId, r.[Name] AS RoleName, ur.SectionId, s.SlugPrefix AS SectionSlugPrefix
    FROM   [UserRole] ur
    JOIN   [Role] r          ON r.Id  = ur.RoleId
    LEFT JOIN [ContentSection] s ON s.Id = ur.SectionId
    WHERE  ur.UserId = @UserId;
END;
GO

-- usp_User_AssignRole
CREATE OR ALTER PROCEDURE [dbo].[usp_User_AssignRole]
    @UserId     BIGINT,
    @RoleId     BIGINT,
    @SectionId  BIGINT = NULL,
    @GrantedById BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (
        SELECT 1 FROM [UserRole]
        WHERE [UserId] = @UserId AND [RoleId] = @RoleId
          AND ([SectionId] = @SectionId OR ([SectionId] IS NULL AND @SectionId IS NULL))
    )
    INSERT INTO [UserRole] ([UserId], [RoleId], [SectionId], [GrantedById], [CreatedAt])
    VALUES (@UserId, @RoleId, @SectionId, @GrantedById, SYSUTCDATETIME());
END;
GO

-- usp_User_RevokeRole
CREATE OR ALTER PROCEDURE [dbo].[usp_User_RevokeRole]
    @UserId    BIGINT,
    @RoleId    BIGINT,
    @SectionId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [UserRole]
    WHERE [UserId]    = @UserId
      AND [RoleId]    = @RoleId
      AND ([SectionId] = @SectionId OR ([SectionId] IS NULL AND @SectionId IS NULL));
END;
GO

-- usp_User_Deactivate
CREATE OR ALTER PROCEDURE [dbo].[usp_User_Deactivate]
    @Id      BIGINT,
    @ActorId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [User] SET [IsActive] = 0, [UpdatedAt] = SYSUTCDATETIME() WHERE [Id] = @Id;
    EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'User', @Id, 'Deactivate', NULL;
END;
GO

-- usp_User_List
CREATE OR ALTER PROCEDURE [dbo].[usp_User_List]
    @SearchTerm NVARCHAR(200) = NULL,
    @IsActive   BIT = 1,
    @Page       INT = 1,
    @PageSize   INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
    FROM   [User]
    WHERE  [IsActive] = @IsActive
      AND  (@SearchTerm IS NULL
            OR [DisplayName] LIKE '%' + @SearchTerm + '%'
            OR [Email]       LIKE '%' + @SearchTerm + '%')
    ORDER  BY [DisplayName]
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH  NEXT @PageSize ROWS ONLY;
END;
GO
```

### 4.5 Workflow

```sql
-- usp_Workflow_Transition
-- Validates the transition is legal, updates status, logs the transition
CREATE OR ALTER PROCEDURE [dbo].[usp_Workflow_Transition]
    @ContentEntryId   BIGINT,
    @ContentVersionId BIGINT,
    @FromStatus       NVARCHAR(20),
    @ToStatus         NVARCHAR(20),
    @ActorId          BIGINT,
    @Comment          NVARCHAR(2000) = NULL,
    @Success          BIT OUTPUT,
    @ErrorMessage     NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Success = 0;
    SET @ErrorMessage = NULL;

    -- Validate current status matches expectation (optimistic concurrency)
    IF NOT EXISTS (SELECT 1 FROM [ContentEntry] WHERE [Id] = @ContentEntryId AND [Status] = @FromStatus)
    BEGIN
        SET @ErrorMessage = 'Content is not in expected status ' + @FromStatus;
        RETURN;
    END;

    -- Validate transition is legal
    IF NOT EXISTS (
        SELECT 1 FROM (VALUES
            ('Draft',    'InReview'),
            ('InReview', 'Approved'),
            ('InReview', 'Draft'),
            ('Approved', 'Published'),
            ('Approved', 'Draft'),
            ('Published','Archived'),
            ('Published','Approved'),   -- unpublish
            ('Draft',    'Published'),  -- admin bypass
            ('Approved', 'Archived')
        ) AS AllowedTransitions(FromS, ToS)
        WHERE FromS = @FromStatus AND ToS = @ToStatus
    )
    BEGIN
        SET @ErrorMessage = 'Transition from ' + @FromStatus + ' to ' + @ToStatus + ' is not permitted';
        RETURN;
    END;

    -- Require comment when returning to Draft
    IF @ToStatus = 'Draft' AND @FromStatus = 'InReview' AND (@Comment IS NULL OR LEN(TRIM(@Comment)) = 0)
    BEGIN
        SET @ErrorMessage = 'A comment is required when returning content to Draft';
        RETURN;
    END;

    -- Apply transition
    UPDATE [ContentEntry]
    SET    [Status]    = @ToStatus,
           [UpdatedAt] = SYSUTCDATETIME()
    WHERE  [Id] = @ContentEntryId;

    -- Log transition
    INSERT INTO [WorkflowTransition]
        ([ContentEntryId], [ContentVersionId], [FromStatus], [ToStatus], [ActorId], [Comment], [CreatedAt])
    VALUES
        (@ContentEntryId, @ContentVersionId, @FromStatus, @ToStatus, @ActorId, @Comment, SYSUTCDATETIME());

    -- Audit log
    EXEC [dbo].[usp_AuditLog_Write]
        @ActorId, 'ContentEntry', @ContentEntryId,
        'WorkflowTransition_' + @ToStatus, NULL;

    SET @Success = 1;
END;
GO
```

### 4.6 Navigation

```sql
-- usp_Navigation_GetMenuTree
CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_GetMenuTree]
    @Handle NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    -- Recursive CTE to build tree in one round-trip
    WITH MenuTree AS (
        SELECT ni.Id, ni.ParentItemId, ni.Label, ni.Url, ni.Target,
               ni.ContentEntryId, ni.SortOrder, ni.IsVisible, 0 AS Depth
        FROM   [NavigationItem] ni
        JOIN   [NavigationMenu] nm ON nm.Id = ni.MenuId
        WHERE  nm.[Handle] = @Handle
          AND  ni.ParentItemId IS NULL
          AND  ni.IsVisible = 1

        UNION ALL

        SELECT ni.Id, ni.ParentItemId, ni.Label, ni.Url, ni.Target,
               ni.ContentEntryId, ni.SortOrder, ni.IsVisible, mt.Depth + 1
        FROM   [NavigationItem] ni
        JOIN   MenuTree mt ON mt.Id = ni.ParentItemId
        WHERE  ni.IsVisible = 1 AND mt.Depth < 3  -- max 3 levels
    )
    SELECT * FROM MenuTree
    ORDER  BY ParentItemId, SortOrder;
END;
GO

-- usp_Navigation_UpsertItem
CREATE OR ALTER PROCEDURE [dbo].[usp_Navigation_UpsertItem]
    @Id             BIGINT = NULL,
    @MenuId         BIGINT,
    @ParentItemId   BIGINT = NULL,
    @Label          NVARCHAR(500),
    @Url            NVARCHAR(2000) = NULL,
    @ContentEntryId BIGINT = NULL,
    @Target         NVARCHAR(10) = '_self',
    @SortOrder      INT = 0,
    @IsVisible      BIT = 1,
    @NewId          BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Id IS NOT NULL
    BEGIN
        UPDATE [NavigationItem]
        SET [ParentItemId] = @ParentItemId, [Label] = @Label, [Url] = @Url,
            [ContentEntryId] = @ContentEntryId, [Target] = @Target,
            [SortOrder] = @SortOrder, [IsVisible] = @IsVisible
        WHERE [Id] = @Id;
        SET @NewId = @Id;
    END
    ELSE
    BEGIN
        INSERT INTO [NavigationItem]
            ([MenuId], [ParentItemId], [Label], [Url], [ContentEntryId], [Target], [SortOrder], [IsVisible])
        VALUES
            (@MenuId, @ParentItemId, @Label, @Url, @ContentEntryId, @Target, @SortOrder, @IsVisible);
        SET @NewId = SCOPE_IDENTITY();
    END;
END;
GO

-- usp_Redirect_GetByPath
CREATE OR ALTER PROCEDURE [dbo].[usp_Redirect_GetByPath]
    @FromPath NVARCHAR(2000)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 [ToPath], [StatusCode]
    FROM   [Redirect]
    WHERE  [FromPath]  = @FromPath
      AND  [IsActive]  = 1;
END;
GO

-- usp_Redirect_Create
CREATE OR ALTER PROCEDURE [dbo].[usp_Redirect_Create]
    @FromPath    NVARCHAR(2000),
    @ToPath      NVARCHAR(2000),
    @StatusCode  INT = 301,
    @CreatedById BIGINT,
    @NewId       BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    -- Deactivate any existing redirect for this path first
    UPDATE [Redirect] SET [IsActive] = 0 WHERE [FromPath] = @FromPath;

    INSERT INTO [Redirect] ([FromPath], [ToPath], [StatusCode], [IsActive], [CreatedById], [CreatedAt])
    VALUES (@FromPath, @ToPath, @StatusCode, 1, @CreatedById, SYSUTCDATETIME());
    SET @NewId = SCOPE_IDENTITY();
END;
GO
```

### 4.7 Search

```sql
-- usp_Search_FullText
CREATE OR ALTER PROCEDURE [dbo].[usp_Search_FullText]
    @Query         NVARCHAR(500),
    @ContentTypeId BIGINT       = NULL,
    @Page          INT          = 1,
    @PageSize      INT          = 25,
    @TotalRows     INT          OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Sanitize query for CONTAINS (wrap in quotes if not already an FTS expression)
    DECLARE @FtsQuery NVARCHAR(600) = '"' + REPLACE(@Query, '"', '') + '"';

    SELECT @TotalRows = COUNT(DISTINCT e.Id)
    FROM   [ContentEntry]  e
    JOIN   [ContentVersion] v ON v.Id = e.PublishedVersionId
    WHERE  e.Status = 'Published'
      AND  CONTAINS(v.[FieldsPlainText], @FtsQuery)
      AND  (@ContentTypeId IS NULL OR e.ContentTypeId = @ContentTypeId);

    SELECT e.Id, e.Slug, e.ContentTypeId, e.Locale, e.UpdatedAt,
           -- Excerpt: first 300 chars of plain text for result snippet
           LEFT(v.FieldsPlainText, 300) AS Excerpt,
           kt.RANK
    FROM   [ContentEntry] e
    JOIN   [ContentVersion] v ON v.Id = e.PublishedVersionId
    JOIN   CONTAINSTABLE([ContentVersion], [FieldsPlainText], @FtsQuery) kt
               ON kt.[KEY] = v.Id
    WHERE  e.Status = 'Published'
      AND  (@ContentTypeId IS NULL OR e.ContentTypeId = @ContentTypeId)
    ORDER  BY kt.RANK DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH  NEXT @PageSize ROWS ONLY;
END;
GO

-- usp_Search_LogQuery (for analytics)
CREATE OR ALTER PROCEDURE [dbo].[usp_Search_LogQuery]
    @Query       NVARCHAR(500),
    @ResultCount INT,
    @UserId      BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [SearchQueryLog] ([Query], [ResultCount], [UserId], [CreatedAt])
    VALUES (@Query, @ResultCount, @UserId, SYSUTCDATETIME());
END;
GO
```

### 4.8 Taxonomy

```sql
-- usp_Taxonomy_GetTermTree
CREATE OR ALTER PROCEDURE [dbo].[usp_Taxonomy_GetTermTree]
    @TaxonomyHandle NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    WITH TermTree AS (
        SELECT t.Id, t.ParentTermId, t.Name, t.Slug, t.SortOrder, 0 AS Depth
        FROM   [TaxonomyTerm] t
        JOIN   [Taxonomy] tx ON tx.Id = t.TaxonomyId
        WHERE  tx.[Handle] = @TaxonomyHandle AND t.ParentTermId IS NULL

        UNION ALL

        SELECT t.Id, t.ParentTermId, t.Name, t.Slug, t.SortOrder, tt.Depth + 1
        FROM   [TaxonomyTerm] t
        JOIN   TermTree tt ON tt.Id = t.ParentTermId
        WHERE  tt.Depth < 5  -- max hierarchy depth
    )
    SELECT * FROM TermTree ORDER BY ParentTermId, SortOrder;
END;
GO

-- usp_Taxonomy_GetEntriesForTerm
CREATE OR ALTER PROCEDURE [dbo].[usp_Taxonomy_GetEntriesForTerm]
    @TermId   BIGINT,
    @Page     INT = 1,
    @PageSize INT = 25
AS
BEGIN
    SET NOCOUNT ON;
    SELECT e.Id, e.Slug, e.ContentTypeId, e.Locale, e.UpdatedAt
    FROM   [ContentEntryTerm] cet
    JOIN   [ContentEntry] e ON e.Id = cet.ContentEntryId
    WHERE  cet.TaxonomyTermId = @TermId
      AND  e.Status = 'Published'
    ORDER  BY e.UpdatedAt DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH  NEXT @PageSize ROWS ONLY;
END;
GO
```

### 4.9 Audit Log

`usp_AuditLog_Write` is called by other stored procedures inside their own transaction and by the API for
events that have no stored procedure of their own (logon, logoff, refresh, policy denials). Since #165 the
row carries where the request came from and whether it succeeded (NIST AU-3).

```sql
CREATE OR ALTER PROCEDURE [dbo].[usp_AuditLog_Write]
    @ActorId       BIGINT,                 -- NULL (or 0) → SESSION_CONTEXT(N'ActorId')
    @EntityType    NVARCHAR(100),
    @EntityId      BIGINT,
    @Action        NVARCHAR(50),
    @DiffJson      NVARCHAR(MAX) = NULL,
    @Outcome       NVARCHAR(20)  = NULL,   -- 'Failure' or anything else → 'Success'
    @SourceIp      NVARCHAR(50)  = NULL,   -- NULL → SESSION_CONTEXT(N'SourceIp')
    @UserAgent     NVARCHAR(500) = NULL,   -- NULL → SESSION_CONTEXT(N'UserAgent')
    @CorrelationId NVARCHAR(100) = NULL    -- NULL → SESSION_CONTEXT(N'CorrelationId')
```

**SESSION_CONTEXT.** For every authenticated request the API's `CmsDatabase` runs `sp_set_session_context` for
`ActorId`, `SourceIp`, `UserAgent` and `CorrelationId` on each connection it opens (pooled connections are reset
on return, so this is per open, not per request). A stored procedure that audits therefore records who/where
without a signature change; procedures that already receive the acting user (`@OwnerId`, `@AuthorId`,
`@GrantedById`, `@UploadedById`, `@CreatedById`) pass it explicitly, the rest take a trailing
`@ActorId BIGINT = NULL`. Background workers and anonymous requests set no context and audit with a NULL actor.

`usp_AuditLog_ListPaged` / `usp_AuditLog_ExportCsv` (V030, V043) return the new columns and accept `@Outcome`
and `@IpAddress` filters; `usp_AuditLog_List` (V008) is unchanged.

**Event catalogue.** `Issue165AcceptanceTests.Mutating_Procedure_Writes_Its_Audit_Row` executes every
procedure below and asserts its row — add a case there when adding an audited procedure.

| EntityType | Action | Written by | Actor | DiffJson |
|---|---|---|---|---|
| `User` | `Provision` | `usp_User_Upsert` (insert branch only) | the new user | externalId, email |
| `User` | `AssignRole` | `usp_User_AssignRole` (also ends the user's sessions) | `@GrantedById` | roleId, role, sectionId |
| `User` | `RevokeRole` | `usp_User_RevokeRole` (also ends the user's sessions) | context | roleId, role, sectionId |
| `User` | `Deactivate` | `usp_User_Deactivate` (also ends the user's sessions) | `@ActorId` | — |
| `Session` | `SessionsRevoked` | `usp_RefreshToken_RevokeAllForUser` | `@ActorId` | reason, refreshTokensRevoked |
| `Session` | `RefreshReplay` (Failure) | `usp_RefreshToken_Validate` | the token's user | familyId, priorReason |
| `Session` | `Logon` / `LogonFailure` (Failure) / `Logoff` / `Refresh` / `RefreshFailure` (Failure) | API auth controllers | the user, or NULL with the attempted UPN in DiffJson | mode, upn, reason, systemUseAcknowledged |
| `Endpoint` | `AuthorizationDenied` (Failure) | API (`IAuthorizationMiddlewareResultHandler`, once per 403) | the caller | method, path, policy, requiredRoles |
| `ContentEntry` | `Create` | `usp_ContentEntry_Create` | `@OwnerId` | contentTypeId, slug, locale |
| `ContentEntry` | `UpdateStatus` | `usp_ContentEntry_UpdateStatus` | context | fromStatus, toStatus, publishedVersionId |
| `ContentEntry` | `Archive`, `UpdateSlug`, `SetSchedule`, `Duplicate`, `WorkflowTransition_<Status>`, `ScheduledPublish` / `ScheduledExpire` | pre-existing (V008, V017–V020, V040) | as before | as before |
| `ContentVersion` | `Create` | `usp_ContentVersion_Create` | `@AuthorId` | contentEntryId, versionNumber, status, changeNote |
| `ContentVersion` | `Restore` | `usp_ContentVersion_Restore` (V016) | `@ActorId` | as before |
| `MediaAsset` | `Create` | `usp_MediaAsset_Create` | `@UploadedById` | fileName, mimeType, fileSizeBytes |
| `MediaAsset` | `UpdateMetadata` | `usp_MediaAsset_UpdateMetadata` | context | altText, title, descriptionChanged, tagsChanged |
| `MediaAsset` | `Delete` | `usp_MediaAsset_SafeDelete` (deleted branch only) | context | fileName |
| `MediaAsset` | `VirusScanRejected`, `UploadRejected` | API `MediaUploadService` (#159) | the uploader | as before |
| `NavigationMenu` | `Create` / `Update` / `Delete` / `Reorder` | `usp_Navigation_CreateMenu` / `_UpdateMenu` / `_DeleteMenu` / `_BulkReorder` | context | name, handle / itemsDeleted / itemsUpdated, items |
| `NavigationItem` | `Create` / `Update` / `Delete` | `usp_Navigation_UpsertItem` / `_DeleteItem` | context | the item fields / menuId, label |
| `Redirect` | `Create` / `Update` / `Deactivate` | `usp_Redirect_Create` / `_Update` / `_Deactivate` | `@CreatedById` / context | fromPath, toPath, statusCode |
| `Webhook` | `Create` / `Delete` | `usp_Webhook_Create` / `_Delete` | `@CreatedById` / context | name, url, events (never the secret) |
| `SearchPin` | `Create` / `Update` / `Delete` | `usp_SearchPin_Create` / `_Delete` | `@CreatedById` / context | queryString, contentEntryId |
| `AdGroupRoleMapping` | `AdGroupMappingUpserted` / `AdGroupMappingDeleted` | `usp_AdGroupMapping_Upsert` / `_Delete` | `@CreatedById` / context | adGroup, roleId, role |
| `SiteSetting` | `SiteSettingUpdated` / `SiteSettingReset` | API `SiteSettingsController` (#150) | the admin | key, old/new value |

Not audited by design: `usp_ContentVersion_UpdateRenderedFields` (derived render output, no user intent),
`usp_MediaAsset_SetVirusScanResult` / `_UpdateWebPPath` (pipeline bookkeeping — the upload itself is audited),
the search query log, notification reads and the scheduler's sweep queries.

### 4.9a Refresh Tokens (#163)

`[RefreshToken]` (V044) holds one row per issued token — `TokenHash BINARY(32)` (SHA-256 of the opaque cookie
value), `FamilyId` (one login = one family, kept through rotation), `IssuedAt`, `ExpiresAt`, `AbsoluteExpiresAt`,
`LastUsedAt`, `RevokedAt`, `RevokedReason`, `ReplacedById`, `CreatedByIp`, `UserAgent`, `GroupsJson`. `[User]`
gains `SessionVersion INT`. `vacms_app` reaches the table only through:

| Procedure | Purpose |
|---|---|
| `usp_RefreshToken_Issue` | new family at login; stores the hash, per-token and absolute expiry, client address, login-time AD groups |
| `usp_RefreshToken_Validate @TokenHash, @IdleMinutes, @RotationGraceSeconds, @SourceIp, @UserAgent` | returns `Status` = `Ok` / `Replay` / `Expired` / `Idle` (no row when unknown). A revoked token presented outside the rotation grace is replay: the whole family is revoked and `Session/RefreshReplay` audited |
| `usp_RefreshToken_Rotate @OldId, @NewTokenHash, @ExpiresAt, …` | revokes the old row (`Rotated`, `ReplacedById`) and inserts its replacement in one transaction; `ExpiresAt` is capped at `AbsoluteExpiresAt` |
| `usp_RefreshToken_Revoke @TokenHash, @Reason` | single token (logout, disabled account) |
| `usp_RefreshToken_RevokeAllForUser @UserId, @ActorId, @Reason` | "sign out everywhere": revokes every live token and bumps `User.SessionVersion`; called by `usp_User_Deactivate`, `usp_User_AssignRole`, `usp_User_RevokeRole` |
| `usp_Maint_PurgeRefreshTokens @RetentionDays = 30` | Agent job (§6.8): deletes revoked/expired rows older than the retention |

### 4.10 Webhooks

```sql
-- usp_Webhook_GetActiveForEvent
CREATE OR ALTER PROCEDURE [dbo].[usp_Webhook_GetActiveForEvent]
    @EventName NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], [Url], [Secret]
    FROM   [Webhook]
    WHERE  [IsActive] = 1
      AND  [EventsJson] LIKE '%"' + @EventName + '"%';
END;
GO

-- usp_WebhookDelivery_Create
CREATE OR ALTER PROCEDURE [dbo].[usp_WebhookDelivery_Create]
    @WebhookId          BIGINT,
    @EventName          NVARCHAR(100),
    @PayloadJson        NVARCHAR(MAX),
    @ResponseStatusCode INT = NULL,
    @AttemptNumber      INT = 1,
    @ErrorMessage       NVARCHAR(2000) = NULL,
    @NewId              BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [WebhookDelivery]
        ([WebhookId], [EventName], [PayloadJson], [ResponseStatusCode],
         [AttemptNumber], [DeliveredAt], [ErrorMessage])
    VALUES
        (@WebhookId, @EventName, @PayloadJson, @ResponseStatusCode,
         @AttemptNumber, SYSUTCDATETIME(), @ErrorMessage);
    SET @NewId = SCOPE_IDENTITY();
END;
GO
```

---

## 5. PetaPoco Calling Convention

All application DB calls use `EXEC usp_*`. No raw DML from the app layer.

```csharp
// ContentEntryRepository.cs — calling SPs via PetaPoco
public async Task<ContentEntry?> GetBySlugAsync(string slug, string locale)
{
    return await _db.FirstOrDefaultAsync<ContentEntry>(
        "EXEC usp_ContentEntry_GetBySlug @0, @1", slug, locale);
}

public async Task<long> CreateAsync(ContentEntry entry)
{
    var param = new
    {
        ContentTypeId = entry.ContentTypeId,
        Slug          = entry.Slug,
        Locale        = entry.Locale,
        OwnerId       = entry.OwnerId,
    };
    // SP returns the new ID via OUTPUT — PetaPoco handles scalar result
    return await _db.ExecuteScalarAsync<long>(
        "DECLARE @NewId BIGINT; EXEC usp_ContentEntry_Create @0, @1, @2, @3, @NewId OUTPUT; SELECT @NewId;",
        param.ContentTypeId, param.Slug, param.Locale, param.OwnerId);
}

public async Task TransitionAsync(WorkflowTransitionRequest req)
{
    await _db.ExecuteAsync(
        @"DECLARE @Success BIT, @Err NVARCHAR(500);
          EXEC usp_Workflow_Transition @0, @1, @2, @3, @4, @5, @Success OUTPUT, @Err OUTPUT;
          IF @Success = 0 RAISERROR(@Err, 16, 1);",
        req.EntryId, req.VersionId, req.FromStatus, req.ToStatus, req.ActorId, req.Comment);
}
```

---

## 6. Database Hygiene

### 6.1 Statistics Maintenance

SQL Server query optimizer relies on up-to-date statistics. The default auto-update kicks in only when 20% of rows change — too slow for tables like `ContentEntry` and `AuditLog` that grow steadily.

```sql
-- migrations/V005__maintenance_jobs.sql

-- Update statistics on all tables, with full scan for smaller tables
CREATE OR ALTER PROCEDURE [dbo].[usp_Maint_UpdateStatistics]
AS
BEGIN
    -- Full scan for critical tables (fast — data fits in memory)
    UPDATE STATISTICS [ContentEntry]   WITH FULLSCAN;
    UPDATE STATISTICS [ContentVersion] WITH FULLSCAN;
    UPDATE STATISTICS [MediaAsset]     WITH FULLSCAN;
    UPDATE STATISTICS [AuditLog]       WITH SAMPLE 30 PERCENT;  -- too large for full scan
    UPDATE STATISTICS [SearchQueryLog] WITH SAMPLE 30 PERCENT;

    -- Remaining tables: use default sample rate
    UPDATE STATISTICS [User];
    UPDATE STATISTICS [NavigationItem];
    UPDATE STATISTICS [TaxonomyTerm];
    UPDATE STATISTICS [ContentEntryTerm];
    UPDATE STATISTICS [WorkflowTransition];
    UPDATE STATISTICS [Webhook];
    UPDATE STATISTICS [WebhookDelivery];
    UPDATE STATISTICS [Redirect];
END;
GO
```

**Schedule:** SQL Server Agent job, daily at 02:00 local time.

### 6.2 Index Maintenance

Fragmentation > 30% = rebuild (drops and recreates). 10–30% = reorganize (online, no lock).

```sql
CREATE OR ALTER PROCEDURE [dbo].[usp_Maint_RebuildIndexes]
    @FragmentationThresholdRebuild FLOAT = 30.0,
    @FragmentationThresholdReorg   FLOAT = 10.0
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TableName  NVARCHAR(256);
    DECLARE @IndexName  NVARCHAR(256);
    DECLARE @Frag       FLOAT;
    DECLARE @SQL        NVARCHAR(MAX);

    DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT
            OBJECT_NAME(ips.object_id)         AS TableName,
            i.name                              AS IndexName,
            ips.avg_fragmentation_in_percent    AS Fragmentation
        FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ips
        JOIN sys.indexes i ON i.object_id = ips.object_id AND i.index_id = ips.index_id
        WHERE ips.index_id > 0                  -- skip heap
          AND ips.avg_fragmentation_in_percent > @FragmentationThresholdReorg
          AND ips.page_count > 100              -- ignore tiny indexes
        ORDER BY ips.avg_fragmentation_in_percent DESC;

    OPEN cur;
    FETCH NEXT FROM cur INTO @TableName, @IndexName, @Frag;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @Frag >= @FragmentationThresholdRebuild
            SET @SQL = N'ALTER INDEX [' + @IndexName + N'] ON [dbo].[' + @TableName + N'] REBUILD WITH (ONLINE = ON);';
        ELSE
            SET @SQL = N'ALTER INDEX [' + @IndexName + N'] ON [dbo].[' + @TableName + N'] REORGANIZE;';

        EXEC sp_executesql @SQL;
        FETCH NEXT FROM cur INTO @TableName, @IndexName, @Frag;
    END;

    CLOSE cur;
    DEALLOCATE cur;

    -- Update stats after rebuild
    EXEC [dbo].[usp_Maint_UpdateStatistics];
END;
GO
```

**Schedule:** SQL Server Agent job, weekly Sunday at 01:00 local time (off-peak).

### 6.3 AuditLog Archival

The AuditLog is write-only and grows indefinitely. Rows older than the retention policy move to an archive table and are purged from the live table.
V043 adds `IpAddress`, `UserAgent`, `CorrelationId` and `Outcome` to both tables and to the batch copy below.

```sql
-- migrations/V006__audit_archive.sql

-- Archive table (identical structure, no FTS, cheaper storage)
CREATE TABLE [AuditLogArchive] (
    [Id]         BIGINT        NOT NULL,
    [ActorId]    BIGINT        NULL,
    [ActorEmail] NVARCHAR(500) NULL,
    [EntityType] NVARCHAR(100) NOT NULL,
    [EntityId]   NVARCHAR(100) NOT NULL,
    [Action]     NVARCHAR(50)  NOT NULL,
    [DiffJson]   NVARCHAR(MAX) NULL,
    [CreatedAt]  DATETIME2     NOT NULL,
    [ArchivedAt] DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE CLUSTERED INDEX [CX_AuditLogArchive_CreatedAt] ON [AuditLogArchive] ([CreatedAt]);
GO

-- Archival SP: moves rows older than N days to archive, deletes from live
CREATE OR ALTER PROCEDURE [dbo].[usp_Maint_ArchiveAuditLog]
    @RetentionDays INT = 730   -- 2 years live; archived rows kept per DBA policy
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -@RetentionDays, SYSUTCDATETIME());

    -- Move to archive in batches to avoid long-running transactions
    DECLARE @BatchSize INT = 5000;
    DECLARE @Moved     INT = 1;

    WHILE @Moved > 0
    BEGIN
        INSERT INTO [AuditLogArchive]
            ([Id], [ActorId], [ActorEmail], [EntityType], [EntityId], [Action], [DiffJson], [CreatedAt])
        SELECT TOP (@BatchSize)
            [Id], [ActorId], [ActorEmail], [EntityType], [EntityId], [Action], [DiffJson], [CreatedAt]
        FROM   [AuditLog]
        WHERE  [CreatedAt] < @CutoffDate;

        SET @Moved = @@ROWCOUNT;

        DELETE FROM [AuditLog]
        WHERE [Id] IN (
            SELECT TOP (@BatchSize) [Id] FROM [AuditLog] WHERE [CreatedAt] < @CutoffDate
        );

        IF @Moved > 0 WAITFOR DELAY '00:00:01'; -- brief pause between batches
    END;
END;
GO
```

**Schedule:** Monthly, first Sunday at 00:00.

### 6.4 Webhook Delivery Log Purge

```sql
CREATE OR ALTER PROCEDURE [dbo].[usp_Maint_PurgeWebhookDeliveries]
    @RetentionDays INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [WebhookDelivery]
    WHERE [DeliveredAt] < DATEADD(DAY, -@RetentionDays, SYSUTCDATETIME())
      AND [ResponseStatusCode] BETWEEN 200 AND 299;  -- only purge successful deliveries
END;
GO
```

### 6.5 Search Query Log Rollup

```sql
CREATE OR ALTER PROCEDURE [dbo].[usp_Maint_RollupSearchLogs]
AS
BEGIN
    SET NOCOUNT ON;
    -- Aggregate yesterday's raw logs into the summary table
    -- Raw logs older than 90 days are purged; summaries are kept indefinitely
    DECLARE @Yesterday DATE = DATEADD(DAY, -1, CAST(SYSUTCDATETIME() AS DATE));

    INSERT INTO [SearchQuerySummary] ([QueryDate], [Query], [SearchCount], [ZeroResults])
    SELECT @Yesterday,
           [Query],
           COUNT(*)                                    AS SearchCount,
           SUM(CASE WHEN [ResultCount] = 0 THEN 1 ELSE 0 END) AS ZeroResults
    FROM   [SearchQueryLog]
    WHERE  CAST([CreatedAt] AS DATE) = @Yesterday
    GROUP  BY [Query];

    DELETE FROM [SearchQueryLog]
    WHERE [CreatedAt] < DATEADD(DAY, -90, SYSUTCDATETIME());
END;
GO
```

### 6.6 Scheduled Publish / Expiry Sweep

The background worker calls these SPs every 60 seconds, not the API request path:

```sql
-- Already defined above:
-- usp_ContentEntry_GetScheduledForPublish  — returns entries due for publish
-- usp_ContentEntry_GetScheduledForExpiry   — returns entries due for unpublish
-- After processing each, the worker calls usp_Workflow_Transition to update status
```

### 6.8 Refresh Token Purge

`usp_Maint_PurgeRefreshTokens @RetentionDays = 30` (V044) deletes revoked or expired `[RefreshToken]` rows older
than the retention in 5 000-row batches, clearing `ReplacedById` links first. Live rows are never touched; the
security-relevant events are already in `AuditLog`. Registered by `infra/sql-agent-jobs/job_Maint_PurgeRefreshTokens.sql`.

### 6.7 Database Monitoring Queries

Expose these as read-only API endpoints for the admin health dashboard:

```sql
-- Index fragmentation summary
CREATE OR ALTER PROCEDURE [dbo].[usp_Monitor_IndexFragmentation]
AS
BEGIN
    SELECT
        OBJECT_NAME(ips.object_id)         AS TableName,
        i.name                              AS IndexName,
        ips.avg_fragmentation_in_percent    AS FragmentationPct,
        ips.page_count                      AS PageCount
    FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ips
    JOIN sys.indexes i ON i.object_id = ips.object_id AND i.index_id = ips.index_id
    WHERE ips.index_id > 0
      AND ips.page_count > 50
    ORDER BY ips.avg_fragmentation_in_percent DESC;
END;
GO

-- Table row counts and storage
CREATE OR ALTER PROCEDURE [dbo].[usp_Monitor_TableSizes]
AS
BEGIN
    SELECT
        t.name                                              AS TableName,
        p.rows                                              AS RowCount,
        SUM(a.total_pages) * 8 / 1024                      AS TotalSizeMB,
        SUM(a.used_pages)  * 8 / 1024                      AS UsedSizeMB
    FROM sys.tables t
    JOIN sys.indexes i      ON t.object_id = i.object_id
    JOIN sys.partitions p   ON i.object_id = p.object_id AND i.index_id = p.index_id
    JOIN sys.allocation_units a ON p.partition_id = a.container_id
    WHERE t.is_ms_shipped = 0 AND i.index_id IN (0, 1)
    GROUP BY t.name, p.rows
    ORDER BY SUM(a.total_pages) DESC;
END;
GO

-- Long-running queries (> 5 seconds)
CREATE OR ALTER PROCEDURE [dbo].[usp_Monitor_LongRunningQueries]
AS
BEGIN
    SELECT TOP 20
        r.session_id,
        r.status,
        r.start_time,
        DATEDIFF(SECOND, r.start_time, SYSUTCDATETIME()) AS DurationSec,
        r.command,
        SUBSTRING(st.text, (r.statement_start_offset/2)+1,
            ((CASE r.statement_end_offset WHEN -1 THEN DATALENGTH(st.text)
              ELSE r.statement_end_offset END - r.statement_start_offset)/2)+1) AS QueryText,
        r.wait_type,
        r.blocking_session_id
    FROM sys.dm_exec_requests r
    CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) st
    WHERE DATEDIFF(SECOND, r.start_time, SYSUTCDATETIME()) > 5
      AND r.session_id <> @@SPID
    ORDER BY DurationSec DESC;
END;
GO
```

---

## 7. Migration File Index

| File | Contents |
|---|---|
| `V001__initial_schema.sql` | All tables + unique indexes |
| `V002__add_fts_catalog.sql` | Full-text catalog, computed column, FTS indexes |
| `V003__security_model.sql` | Logins, users, EXECUTE grant, DENY on tables |
| `V004__non_clustered_indexes.sql` | All non-clustered indexes from section 3.2 |
| `V005__maintenance_sps.sql` | `usp_Maint_*` stored procedures |
| `V006__audit_archive.sql` | `AuditLogArchive` table + archival SP |
| `V007__search_analytics_tables.sql` | `SearchQueryLog`, `SearchQuerySummary` tables |
| `V008__stored_procedures.sql` | All `usp_*` application SPs |
| `V009__monitoring_sps.sql` | `usp_Monitor_*` SPs |
| `V010`–`V042` | feature migrations — see each file's header comment |
| `V043__audit_log_columns.sql` | `AuditLog.CorrelationId` / `Outcome` (+ archive), `usp_AuditLog_Write` with SESSION_CONTEXT fallback, viewer filters (#165) |
| `V044__refresh_tokens.sql` | `RefreshToken` table, `User.SessionVersion`, `usp_RefreshToken_*`, `usp_Maint_PurgeRefreshTokens` (#163) |
| `V045__audit_coverage.sql` | every mutating SP audits inside its transaction; role changes end sessions (#165) |

---

## 8. SQL Server Agent Job Schedule

| Job | Procedure | Schedule |
|---|---|---|
| Statistics Update | `usp_Maint_UpdateStatistics` | Daily 02:00 |
| Index Maintenance | `usp_Maint_RebuildIndexes` | Weekly Sun 01:00 |
| Audit Log Archival | `usp_Maint_ArchiveAuditLog` | Monthly 1st Sun 00:00 |
| Webhook Log Purge | `usp_Maint_PurgeWebhookDeliveries` | Weekly Sun 03:00 |
| Search Log Rollup | `usp_Maint_RollupSearchLogs` | Daily 00:15 |
| Refresh Token Purge | `usp_Maint_PurgeRefreshTokens` | Daily 02:30 |

All jobs log to the SQL Server Agent job history. A monitoring SP (`usp_Monitor_AgentJobStatus`) checks the last run result of each job and surfaces failures to the admin health dashboard.
