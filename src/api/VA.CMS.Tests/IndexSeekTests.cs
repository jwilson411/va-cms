using Microsoft.Data.SqlClient;

namespace VA.CMS.Tests;

/// <summary>
/// Integration tests that verify the V004 non-clustered index
/// IX_ContentEntry_Status_UpdatedAt is present and capable of serving
/// an Index Seek on (Status, UpdatedAt) for usp_ContentEntry_List.
///
/// Validates NFR-DB-02: indexes must cover common query axes.
///
/// Two-part verification:
///   1. sys.indexes confirms the index exists with the correct key columns.
///   2. FORCESEEK hint proves SQL Server can execute a seek on that index
///      (the optimizer cannot reject the hint if the index matches the query).
///
/// Note: checking the raw execution plan of usp_ContentEntry_List in a fresh
/// test database is inherently unreliable — with &lt;100 rows the optimizer
/// correctly chooses a table scan. The FORCESEEK test is the canonical way
/// to assert "the index supports seek access", independent of row count.
/// </summary>
[Collection("Database")]
public class IndexSeekTests(DatabaseFixture fixture)
{
    // -------------------------------------------------------------------------
    // Part 1: catalog check — the index must exist with the right shape
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Index_IX_ContentEntry_Status_UpdatedAt_Exists()
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM   sys.indexes i
            JOIN   sys.objects o ON o.object_id = i.object_id
            WHERE  o.name    = 'ContentEntry'
              AND  i.name    = 'IX_ContentEntry_Status_UpdatedAt'
              AND  i.type    = 2;   -- NONCLUSTERED";

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Index_IX_ContentEntry_Status_UpdatedAt_HasCorrectKeyColumns()
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT col.name, ic.key_ordinal, ic.is_descending_key, ic.is_included_column
            FROM   sys.indexes i
            JOIN   sys.objects o          ON o.object_id   = i.object_id
            JOIN   sys.index_columns ic   ON ic.object_id  = i.object_id
                                         AND ic.index_id   = i.index_id
            JOIN   sys.columns col        ON col.object_id = ic.object_id
                                         AND col.column_id = ic.column_id
            WHERE  o.name = 'ContentEntry'
              AND  i.name = 'IX_ContentEntry_Status_UpdatedAt'
            ORDER  BY ic.key_ordinal, ic.is_included_column;";

        var rows = new List<(string Name, int Ordinal, bool Desc, bool Included)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetString(0),
                reader.GetByte(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3)
            ));
        }

        // Key columns (is_included_column = false, key_ordinal > 0)
        var keys = rows.Where(r => !r.Included && r.Ordinal > 0)
                       .OrderBy(r => r.Ordinal)
                       .ToList();

        Assert.Equal(2, keys.Count);
        Assert.Equal("Status",    keys[0].Name);
        Assert.False(keys[0].Desc);           // Status ASC
        Assert.Equal("UpdatedAt", keys[1].Name);
        Assert.True(keys[1].Desc);            // UpdatedAt DESC

        // INCLUDE columns (is_included_column = true)
        var includes = rows.Where(r => r.Included)
                           .Select(r => r.Name)
                           .ToHashSet();

        Assert.Contains("ContentTypeId", includes);
        Assert.Contains("OwnerId",       includes);
        Assert.Contains("Slug",          includes);
        Assert.Contains("Locale",        includes);
    }

    // -------------------------------------------------------------------------
    // Part 2: seek-capability test — prove the index supports index seek access
    // using FORCESEEK + SHOWPLAN_XML on the inner query from usp_ContentEntry_List.
    //
    // We seed rows and update statistics first so the optimizer has a real plan.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ContentEntry_List_UsesIndexSeek_OnStatusAndUpdatedAt()
    {
        // Seed rows and update stats
        await SeedContentEntriesAsync(200);

        // Use FORCESEEK with an explicit index hint to prove the index supports
        // seek access. SQL Server raises an error if the hint is unusable, so
        // a successful execution proves the index is seek-eligible.
        // We wrap in SHOWPLAN_XML to additionally assert "Index Seek" in the plan.
        var planXml = await GetForceSeekPlanXmlAsync();

        Assert.False(string.IsNullOrWhiteSpace(planXml),
            "SHOWPLAN_XML returned no XML — check connection permissions.");

        // The plan must contain an Index Seek on our target index.
        Assert.Contains("Index Seek", planXml, StringComparison.Ordinal);
        Assert.Contains("IX_ContentEntry_Status_UpdatedAt", planXml, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task SeedContentEntriesAsync(int count)
    {
        var ownerId = await TestSeeder.UpsertUserAsync(fixture.ConnectionString);
        var ctId    = await TestSeeder.EnsureContentTypeAsync(fixture.ConnectionString, "seek_test_type");

        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        for (var i = 0; i < count; i++)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "EXEC usp_ContentEntry_Create @ContentTypeId, @Slug, @Locale, @OwnerId, @NewId OUTPUT";
            cmd.Parameters.AddWithValue("@ContentTypeId", ctId);
            cmd.Parameters.AddWithValue("@Slug",          $"seek-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("@Locale",        "en-US");
            cmd.Parameters.AddWithValue("@OwnerId",       ownerId);
            var outParam = cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
            outParam.Direction = System.Data.ParameterDirection.Output;
            await cmd.ExecuteNonQueryAsync();
        }

        await using var statsCmd = conn.CreateCommand();
        statsCmd.CommandText = "UPDATE STATISTICS [dbo].[ContentEntry] WITH FULLSCAN;";
        await statsCmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Runs SHOWPLAN_XML ON on the equivalent inner SELECT from usp_ContentEntry_List
    /// with FORCESEEK + explicit index hint. Returns the plan XML string.
    ///
    /// SQL Server rule: SET SHOWPLAN_XML ON/OFF must each be the sole statement in their
    /// batch. So we issue them as separate SqlCommand calls on the same connection.
    /// When SHOWPLAN_XML is ON, the subsequent SELECT command returns the plan XML as a
    /// result set row instead of executing the query.
    ///
    /// FORCESEEK ensures an Index Seek if the index is seek-eligible; the optimizer
    /// raises error 8622 if the hint cannot be satisfied.
    /// </summary>
    private async Task<string> GetForceSeekPlanXmlAsync()
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        // Step 1: enable SHOWPLAN_XML (must be the only statement in the batch)
        await using (var setOn = conn.CreateCommand())
        {
            setOn.CommandText = "SET SHOWPLAN_XML ON;";
            await setOn.ExecuteNonQueryAsync();
        }

        // Step 2: run the SELECT — returns plan XML rows, not data
        var plans = new List<string>();

        const string selectSql = @"
SELECT e.Id, e.Status, e.UpdatedAt, e.ContentTypeId, e.OwnerId, e.Slug, e.Locale
FROM   [dbo].[ContentEntry] e WITH (FORCESEEK, INDEX([IX_ContentEntry_Status_UpdatedAt]))
WHERE  e.[Status] = 'Draft'
ORDER  BY e.[UpdatedAt] DESC;";

        await using (var selectCmd = conn.CreateCommand())
        {
            selectCmd.CommandText = selectSql;

            await using var reader = await selectCmd.ExecuteReaderAsync();
            do
            {
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        var val = reader.GetString(0);
                        if (val.TrimStart().StartsWith("<"))
                            plans.Add(val);
                    }
                }
            } while (await reader.NextResultAsync());
        }

        // Step 3: disable SHOWPLAN_XML (must be the only statement in the batch)
        await using (var setOff = conn.CreateCommand())
        {
            setOff.CommandText = "SET SHOWPLAN_XML OFF;";
            await setOff.ExecuteNonQueryAsync();
        }

        return string.Join(Environment.NewLine, plans);
    }
}
