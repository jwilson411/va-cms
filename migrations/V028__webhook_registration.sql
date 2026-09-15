-- V028__webhook_registration.sql
-- Issue #54: Implement webhook registration, delivery, and HMAC signing (BRD FR-DEV-07).
-- The Webhook and WebhookDelivery tables are already defined in V001.
-- This migration adds the stored procedures for webhook CRUD and delivery.

-- ── usp_Webhook_Create ────────────────────────────────────────────────────────
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
    INSERT INTO [Webhook] ([Name], [Url], [Secret], [EventsJson], [IsActive], [CreatedById], [CreatedAt])
    VALUES (@Name, @Url, @Secret, @EventsJson, 1, @CreatedById, SYSUTCDATETIME());
    SET @NewId = SCOPE_IDENTITY();
END;
GO

-- ── usp_Webhook_List ─────────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE [dbo].[usp_Webhook_List]
    @CreatedById BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], [Name], [Url], [EventsJson], [IsActive], [CreatedById], [CreatedAt]
    FROM   [Webhook]
    -- Never return the secret in list — callers use the Id to manage
    WHERE  (@CreatedById IS NULL OR [CreatedById] = @CreatedById)
    ORDER  BY [CreatedAt] DESC;
END;
GO

-- ── usp_Webhook_GetById ──────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE [dbo].[usp_Webhook_GetById]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], [Name], [Url], [Secret], [EventsJson], [IsActive], [CreatedById], [CreatedAt]
    FROM   [Webhook]
    WHERE  [Id] = @Id;
END;
GO

-- ── usp_Webhook_Delete ───────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE [dbo].[usp_Webhook_Delete]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    -- Soft-delete: mark inactive rather than physical delete to preserve delivery history
    UPDATE [Webhook]
    SET    [IsActive] = 0
    WHERE  [Id] = @Id;
END;
GO

-- ── usp_Webhook_GetActiveForEvent already defined in V008__stored_procedures.sql ──
-- Ensure it exists via CREATE OR ALTER (idempotent)
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

-- ── usp_WebhookDelivery_Create already defined in V008__stored_procedures.sql ──
-- Ensure it exists via CREATE OR ALTER (idempotent)
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
