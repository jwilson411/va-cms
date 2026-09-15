namespace VA.CMS.Infrastructure.Data.Pocos;

/// <summary>
/// Represents one row in the SearchPin table.
/// Issue #52 — Pinned search results management.
/// </summary>
public sealed class SearchPin
{
    public long    Id             { get; set; }
    public string  QueryString    { get; set; } = string.Empty;
    public long    ContentEntryId { get; set; }
    public long?   CreatedById    { get; set; }
    public DateTime CreatedAt     { get; set; }

    // Denormalised columns returned by usp_SearchPin_List (joined from ContentEntry / ContentVersion)
    public string? EntrySlug          { get; set; }
    public string? EntryStatus        { get; set; }
    public long?   EntryContentTypeId { get; set; }
    public string? EntryTitle         { get; set; }
}
