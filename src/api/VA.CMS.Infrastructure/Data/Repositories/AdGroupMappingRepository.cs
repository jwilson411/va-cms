using System.Text.Json;
using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Infrastructure.Data.Repositories;

/// <summary>
/// AD group → CMS role mapping repository.
/// All DB access via EXEC usp_AdGroupMapping_* stored procedures.
/// The app service account has EXECUTE only — no direct DML.
/// </summary>
public class AdGroupMappingRepository : IAdGroupMappingRepository
{
    private readonly CmsDatabase _db;

    public AdGroupMappingRepository(CmsDatabase db) => _db = db;

    public async Task<IEnumerable<AdGroupRoleMappingRow>> ListAsync()
    {
        // Use ADO.NET — result projection includes RoleName which is not a table column.
        var results = new List<AdGroupRoleMappingRow>();
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_AdGroupMapping_List";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new AdGroupRoleMappingRow
            {
                Id          = reader.GetInt64(reader.GetOrdinal("Id")),
                AdGroup     = reader.GetString(reader.GetOrdinal("AdGroup")),
                RoleId      = reader.GetInt64(reader.GetOrdinal("RoleId")),
                RoleName    = reader.GetString(reader.GetOrdinal("RoleName")),
                CreatedById = reader.GetInt64(reader.GetOrdinal("CreatedById")),
                CreatedAt   = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                UpdatedAt   = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
            });
        }
        return results;
    }

    public async Task<long> UpsertAsync(string adGroup, long roleId, long createdById)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_AdGroupMapping_Upsert @AdGroup, @RoleId, @CreatedById, @NewId OUTPUT";
        cmd.Parameters.AddWithValue("@AdGroup",     adGroup);
        cmd.Parameters.AddWithValue("@RoleId",      roleId);
        cmd.Parameters.AddWithValue("@CreatedById", createdById);

        var outParam = cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;

        await cmd.ExecuteNonQueryAsync();
        return (long)outParam.Value;
    }

    public async Task DeleteAsync(long id)
    {
        await _db.ExecuteAsync("EXEC usp_AdGroupMapping_Delete @0", id);
    }

    public async Task<IEnumerable<(long RoleId, string RoleName)>> ResolveRolesForGroupsAsync(
        IEnumerable<string> adGroups)
    {
        var groupList = adGroups.ToList();
        if (groupList.Count == 0)
            return Enumerable.Empty<(long, string)>();

        // Serialize to JSON array for the SP's OPENJSON call.
        var groupsJson = JsonSerializer.Serialize(groupList);

        var results = new List<(long RoleId, string RoleName)>();
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_AdGroupMapping_ResolveRoles @GroupsJson";
        cmd.Parameters.AddWithValue("@GroupsJson", groupsJson);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add((
                RoleId:   reader.GetInt64(reader.GetOrdinal("RoleId")),
                RoleName: reader.GetString(reader.GetOrdinal("RoleName"))
            ));
        }
        return results;
    }
}
