-- V007__search_analytics_tables.sql
-- SearchQueryLog and SearchQuerySummary tables.
-- See DATABASE_LAYER.md §6.5

CREATE TABLE [dbo].[SearchQueryLog] (
    [Id]          BIGINT IDENTITY(1,1) NOT NULL,
    [Query]       NVARCHAR(500)        NOT NULL,
    [ResultCount] INT                  NOT NULL,
    [UserId]      BIGINT               NULL,
    [CreatedAt]   DATETIME2            NOT NULL CONSTRAINT [DF_SearchQueryLog_CreatedAt] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_SearchQueryLog] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

CREATE TABLE [dbo].[SearchQuerySummary] (
    [Id]           BIGINT IDENTITY(1,1) NOT NULL,
    [QueryDate]    DATE                 NOT NULL,
    [Query]        NVARCHAR(500)        NOT NULL,
    [SearchCount]  INT                  NOT NULL,
    [ZeroResults]  INT                  NOT NULL,
    CONSTRAINT [PK_SearchQuerySummary] PRIMARY KEY CLUSTERED ([Id] ASC)
);
CREATE UNIQUE INDEX [UX_SearchQuerySummary_Date_Query]
    ON [dbo].[SearchQuerySummary] ([QueryDate], [Query]);
GO

-- Search log rollup SP
CREATE OR ALTER PROCEDURE [dbo].[usp_Maint_RollupSearchLogs]
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Yesterday DATE = DATEADD(DAY, -1, CAST(SYSUTCDATETIME() AS DATE));

    INSERT INTO [dbo].[SearchQuerySummary] ([QueryDate], [Query], [SearchCount], [ZeroResults])
    SELECT @Yesterday,
           [Query],
           COUNT(*)                                              AS SearchCount,
           SUM(CASE WHEN [ResultCount] = 0 THEN 1 ELSE 0 END)  AS ZeroResults
    FROM   [dbo].[SearchQueryLog]
    WHERE  CAST([CreatedAt] AS DATE) = @Yesterday
    GROUP  BY [Query];

    DELETE FROM [dbo].[SearchQueryLog]
    WHERE [CreatedAt] < DATEADD(DAY, -90, SYSUTCDATETIME());
END;
GO
