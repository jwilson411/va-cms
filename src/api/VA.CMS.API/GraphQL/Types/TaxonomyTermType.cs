namespace VA.CMS.API.GraphQL.Types;

/// <summary>
/// Hot Chocolate GraphQL type for a taxonomy term.
/// Maps to the TaxonomyTerm POCO / DB table.
/// </summary>
public class TaxonomyTermType
{
    public long   Id           { get; init; }
    public long   TaxonomyId   { get; init; }
    public long?  ParentTermId { get; init; }
    public string Name         { get; init; } = string.Empty;
    public string Slug         { get; init; } = string.Empty;
    public int    SortOrder    { get; init; }
    public int    Depth        { get; init; }
}
