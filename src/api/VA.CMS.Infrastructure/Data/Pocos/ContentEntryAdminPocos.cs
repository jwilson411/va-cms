namespace VA.CMS.Infrastructure.Data.Pocos;

/// <summary>
/// Row returned by usp_ContentEntry_ListAdmin (issue #29).
/// Represents one row in the admin content entry list.
/// </summary>
public class ContentEntryAdminRow
{
    public long Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long ContentTypeId { get; set; }
    public string ContentTypeName { get; set; } = string.Empty;
    public long OwnerId { get; set; }
    public string AuthorDisplayName { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// Paginated result returned by IContentEntryRepository.ListAdminAsync.
/// </summary>
public class ContentEntryAdminPage
{
    public IReadOnlyList<ContentEntryAdminRow> Items { get; set; } = [];
    public int TotalRows { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalRows / PageSize) : 0;
}
