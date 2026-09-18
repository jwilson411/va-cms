-- V047__redirect_resolution.sql
-- Issue #169 (epic #152) — redirects are finally served.
--
-- Until now nothing consumed the Redirect table: the public site had no resolver,
-- and the rows usp_ContentEntry_UpdateSlug wrote were bare slugs ("old-slug" →
-- "new-slug") while admin-created rows are site-relative public paths
-- ("/pages/old" → "/pages/new"). This migration makes every row a public path
-- so a single exact FromPath lookup resolves a request:
--
--   usp_ContentEntry_UpdateSlug  writes /pages/{slug} (or /news/{slug} for
--                                news_article) and re-activates the new path
--                                (a redirect *from* a live page must not shadow it)
--   usp_Redirect_Create/_Update  flatten chains: any active rule whose ToPath is
--                                the new FromPath is re-pointed at the new ToPath,
--                                so a resolved redirect never needs a second hop
--   usp_Redirect_GetByPath       unchanged contract; also tolerates a trailing '/'
--   existing bare-slug rows      rewritten to public paths, then chains collapsed
--
-- Loop rejection (A→B when B→A exists) is enforced by the API before it calls
-- usp_Redirect_Create / _Update (RedirectAdminController.ValidateAsync); the SP
-- side only has to keep the stored graph one hop deep.

-- ── 1. Normalise existing slug-change rows to public paths ───────────────────
-- The public route prefix comes from the content type of the entry the redirect
-- pointed at when it was written. A ToPath that no longer matches any entry
-- (the entry was renamed again, or deleted) falls back to /pages/.
UPDATE r
SET    r.[FromPath] = p.[Prefix] + r.[FromPath],
       r.[ToPath]   = p.[Prefix] + r.[ToPath]
FROM   [dbo].[Redirect] r
CROSS APPLY (
    SELECT TOP 1 CASE WHEN ct.[Name] = 'news_article' THEN N'/news/' ELSE N'/pages/' END AS [Prefix]
    FROM   [dbo].[ContentEntry] ce
    JOIN   [dbo].[ContentType]  ct ON ct.[Id] = ce.[ContentTypeId]
    WHERE  ce.[Slug] = r.[ToPath]
    UNION ALL SELECT N'/pages/'
) p
WHERE  r.[FromPath] NOT LIKE '/%'
  AND  r.[ToPath]   NOT LIKE '/%'
  AND  r.[ToPath]   NOT LIKE 'http%';
GO

-- ── 2. Collapse existing chains (bounded; a cycle simply stops iterating) ────
DECLARE @Pass INT = 0;
WHILE @Pass < 10 AND EXISTS (
    SELECT 1
    FROM   [dbo].[Redirect] a
    JOIN   [dbo].[Redirect] b ON b.[FromPath] = a.[ToPath] AND b.[IsActive] = 1
    WHERE  a.[IsActive] = 1 AND b.[ToPath] <> a.[FromPath] AND b.[ToPath] <> a.[ToPath])
BEGIN
    UPDATE a
    SET    a.[ToPath] = b.[ToPath]
    FROM   [dbo].[Redirect] a
    JOIN   [dbo].[Redirect] b ON b.[FromPath] = a.[ToPath] AND b.[IsActive] = 1
    WHERE  a.[IsActive] = 1 AND b.[ToPath] <> a.[FromPath] AND b.[ToPath] <> a.[ToPath];
    SET @Pass += 1;
END;
GO

-- ── 3. usp_Redirect_GetByPath: exact match, then the trailing-slash twin ─────
CREATE OR ALTER PROCEDURE [dbo].[usp_Redirect_GetByPath]
    @FromPath NVARCHAR(2000)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Twin NVARCHAR(2000) =
        CASE WHEN LEN(@FromPath) > 1 AND RIGHT(@FromPath, 1) = '/' THEN LEFT(@FromPath, LEN(@FromPath) - 1)
             ELSE @FromPath + '/' END;

    SELECT TOP 1 [FromPath], [ToPath], [StatusCode]
    FROM   [dbo].[Redirect]
    WHERE  [FromPath] IN (@FromPath, @Twin)
      AND  [IsActive] = 1
    ORDER  BY CASE WHEN [FromPath] = @FromPath THEN 0 ELSE 1 END, [CreatedAt] DESC;
END;
GO

-- ── 4. usp_Redirect_Create: one active rule per FromPath, chains flattened ───
-- Body as V045 (transaction + audit diff) plus the flattening step.
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

    -- X → FromPath becomes X → ToPath so a visitor never bounces twice.
    UPDATE [dbo].[Redirect]
    SET    [ToPath] = @ToPath
    WHERE  [IsActive] = 1
      AND  [ToPath]   = @FromPath
      AND  [Id]      <> @NewId
      AND  [FromPath] <> @ToPath;   -- would form a loop; the API rejects that case up front

    DECLARE @Diff NVARCHAR(MAX) = (SELECT @FromPath AS fromPath, @ToPath AS toPath, @StatusCode AS statusCode FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXEC [dbo].[usp_AuditLog_Write] @CreatedById, 'Redirect', @NewId, 'Create', @Diff;
    COMMIT TRANSACTION;
END;
GO

-- ── 5. usp_Redirect_Update: same flattening after an edit ────────────────────
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
        UPDATE [dbo].[Redirect]
        SET    [ToPath] = @ToPath
        WHERE  [IsActive] = 1
          AND  [ToPath]   = @FromPath
          AND  [Id]      <> @Id
          AND  [FromPath] <> @ToPath;

        DECLARE @Diff NVARCHAR(MAX) = (SELECT @FromPath AS fromPath, @ToPath AS toPath, @StatusCode AS statusCode FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'Redirect', @Id, 'Update', @Diff;
    END;
    COMMIT TRANSACTION;
END;
GO

-- ── 6. usp_ContentEntry_UpdateSlug: public paths, live path un-shadowed ──────
CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_UpdateSlug]
    @Id           BIGINT,
    @NewSlug      NVARCHAR(500),
    @ActorId      BIGINT,
    @Success      BIT            OUTPUT,
    @ErrorMessage NVARCHAR(500)  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Success = 0;
    SET @ErrorMessage = NULL;

    DECLARE @OldSlug  NVARCHAR(500);
    DECLARE @Locale   NVARCHAR(10);
    DECLARE @Status   NVARCHAR(20);
    DECLARE @TypeName NVARCHAR(100);

    SELECT @OldSlug  = ce.[Slug],
           @Locale   = ce.[Locale],
           @Status   = ce.[Status],
           @TypeName = ct.[Name]
    FROM   [ContentEntry] ce
    JOIN   [ContentType]  ct ON ct.[Id] = ce.[ContentTypeId]
    WHERE  ce.[Id] = @Id;

    IF @OldSlug IS NULL
    BEGIN
        SET @ErrorMessage = 'Content entry not found.';
        RETURN;
    END;

    IF @OldSlug = @NewSlug
    BEGIN
        SET @Success = 1;
        RETURN;
    END;

    IF EXISTS (
        SELECT 1 FROM [ContentEntry]
        WHERE  [Slug]   = @NewSlug
          AND  [Locale] = @Locale
          AND  [Id]    <> @Id
    )
    BEGIN
        SET @ErrorMessage = 'A content entry with slug ''' + @NewSlug + ''' already exists for locale ' + @Locale + '.';
        RETURN;
    END;

    -- The public site serves standard pages at /pages/{slug} and news at /news/{slug}
    -- (src/public/app/**/[...slug]); the redirect is stored as that public path.
    DECLARE @Prefix  NVARCHAR(10)   = CASE WHEN @TypeName = 'news_article' THEN N'/news/' ELSE N'/pages/' END;
    DECLARE @OldPath NVARCHAR(2000) = @Prefix + @OldSlug;
    DECLARE @NewPath NVARCHAR(2000) = @Prefix + @NewSlug;

    -- The new path is live again: any rule redirecting *away* from it must stop.
    UPDATE [dbo].[Redirect] SET [IsActive] = 0 WHERE [FromPath] = @NewPath AND [IsActive] = 1;

    IF @Status = 'Published'
    BEGIN
        DECLARE @Ignored BIGINT;
        EXEC [dbo].[usp_Redirect_Create]
            @FromPath    = @OldPath,
            @ToPath      = @NewPath,
            @StatusCode  = 301,
            @CreatedById = @ActorId,
            @NewId       = @Ignored OUTPUT;
    END;

    UPDATE [ContentEntry]
    SET    [Slug]      = @NewSlug,
           [UpdatedAt] = SYSUTCDATETIME()
    WHERE  [Id] = @Id;

    EXEC [dbo].[usp_AuditLog_Write]
        @ActorId    = @ActorId,
        @EntityType = 'ContentEntry',
        @EntityId   = @Id,
        @Action     = 'UpdateSlug',
        @DiffJson   = NULL;

    SET @Success = 1;
END;
GO
