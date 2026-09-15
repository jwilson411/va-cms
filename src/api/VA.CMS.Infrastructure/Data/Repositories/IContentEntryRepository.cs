using VA.CMS.Infrastructure.Data.Pocos;
using PetaPoco;

namespace VA.CMS.Infrastructure.Data.Repositories;

public interface IContentEntryRepository
{
    Task<ContentEntry?> GetByIdAsync(long id);
    Task<ContentEntry?> GetBySlugAsync(string slug, string locale = "en-US");
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
}
