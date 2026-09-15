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
}
