using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Controllers.Admin;

/// <summary>
/// Audit log viewer endpoint.
/// Story #57 — Build audit log viewer in admin (BRD FR-USERS-06).
///
///   GET  /api/v1/admin/audit               — paged list with filters
///   GET  /api/v1/admin/audit/export.csv    — CSV export of filtered results (up to 1000 rows)
/// </summary>
[ApiController]
[Route("api/v1/admin/audit")]
[Authorize(Policy = CmsRoles.Policies.CanAdminSystem)]
public class AuditLogController : ControllerBase
{
    private readonly IAuditLogRepository  _audit;
    private readonly ISiteSettingsService _settings;

    public AuditLogController(IAuditLogRepository audit, ISiteSettingsService settings)
    {
        _audit    = audit;
        _settings = settings;
    }

    /// <summary>
    /// List audit log rows, newest first, with optional filters.
    /// Acceptance criteria:
    ///   - /admin/audit lists all AuditLog rows, newest first
    ///   - Filters: User (actorId), Action Type (action), Entity Type (entityType), Date Range (fromDate, toDate)
    /// </summary>
    /// <param name="actorId">Filter by actor user ID.</param>
    /// <param name="action">Filter by action string (e.g. "Publish", "Deactivate").</param>
    /// <param name="entityType">Filter by entity type (e.g. "ContentEntry", "User").</param>
    /// <param name="fromDate">ISO 8601 UTC date-time — include rows on or after.</param>
    /// <param name="toDate">ISO 8601 UTC date-time — include rows on or before.</param>
    /// <param name="page">1-based page number (default 1).</param>
    /// <param name="pageSize">Rows per page, 1–api.maxPageSize (default 50).</param>
    /// <param name="outcome">Filter by outcome: Success or Failure (#165).</param>
    /// <param name="ipAddress">Filter by exact source IP address (#165).</param>
    [HttpGet]
    [ProducesResponseType(typeof(AuditLogPage), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAuditLog(
        [FromQuery] long? actorId     = null,
        [FromQuery] string? action    = null,
        [FromQuery] string? entityType = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] int page           = 1,
        [FromQuery] int pageSize       = 50,
        [FromQuery] string? outcome    = null,
        [FromQuery] string? ipAddress  = null)
    {
        pageSize = _settings.ClampPageSize(pageSize);
        page     = Math.Max(1, page);
        if (NormalizeOutcome(outcome) is { } bad)
            return BadRequest(bad);

        var result = await _audit.ListPagedAsync(
            actorId, action, entityType, fromDate, toDate, page, pageSize,
            EmptyToNull(outcome), EmptyToNull(ipAddress));

        return Ok(result);
    }

    /// <summary>
    /// Export filtered audit log rows as a CSV file (up to 1000 rows).
    /// Acceptance criteria: Exports filtered results to CSV.
    /// </summary>
    /// <param name="actorId">Filter by actor user ID.</param>
    /// <param name="action">Filter by action string.</param>
    /// <param name="entityType">Filter by entity type.</param>
    /// <param name="fromDate">ISO 8601 UTC date-time — include rows on or after.</param>
    /// <param name="toDate">ISO 8601 UTC date-time — include rows on or before.</param>
    /// <param name="outcome">Filter by outcome: Success or Failure (#165).</param>
    /// <param name="ipAddress">Filter by exact source IP address (#165).</param>
    [HttpGet("export.csv")]
    [Produces("text/csv")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] long? actorId     = null,
        [FromQuery] string? action    = null,
        [FromQuery] string? entityType = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] string? outcome    = null,
        [FromQuery] string? ipAddress  = null)
    {
        if (NormalizeOutcome(outcome) is { } bad)
            return BadRequest(bad);

        var rows = await _audit.ExportAsync(actorId, action, entityType, fromDate, toDate,
            EmptyToNull(outcome), EmptyToNull(ipAddress));

        var csv = BuildCsv(rows);
        var bytes = Encoding.UTF8.GetBytes(csv);

        return File(bytes, "text/csv", "audit-log.csv");
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOutcome(string? outcome)
        => EmptyToNull(outcome) is { } o && o != AuditOutcome.Success && o != AuditOutcome.Failure
            ? $"outcome must be '{AuditOutcome.Success}' or '{AuditOutcome.Failure}'."
            : null;

    // ── CSV builder ───────────────────────────────────────────────────────────

    private static string BuildCsv(IReadOnlyList<AuditLogRow> rows)
    {
        var sb = new StringBuilder();
        // Header
        sb.AppendLine("Id,CreatedAt,ActorEmail,ActorDisplayName,EntityType,EntityId,Action,Outcome,IpAddress,UserAgent,CorrelationId");

        foreach (var row in rows)
        {
            sb.Append(row.Id).Append(',');
            sb.Append(row.CreatedAt.ToString("O")).Append(',');
            sb.Append(EscapeCsv(row.ActorEmail)).Append(',');
            sb.Append(EscapeCsv(row.ActorDisplayName)).Append(',');
            sb.Append(EscapeCsv(row.EntityType)).Append(',');
            sb.Append(EscapeCsv(row.EntityId)).Append(',');
            sb.Append(EscapeCsv(row.Action)).Append(',');
            sb.Append(EscapeCsv(row.Outcome)).Append(',');
            sb.Append(EscapeCsv(row.IpAddress)).Append(',');
            sb.Append(EscapeCsv(row.UserAgent)).Append(',');
            sb.AppendLine(EscapeCsv(row.CorrelationId));
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        if (value is null) return string.Empty;
        // Quote fields that contain a comma, double-quote, or newline. A leading formula
        // character is prefixed so a user agent like "=cmd|..." cannot execute in Excel.
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
            value = "'" + value;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
