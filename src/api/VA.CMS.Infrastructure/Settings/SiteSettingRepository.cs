using System.Data;
using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data;

namespace VA.CMS.Infrastructure.Settings;

/// <summary>One [SiteSetting] row as returned by usp_SiteSetting_List.</summary>
public sealed class SiteSettingRow
{
    public long    Id            { get; set; }
    public string  Key           { get; set; } = string.Empty;
    /// <summary>Admin-set value; null means "use <see cref="DefaultValue"/>".</summary>
    public string? Value         { get; set; }
    public string? DefaultValue  { get; set; }
    public string  DataType      { get; set; } = "string";
    public string  Category      { get; set; } = string.Empty;
    public string  Scope         { get; set; } = "Server";
    public string? Description   { get; set; }
    public int     SortOrder     { get; set; }
    public long?   UpdatedById   { get; set; }
    public string? UpdatedByName { get; set; }
    public DateTime UpdatedAt    { get; set; }

    /// <summary>The value the application should use.</summary>
    public string? EffectiveValue => Value ?? DefaultValue;
}

/// <summary>
/// [SiteSetting] data access. All DB access via EXEC usp_SiteSetting_* (NFR-DB-01).
/// Issue #142 (epic #141).
/// </summary>
public interface ISiteSettingRepository
{
    Task<IReadOnlyList<SiteSettingRow>> ListAsync(CancellationToken ct = default);

    /// <summary>Insert-or-refresh the rows for code-declared definitions; never overwrites Value.</summary>
    Task EnsureDefinitionsAsync(IEnumerable<SiteSettingDefinition> definitions, CancellationToken ct = default);

    /// <summary>Returns false when the key is not declared.</summary>
    Task<bool> SetValueAsync(string key, string? value, long updatedById, CancellationToken ct = default);

    /// <summary>Value := NULL so the default applies again. Returns false when the key is not declared.</summary>
    Task<bool> ResetAsync(string key, long updatedById, CancellationToken ct = default);
}

public sealed class SiteSettingRepository : ISiteSettingRepository
{
    private readonly string _connectionString;

    public SiteSettingRepository(CmsDatabase db) : this(db.ConnectionString) { }

    public SiteSettingRepository(string connectionString) => _connectionString = connectionString;

    public async Task<IReadOnlyList<SiteSettingRow>> ListAsync(CancellationToken ct = default)
    {
        var rows = new List<SiteSettingRow>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_SiteSetting_List";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new SiteSettingRow
            {
                Id            = reader.GetInt64(reader.GetOrdinal("Id")),
                Key           = reader.GetString(reader.GetOrdinal("Key")),
                Value         = reader.IsDBNull(reader.GetOrdinal("Value"))         ? null : reader.GetString(reader.GetOrdinal("Value")),
                DefaultValue  = reader.IsDBNull(reader.GetOrdinal("DefaultValue"))  ? null : reader.GetString(reader.GetOrdinal("DefaultValue")),
                DataType      = reader.GetString(reader.GetOrdinal("DataType")),
                Category      = reader.GetString(reader.GetOrdinal("Category")),
                Scope         = reader.GetString(reader.GetOrdinal("Scope")),
                Description   = reader.IsDBNull(reader.GetOrdinal("Description"))   ? null : reader.GetString(reader.GetOrdinal("Description")),
                SortOrder     = reader.GetInt32(reader.GetOrdinal("SortOrder")),
                UpdatedById   = reader.IsDBNull(reader.GetOrdinal("UpdatedById"))   ? null : reader.GetInt64(reader.GetOrdinal("UpdatedById")),
                UpdatedByName = reader.IsDBNull(reader.GetOrdinal("UpdatedByName")) ? null : reader.GetString(reader.GetOrdinal("UpdatedByName")),
                UpdatedAt     = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
            });
        }
        return rows;
    }

    public async Task EnsureDefinitionsAsync(IEnumerable<SiteSettingDefinition> definitions, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        foreach (var d in definitions)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "EXEC usp_SiteSetting_EnsureDefinition @Key, @DefaultValue, @DataType, @Category, @Scope, @Description, @SortOrder";
            cmd.Parameters.AddWithValue("@Key",          d.Key);
            cmd.Parameters.AddWithValue("@DefaultValue", d.Default);
            cmd.Parameters.AddWithValue("@DataType",     d.Type.ToString().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@Category",     d.Category);
            cmd.Parameters.AddWithValue("@Scope",        d.Scope.ToString());
            cmd.Parameters.AddWithValue("@Description",  d.Description);
            cmd.Parameters.AddWithValue("@SortOrder",    d.SortOrder);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<bool> SetValueAsync(string key, string? value, long updatedById, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_SiteSetting_SetValue @Key, @Value, @UpdatedById, @Success OUTPUT";
        cmd.Parameters.AddWithValue("@Key",         key);
        cmd.Parameters.AddWithValue("@Value",       (object?)value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UpdatedById", updatedById);
        var success = cmd.Parameters.Add("@Success", SqlDbType.Bit);
        success.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);
        return success.Value is bool b && b;
    }

    public async Task<bool> ResetAsync(string key, long updatedById, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_SiteSetting_Reset @Key, @UpdatedById, @Success OUTPUT";
        cmd.Parameters.AddWithValue("@Key",         key);
        cmd.Parameters.AddWithValue("@UpdatedById", updatedById);
        var success = cmd.Parameters.Add("@Success", SqlDbType.Bit);
        success.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);
        return success.Value is bool b && b;
    }
}
