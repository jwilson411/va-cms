using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using VA.CMS.API.Navigation;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Public redirect resolver consumed by the Next.js site (#169, BRD FR-NAV-05/06).
///
///   GET /api/v1/redirects/resolve?path=/pages/old-slug   — anonymous
///
/// 200 { fromPath, toPath, statusCode } when an active rule matches the path (exactly,
/// or with/without a trailing slash); 404 otherwise. Both answers carry
/// Cache-Control: public, max-age=redirects.cacheSeconds so the site's proxy — which
/// asks on every page request — can hold the answer for the same window the API does.
/// A path that cannot be a stored FromPath (not site-relative, over 2000 chars) is a 400.
/// Query strings are ignored for matching; the site re-attaches them to the target.
/// </summary>
[ApiController]
[Route("api/v1/redirects")]
[AllowAnonymous]
public class RedirectController : ControllerBase
{
    private readonly IRedirectResolver _resolver;

    public RedirectController(IRedirectResolver resolver) => _resolver = resolver;

    [HttpGet("resolve")]
    [ProducesResponseType(typeof(RedirectResolveResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Resolve([FromQuery] string? path, CancellationToken ct)
    {
        if (RedirectResolver.Normalize(path) is null)
            return BadRequest(new { error = "path must be a site-relative path starting with '/'." });

        var hit = await _resolver.ResolveAsync(path!, ct);

        var ttl = (int)_resolver.CacheTtl.TotalSeconds;
        Response.Headers[HeaderNames.CacheControl] = ttl > 0 ? $"public, max-age={ttl}" : "no-store";

        if (hit is null) return NotFound();
        return Ok(new RedirectResolveResponse(hit.FromPath, hit.ToPath, hit.StatusCode));
    }
}

/// <summary>Body of GET /api/v1/redirects/resolve on a hit.</summary>
public sealed record RedirectResolveResponse(string FromPath, string ToPath, int StatusCode);
