using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Encodings.Web;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Markdown;

namespace VA.CMS.API.Controllers;

/// <summary>
/// Live content preview endpoints — issue #34, BRD FR-AUTH-08, and issue #65 FR-AUTH-02.
///
///   POST /api/v1/content/{id}/preview-token  — Issue a signed preview token (CanRead).
///   GET  /api/v1/preview                     — Render preview HTML (token-authenticated, no login required).
///   POST /api/v1/preview/render              — Render arbitrary Markdown to HTML (CanRead) — issue #65.
///
/// The preview token is a self-contained, HMAC-signed, 60-minute token that encodes
/// the content entry id.  It does not require a database row and survives API restarts
/// only as long as the signing key is constant (expected for the dev container).
///
/// The GET /preview endpoint accepts optional `fields` query param (URL-encoded JSON)
/// containing the current unsaved form state, allowing preview of content that has not
/// yet been saved to the database (AC 3: reflects current unsaved form state).
///
/// POST /api/v1/preview/render (issue #65):
///   Accepts { markdown: string }, runs it through the same Markdig DisableHtml()
///   pipeline used at publish time, returns { html: string }.  Used by the Milkdown
///   editor split-pane preview, debounced 500ms.  No entry id or token required —
///   just a valid JWT (CanRead) to avoid anonymous abuse.
/// </summary>
[ApiController]
public class PreviewController : ControllerBase
{
    private readonly IPreviewTokenService     _tokens;
    private readonly IContentEntryRepository  _entries;
    private readonly IContentVersionRepository _versions;
    private readonly IUswdsMarkdownRenderer   _markdown;

    public PreviewController(
        IPreviewTokenService     tokens,
        IContentEntryRepository  entries,
        IContentVersionRepository versions,
        IUswdsMarkdownRenderer   markdown)
    {
        _tokens   = tokens;
        _entries  = entries;
        _versions = versions;
        _markdown = markdown;
    }

    // ── POST /api/v1/content/{id}/preview-token ───────────────────────────────

    /// <summary>
    /// Issue a signed preview token for the given content entry.
    /// Requires an authenticated session (CanRead).  Returns a token valid for 60 minutes.
    ///
    /// AC: Preview does not require publishing — a token is minted for any draft.
    /// </summary>
    [HttpPost("api/v1/content/{id:long}/preview-token")]
    [Authorize(Policy = CmsRoles.Policies.CanRead)]
    [ProducesResponseType(typeof(PreviewTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IssueToken(long id)
    {
        var entry = await _entries.GetByIdAsync(id);
        if (entry is null) return NotFound();

        var token = _tokens.Issue(id);
        return Ok(new PreviewTokenResponse(token, ExpiresInSeconds: 3600));
    }

    // ── POST /api/v1/preview/render ───────────────────────────────────────────

    /// <summary>
    /// Render arbitrary Markdown to HTML using the same Markdig DisableHtml() pipeline
    /// used at publish time.  Issue #65 — Milkdown split-pane live preview.
    ///
    /// Called by the admin SPA MarkdownField component, debounced 500ms, as the user
    /// types in the Milkdown editor.  Returns raw HTML fragment (not a full page).
    ///
    /// DisableHtml() is enforced on the server side — raw HTML entries in the Markdown
    /// source are stripped, matching the publish pipeline exactly.
    ///
    /// Requires CanRead — no anonymous access.
    /// </summary>
    [HttpPost("api/v1/preview/render")]
    [Authorize(Policy = CmsRoles.Policies.CanRead)]
    [ProducesResponseType(typeof(PreviewRenderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult RenderMarkdown([FromBody] PreviewRenderRequest request)
    {
        if (request is null || request.Markdown is null)
            return BadRequest(new { error = "markdown field is required." });

        var html = _markdown.Render(request.Markdown);
        return Ok(new PreviewRenderResponse(html));
    }

    // ── GET /api/v1/preview ───────────────────────────────────────────────────

    /// <summary>
    /// Render the USWDS public preview page for a content entry.
    /// Authentication: signed preview token (no session cookie required).
    ///
    /// Query params:
    ///   token  — required.  Signed preview token from POST /preview-token.
    ///   fields — optional. URL-encoded JSON of current field values from the admin form
    ///            (unsaved state).  When present, these values override the saved draft
    ///            for the preview — no publish required.
    ///
    /// Returns an HTML document using the USWDS template.
    /// </summary>
    [HttpGet("api/v1/preview")]
    [AllowAnonymous]           // token validates the caller; no JWT session needed
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Render(
        [FromQuery] string? token,
        [FromQuery] string? fields)
    {
        // ── 1. Validate token ─────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { error = "token query parameter is required." });

        var entryId = _tokens.Validate(token);
        if (entryId is null)
            return Unauthorized(new { error = "Preview token is invalid or expired." });

        // ── 2. Load entry ─────────────────────────────────────────────────────
        var entry = await _entries.GetByIdAsync(entryId.Value);
        if (entry is null) return NotFound();

        // ── 3. Resolve field values ────────────────────────────────────────────
        //   Priority: unsaved form state (fields param) > latest saved version
        Dictionary<string, object?> fieldValues;

        if (!string.IsNullOrWhiteSpace(fields))
        {
            // AC 3: reflect current unsaved form state passed via query param
            try
            {
                fieldValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(fields,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new Dictionary<string, object?>();
            }
            catch
            {
                return BadRequest(new { error = "fields parameter is not valid JSON." });
            }
        }
        else
        {
            // Fall back to the latest saved version for this entry
            var versions = await _versions.ListWithAuthorAsync(entryId.Value, page: 1, pageSize: 1);
            var latestVersion = versions.FirstOrDefault();

            if (latestVersion is not null)
            {
                try
                {
                    fieldValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                        latestVersion.FieldsJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? new Dictionary<string, object?>();
                }
                catch
                {
                    fieldValues = new Dictionary<string, object?>();
                }
            }
            else
            {
                fieldValues = new Dictionary<string, object?>();
            }
        }

        // ── 4. Render fields using same Markdig pipeline as publish ───────────
        var renderedFields = new Dictionary<string, string?>();
        foreach (var (key, value) in fieldValues)
        {
            var raw = value?.ToString();
            // Render markdown fields; other types pass through as plain text
            renderedFields[key] = string.IsNullOrEmpty(raw) ? raw : _markdown.Render(raw);
        }

        // ── 5. Emit USWDS public template HTML ────────────────────────────────
        var title       = GetField(fieldValues, "title")    ?? entry.Slug;
        var summary     = GetField(fieldValues, "summary");
        var bodyHtml    = GetRendered(renderedFields, "body");
        var slug        = entry.Slug;

        var html = BuildPreviewHtml(title, summary, bodyHtml, slug);
        return Content(html, "text/html; charset=utf-8");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? GetField(Dictionary<string, object?> fields, string key)
    {
        if (fields.TryGetValue(key, out var val))
            return val?.ToString();

        // Case-insensitive fallback
        foreach (var kv in fields)
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                return kv.Value?.ToString();

        return null;
    }

    private static string? GetRendered(Dictionary<string, string?> rendered, string key)
    {
        if (rendered.TryGetValue(key, out var val)) return val;
        foreach (var kv in rendered)
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        return null;
    }

    /// <summary>
    /// Generates a complete USWDS-compliant preview HTML page.
    /// Uses the same visual structure as the public Next.js site.
    /// Marks the page clearly as PREVIEW to avoid confusion with published content.
    /// </summary>
    private static string BuildPreviewHtml(
        string  title,
        string? summary,
        string? bodyHtml,
        string  slug)
    {
        var encoder = HtmlEncoder.Default;
        var safeTitle   = encoder.Encode(title);
        var safeSummary = summary is not null ? encoder.Encode(summary) : null;
        var safeSlug    = encoder.Encode(slug);

        // bodyHtml comes from Markdig (DisableHtml()) — safe, no raw HTML passthrough.
        var bodySection = string.IsNullOrEmpty(bodyHtml)
            ? "<p class=\"usa-prose\"><em>No body content yet.</em></p>"
            : bodyHtml;

        var summaryHtml = safeSummary is not null
            ? $"<p class=\"usa-intro\">{safeSummary}</p>"
            : string.Empty;

        // Use $$""" raw string so CSS braces don't need doubling
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="UTF-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Preview: {{safeTitle}}</title>
              <link rel="stylesheet" href="https://designsystem.digital.gov/assets/dist/css/uswds.min.css">
              <style>
                .usa-preview-banner {
                  background: #fce9ac;
                  border-bottom: 2px solid #947100;
                  color: #1b1b1b;
                  font-weight: 700;
                  padding: 0.5rem 1rem;
                  text-align: center;
                }
              </style>
            </head>
            <body>
              <!-- PREVIEW BANNER: not visible on the published site -->
              <div class="usa-preview-banner" role="status" aria-label="Preview mode">
                &#9888; PREVIEW &#8212; This page has not been published. Changes are not live.
              </div>

              <!-- USWDS Official Banner (mirrored from public site) -->
              <section class="usa-banner" aria-label="Official website of the United States government">
                <div class="usa-accordion">
                  <header class="usa-banner__header">
                    <div class="usa-banner__inner">
                      <div class="grid-col-auto">
                        <img class="usa-banner__header-flag"
                             src="https://designsystem.digital.gov/assets/img/us_flag_small.png"
                             alt="U.S. flag">
                      </div>
                      <div class="grid-col-fill tablet:grid-col-auto">
                        <p class="usa-banner__header-text">An official website of the United States government</p>
                        <p class="usa-banner__header-action" aria-hidden="true">Here's how you know</p>
                      </div>
                    </div>
                  </header>
                </div>
              </section>

              <!-- Site header -->
              <header class="usa-header usa-header--basic" role="banner">
                <div class="usa-nav-container">
                  <div class="usa-navbar">
                    <div class="usa-logo">
                      <em class="usa-logo__text">Department of Veterans Affairs</em>
                    </div>
                  </div>
                </div>
              </header>

              <!-- Main content -->
              <main id="main-content">
                <div class="grid-container">
                  <!-- Breadcrumb -->
                  <nav class="usa-breadcrumb" aria-label="Breadcrumbs">
                    <ol class="usa-breadcrumb__list">
                      <li class="usa-breadcrumb__list-item">
                        <a class="usa-breadcrumb__link" href="/">Home</a>
                      </li>
                      <li class="usa-breadcrumb__list-item usa-current" aria-current="page">
                        <span>{{safeTitle}}</span>
                      </li>
                    </ol>
                  </nav>

                  <div class="usa-section">
                    <h1>{{safeTitle}}</h1>

                    {{summaryHtml}}

                    <div class="usa-prose">
                      {{bodySection}}
                    </div>
                  </div>
                </div>
              </main>

              <!-- Footer -->
              <footer class="usa-footer usa-footer--slim">
                <div class="grid-container usa-footer__return-to-top">
                  <a href="#">Return to top</a>
                </div>
                <div class="usa-footer__primary-section">
                  <div class="usa-footer__primary-container grid-row">
                    <div class="mobile-lg:grid-col-8">
                      <p class="usa-footer__logo-heading">Department of Veterans Affairs</p>
                    </div>
                  </div>
                </div>
              </footer>

              <!-- USWDS JS -->
              <script src="https://designsystem.digital.gov/assets/dist/js/uswds-init.min.js"></script>
              <script src="https://designsystem.digital.gov/assets/dist/js/uswds.min.js"></script>
            </body>
            </html>
            """;
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>Response from POST /preview-token.</summary>
public sealed record PreviewTokenResponse(
    string Token,
    int    ExpiresInSeconds);

/// <summary>
/// Request body for POST /api/v1/preview/render (issue #65).
/// Accepts a CommonMark Markdown string to render via Markdig.
/// </summary>
public sealed record PreviewRenderRequest(string? Markdown);

/// <summary>
/// Response from POST /api/v1/preview/render — issue #66, BRD FR-AUTH-02b.
/// Returns sanitised HTML fragment (not a full page) — DisableHtml() applied.
/// Field name is renderedHtml per acceptance criteria.
/// </summary>
public sealed record PreviewRenderResponse(string RenderedHtml);
