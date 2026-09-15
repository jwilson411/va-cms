using PetaPoco;

namespace VA.CMS.Infrastructure.Data.Pocos;

/// <summary>
/// ContentVersion row with joined author display name.
/// Returned by usp_ContentVersion_List and usp_ContentVersion_GetById.
/// </summary>
public class ContentVersionWithAuthor
{
    public long Id { get; set; }
    public long ContentEntryId { get; set; }
    public int VersionNumber { get; set; }
    public string FieldsJson { get; set; } = "{}";
    public string? RenderedFieldsJson { get; set; }
    public string Status { get; set; } = "Draft";
    public long AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string? ChangeNote { get; set; }
    public DateTime CreatedAt { get; set; }
}
