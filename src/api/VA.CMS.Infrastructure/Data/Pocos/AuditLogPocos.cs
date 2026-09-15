namespace VA.CMS.Infrastructure.Data.Pocos;

/// <summary>
/// Audit log row returned by usp_AuditLog_ListPaged and usp_AuditLog_ExportCsv (issue #57).
/// Includes the actor's display name joined from the User table.
/// </summary>
public class AuditLogRow
{
    public long Id { get; set; }
    public long? ActorId { get; set; }
    public string? ActorEmail { get; set; }
    public string? ActorDisplayName { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? DiffJson { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Paged result set from usp_AuditLog_ListPaged (issue #57).
/// </summary>
public class AuditLogPage
{
    public IReadOnlyList<AuditLogRow> Items { get; set; } = [];
    public int TotalItems { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
