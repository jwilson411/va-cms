namespace VA.CMS.Infrastructure.Data;

/// <summary>
/// Who/where for the current unit of work (NIST AU-3, #165). Populated once per
/// HTTP request by the API's audit-context middleware; read by
/// <see cref="CmsDatabase"/> when it opens a connection (pushed into SESSION_CONTEXT
/// so stored procedures that audit themselves can record the actor and source IP)
/// and by <c>AuditLogRepository</c> for rows written from C#.
///
/// Every property is optional: background workers and anonymous requests have no
/// actor, and unit tests need none of it.
/// </summary>
public interface IAuditContext
{
    /// <summary>CMS user id from the bearer token, or null when unauthenticated.</summary>
    long? ActorId { get; }

    /// <summary>Client address after forwarded-header resolution.</summary>
    string? SourceIp { get; }

    /// <summary>User-Agent header, truncated to the column width.</summary>
    string? UserAgent { get; }

    /// <summary>Request correlation id (X-Correlation-Id when supplied, else the trace identifier).</summary>
    string? CorrelationId { get; }
}

/// <summary>Mutable per-scope implementation; the middleware fills it in once authentication has run.</summary>
public sealed class AuditContext : IAuditContext
{
    public const int UserAgentMaxLength     = 500;
    public const int CorrelationIdMaxLength = 100;
    public const int SourceIpMaxLength      = 50;

    public long?   ActorId       { get; set; }
    public string? SourceIp      { get; set; }
    public string? UserAgent     { get; set; }
    public string? CorrelationId { get; set; }

    /// <summary>A context with nothing in it — the default for CLI, workers and tests.</summary>
    public static readonly IAuditContext Empty = new AuditContext();

    public static string? Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
