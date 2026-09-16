using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Infrastructure.Data.Repositories;

/// <summary>
/// ContentType table access. Content types are defined in code (IFieldTypeRegistry);
/// the DB row exists because ContentEntry.ContentTypeId is a foreign key to it.
/// </summary>
public interface IContentTypeRepository
{
    Task<ContentType?> GetByNameAsync(string name);

    /// <summary>Insert-or-update by <see cref="ContentType.Name"/>; returns the row id.</summary>
    Task<long> UpsertAsync(ContentType type);
}
