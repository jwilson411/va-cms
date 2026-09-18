-- V046__webhook_hardening.sql
-- Issue #168 (epic #152): webhook SSRF/egress controls, secret at rest, delivery log.
--
--   * [Webhook].[Secret] widens to NVARCHAR(1000): the API now stores the HMAC secret
--     as an ASP.NET Data Protection payload ("dp1:<base64>") instead of clear text.
--     Existing clear-text rows are re-keyed by the API on its next start
--     (WebhookSecretRekeyService) through usp_Webhook_ListSecretsForRekey /
--     usp_Webhook_UpdateSecret.
--   * vacms_readonly can no longer SELECT the Secret column; dbo.vw_Webhook is the
--     reporting projection without it.
--   * [WebhookDelivery].[RedeliveryOfId] records an operator-triggered redelivery of a
--     previous attempt; usp_WebhookDelivery_ListByWebhook / _GetById back the admin
--     delivery log.

-- ── Secret column ────────────────────────────────────────────────────────────
ALTER TABLE [dbo].[Webhook] ALTER COLUMN [Secret] NVARCHAR(1000) NOT NULL;
GO

-- ── Reporting projection without the secret ──────────────────────────────────
CREATE OR ALTER VIEW [dbo].[vw_Webhook]
AS
SELECT [Id], [Name], [Url], [EventsJson], [IsActive], [CreatedById], [CreatedAt]
FROM   [dbo].[Webhook];
GO

-- Column-level DENY beats a view alone: SELECT * on the base table now fails for the
-- reporting login, so the secret cannot be harvested through any report. Guarded the
-- same way as V003 so a database without the login still migrates; provision-logins.sql
-- applies the identical DENY when the login is created later.
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE [name] = N'vacms_readonly')
BEGIN
    DENY SELECT ON OBJECT::[dbo].[Webhook] ([Secret]) TO [vacms_readonly];
END
ELSE
    PRINT N'V046: login vacms_readonly not provisioned — Secret column DENY will be applied by infra/sql/provision-logins.sql.';
GO

-- ── Delivery log: redelivery lineage ─────────────────────────────────────────
IF COL_LENGTH('dbo.WebhookDelivery', 'RedeliveryOfId') IS NULL
BEGIN
    ALTER TABLE [dbo].[WebhookDelivery] ADD [RedeliveryOfId] BIGINT NULL
        CONSTRAINT [FK_WebhookDelivery_RedeliveryOf] FOREIGN KEY REFERENCES [dbo].[WebhookDelivery] ([Id]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_WebhookDelivery_Webhook_DeliveredAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_WebhookDelivery_Webhook_DeliveredAt]
        ON [dbo].[WebhookDelivery] ([WebhookId], [DeliveredAt] DESC);
END;
GO

-- ── usp_WebhookDelivery_Create (adds @RedeliveryOfId) ────────────────────────
CREATE OR ALTER PROCEDURE [dbo].[usp_WebhookDelivery_Create]
    @WebhookId          BIGINT,
    @EventName          NVARCHAR(100),
    @PayloadJson        NVARCHAR(MAX),
    @ResponseStatusCode INT = NULL,
    @AttemptNumber      INT = 1,
    @ErrorMessage       NVARCHAR(2000) = NULL,
    @RedeliveryOfId     BIGINT = NULL,
    @NewId              BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [WebhookDelivery]
        ([WebhookId], [EventName], [PayloadJson], [ResponseStatusCode],
         [AttemptNumber], [DeliveredAt], [ErrorMessage], [RedeliveryOfId])
    VALUES
        (@WebhookId, @EventName, @PayloadJson, @ResponseStatusCode,
         @AttemptNumber, SYSUTCDATETIME(), @ErrorMessage, @RedeliveryOfId);
    SET @NewId = SCOPE_IDENTITY();
END;
GO

-- ── usp_WebhookDelivery_ListByWebhook ────────────────────────────────────────
-- Newest first. Payload is returned so an operator can inspect what was sent and
-- redeliver it; @TotalRows drives paging in the admin UI.
CREATE OR ALTER PROCEDURE [dbo].[usp_WebhookDelivery_ListByWebhook]
    @WebhookId BIGINT,
    @Page      INT = 1,
    @PageSize  INT = 25,
    @TotalRows INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Page < 1 SET @Page = 1;
    IF @PageSize < 1 SET @PageSize = 25;
    IF @PageSize > 200 SET @PageSize = 200;

    SELECT @TotalRows = COUNT(1) FROM [WebhookDelivery] WHERE [WebhookId] = @WebhookId;

    SELECT [Id], [WebhookId], [EventName], [PayloadJson], [ResponseStatusCode],
           [AttemptNumber], [DeliveredAt], [ErrorMessage], [RedeliveryOfId]
    FROM   [WebhookDelivery]
    WHERE  [WebhookId] = @WebhookId
    ORDER  BY [DeliveredAt] DESC, [Id] DESC
    OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
GO

-- ── usp_WebhookDelivery_GetById ──────────────────────────────────────────────
CREATE OR ALTER PROCEDURE [dbo].[usp_WebhookDelivery_GetById]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], [WebhookId], [EventName], [PayloadJson], [ResponseStatusCode],
           [AttemptNumber], [DeliveredAt], [ErrorMessage], [RedeliveryOfId]
    FROM   [WebhookDelivery]
    WHERE  [Id] = @Id;
END;
GO

-- ── Secret re-keying (clear text → Data Protection payload) ──────────────────
CREATE OR ALTER PROCEDURE [dbo].[usp_Webhook_ListSecretsForRekey]
AS
BEGIN
    SET NOCOUNT ON;
    -- Every row, active or not: an inactive webhook's secret is still a secret.
    SELECT [Id], [Secret]
    FROM   [Webhook]
    WHERE  [Secret] NOT LIKE 'dp1:%';
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Webhook_UpdateSecret]
    @Id     BIGINT,
    @Secret NVARCHAR(1000)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [Webhook] SET [Secret] = @Secret WHERE [Id] = @Id;
END;
GO
