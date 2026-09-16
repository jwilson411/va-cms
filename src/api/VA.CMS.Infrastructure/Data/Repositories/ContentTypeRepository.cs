using Microsoft.Data.SqlClient;
using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Infrastructure.Data.Repositories;

/// <inheritdoc cref="IContentTypeRepository"/>
public class ContentTypeRepository : IContentTypeRepository
{
    private readonly CmsDatabase _db;

    public ContentTypeRepository(CmsDatabase db)
    {
        _db = db;
    }

    public async Task<ContentType?> GetByNameAsync(string name)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC usp_ContentType_GetByName @Name";
        cmd.Parameters.AddWithValue("@Name", name);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var ordDesc = reader.GetOrdinal("Description");
        var ordTmpl = reader.GetOrdinal("TemplateId");
        return new ContentType
        {
            Id              = reader.GetInt64(reader.GetOrdinal("Id")),
            Name            = reader.GetString(reader.GetOrdinal("Name")),
            DisplayName     = reader.GetString(reader.GetOrdinal("DisplayName")),
            Description     = reader.IsDBNull(ordDesc) ? null : reader.GetString(ordDesc),
            TemplateId      = reader.IsDBNull(ordTmpl) ? null : reader.GetString(ordTmpl),
            IsSystemType    = reader.GetBoolean(reader.GetOrdinal("IsSystemType")),
            AllowWorkflow   = reader.GetBoolean(reader.GetOrdinal("AllowWorkflow")),
            FieldSchemaJson = reader.GetString(reader.GetOrdinal("FieldSchemaJson")),
            CreatedAt       = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt       = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
        };
    }

    public async Task<long> UpsertAsync(ContentType type)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXEC usp_ContentType_Upsert @Name, @DisplayName, @Description, @TemplateId, @AllowWorkflow, @FieldSchemaJson, @Id OUTPUT";
        cmd.Parameters.AddWithValue("@Name",            type.Name);
        cmd.Parameters.AddWithValue("@DisplayName",     type.DisplayName);
        cmd.Parameters.AddWithValue("@Description",     (object?)type.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TemplateId",      (object?)type.TemplateId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AllowWorkflow",   type.AllowWorkflow);
        cmd.Parameters.AddWithValue("@FieldSchemaJson", type.FieldSchemaJson);
        var outParam = cmd.Parameters.Add("@Id", System.Data.SqlDbType.BigInt);
        outParam.Direction = System.Data.ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return (long)outParam.Value;
    }
}
