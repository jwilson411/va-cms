-- V035__demo_seed_owner_fix.sql
-- usp_DemoSeed_Run resolved the content owner by ExternalId 'demo-superadmin',
-- but V012 renamed the SuperAdmin role to SystemAdmin, so the demo user is now
-- 'demo-systemadmin'. @DemoOwnerId came back NULL and every ContentEntry insert
-- failed with "Cannot insert the value NULL into column 'OwnerId'".
-- Also: the standard-page FieldsJson was built with REPLACE(@Body, '"', '\"'),
-- which leaves the multi-line body's raw newlines unescaped — invalid JSON that
-- every consumer (public delivery, admin editor) fails to parse. Now uses
-- STRING_ESCAPE(..., 'json'). Body is otherwise identical to V010.

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_DemoSeed_Run]
AS
BEGIN
    SET NOCOUNT ON;

    -- -------------------------------------------------------
    -- 1. Ensure taxonomy "Topics" exists
    -- -------------------------------------------------------
    IF NOT EXISTS (SELECT 1 FROM [dbo].[Taxonomy] WHERE [Handle] = 'topics')
        INSERT INTO [dbo].[Taxonomy] ([Name], [Handle], [IsHierarchical])
        VALUES ('Topics', 'topics', 0);

    DECLARE @TaxId BIGINT;
    SELECT @TaxId = [Id] FROM [dbo].[Taxonomy] WHERE [Handle] = 'topics';

    -- Taxonomy term 1
    IF NOT EXISTS (SELECT 1 FROM [dbo].[TaxonomyTerm] WHERE [Slug] = 'benefits' AND [TaxonomyId] = @TaxId)
        INSERT INTO [dbo].[TaxonomyTerm] ([TaxonomyId], [ParentTermId], [Name], [Slug], [SortOrder])
        VALUES (@TaxId, NULL, 'Benefits', 'benefits', 10);

    -- Taxonomy term 2
    IF NOT EXISTS (SELECT 1 FROM [dbo].[TaxonomyTerm] WHERE [Slug] = 'health-care' AND [TaxonomyId] = @TaxId)
        INSERT INTO [dbo].[TaxonomyTerm] ([TaxonomyId], [ParentTermId], [Name], [Slug], [SortOrder])
        VALUES (@TaxId, NULL, 'Health Care', 'health-care', 20);

    -- -------------------------------------------------------
    -- 2. Ensure content types exist
    -- -------------------------------------------------------
    IF NOT EXISTS (SELECT 1 FROM [dbo].[ContentType] WHERE [Name] = 'standard_page')
        INSERT INTO [dbo].[ContentType]
            ([Name], [DisplayName], [Description], [TemplateId], [IsSystemType], [AllowWorkflow], [FieldSchemaJson])
        VALUES
            ('standard_page', 'Standard Page', 'A general-purpose content page.', 'StandardPageTemplate', 1, 1,
             '[{"name":"title","type":"ShortText","required":true},{"name":"body","type":"RichText","required":true}]');

    IF NOT EXISTS (SELECT 1 FROM [dbo].[ContentType] WHERE [Name] = 'news_article')
        INSERT INTO [dbo].[ContentType]
            ([Name], [DisplayName], [Description], [TemplateId], [IsSystemType], [AllowWorkflow], [FieldSchemaJson])
        VALUES
            ('news_article', 'News Article', 'A news or announcement entry.', 'NewsArticleTemplate', 1, 1,
             '[{"name":"title","type":"ShortText","required":true},{"name":"summary","type":"LongText","required":true},{"name":"body","type":"RichText","required":true}]');

    DECLARE @StdPageTypeId  BIGINT;
    DECLARE @NewsTypeId     BIGINT;
    SELECT @StdPageTypeId = [Id] FROM [dbo].[ContentType] WHERE [Name] = 'standard_page';
    SELECT @NewsTypeId    = [Id] FROM [dbo].[ContentType] WHERE [Name] = 'news_article';

    -- -------------------------------------------------------
    -- 3. Seed demo users (1 per role).
    --    ExternalId uses "demo-{role}" so it is stable across re-runs.
    -- -------------------------------------------------------
    DECLARE @RoleId    BIGINT;
    DECLARE @UserId    BIGINT;
    DECLARE @RoleName  NVARCHAR(100);
    DECLARE @ExtId     NVARCHAR(200);
    DECLARE @Email     NVARCHAR(500);
    DECLARE @Display   NVARCHAR(500);
    DECLARE @GranterId BIGINT;  -- set to the first demo user created

    DECLARE role_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT [Id], [Name] FROM [dbo].[Role] ORDER BY [Id];

    OPEN role_cur;
    FETCH NEXT FROM role_cur INTO @RoleId, @RoleName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @ExtId   = 'demo-' + LOWER(@RoleName);
        SET @Email   = LOWER(@RoleName) + '@demo.va.gov';
        SET @Display = 'Demo ' + @RoleName;

        IF NOT EXISTS (SELECT 1 FROM [dbo].[User] WHERE [ExternalId] = @ExtId)
            INSERT INTO [dbo].[User] ([ExternalId], [Email], [DisplayName], [IsActive])
            VALUES (@ExtId, @Email, @Display, 1);

        SELECT @UserId = [Id] FROM [dbo].[User] WHERE [ExternalId] = @ExtId;

        -- Grant the matching role if not already assigned
        IF @GranterId IS NULL SET @GranterId = @UserId;  -- first user grants subsequent ones

        IF NOT EXISTS (
            SELECT 1 FROM [dbo].[UserRole]
            WHERE [UserId] = @UserId AND [RoleId] = @RoleId AND [SectionId] IS NULL
        )
            INSERT INTO [dbo].[UserRole] ([UserId], [RoleId], [SectionId], [GrantedById])
            VALUES (@UserId, @RoleId, NULL, @GranterId);

        FETCH NEXT FROM role_cur INTO @RoleId, @RoleName;
    END;

    CLOSE role_cur;
    DEALLOCATE role_cur;

    -- Resolve the admin demo user's ID to use as OwnerId for content.
    -- V012 renamed SuperAdmin -> SystemAdmin; accept either, then fall back to
    -- any demo user so ContentEntry.OwnerId (NOT NULL) is always satisfied.
    DECLARE @DemoOwnerId BIGINT;
    SELECT @DemoOwnerId = [Id] FROM [dbo].[User] WHERE [ExternalId] = 'demo-systemadmin';
    IF @DemoOwnerId IS NULL
        SELECT @DemoOwnerId = [Id] FROM [dbo].[User] WHERE [ExternalId] = 'demo-superadmin';
    IF @DemoOwnerId IS NULL
        SELECT TOP (1) @DemoOwnerId = [Id] FROM [dbo].[User] WHERE [ExternalId] LIKE 'demo-%' ORDER BY [Id];
    IF @DemoOwnerId IS NULL
        THROW 50000, 'usp_DemoSeed_Run: no demo user available to own seeded content (Role table empty?).', 1;

    -- -------------------------------------------------------
    -- 4. Seed 5 standard pages (idempotent by slug)
    -- -------------------------------------------------------
    DECLARE @EntryId   BIGINT;
    DECLARE @VersionId BIGINT;
    DECLARE @Slug      NVARCHAR(500);
    DECLARE @Title     NVARCHAR(200);
    DECLARE @Body      NVARCHAR(MAX);

    -- Helper: seed one standard page
    -- Page 1
    SET @Slug  = 'demo/home';
    SET @Title = 'Welcome to VA CMS Demo';
    SET @Body  = '## Welcome

This is the demo home page. Use it to explore the VA CMS content management features.';

    IF NOT EXISTS (SELECT 1 FROM [dbo].[ContentEntry] WHERE [Slug] = @Slug AND [Locale] = 'en-US')
    BEGIN
        INSERT INTO [dbo].[ContentEntry]
            ([ContentTypeId], [Slug], [Locale], [Status], [OwnerId])
        VALUES
            (@StdPageTypeId, @Slug, 'en-US', 'Published', @DemoOwnerId);

        SET @EntryId = SCOPE_IDENTITY();

        INSERT INTO [dbo].[ContentVersion]
            ([ContentEntryId], [VersionNumber], [FieldsJson], [Status], [AuthorId], [ChangeNote])
        VALUES
            (@EntryId, 1, '{"title":"' + STRING_ESCAPE(@Title, 'json') + '","body":"' + STRING_ESCAPE(@Body, 'json') + '"}',
             'Published', @DemoOwnerId, 'Demo seed');

        SET @VersionId = SCOPE_IDENTITY();

        UPDATE [dbo].[ContentEntry]
        SET [PublishedVersionId] = @VersionId
        WHERE [Id] = @EntryId;
    END;

    -- Page 2
    SET @Slug  = 'demo/about';
    SET @Title = 'About This Demo';
    SET @Body  = '## About

This page demonstrates a standard page content type. Edit it in the admin to see how content editing works.';

    IF NOT EXISTS (SELECT 1 FROM [dbo].[ContentEntry] WHERE [Slug] = @Slug AND [Locale] = 'en-US')
    BEGIN
        INSERT INTO [dbo].[ContentEntry] ([ContentTypeId], [Slug], [Locale], [Status], [OwnerId])
        VALUES (@StdPageTypeId, @Slug, 'en-US', 'Published', @DemoOwnerId);
        SET @EntryId = SCOPE_IDENTITY();
        INSERT INTO [dbo].[ContentVersion]
            ([ContentEntryId], [VersionNumber], [FieldsJson], [Status], [AuthorId], [ChangeNote])
        VALUES (@EntryId, 1,
            '{"title":"' + STRING_ESCAPE(@Title, 'json') + '","body":"' + STRING_ESCAPE(@Body, 'json') + '"}',
            'Published', @DemoOwnerId, 'Demo seed');
        SET @VersionId = SCOPE_IDENTITY();
        UPDATE [dbo].[ContentEntry] SET [PublishedVersionId] = @VersionId WHERE [Id] = @EntryId;
    END;

    -- Page 3
    SET @Slug  = 'demo/contact';
    SET @Title = 'Contact Us';
    SET @Body  = '## Contact

For questions about benefits or health care, contact your local VA office or call 1-800-827-1000.';

    IF NOT EXISTS (SELECT 1 FROM [dbo].[ContentEntry] WHERE [Slug] = @Slug AND [Locale] = 'en-US')
    BEGIN
        INSERT INTO [dbo].[ContentEntry] ([ContentTypeId], [Slug], [Locale], [Status], [OwnerId])
        VALUES (@StdPageTypeId, @Slug, 'en-US', 'Published', @DemoOwnerId);
        SET @EntryId = SCOPE_IDENTITY();
        INSERT INTO [dbo].[ContentVersion]
            ([ContentEntryId], [VersionNumber], [FieldsJson], [Status], [AuthorId], [ChangeNote])
        VALUES (@EntryId, 1,
            '{"title":"' + STRING_ESCAPE(@Title, 'json') + '","body":"' + STRING_ESCAPE(@Body, 'json') + '"}',
            'Published', @DemoOwnerId, 'Demo seed');
        SET @VersionId = SCOPE_IDENTITY();
        UPDATE [dbo].[ContentEntry] SET [PublishedVersionId] = @VersionId WHERE [Id] = @EntryId;
    END;

    -- Page 4
    SET @Slug  = 'demo/benefits-overview';
    SET @Title = 'Benefits Overview';
    SET @Body  = '## Benefits Overview

VA benefits include compensation, pension, education, housing, and more. This page is a demo standard page.';

    IF NOT EXISTS (SELECT 1 FROM [dbo].[ContentEntry] WHERE [Slug] = @Slug AND [Locale] = 'en-US')
    BEGIN
        INSERT INTO [dbo].[ContentEntry] ([ContentTypeId], [Slug], [Locale], [Status], [OwnerId])
        VALUES (@StdPageTypeId, @Slug, 'en-US', 'Draft', @DemoOwnerId);
        SET @EntryId = SCOPE_IDENTITY();
        INSERT INTO [dbo].[ContentVersion]
            ([ContentEntryId], [VersionNumber], [FieldsJson], [Status], [AuthorId], [ChangeNote])
        VALUES (@EntryId, 1,
            '{"title":"' + STRING_ESCAPE(@Title, 'json') + '","body":"' + STRING_ESCAPE(@Body, 'json') + '"}',
            'Draft', @DemoOwnerId, 'Demo seed');
    END;

    -- Page 5
    SET @Slug  = 'demo/health-care-overview';
    SET @Title = 'Health Care Overview';
    SET @Body  = '## Health Care Overview

VA health care provides a wide range of services. This page is in Draft status as a demo of the editorial workflow.';

    IF NOT EXISTS (SELECT 1 FROM [dbo].[ContentEntry] WHERE [Slug] = @Slug AND [Locale] = 'en-US')
    BEGIN
        INSERT INTO [dbo].[ContentEntry] ([ContentTypeId], [Slug], [Locale], [Status], [OwnerId])
        VALUES (@StdPageTypeId, @Slug, 'en-US', 'InReview', @DemoOwnerId);
        SET @EntryId = SCOPE_IDENTITY();
        INSERT INTO [dbo].[ContentVersion]
            ([ContentEntryId], [VersionNumber], [FieldsJson], [Status], [AuthorId], [ChangeNote])
        VALUES (@EntryId, 1,
            '{"title":"' + STRING_ESCAPE(@Title, 'json') + '","body":"' + STRING_ESCAPE(@Body, 'json') + '"}',
            'InReview', @DemoOwnerId, 'Demo seed');
    END;

    -- -------------------------------------------------------
    -- 5. Seed 3 news articles
    -- -------------------------------------------------------
    -- Article 1
    SET @Slug  = 'demo/news/va-cms-launched';
    SET @Title = 'VA CMS Launched for Internal Teams';
    IF NOT EXISTS (SELECT 1 FROM [dbo].[ContentEntry] WHERE [Slug] = @Slug AND [Locale] = 'en-US')
    BEGIN
        INSERT INTO [dbo].[ContentEntry] ([ContentTypeId], [Slug], [Locale], [Status], [OwnerId])
        VALUES (@NewsTypeId, @Slug, 'en-US', 'Published', @DemoOwnerId);
        SET @EntryId = SCOPE_IDENTITY();
        INSERT INTO [dbo].[ContentVersion]
            ([ContentEntryId], [VersionNumber], [FieldsJson], [Status], [AuthorId], [ChangeNote])
        VALUES (@EntryId, 1,
            '{"title":"VA CMS Launched for Internal Teams","summary":"The new VA CMS is now available.","body":"## VA CMS Launched\n\nThe VA Content Management System is now live for all internal editorial teams."}',
            'Published', @DemoOwnerId, 'Demo seed');
        SET @VersionId = SCOPE_IDENTITY();
        UPDATE [dbo].[ContentEntry] SET [PublishedVersionId] = @VersionId WHERE [Id] = @EntryId;
    END;

    -- Article 2
    SET @Slug  = 'demo/news/benefits-update-2026';
    SET @Title = 'Benefits Update for 2026';
    IF NOT EXISTS (SELECT 1 FROM [dbo].[ContentEntry] WHERE [Slug] = @Slug AND [Locale] = 'en-US')
    BEGIN
        INSERT INTO [dbo].[ContentEntry] ([ContentTypeId], [Slug], [Locale], [Status], [OwnerId])
        VALUES (@NewsTypeId, @Slug, 'en-US', 'Published', @DemoOwnerId);
        SET @EntryId = SCOPE_IDENTITY();
        INSERT INTO [dbo].[ContentVersion]
            ([ContentEntryId], [VersionNumber], [FieldsJson], [Status], [AuthorId], [ChangeNote])
        VALUES (@EntryId, 1,
            '{"title":"Benefits Update for 2026","summary":"Key changes to VA benefits for fiscal year 2026.","body":"## Benefits Update\n\nSeveral benefit programs have been updated for fiscal year 2026. See the details below."}',
            'Published', @DemoOwnerId, 'Demo seed');
        SET @VersionId = SCOPE_IDENTITY();
        UPDATE [dbo].[ContentEntry] SET [PublishedVersionId] = @VersionId WHERE [Id] = @EntryId;
    END;

    -- Article 3
    SET @Slug  = 'demo/news/health-care-expansion';
    SET @Title = 'Health Care Services Expanding to New Locations';
    IF NOT EXISTS (SELECT 1 FROM [dbo].[ContentEntry] WHERE [Slug] = @Slug AND [Locale] = 'en-US')
    BEGIN
        INSERT INTO [dbo].[ContentEntry] ([ContentTypeId], [Slug], [Locale], [Status], [OwnerId])
        VALUES (@NewsTypeId, @Slug, 'en-US', 'Draft', @DemoOwnerId);
        SET @EntryId = SCOPE_IDENTITY();
        INSERT INTO [dbo].[ContentVersion]
            ([ContentEntryId], [VersionNumber], [FieldsJson], [Status], [AuthorId], [ChangeNote])
        VALUES (@EntryId, 1,
            '{"title":"Health Care Services Expanding to New Locations","summary":"New VA clinics are opening across the country.","body":"## New Locations\n\nVA health care is expanding access with new clinic openings in underserved communities."}',
            'Draft', @DemoOwnerId, 'Demo seed');
    END;
END;
GO
