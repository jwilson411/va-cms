using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VA.CMS.API.Auth;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Controllers.Admin;

/// <summary>
/// Admin content entry list endpoint (issue #29, FR-AUTH-01).
///
///   GET /api/v1/admin/content-entries
///       ?contentTypeId=&amp;status=&amp;authorSearch=&amp;dateFrom=&amp;dateTo=
///       &amp;sortBy=UpdatedAt&amp;sortDir=DESC&amp;page=1&amp;pageSize=25
///
/// Returns a paginated list of content entries with joined ContentType name,
/// Author display name, and title (from latest version's FieldsJson).
/// Used exclusively by the admin SPA content entry list screen.
/// </summary>
[ApiController]
[Route("api/v1/admin/content-entries")]
[Authorize(Policy = CmsRoles.Policies.CanRead)]
public class ContentEntryListController : ControllerBase
{
    private readonly IContentEntryRepository _entries;
    private readonly ISiteSettingsService    _settings;

    public ContentEntryListController(IContentEntryRepository entries, ISiteSettingsService settings)
    {
        _entries  = entries;
        _settings = settings;
    }

    /// <summary>
    /// List content entries for the admin list screen.
    /// Paginated 25/page; supports filter by type, status, author, date range.
    /// Sortable by Title, Status, UpdatedAt.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ContentEntryAdminPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] long?    contentTypeId = null,
        [FromQuery] string?  status        = null,
        [FromQuery] string?  authorSearch  = null,
        [FromQuery] DateTime? dateFrom     = null,
        [FromQuery] DateTime? dateTo       = null,
        [FromQuery] string   sortBy        = "UpdatedAt",
        [FromQuery] string   sortDir       = "DESC",
        [FromQuery] int      page          = 1,
        [FromQuery] int      pageSize      = 25)
    {
        // Clamp pageSize to [1, api.maxPageSize]
        pageSize = _settings.ClampPageSize(pageSize);
        page     = Math.Max(1, page);

        var result = await _entries.ListAdminAsync(
            contentTypeId, status, authorSearch,
            dateFrom, dateTo,
            sortBy, sortDir, page, pageSize);

        return Ok(new ContentEntryAdminPageDto(result));
    }
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

/// <summary>API response shape for the admin content entry list.</summary>
public sealed class ContentEntryAdminPageDto
{
    public int TotalRows  { get; }
    public int TotalPages { get; }
    public int Page       { get; }
    public int PageSize   { get; }
    public IReadOnlyList<ContentEntryAdminRowDto> Items { get; }

    public ContentEntryAdminPageDto(ContentEntryAdminPage page)
    {
        TotalRows  = page.TotalRows;
        TotalPages = page.TotalPages;
        Page       = page.Page;
        PageSize   = page.PageSize;
        Items      = page.Items.Select(r => new ContentEntryAdminRowDto(r)).ToList();
    }
}

/// <summary>Single row in the admin content entry list response.</summary>
public sealed class ContentEntryAdminRowDto
{
    public long   Id               { get; }
    public string Slug             { get; }
    public string Title            { get; }
    public string ContentTypeName  { get; }
    public string AuthorDisplayName { get; }
    public string Status           { get; }
    public string LastModified     { get; }  // ISO-8601 UTC

    public ContentEntryAdminRowDto(ContentEntryAdminRow row)
    {
        Id                = row.Id;
        Slug              = row.Slug;
        Title             = row.Title;
        ContentTypeName   = row.ContentTypeName;
        AuthorDisplayName = row.AuthorDisplayName;
        Status            = row.Status;
        LastModified      = row.UpdatedAt.ToString("o");
    }
}
