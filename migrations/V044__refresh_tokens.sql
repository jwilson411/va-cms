-- V044__refresh_tokens.sql
-- Issue #163 (epic #152) — DB-backed refresh tokens with rotation, replay
-- detection and per-user revocation (BRD FR-SECURITY-02).
--
--   * [RefreshToken]: one row per issued token; only the SHA-256 hash is stored.
--     Rotation links the old row to its replacement (ReplacedById) and every token
--     of one login shares a FamilyId so a replayed (already-rotated) token can
--     revoke the whole chain.
--   * [User].[SessionVersion]: bumped by usp_RefreshToken_RevokeAllForUser. The JWT
--     carries it as the "sv" claim; the API rejects access tokens whose version is
--     behind the row, so deactivation and role changes cut off open sessions
--     without waiting for the access token to expire.
--   * usp_RefreshToken_* are the only way the EXECUTE-only vacms_app login touches
--     the table. usp_Maint_PurgeRefreshTokens runs from the nightly Agent job
--     (infra/sql-agent-jobs/job_Maint_PurgeRefreshTokens.sql).
--
-- Session limits are site settings (#164): auth.refreshTokenHours (per-token
-- lifetime), auth.idleTimeoutMinutes (max gap between uses) and
-- auth.absoluteSessionHours (hard cap from the original login). The API passes
-- the resolved values in; the SPs enforce them.

-- ============================================================
-- Schema
-- ============================================================

IF COL_LENGTH('dbo.User', 'SessionVersion') IS NULL
BEGIN
    ALTER TABLE [dbo].[User]
        ADD [SessionVersion] INT NOT NULL CONSTRAINT [DF_User_SessionVersion] DEFAULT 0;
END;
GO

IF OBJECT_ID('dbo.RefreshToken', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RefreshToken] (
        [Id]                BIGINT IDENTITY(1,1) NOT NULL,
        [UserId]            BIGINT               NOT NULL,
        [TokenHash]         BINARY(32)           NOT NULL,   -- SHA-256 of the opaque cookie value
        [FamilyId]          UNIQUEIDENTIFIER     NOT NULL,   -- one login = one family; rotation keeps it
        [IssuedAt]          DATETIME2            NOT NULL CONSTRAINT [DF_RefreshToken_IssuedAt] DEFAULT SYSUTCDATETIME(),
        [ExpiresAt]         DATETIME2            NOT NULL,   -- per-token lifetime (auth.refreshTokenHours)
        [AbsoluteExpiresAt] DATETIME2            NOT NULL,   -- login + auth.absoluteSessionHours; copied on rotation
        [LastUsedAt]        DATETIME2            NOT NULL CONSTRAINT [DF_RefreshToken_LastUsedAt] DEFAULT SYSUTCDATETIME(),
        [RevokedAt]         DATETIME2            NULL,
        [RevokedReason]     NVARCHAR(50)         NULL,       -- Rotated | Logout | Replay | Deactivated | RoleChange | Admin
        [ReplacedById]      BIGINT               NULL,
        [CreatedByIp]       NVARCHAR(50)         NULL,
        [UserAgent]         NVARCHAR(500)        NULL,
        [GroupsJson]        NVARCHAR(MAX)        NULL,       -- AD groups observed at login (#153)
        CONSTRAINT [PK_RefreshToken]           PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_RefreshToken_User]      FOREIGN KEY ([UserId]) REFERENCES [dbo].[User] ([Id]),
        CONSTRAINT [FK_RefreshToken_Replaced]  FOREIGN KEY ([ReplacedById]) REFERENCES [dbo].[RefreshToken] ([Id])
    );
    CREATE UNIQUE INDEX [UX_RefreshToken_TokenHash] ON [dbo].[RefreshToken] ([TokenHash]);
    CREATE INDEX [IX_RefreshToken_User_Live]  ON [dbo].[RefreshToken] ([UserId]) WHERE [RevokedAt] IS NULL;
    CREATE INDEX [IX_RefreshToken_Family]     ON [dbo].[RefreshToken] ([FamilyId]);
    CREATE INDEX [IX_RefreshToken_ExpiresAt]  ON [dbo].[RefreshToken] ([ExpiresAt]);
END;
GO

-- ============================================================
-- usp_RefreshToken_Issue — new family at login
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_RefreshToken_Issue]
    @UserId            BIGINT,
    @TokenHash         BINARY(32),
    @ExpiresAt         DATETIME2,
    @AbsoluteExpiresAt DATETIME2,
    @CreatedByIp       NVARCHAR(50)  = NULL,
    @UserAgent         NVARCHAR(500) = NULL,
    @GroupsJson        NVARCHAR(MAX) = NULL,
    @NewId             BIGINT        OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[RefreshToken]
        ([UserId], [TokenHash], [FamilyId], [IssuedAt], [ExpiresAt], [AbsoluteExpiresAt], [LastUsedAt],
         [CreatedByIp], [UserAgent], [GroupsJson])
    VALUES
        (@UserId, @TokenHash, NEWID(), SYSUTCDATETIME(), @ExpiresAt, @AbsoluteExpiresAt, SYSUTCDATETIME(),
         @CreatedByIp, @UserAgent, @GroupsJson);
    SET @NewId = SCOPE_IDENTITY();
END;
GO

-- ============================================================
-- usp_RefreshToken_Validate
-- Resolves a presented token. Returns one row with [Status]:
--   Ok       — usable; the caller rotates it
--   Replay   — token was already revoked (rotated, logged out, …): the whole
--              family is revoked here and the event audited (replay detection)
--   Expired  — past ExpiresAt or AbsoluteExpiresAt
--   Idle     — unused for longer than @IdleMinutes
-- No row when the hash is unknown.
-- A token rotated less than @RotationGraceSeconds ago is still 'Ok' so two
-- tabs refreshing at the same moment do not lock each other out.
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_RefreshToken_Validate]
    @TokenHash            BINARY(32),
    @IdleMinutes          INT,
    @RotationGraceSeconds INT           = 30,
    @SourceIp             NVARCHAR(50)  = NULL,
    @UserAgent            NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Id BIGINT, @UserId BIGINT, @FamilyId UNIQUEIDENTIFIER, @GroupsJson NVARCHAR(MAX),
            @ExpiresAt DATETIME2, @AbsoluteExpiresAt DATETIME2, @LastUsedAt DATETIME2,
            @RevokedAt DATETIME2, @RevokedReason NVARCHAR(50);
    DECLARE @Now DATETIME2 = SYSUTCDATETIME();

    SELECT @Id = [Id], @UserId = [UserId], @FamilyId = [FamilyId], @GroupsJson = [GroupsJson],
           @ExpiresAt = [ExpiresAt], @AbsoluteExpiresAt = [AbsoluteExpiresAt], @LastUsedAt = [LastUsedAt],
           @RevokedAt = [RevokedAt], @RevokedReason = [RevokedReason]
    FROM   [dbo].[RefreshToken]
    WHERE  [TokenHash] = @TokenHash;

    IF @Id IS NULL
        RETURN;

    DECLARE @Status NVARCHAR(20) = 'Ok';

    IF @RevokedAt IS NOT NULL
       AND NOT (@RevokedReason = 'Rotated' AND DATEDIFF(SECOND, @RevokedAt, @Now) <= @RotationGraceSeconds)
    BEGIN
        SET @Status = 'Replay';

        BEGIN TRANSACTION;
        UPDATE [dbo].[RefreshToken]
        SET    [RevokedAt] = @Now, [RevokedReason] = 'Replay'
        WHERE  [FamilyId] = @FamilyId AND [RevokedAt] IS NULL;

        DECLARE @Diff NVARCHAR(MAX) = (SELECT @FamilyId AS familyId, @RevokedReason AS priorReason FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        EXEC [dbo].[usp_AuditLog_Write]
            @ActorId = @UserId, @EntityType = 'Session', @EntityId = @Id, @Action = 'RefreshReplay',
            @DiffJson = @Diff, @Outcome = 'Failure', @SourceIp = @SourceIp, @UserAgent = @UserAgent;
        COMMIT TRANSACTION;
    END
    ELSE IF @Now >= @ExpiresAt OR @Now >= @AbsoluteExpiresAt
        SET @Status = 'Expired';
    ELSE IF DATEDIFF(MINUTE, @LastUsedAt, @Now) > @IdleMinutes
        SET @Status = 'Idle';

    SELECT @Status AS [Status], @Id AS [Id], @UserId AS [UserId], @FamilyId AS [FamilyId],
           @GroupsJson AS [GroupsJson], @AbsoluteExpiresAt AS [AbsoluteExpiresAt];
END;
GO

-- ============================================================
-- usp_RefreshToken_Rotate — revoke @OldId, issue its replacement in one transaction
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_RefreshToken_Rotate]
    @OldId        BIGINT,
    @NewTokenHash BINARY(32),
    @ExpiresAt    DATETIME2,
    @CreatedByIp  NVARCHAR(50)  = NULL,
    @UserAgent    NVARCHAR(500) = NULL,
    @NewId        BIGINT        OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Now DATETIME2 = SYSUTCDATETIME();

    BEGIN TRANSACTION;

    INSERT INTO [dbo].[RefreshToken]
        ([UserId], [TokenHash], [FamilyId], [IssuedAt], [ExpiresAt], [AbsoluteExpiresAt], [LastUsedAt],
         [CreatedByIp], [UserAgent], [GroupsJson])
    SELECT [UserId], @NewTokenHash, [FamilyId], @Now,
           CASE WHEN @ExpiresAt < [AbsoluteExpiresAt] THEN @ExpiresAt ELSE [AbsoluteExpiresAt] END,
           [AbsoluteExpiresAt], @Now, @CreatedByIp, @UserAgent, [GroupsJson]
    FROM   [dbo].[RefreshToken]
    WHERE  [Id] = @OldId;

    SET @NewId = SCOPE_IDENTITY();

    UPDATE [dbo].[RefreshToken]
    SET    [RevokedAt]     = COALESCE([RevokedAt], @Now),
           [RevokedReason] = COALESCE([RevokedReason], 'Rotated'),
           [ReplacedById]  = @NewId,
           [LastUsedAt]    = @Now
    WHERE  [Id] = @OldId;

    COMMIT TRANSACTION;
END;
GO

-- ============================================================
-- usp_RefreshToken_Revoke — single token (logout, disabled account)
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_RefreshToken_Revoke]
    @TokenHash BINARY(32),
    @Reason    NVARCHAR(50) = 'Logout'
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [dbo].[RefreshToken]
    SET    [RevokedAt] = SYSUTCDATETIME(), [RevokedReason] = @Reason
    WHERE  [TokenHash] = @TokenHash AND [RevokedAt] IS NULL;
END;
GO

-- ============================================================
-- usp_RefreshToken_RevokeAllForUser — "sign out everywhere"
-- Revokes every live refresh token and bumps SessionVersion so access tokens
-- minted before this call are rejected too. Audited as Session/SessionsRevoked.
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_RefreshToken_RevokeAllForUser]
    @UserId  BIGINT,
    @ActorId BIGINT       = NULL,
    @Reason  NVARCHAR(50) = 'Admin'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    UPDATE [dbo].[RefreshToken]
    SET    [RevokedAt] = SYSUTCDATETIME(), [RevokedReason] = @Reason
    WHERE  [UserId] = @UserId AND [RevokedAt] IS NULL;
    DECLARE @Revoked INT = @@ROWCOUNT;

    UPDATE [dbo].[User]
    SET    [SessionVersion] = [SessionVersion] + 1, [UpdatedAt] = SYSUTCDATETIME()
    WHERE  [Id] = @UserId;

    DECLARE @Diff NVARCHAR(MAX) = (SELECT @Reason AS reason, @Revoked AS refreshTokensRevoked FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    EXEC [dbo].[usp_AuditLog_Write]
        @ActorId = @ActorId, @EntityType = 'Session', @EntityId = @UserId,
        @Action = 'SessionsRevoked', @DiffJson = @Diff;

    COMMIT TRANSACTION;
END;
GO

-- ============================================================
-- usp_User_Deactivate — now also ends every open session (#163)
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_User_Deactivate]
    @Id      BIGINT,
    @ActorId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    UPDATE [dbo].[User] SET [IsActive] = 0, [UpdatedAt] = SYSUTCDATETIME() WHERE [Id] = @Id;
    EXEC [dbo].[usp_AuditLog_Write] @ActorId, 'User', @Id, 'Deactivate', NULL;
    EXEC [dbo].[usp_RefreshToken_RevokeAllForUser] @UserId = @Id, @ActorId = @ActorId, @Reason = 'Deactivated';
    COMMIT TRANSACTION;
END;
GO

-- ============================================================
-- usp_Maint_PurgeRefreshTokens — nightly Agent job (§6.8)
-- Deletes revoked/expired rows older than @RetentionDays. Live rows are never
-- touched; the audit log already holds the security-relevant events.
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_Maint_PurgeRefreshTokens]
    @RetentionDays INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Cutoff DATETIME2 = DATEADD(DAY, -@RetentionDays, SYSUTCDATETIME());
    DECLARE @BatchSize INT = 5000;
    DECLARE @Deleted INT = 1;

    WHILE @Deleted > 0
    BEGIN
        -- Children first: ReplacedById points at the newer row, so clear the link
        -- on anything we are about to delete before removing it.
        UPDATE [dbo].[RefreshToken]
        SET    [ReplacedById] = NULL
        WHERE  [ReplacedById] IN (
            SELECT TOP (@BatchSize) [Id] FROM [dbo].[RefreshToken]
            WHERE  ([RevokedAt] < @Cutoff) OR ([RevokedAt] IS NULL AND [ExpiresAt] < @Cutoff));

        DELETE FROM [dbo].[RefreshToken]
        WHERE [Id] IN (
            SELECT TOP (@BatchSize) [Id] FROM [dbo].[RefreshToken]
            WHERE  ([RevokedAt] < @Cutoff) OR ([RevokedAt] IS NULL AND [ExpiresAt] < @Cutoff));

        SET @Deleted = @@ROWCOUNT;
        IF @Deleted > 0 WAITFOR DELAY '00:00:01';
    END;
END;
GO
