using GreenDonut;
using VA.CMS.API.GraphQL.Types;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.GraphQL.DataLoaders;

/// <summary>
/// DataLoader that batches MediaAsset loads by ID, preventing N+1 queries.
/// </summary>
public sealed class MediaAssetByIdDataLoader(
    IBatchScheduler batchScheduler,
    DataLoaderOptions options,
    IServiceProvider serviceProvider)
    : BatchDataLoader<long, MediaAssetType?>(batchScheduler, options)
{
    protected override async Task<IReadOnlyDictionary<long, MediaAssetType?>> LoadBatchAsync(
        IReadOnlyList<long> keys,
        CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IMediaAssetRepository>();

        var result = new Dictionary<long, MediaAssetType?>();
        foreach (var id in keys)
        {
            var asset = await repo.GetByIdAsync(id);
            result[id] = asset is null ? null : MapToType(asset);
        }

        return result;
    }

    public static MediaAssetType MapToType(MediaAsset a) => new()
    {
        Id                = a.Id,
        FileName          = a.FileName,
        StoragePath       = a.StoragePath,
        StorageBackend    = a.StorageBackend,
        MimeType          = a.MimeType,
        FileSizeBytes     = a.FileSizeBytes,
        AltText           = a.AltText,
        Title             = a.Title,
        Description       = a.Description,
        Width             = a.Width,
        Height            = a.Height,
        UploadedById      = a.UploadedById,
        IsVirusScanPassed = a.IsVirusScanPassed,
        CreatedAt         = a.CreatedAt,
        UpdatedAt         = a.UpdatedAt,
    };
}
