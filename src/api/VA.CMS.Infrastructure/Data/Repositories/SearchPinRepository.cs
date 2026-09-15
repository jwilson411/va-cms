using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Infrastructure.Data.Repositories;

/// <summary>
/// Pinned search results repository.
/// All DB access via EXEC usp_SearchPin_* stored procedures (NFR-DB-01).
/// Issue #52 — BRD FR-SEARCH-04.
/// </summary>
public class SearchPinRepository : ISearchPinRepository
{
    private readonly CmsDatabase _db;

    public SearchPinRepository(CmsDatabase db) => _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchPin>> ListAsync(string? queryString = null)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_SearchPin_List @QueryString";
        cmd.Parameters.AddWithValue("@QueryString", (object?)queryString ?? DBNull.Value);

        var results = new List<SearchPin>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapPin(reader));

        return results;
    }

    /// <inheritdoc />
    public async Task<long> CreateAsync(string queryString, long contentEntryId, long? createdById = null)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_SearchPin_Create @QueryString, @ContentEntryId, @CreatedById, @NewId OUTPUT";
        cmd.Parameters.AddWithValue("@QueryString",    queryString);
        cmd.Parameters.AddWithValue("@ContentEntryId", contentEntryId);
        cmd.Parameters.AddWithValue("@CreatedById",    (object?)createdById ?? DBNull.Value);

        var outParam = cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;

        await cmd.ExecuteNonQueryAsync();
        return (long)outParam.Value;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long id)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_SearchPin_Delete @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static SearchPin MapPin(SqlDataReader r)
    {
        var ordId             = r.GetOrdinal("Id");
        var ordQueryString    = r.GetOrdinal("QueryString");
        var ordEntryId        = r.GetOrdinal("ContentEntryId");
        var ordCreatedById    = r.GetOrdinal("CreatedById");
        var ordCreatedAt      = r.GetOrdinal("CreatedAt");
        var ordEntrySlug      = r.GetOrdinal("EntrySlug");
        var ordEntryStatus    = r.GetOrdinal("EntryStatus");
        var ordEntryTypeId    = r.GetOrdinal("EntryContentTypeId");
        var ordEntryTitle     = r.GetOrdinal("EntryTitle");

        return new SearchPin
        {
            Id             = r.GetInt64(ordId),
            QueryString    = r.GetString(ordQueryString),
            ContentEntryId = r.GetInt64(ordEntryId),
            CreatedById    = r.IsDBNull(ordCreatedById) ? null : r.GetInt64(ordCreatedById),
            CreatedAt      = r.GetDateTime(ordCreatedAt),
            EntrySlug      = r.IsDBNull(ordEntrySlug)   ? null : r.GetString(ordEntrySlug),
            EntryStatus    = r.IsDBNull(ordEntryStatus)  ? null : r.GetString(ordEntryStatus),
            EntryContentTypeId = r.IsDBNull(ordEntryTypeId) ? null : r.GetInt64(ordEntryTypeId),
            EntryTitle     = r.IsDBNull(ordEntryTitle)   ? null : r.GetString(ordEntryTitle),
        };
    }
}
