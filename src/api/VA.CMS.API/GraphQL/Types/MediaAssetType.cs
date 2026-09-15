namespace VA.CMS.API.GraphQL.Types;

/// <summary>
/// Hot Chocolate GraphQL type for a CMS media asset.
/// Maps to the MediaAsset POCO / DB table.
/// </summary>
public class MediaAssetType
{
    public long    Id               { get; init; }
    public string  FileName         { get; init; } = string.Empty;
    public string  StoragePath      { get; init; } = string.Empty;
    public string  StorageBackend   { get; init; } = "local";
    public string  MimeType         { get; init; } = string.Empty;
    public long    FileSizeBytes    { get; init; }
    public string? AltText          { get; init; }
    public string? Title            { get; init; }
    public string? Description      { get; init; }
    public int?    Width            { get; init; }
    public int?    Height           { get; init; }
    public long    UploadedById     { get; init; }
    public bool?   IsVirusScanPassed { get; init; }
    public DateTime CreatedAt       { get; init; }
    public DateTime UpdatedAt       { get; init; }
}
