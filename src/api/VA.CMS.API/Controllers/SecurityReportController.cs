using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Receives Content-Security-Policy violation reports (#162) from the API's own
/// headers, the admin SPA and the public site, and writes them to the log so the
/// report-only phase has something to review before enforcement is switched on.
/// Anonymous by design (browsers send reports without credentials); bodies are
/// capped so the endpoint cannot be used to fill the log.
/// </summary>
[ApiController]
[Route("api/v1/security")]
public class SecurityReportController(ILogger<SecurityReportController> logger) : ControllerBase
{
    private const int MaxBodyBytes = 16 * 1024;

    [HttpPost("csp-report")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(VA.CMS.API.RateLimiting.RateLimitPolicies.AnalyticsWrite)]
    [Consumes("application/csp-report", "application/reports+json", "application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> CspReport(CancellationToken ct)
    {
        if (Request.ContentLength > MaxBodyBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);

        using var reader = new StreamReader(Request.Body);
        var buffer = new char[MaxBodyBytes + 1];
        var read   = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        if (read > MaxBodyBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);

        var body = new string(buffer, 0, read);
        var summary = Summarise(body);
        logger.LogWarning("CSP violation reported from {Origin}: {Summary}",
            Request.Headers.Origin.ToString() is { Length: > 0 } o ? o : "(no origin)", summary);

        return NoContent();
    }

    /// <summary>Pulls the interesting fields out of either report format; falls back to a truncated body.</summary>
    public static string Summarise(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Legacy report-uri format: { "csp-report": { ... } }
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("csp-report", out var legacy))
                return Fields(legacy, "document-uri", "violated-directive", "blocked-uri", "source-file", "line-number");

            // Reporting API format: [ { "type": "csp-violation", "body": { ... } } ]
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
                && root[0].TryGetProperty("body", out var modern))
                return Fields(modern, "documentURL", "effectiveDirective", "blockedURL", "sourceFile", "lineNumber");
        }
        catch (JsonException) { }

        return body.Length > 500 ? body[..500] + "…" : body;
    }

    private static string Fields(JsonElement e, params string[] names)
        => string.Join(" ", names.Select(n => e.TryGetProperty(n, out var v) ? $"{n}={v}" : null).Where(x => x is not null));
}
