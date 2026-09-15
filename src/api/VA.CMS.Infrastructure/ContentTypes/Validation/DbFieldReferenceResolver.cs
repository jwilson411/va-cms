using VA.CMS.Infrastructure.Data;
using VA.CMS.Infrastructure.ContentTypes.Validation;

namespace VA.CMS.Infrastructure.ContentTypes.Validation;

/// <summary>
/// Default DB-backed implementation of <see cref="IFieldReferenceResolver"/>.
/// Calls stored procedures via PetaPoco — no direct DML.
/// </summary>
public sealed class DbFieldReferenceResolver : IFieldReferenceResolver
{
    private readonly CmsDatabase _db;

    public DbFieldReferenceResolver(CmsDatabase db)
    {
        _db = db;
    }

    public async Task<bool> MediaAssetExistsAsync(long id, CancellationToken ct = default)
    {
        var result = await _db.ExecuteScalarAsync<int>(
            "EXEC usp_MediaAsset_ExistsById @0", id);
        return result == 1;
    }

    public async Task<bool> ContentEntryExistsAsync(long id, CancellationToken ct = default)
    {
        var result = await _db.ExecuteScalarAsync<int>(
            "EXEC usp_ContentEntry_ExistsById @0", id);
        return result == 1;
    }
}
