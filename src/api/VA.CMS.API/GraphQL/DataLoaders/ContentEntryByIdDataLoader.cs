using GreenDonut;
using VA.CMS.API.GraphQL.Types;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.GraphQL.DataLoaders;

/// <summary>
/// DataLoader that batches ContentEntry loads by ID, preventing N+1 queries
/// when many resolver calls would otherwise each issue an individual DB round-trip.
///
/// Hot Chocolate automatically batches all IDs collected within one request's
/// execution phase and dispatches a single call to LoadBatchAsync.
/// </summary>
public sealed class ContentEntryByIdDataLoader(
    IBatchScheduler batchScheduler,
    DataLoaderOptions options,
    IServiceProvider serviceProvider)
    : BatchDataLoader<long, ContentEntryType?>(batchScheduler, options)
{
    protected override async Task<IReadOnlyDictionary<long, ContentEntryType?>> LoadBatchAsync(
        IReadOnlyList<long> keys,
        CancellationToken cancellationToken)
    {
        // Resolve the scoped repository from the request scope.
        using var scope = serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IContentEntryRepository>();

        var result = new Dictionary<long, ContentEntryType?>();

        // Fetch each entry. In practice the set is small (one per field resolution
        // within a single request), so individual SP calls per ID are fine.
        // A future optimisation can batch via an IN-list SP.
        foreach (var id in keys)
        {
            var entry = await repo.GetByIdAsync(id);
            result[id] = entry is null ? null : MapToType(entry);
        }

        return result;
    }

    public static ContentEntryType MapToType(ContentEntry e) => new()
    {
        Id                 = e.Id,
        ContentTypeId      = e.ContentTypeId,
        Slug               = e.Slug,
        Locale             = e.Locale,
        Status             = e.Status,
        PublishedVersionId = e.PublishedVersionId,
        OwnerId            = e.OwnerId,
        CreatedAt          = e.CreatedAt,
        UpdatedAt          = e.UpdatedAt,
    };
}
