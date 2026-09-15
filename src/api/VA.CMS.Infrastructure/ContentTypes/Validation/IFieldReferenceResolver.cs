namespace VA.CMS.Infrastructure.ContentTypes.Validation;

/// <summary>
/// Resolves entity references for MediaReference, SingleRelation, and
/// MultiRelation fields. Called at publish-time to confirm IDs exist in the DB.
/// </summary>
public interface IFieldReferenceResolver
{
    /// <summary>
    /// Returns true when a MediaAsset with <paramref name="id"/> exists.
    /// </summary>
    Task<bool> MediaAssetExistsAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Returns true when a ContentEntry with <paramref name="id"/> exists.
    /// </summary>
    Task<bool> ContentEntryExistsAsync(long id, CancellationToken ct = default);
}
