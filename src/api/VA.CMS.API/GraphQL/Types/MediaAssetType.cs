using HotChocolate.Authorization;
using VA.CMS.API.Auth;

namespace VA.CMS.API.GraphQL.Types;

/// <summary>
/// Hot Chocolate GraphQL type for a CMS media asset.
/// Maps to the MediaAsset POCO / DB table.
/// </summary>
public class MediaAssetType
{
    public long    Id               { get; init; }
    public string  FileName         { get; init; } = string.Empty;
    /// <summary>Backend-relative storage path — hidden from the anonymous audience (#156).</summary>
    [Authorize(Policy = CmsRoles.Policies.CanRead)]
    public string  StoragePath      { get; init; } = string.Empty;
    public string  StorageBackend   { get; init; } = "local";
    public string  MimeType         { get; init; } = string.Empty;
    public long    FileSizeBytes    { get; init; }
    public string? AltText          { get; init; }
    public string? Title            { get; init; }
    public string? Description      { get; init; }
    public int?    Width            { get; init; }
    public int?    Height           { get; init; }
    /// <summary>Internal user id — hidden from the anonymous audience (#156).</summary>
    [Authorize(Policy = CmsRoles.Policies.CanRead)]
    public long    UploadedById     { get; init; }
    [Authorize(Policy = CmsRoles.Policies.CanRead)]
    public bool?   IsVirusScanPassed { get; init; }
    public DateTime CreatedAt       { get; init; }
    public DateTime UpdatedAt       { get; init; }
}
