using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Read-only settings for the front ends (issue #143, epic #141). Values come from the
/// in-memory snapshot, so these endpoints are cheap enough to call on every page render.
///
///   GET /api/v1/settings/public — anonymous; Scope = Public only (Next.js public site)
///   GET /api/v1/settings/client — any CMS role; Scope = Public + Admin (admin SPA)
///
/// Server-scoped settings are never returned here; SystemAdmins manage them through
/// /api/v1/admin/settings.
/// </summary>
[ApiController]
[Route("api/v1/settings")]
public class SettingsController : ControllerBase
{
    private readonly ISiteSettingsService _settings;

    public SettingsController(ISiteSettingsService settings) => _settings = settings;

    /// <summary>Public-scoped settings as a flat key → value map.</summary>
    [HttpGet("public")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Dictionary<string, string?>), StatusCodes.Status200OK)]
    public IActionResult Public() => Ok(Map(SiteSettingScope.Public));

    /// <summary>Admin- and Public-scoped settings as a flat key → value map.</summary>
    [HttpGet("client")]
    [Authorize(Policy = CmsRoles.Policies.AnyRole)]
    [ProducesResponseType(typeof(Dictionary<string, string?>), StatusCodes.Status200OK)]
    public IActionResult Client() => Ok(Map(SiteSettingScope.Admin, SiteSettingScope.Public));

    private Dictionary<string, string?> Map(params SiteSettingScope[] scopes)
    {
        // Iterate the code definitions so every declared key is present (with its default)
        // even before the first database load.
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in SiteSettingDefinitions.All.Where(d => scopes.Contains(d.Scope)))
            result[d.Key] = _settings.GetString(d.Key);
        return result;
    }
}
