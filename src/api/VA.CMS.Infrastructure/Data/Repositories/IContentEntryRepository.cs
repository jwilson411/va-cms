using VA.CMS.Infrastructure.Data.Pocos;
using PetaPoco;

namespace VA.CMS.Infrastructure.Data.Repositories;

public interface IContentEntryRepository
{
    Task<ContentEntry?> GetByIdAsync(long id);
    Task<ContentEntry?> GetBySlugAsync(string slug, string locale = "en-US");

    /// <summary>
    /// Public delivery lookup: the Published entry for a slug joined with its
    /// published version and content type. Null when there is no published entry.
    /// </summary>
    Task<PublishedContentEntry?> GetPublishedBySlugAsync(string slug, string locale = "en-US");
    Task<Page<ContentEntry>> ListAsync(int page, int pageSize, string? status = null, long? contentTypeId = null);
    Task<long> CreateAsync(ContentEntry entry);
    Task UpdateAsync(ContentEntry entry);
    /// <summary>Soft-archive a content entry (story #23: section-scope enforced by controller).</summary>
    Task ArchiveAsync(long id, long actorId);

    /// <summary>
    /// Admin list with joins to ContentType + User. Supports filter, sort, pagination.
    /// Used by the content entry list screen (issue #29, FR-AUTH-01).
    /// </summary>
    Task<ContentEntryAdminPage> ListAdminAsync(
        long?    contentTypeId  = null,
        string?  status        = null,
        string?  authorSearch  = null,
        DateTime? dateFrom     = null,
        DateTime? dateTo       = null,
        string   sortBy        = "UpdatedAt",
        string   sortDir       = "DESC",
        int      page          = 1,
        int      pageSize      = 25);

    /// <summary>
    /// Update the slug for a content entry.
    /// Validates uniqueness; creates a 301 redirect if the entry is Published.
    /// Returns (success, errorMessage). On success errorMessage is null.
    /// Issue #33: FR-NAV-05.
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> UpdateSlugAsync(long id, string newSlug, long actorId);

    /// <summary>
    /// Set or clear the scheduled publish/expire times for a content entry.
    /// Issue #35: FR-AUTH-04 scheduled publish and expiry.
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> SetScheduleAsync(
        long id,
        DateTime? scheduledPublishAt,
        DateTime? scheduledExpireAt,
        long actorId);

    /// <summary>
    /// Returns content entries that are Approved, have ScheduledPublishAt set,
    /// and that time has passed. Used by the background scheduler.
    /// Issue #35.
    /// </summary>
    Task<IList<ContentEntry>> GetScheduledForPublishAsync();

    /// <summary>
    /// Returns content entries that are Published, have ScheduledExpireAt set,
    /// and that time has passed. Used by the background scheduler.
    /// Issue #35.
    /// </summary>
    Task<IList<ContentEntry>> GetScheduledForExpiryAsync();

    /// <summary>
    /// Publish a content entry: sets Status = 'Published', clears ScheduledPublishAt.
    /// Called by the background scheduler after the scheduled time passes.
    /// Issue #35.
    /// </summary>
    Task PublishScheduledAsync(long id, long systemActorId);

    /// <summary>
    /// Unpublish (expire) a content entry: sets Status = 'Approved', clears ScheduledExpireAt.
    /// Called by the background scheduler after the expiry time passes.
    /// Issue #35.
    /// </summary>
    Task ExpireScheduledAsync(long id, long systemActorId);

    /// <summary>
    /// Duplicate a content entry: creates a new Draft with '(Copy)' appended to title,
    /// slug cleared (must be set before publish), all field values copied,
    /// and media references shared (not re-uploaded).
    /// Issue #36: BRD FR-AUTH-07.
    /// Returns (success, newEntryId, errorMessage). On success errorMessage is null.
    /// </summary>
    Task<(bool Success, long? NewEntryId, string? ErrorMessage)> DuplicateAsync(
        long sourceEntryId, long actorId);
}
