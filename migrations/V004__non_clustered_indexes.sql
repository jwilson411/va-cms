-- V004__non_clustered_indexes.sql
-- All non-clustered covering indexes as specified in DATABASE_LAYER.md §3.2.
-- Separated from initial schema so they can be dropped/rebuilt independently
-- for maintenance without touching the table definitions.

-- Required SET options for filtered indexes and indexes on computed columns
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ContentEntry: most common filter axes
CREATE INDEX [IX_ContentEntry_Status_UpdatedAt]
    ON [dbo].[ContentEntry] ([Status], [UpdatedAt] DESC)
    INCLUDE ([ContentTypeId], [OwnerId], [Slug], [Locale]);

CREATE INDEX [IX_ContentEntry_ContentTypeId_Status]
    ON [dbo].[ContentEntry] ([ContentTypeId], [Status])
    INCLUDE ([Slug], [UpdatedAt]);

CREATE INDEX [IX_ContentEntry_OwnerId]
    ON [dbo].[ContentEntry] ([OwnerId]);

CREATE INDEX [IX_ContentEntry_ScheduledPublishAt]
    ON [dbo].[ContentEntry] ([ScheduledPublishAt])
    WHERE [ScheduledPublishAt] IS NOT NULL AND [Status] = 'Approved';

CREATE INDEX [IX_ContentEntry_ScheduledExpireAt]
    ON [dbo].[ContentEntry] ([ScheduledExpireAt])
    WHERE [ScheduledExpireAt] IS NOT NULL AND [Status] = 'Published';
GO

-- ContentVersion: version history lookups
CREATE INDEX [IX_ContentVersion_EntryId_VersionNumber]
    ON [dbo].[ContentVersion] ([ContentEntryId], [VersionNumber] DESC)
    INCLUDE ([AuthorId], [Status], [CreatedAt]);
GO

-- MediaAsset: library browser filters
CREATE INDEX [IX_MediaAsset_MimeType_CreatedAt]
    ON [dbo].[MediaAsset] ([MimeType], [CreatedAt] DESC);

CREATE INDEX [IX_MediaAsset_UploadedById]
    ON [dbo].[MediaAsset] ([UploadedById]);
GO

-- MediaUsage: "which entries use this asset?" lookup
CREATE INDEX [IX_MediaUsage_AssetId]
    ON [dbo].[MediaUsage] ([MediaAssetId])
    INCLUDE ([ContentEntryId]);
GO

-- User: login by external ID (AAD Object ID / UPN)
CREATE INDEX [IX_User_ExternalId]
    ON [dbo].[User] ([ExternalId]);
GO

-- AuditLog: filter by actor and entity
CREATE INDEX [IX_AuditLog_ActorId_CreatedAt]
    ON [dbo].[AuditLog] ([ActorId], [CreatedAt] DESC);

CREATE INDEX [IX_AuditLog_EntityType_EntityId]
    ON [dbo].[AuditLog] ([EntityType], [EntityId])
    INCLUDE ([ActorId], [Action], [CreatedAt]);
GO

-- WorkflowTransition: history for a content entry
CREATE INDEX [IX_WorkflowTransition_EntryId_CreatedAt]
    ON [dbo].[WorkflowTransition] ([ContentEntryId], [CreatedAt] DESC);
GO

-- NavigationItem: menu tree traversal
CREATE INDEX [IX_NavigationItem_MenuId_ParentItemId]
    ON [dbo].[NavigationItem] ([MenuId], [ParentItemId])
    INCLUDE ([Label], [Url], [SortOrder], [IsVisible]);
GO

-- TaxonomyTerm: hierarchy + taxonomy filter
CREATE INDEX [IX_TaxonomyTerm_TaxonomyId_ParentTermId]
    ON [dbo].[TaxonomyTerm] ([TaxonomyId], [ParentTermId]);
GO

-- ContentEntryTerm: "all entries for this term"
CREATE INDEX [IX_ContentEntryTerm_TermId]
    ON [dbo].[ContentEntryTerm] ([TaxonomyTermId])
    INCLUDE ([ContentEntryId]);
GO

-- Webhook: active webhooks by event (delivery fan-out)
CREATE INDEX [IX_Webhook_IsActive]
    ON [dbo].[Webhook] ([IsActive])
    WHERE [IsActive] = 1;
GO

-- WebhookDelivery: retry queue
CREATE INDEX [IX_WebhookDelivery_WebhookId_AttemptNumber]
    ON [dbo].[WebhookDelivery] ([WebhookId], [AttemptNumber])
    INCLUDE ([EventName], [DeliveredAt], [ResponseStatusCode]);
GO

-- Redirect: fast lookup on incoming path
CREATE INDEX [IX_Redirect_FromPath_IsActive]
    ON [dbo].[Redirect] ([FromPath])
    WHERE [IsActive] = 1;
GO
