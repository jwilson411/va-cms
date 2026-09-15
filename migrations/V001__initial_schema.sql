-- V001__initial_schema.sql
-- Creates all core tables as defined in DATA_MODEL.md.
-- DbUp runs this exactly once on first startup against a clean database.

-- Required SET options for computed columns, filtered indexes, and FTS
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ============================================================
-- CORE CONTENT TABLES
-- ============================================================

CREATE TABLE [dbo].[ContentType] (
    [Id]              BIGINT IDENTITY(1,1) NOT NULL,
    [Name]            NVARCHAR(100)        NOT NULL,
    [DisplayName]     NVARCHAR(200)        NOT NULL,
    [Description]     NVARCHAR(1000)       NULL,
    [TemplateId]      NVARCHAR(200)        NULL,
    [IsSystemType]    BIT                  NOT NULL CONSTRAINT [DF_ContentType_IsSystemType]    DEFAULT 0,
    [AllowWorkflow]   BIT                  NOT NULL CONSTRAINT [DF_ContentType_AllowWorkflow]   DEFAULT 1,
    [FieldSchemaJson] NVARCHAR(MAX)        NOT NULL CONSTRAINT [DF_ContentType_FieldSchemaJson] DEFAULT '[]',
    [CreatedAt]       DATETIME2            NOT NULL CONSTRAINT [DF_ContentType_CreatedAt]       DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]       DATETIME2            NOT NULL CONSTRAINT [DF_ContentType_UpdatedAt]       DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_ContentType] PRIMARY KEY CLUSTERED ([Id] ASC)
);
CREATE UNIQUE INDEX [UX_ContentType_Name] ON [dbo].[ContentType] ([Name]);
GO

CREATE TABLE [dbo].[ContentEntry] (
    [Id]                 BIGINT IDENTITY(1,1) NOT NULL,
    [ContentTypeId]      BIGINT               NOT NULL,
    [Slug]               NVARCHAR(500)        NOT NULL,
    [Locale]             NVARCHAR(10)         NOT NULL CONSTRAINT [DF_ContentEntry_Locale]             DEFAULT 'en-US',
    [Status]             NVARCHAR(20)         NOT NULL CONSTRAINT [DF_ContentEntry_Status]             DEFAULT 'Draft',
    [PublishedVersionId] BIGINT               NULL,
    [ScheduledPublishAt] DATETIME2            NULL,
    [ScheduledExpireAt]  DATETIME2            NULL,
    [OwnerId]            BIGINT               NOT NULL,
    [CreatedAt]          DATETIME2            NOT NULL CONSTRAINT [DF_ContentEntry_CreatedAt]          DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]          DATETIME2            NOT NULL CONSTRAINT [DF_ContentEntry_UpdatedAt]          DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_ContentEntry] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ContentEntry_ContentType] FOREIGN KEY ([ContentTypeId]) REFERENCES [dbo].[ContentType] ([Id])
);
CREATE UNIQUE INDEX [UX_ContentEntry_Slug_Locale]       ON [dbo].[ContentEntry] ([Slug], [Locale]);
CREATE INDEX        [IX_ContentEntry_Status]             ON [dbo].[ContentEntry] ([Status]);
CREATE INDEX        [IX_ContentEntry_ContentTypeId]      ON [dbo].[ContentEntry] ([ContentTypeId]);
GO

CREATE TABLE [dbo].[ContentVersion] (
    [Id]                 BIGINT IDENTITY(1,1) NOT NULL,
    [ContentEntryId]     BIGINT               NOT NULL,
    [VersionNumber]      INT                  NOT NULL,
    [FieldsJson]         NVARCHAR(MAX)        NOT NULL,
    [RenderedFieldsJson] NVARCHAR(MAX)        NULL,
    [Status]             NVARCHAR(20)         NOT NULL,
    [AuthorId]           BIGINT               NOT NULL,
    [ChangeNote]         NVARCHAR(1000)       NULL,
    [CreatedAt]          DATETIME2            NOT NULL CONSTRAINT [DF_ContentVersion_CreatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_ContentVersion] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ContentVersion_ContentEntry] FOREIGN KEY ([ContentEntryId]) REFERENCES [dbo].[ContentEntry] ([Id])
);
CREATE UNIQUE INDEX [UX_ContentVersion_EntryId_VersionNumber] ON [dbo].[ContentVersion] ([ContentEntryId], [VersionNumber]);
GO

-- Add FK from ContentEntry back to ContentVersion (circular — added after both tables exist)
ALTER TABLE [dbo].[ContentEntry]
    ADD CONSTRAINT [FK_ContentEntry_PublishedVersion]
        FOREIGN KEY ([PublishedVersionId]) REFERENCES [dbo].[ContentVersion] ([Id]);
GO

-- ============================================================
-- MEDIA TABLES
-- ============================================================

CREATE TABLE [dbo].[MediaAsset] (
    [Id]                BIGINT IDENTITY(1,1) NOT NULL,
    [FileName]          NVARCHAR(500)        NOT NULL,
    [StoragePath]       NVARCHAR(2000)       NOT NULL,
    [StorageBackend]    NVARCHAR(50)         NOT NULL,
    [MimeType]          NVARCHAR(200)        NOT NULL,
    [FileSizeBytes]     BIGINT               NOT NULL,
    [AltText]           NVARCHAR(500)        NULL,
    [Title]             NVARCHAR(500)        NULL,
    [Description]       NVARCHAR(2000)       NULL,
    [Width]             INT                  NULL,
    [Height]            INT                  NULL,
    [Tags]              NVARCHAR(MAX)        NULL,
    [UploadedById]      BIGINT               NOT NULL,
    [IsVirusScanPassed] BIT                  NULL,
    [CreatedAt]         DATETIME2            NOT NULL CONSTRAINT [DF_MediaAsset_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]         DATETIME2            NOT NULL CONSTRAINT [DF_MediaAsset_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_MediaAsset] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

CREATE TABLE [dbo].[MediaUsage] (
    [Id]             BIGINT IDENTITY(1,1) NOT NULL,
    [MediaAssetId]   BIGINT               NOT NULL,
    [ContentEntryId] BIGINT               NOT NULL,
    [FieldName]      NVARCHAR(100)        NOT NULL,
    [CreatedAt]      DATETIME2            NOT NULL CONSTRAINT [DF_MediaUsage_CreatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_MediaUsage] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_MediaUsage_MediaAsset]   FOREIGN KEY ([MediaAssetId])   REFERENCES [dbo].[MediaAsset]  ([Id]),
    CONSTRAINT [FK_MediaUsage_ContentEntry] FOREIGN KEY ([ContentEntryId]) REFERENCES [dbo].[ContentEntry] ([Id])
);
GO

-- ============================================================
-- USERS AND ACCESS CONTROL
-- ============================================================

CREATE TABLE [dbo].[User] (
    [Id]          BIGINT IDENTITY(1,1) NOT NULL,
    [ExternalId]  NVARCHAR(200)        NOT NULL,
    [Email]       NVARCHAR(500)        NOT NULL,
    [DisplayName] NVARCHAR(500)        NOT NULL,
    [IsActive]    BIT                  NOT NULL CONSTRAINT [DF_User_IsActive] DEFAULT 1,
    [LastLoginAt] DATETIME2            NULL,
    [CreatedAt]   DATETIME2            NOT NULL CONSTRAINT [DF_User_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]   DATETIME2            NOT NULL CONSTRAINT [DF_User_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_User] PRIMARY KEY CLUSTERED ([Id] ASC)
);
CREATE UNIQUE INDEX [UX_User_Email]      ON [dbo].[User] ([Email]);
CREATE UNIQUE INDEX [UX_User_ExternalId] ON [dbo].[User] ([ExternalId]);
GO

CREATE TABLE [dbo].[Role] (
    [Id]           BIGINT IDENTITY(1,1) NOT NULL,
    [Name]         NVARCHAR(100)        NOT NULL,
    [DisplayName]  NVARCHAR(200)        NOT NULL,
    [IsSystemRole] BIT                  NOT NULL CONSTRAINT [DF_Role_IsSystemRole] DEFAULT 0,
    CONSTRAINT [PK_Role] PRIMARY KEY CLUSTERED ([Id] ASC)
);
CREATE UNIQUE INDEX [UX_Role_Name] ON [dbo].[Role] ([Name]);
GO

CREATE TABLE [dbo].[ContentSection] (
    [Id]              BIGINT IDENTITY(1,1) NOT NULL,
    [Name]            NVARCHAR(200)        NOT NULL,
    [SlugPrefix]      NVARCHAR(500)        NOT NULL,
    [ParentSectionId] BIGINT               NULL,
    CONSTRAINT [PK_ContentSection] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ContentSection_Parent] FOREIGN KEY ([ParentSectionId]) REFERENCES [dbo].[ContentSection] ([Id])
);
GO

CREATE TABLE [dbo].[UserRole] (
    [Id]          BIGINT IDENTITY(1,1) NOT NULL,
    [UserId]      BIGINT               NOT NULL,
    [RoleId]      BIGINT               NOT NULL,
    [SectionId]   BIGINT               NULL,
    [GrantedById] BIGINT               NOT NULL,
    [CreatedAt]   DATETIME2            NOT NULL CONSTRAINT [DF_UserRole_CreatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_UserRole] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_UserRole_User]           FOREIGN KEY ([UserId])      REFERENCES [dbo].[User]           ([Id]),
    CONSTRAINT [FK_UserRole_Role]           FOREIGN KEY ([RoleId])      REFERENCES [dbo].[Role]           ([Id]),
    CONSTRAINT [FK_UserRole_Section]        FOREIGN KEY ([SectionId])   REFERENCES [dbo].[ContentSection] ([Id]),
    CONSTRAINT [FK_UserRole_GrantedByUser]  FOREIGN KEY ([GrantedById]) REFERENCES [dbo].[User]           ([Id])
);
GO

-- ============================================================
-- NAVIGATION
-- ============================================================

CREATE TABLE [dbo].[NavigationMenu] (
    [Id]        BIGINT IDENTITY(1,1) NOT NULL,
    [Name]      NVARCHAR(200)        NOT NULL,
    [Handle]    NVARCHAR(100)        NOT NULL,
    [CreatedAt] DATETIME2            NOT NULL CONSTRAINT [DF_NavigationMenu_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [UpdatedAt] DATETIME2            NOT NULL CONSTRAINT [DF_NavigationMenu_UpdatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_NavigationMenu] PRIMARY KEY CLUSTERED ([Id] ASC)
);
CREATE UNIQUE INDEX [UX_NavigationMenu_Handle] ON [dbo].[NavigationMenu] ([Handle]);
GO

CREATE TABLE [dbo].[NavigationItem] (
    [Id]             BIGINT IDENTITY(1,1) NOT NULL,
    [MenuId]         BIGINT               NOT NULL,
    [ParentItemId]   BIGINT               NULL,
    [Label]          NVARCHAR(500)        NOT NULL,
    [Url]            NVARCHAR(2000)       NULL,
    [ContentEntryId] BIGINT               NULL,
    [Target]         NVARCHAR(10)         NOT NULL CONSTRAINT [DF_NavigationItem_Target]    DEFAULT '_self',
    [SortOrder]      INT                  NOT NULL CONSTRAINT [DF_NavigationItem_SortOrder] DEFAULT 0,
    [IsVisible]      BIT                  NOT NULL CONSTRAINT [DF_NavigationItem_IsVisible] DEFAULT 1,
    CONSTRAINT [PK_NavigationItem] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_NavigationItem_Menu]         FOREIGN KEY ([MenuId])         REFERENCES [dbo].[NavigationMenu]  ([Id]),
    CONSTRAINT [FK_NavigationItem_Parent]       FOREIGN KEY ([ParentItemId])   REFERENCES [dbo].[NavigationItem]  ([Id]),
    CONSTRAINT [FK_NavigationItem_ContentEntry] FOREIGN KEY ([ContentEntryId]) REFERENCES [dbo].[ContentEntry]    ([Id])
);
GO

CREATE TABLE [dbo].[Redirect] (
    [Id]          BIGINT IDENTITY(1,1) NOT NULL,
    [FromPath]    NVARCHAR(2000)       NOT NULL,
    [ToPath]      NVARCHAR(2000)       NOT NULL,
    [StatusCode]  INT                  NOT NULL CONSTRAINT [DF_Redirect_StatusCode] DEFAULT 301,
    [IsActive]    BIT                  NOT NULL CONSTRAINT [DF_Redirect_IsActive]   DEFAULT 1,
    [CreatedById] BIGINT               NOT NULL,
    [CreatedAt]   DATETIME2            NOT NULL CONSTRAINT [DF_Redirect_CreatedAt]  DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Redirect] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Redirect_CreatedBy] FOREIGN KEY ([CreatedById]) REFERENCES [dbo].[User] ([Id])
);
GO

-- ============================================================
-- TAXONOMY
-- ============================================================

CREATE TABLE [dbo].[Taxonomy] (
    [Id]            BIGINT IDENTITY(1,1) NOT NULL,
    [Name]          NVARCHAR(200)        NOT NULL,
    [Handle]        NVARCHAR(100)        NOT NULL,
    [IsHierarchical] BIT                 NOT NULL CONSTRAINT [DF_Taxonomy_IsHierarchical] DEFAULT 0,
    CONSTRAINT [PK_Taxonomy] PRIMARY KEY CLUSTERED ([Id] ASC)
);
CREATE UNIQUE INDEX [UX_Taxonomy_Handle] ON [dbo].[Taxonomy] ([Handle]);
GO

CREATE TABLE [dbo].[TaxonomyTerm] (
    [Id]           BIGINT IDENTITY(1,1) NOT NULL,
    [TaxonomyId]   BIGINT               NOT NULL,
    [ParentTermId] BIGINT               NULL,
    [Name]         NVARCHAR(500)        NOT NULL,
    [Slug]         NVARCHAR(500)        NOT NULL,
    [SortOrder]    INT                  NOT NULL CONSTRAINT [DF_TaxonomyTerm_SortOrder] DEFAULT 0,
    CONSTRAINT [PK_TaxonomyTerm] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_TaxonomyTerm_Taxonomy]  FOREIGN KEY ([TaxonomyId])   REFERENCES [dbo].[Taxonomy]     ([Id]),
    CONSTRAINT [FK_TaxonomyTerm_ParentTerm] FOREIGN KEY ([ParentTermId]) REFERENCES [dbo].[TaxonomyTerm] ([Id])
);
GO

CREATE TABLE [dbo].[ContentEntryTerm] (
    [ContentEntryId] BIGINT NOT NULL,
    [TaxonomyTermId] BIGINT NOT NULL,
    CONSTRAINT [PK_ContentEntryTerm] PRIMARY KEY CLUSTERED ([ContentEntryId], [TaxonomyTermId]),
    CONSTRAINT [FK_ContentEntryTerm_ContentEntry] FOREIGN KEY ([ContentEntryId]) REFERENCES [dbo].[ContentEntry]  ([Id]),
    CONSTRAINT [FK_ContentEntryTerm_TaxonomyTerm] FOREIGN KEY ([TaxonomyTermId]) REFERENCES [dbo].[TaxonomyTerm]  ([Id])
);
GO

-- ============================================================
-- WORKFLOW AND AUDIT
-- ============================================================

CREATE TABLE [dbo].[WorkflowTransition] (
    [Id]               BIGINT IDENTITY(1,1) NOT NULL,
    [ContentEntryId]   BIGINT               NOT NULL,
    [ContentVersionId] BIGINT               NOT NULL,
    [FromStatus]       NVARCHAR(20)         NOT NULL,
    [ToStatus]         NVARCHAR(20)         NOT NULL,
    [ActorId]          BIGINT               NOT NULL,
    [Comment]          NVARCHAR(2000)       NULL,
    [CreatedAt]        DATETIME2            NOT NULL CONSTRAINT [DF_WorkflowTransition_CreatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_WorkflowTransition] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_WorkflowTransition_ContentEntry]   FOREIGN KEY ([ContentEntryId])   REFERENCES [dbo].[ContentEntry]   ([Id]),
    CONSTRAINT [FK_WorkflowTransition_ContentVersion] FOREIGN KEY ([ContentVersionId]) REFERENCES [dbo].[ContentVersion]  ([Id]),
    CONSTRAINT [FK_WorkflowTransition_Actor]          FOREIGN KEY ([ActorId])          REFERENCES [dbo].[User]            ([Id])
);
GO

CREATE TABLE [dbo].[AuditLog] (
    [Id]         BIGINT IDENTITY(1,1) NOT NULL,
    [ActorId]    BIGINT               NULL,
    [ActorEmail] NVARCHAR(500)        NULL,
    [EntityType] NVARCHAR(100)        NOT NULL,
    [EntityId]   NVARCHAR(100)        NOT NULL,
    [Action]     NVARCHAR(50)         NOT NULL,
    [DiffJson]   NVARCHAR(MAX)        NULL,
    [IpAddress]  NVARCHAR(50)         NULL,
    [UserAgent]  NVARCHAR(500)        NULL,
    [CreatedAt]  DATETIME2            NOT NULL CONSTRAINT [DF_AuditLog_CreatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_AuditLog] PRIMARY KEY NONCLUSTERED ([Id] ASC)
);
-- Clustered on CreatedAt for date-range query performance (see DATABASE_LAYER.md §3.1)
CREATE CLUSTERED INDEX [CX_AuditLog_CreatedAt] ON [dbo].[AuditLog] ([CreatedAt] DESC);
GO

-- ============================================================
-- WEBHOOKS
-- ============================================================

CREATE TABLE [dbo].[Webhook] (
    [Id]          BIGINT IDENTITY(1,1) NOT NULL,
    [Name]        NVARCHAR(200)        NOT NULL,
    [Url]         NVARCHAR(2000)       NOT NULL,
    [Secret]      NVARCHAR(500)        NOT NULL,
    [EventsJson]  NVARCHAR(MAX)        NOT NULL CONSTRAINT [DF_Webhook_EventsJson] DEFAULT '[]',
    [IsActive]    BIT                  NOT NULL CONSTRAINT [DF_Webhook_IsActive]   DEFAULT 1,
    [CreatedById] BIGINT               NOT NULL,
    [CreatedAt]   DATETIME2            NOT NULL CONSTRAINT [DF_Webhook_CreatedAt]  DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Webhook] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Webhook_CreatedBy] FOREIGN KEY ([CreatedById]) REFERENCES [dbo].[User] ([Id])
);
GO

CREATE TABLE [dbo].[WebhookDelivery] (
    [Id]                 BIGINT IDENTITY(1,1) NOT NULL,
    [WebhookId]          BIGINT               NOT NULL,
    [EventName]          NVARCHAR(100)        NOT NULL,
    [PayloadJson]        NVARCHAR(MAX)        NOT NULL,
    [ResponseStatusCode] INT                  NULL,
    [AttemptNumber]      INT                  NOT NULL CONSTRAINT [DF_WebhookDelivery_AttemptNumber] DEFAULT 1,
    [DeliveredAt]        DATETIME2            NOT NULL CONSTRAINT [DF_WebhookDelivery_DeliveredAt]   DEFAULT SYSUTCDATETIME(),
    [ErrorMessage]       NVARCHAR(2000)       NULL,
    CONSTRAINT [PK_WebhookDelivery] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_WebhookDelivery_Webhook] FOREIGN KEY ([WebhookId]) REFERENCES [dbo].[Webhook] ([Id])
);
GO

-- ============================================================
-- SEED: built-in roles
-- ============================================================

INSERT INTO [dbo].[Role] ([Name], [DisplayName], [IsSystemRole])
VALUES
    ('SuperAdmin',    'Super Administrator', 1),
    ('Admin',         'Administrator',       1),
    ('Editor',        'Editor',              1),
    ('ContentOwner',  'Content Owner',       1),
    ('Reviewer',      'Reviewer',            1),
    ('ReadOnly',      'Read Only',           1);
GO
