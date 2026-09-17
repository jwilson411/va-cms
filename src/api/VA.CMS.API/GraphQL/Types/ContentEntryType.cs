using HotChocolate.Authorization;
using VA.CMS.API.Auth;

namespace VA.CMS.API.GraphQL.Types;

/// <summary>
/// Hot Chocolate GraphQL type for a CMS content entry.
/// Maps to the ContentEntry POCO / DB table.
/// </summary>
public class ContentEntryType
{
    public long   Id                  { get; init; }
    public long   ContentTypeId       { get; init; }
    public string Slug                { get; init; } = string.Empty;
    public string Locale              { get; init; } = "en-US";
    public string Status              { get; init; } = "Draft";
    public long?  PublishedVersionId  { get; init; }
    /// <summary>Internal user id — hidden from the anonymous audience (#156).</summary>
    [Authorize(Policy = CmsRoles.Policies.CanRead)]
    public long   OwnerId             { get; init; }
    public string? FieldsJson         { get; init; }
    public DateTime CreatedAt         { get; init; }
    public DateTime UpdatedAt         { get; init; }
}
