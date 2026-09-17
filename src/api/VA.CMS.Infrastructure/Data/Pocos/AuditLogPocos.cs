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
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
    public string Outcome { get; set; } = AuditOutcome.Success;
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

/// <summary>Result of usp_RefreshToken_Validate (#163).</summary>
public sealed class RefreshTokenLookup
{
    /// <summary>Ok | Replay | Expired | Idle — see the stored procedure header.</summary>
    public string Status { get; set; } = string.Empty;
    public long Id { get; set; }
    public long UserId { get; set; }
    public Guid FamilyId { get; set; }
    public string? GroupsJson { get; set; }
    public DateTime AbsoluteExpiresAt { get; set; }
}

/// <summary>Values of RefreshToken.Status returned by usp_RefreshToken_Validate.</summary>
public static class RefreshTokenStatus
{
    public const string Ok      = "Ok";
    public const string Replay  = "Replay";
    public const string Expired = "Expired";
    public const string Idle    = "Idle";
}
