-- V017__slug_update_sp.sql
-- Issue #33: slug auto-generation, URL preview, and 301 redirect on slug change.
--
-- Adds usp_ContentEntry_UpdateSlug:
--   - Validates uniqueness of new slug within locale (returns error if duplicate)
--   - If the entry is Published and the slug is changing, creates a 301 Redirect
--     from the old path to the new path (deactivating any existing redirect for the old path)
--   - Updates the ContentEntry slug
--   - Audit logs the change

CREATE OR ALTER PROCEDURE [dbo].[usp_ContentEntry_UpdateSlug]
    @Id          BIGINT,
    @NewSlug     NVARCHAR(500),
    @ActorId     BIGINT,
    @Success     BIT            OUTPUT,
    @ErrorMessage NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Success = 0;
    SET @ErrorMessage = NULL;

    -- 1. Load the current entry
    DECLARE @OldSlug NVARCHAR(500);
    DECLARE @Locale  NVARCHAR(10);
    DECLARE @Status  NVARCHAR(20);

    SELECT @OldSlug = [Slug],
           @Locale  = [Locale],
           @Status  = [Status]
    FROM   [ContentEntry]
    WHERE  [Id] = @Id;

    IF @OldSlug IS NULL
    BEGIN
        SET @ErrorMessage = 'Content entry not found.';
        RETURN;
    END;

    -- 2. No-op if slug is unchanged
    IF @OldSlug = @NewSlug
    BEGIN
        SET @Success = 1;
        RETURN;
    END;

    -- 3. Duplicate slug check within same locale
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

    -- 4. If entry is Published, create a 301 redirect from old path to new path
    IF @Status = 'Published'
    BEGIN
        DECLARE @Ignored BIGINT;
        EXEC [dbo].[usp_Redirect_Create]
            @FromPath    = @OldSlug,
            @ToPath      = @NewSlug,
            @StatusCode  = 301,
            @CreatedById = @ActorId,
            @NewId       = @Ignored OUTPUT;
    END;

    -- 5. Update the slug
    UPDATE [ContentEntry]
    SET    [Slug]      = @NewSlug,
           [UpdatedAt] = SYSUTCDATETIME()
    WHERE  [Id] = @Id;

    -- 6. Audit
    EXEC [dbo].[usp_AuditLog_Write]
        @ActorId    = @ActorId,
        @EntityType = 'ContentEntry',
        @EntityId   = @Id,
        @Action     = 'UpdateSlug',
        @DiffJson   = NULL;

    SET @Success = 1;
END;
GO
