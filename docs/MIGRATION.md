# SharePoint 2016 Migration

**Status:** epic #13 in progress. This document covers what is built: the export package format,
the farm-side exporter, the `--dry-run` validation/inventory pass (#191) and the page-body
normaliser that turns SharePoint HTML into the Markdown the CMS stores (#192). The import itself —
pages → Draft entries (#193), documents → media (#194), user mapping (#195), report files (#196) —
lands in the follow-on stories and this page grows with them. BRD § 11, MIG-01 … MIG-05.

## How the migration works

Nothing talks to SharePoint over the network from the CMS side, and nothing leaves the VA network
(the CMS is on-prem only). The path is:

```
SharePoint 2016 WFE                                   CMS host
───────────────────                                   ────────
Export-VacmsSharePoint.ps1   ──copy the folder──▶     vacms migrate sharepoint --package <dir> --dry-run
  (server object model,                                 validates + inventories, writes nothing
   farm admin, read-only)                             vacms migrate sharepoint --package <dir>
                                                        imports as Draft content (#193 onward)
```

1. **Export on the farm.** A SharePoint administrator runs `infra/sharepoint/Export-VacmsSharePoint.ps1`
   on a web front end. It walks the site's pages, document libraries and users and writes a plain
   folder — the *package*. SharePoint is never modified.
2. **Hand the package over.** The folder is copied (file share, removable media, whatever the
   office's data-handling rules allow) to the machine that runs the `vacms` CLI against the CMS
   database. `manifest.json` is human-readable; an ISSO can inspect exactly what is in it.
3. **Dry-run.** `vacms migrate sharepoint --package <dir> --dry-run` reads the package, checks it,
   and prints an inventory and every problem found. Exit 0 means an import could start; exit 1
   means fix the export first. Nothing is written.
4. **Import** (follow-on stories). Pages become Draft `standard_page` entries owned by the mapped
   CMS user; documents go through the normal upload pipeline (MIME sniffing, virus scan); a report
   is written beside the package. Content is reviewed and published from the admin UI like any
   other draft — the migration never publishes.

## Running the exporter

Requirements: Windows PowerShell 5.1 on a SharePoint 2016 server (the `Microsoft.SharePoint.PowerShell`
snap-in is not available from PowerShell 7 or from a workstation), run as a farm administrator with
read access to the site collection.

```powershell
# List what would be exported; write nothing
.\Export-VacmsSharePoint.ps1 -SiteUrl https://intranet.va.gov/sites/vba-example `
                             -OutputPath D:\exports\vba-example -Recurse -WhatIf

# Export the site and all sub-webs, resolving UPNs from Active Directory
.\Export-VacmsSharePoint.ps1 -SiteUrl https://intranet.va.gov/sites/vba-example `
                             -OutputPath D:\exports\vba-example -Recurse -ResolveUpnFromAd
```

| Parameter | Meaning |
|---|---|
| `-SiteUrl` | The web to export. With `-Recurse`, every sub-web beneath it too (one manifest). |
| `-OutputPath` | Package folder. Must be empty unless `-Force`. |
| `-ExcludeLibrary <patterns>` | Document libraries to skip, wildcards allowed. Structural libraries (Master Page Gallery, Style Library, Form Templates, …) are always skipped; `Site Assets`, `Images` and `Site Collection Images` are always included because pages embed pictures from them. |
| `-MaxFileSizeMB` | Files above this are left out with a warning. Default 100, the CMS `media.maxUploadBytes` default. |
| `-ResolveUpnFromAd` | Fill `users[].upn` from AD (`System.DirectoryServices.AccountManagement`). Without it, the CMS matches users on the SharePoint e-mail column, which is usually the UPN anyway. |
| `-WhatIf` | Standard PowerShell: prints every page/document/manifest write that would happen. |

What the exporter reads, per web:

| SharePoint source | Package |
|---|---|
| `Pages` library items (publishing pages) — `PublishingPageContent` | `pages[]` with `layout: publishing` |
| `Site Pages` items with a `WikiField` (wiki pages) | `pages[]` with `layout: wiki` |
| Other `.aspx` in those libraries (web part pages) — raw file bytes | `pages[]` with `layout: webpartpage` (best effort; usually no importable body) |
| Every visible document library, every file | `documents[]` + the file under `documents/<Library>/…` |
| `SPWeb.SiteUsers`, plus any page/document author or editor no longer in the site | `users[]` |

## Package format `vacms-sharepoint-export/1`

```
<package>/
  manifest.json
  pages/<page id>.html            raw body HTML as SharePoint stored it (not cleaned — that is the importer's job, #192)
  documents/<Library>/<path>      files exactly as they were in the library; sub-web files are prefixed with the sub-web path
```

Every path in the manifest is relative to the package root, uses `/`, and must resolve inside the
package. The reader rejects rooted paths (`/etc/…`, `C:\…`, `\\server\…`) and anything that walks
out with `..`, even if the target exists.

### `manifest.json`

```jsonc
{
  "format": "vacms-sharepoint-export/1",          // required, exact
  "exportedAt": "2026-09-18T13:05:00Z",           // ISO-8601, informational
  "exporterVersion": "Export-VacmsSharePoint.ps1/1.0",
  "sourceWeb": { "url": "https://…/sites/vba-example", "title": "…", "id": "<web GUID>" },  // url required
  "pages":     [ … ],
  "documents": [ … ],
  "users":     [ … ]
}
```

**`pages[]`**

| Field | Required | Meaning |
|---|---|---|
| `id` | yes | List item `UniqueId` (GUID). Stable across re-exports; becomes the migration source id so re-runs are idempotent (#193). Must be unique. |
| `url` | yes | Server-relative URL, e.g. `/sites/vba-example/Pages/About-Us.aspx`. The CMS slug is derived from it. |
| `title` | no | Page title. Missing → the file name is used and a warning is recorded. |
| `layout` | no | `publishing`, `wiki` or `webpartpage`. Unknown → treated as `webpartpage` with a warning. |
| `contentFile` | yes | Package-relative path of the body HTML. Must exist. |
| `status` | no | `published`, `draft` or `checkedout` (SharePoint `SPFile.Level`). Unknown → `draft` with a warning. Note: every page imports as a CMS **Draft** regardless; this is recorded for the report. |
| `description` | no | Publishing page description / comments → CMS `summary`. |
| `author`, `editor` | no | Claims logins matching `users[].login`. An author not in `users[]` is a warning. |
| `created`, `modified` | no | ISO-8601. |

**`documents[]`**

| Field | Required | Meaning |
|---|---|---|
| `id` | yes | List item `UniqueId`, unique. |
| `library` | no | Library title (default `Documents`). |
| `url` | yes | Server-relative URL of the file; page links to it are rewritten to the imported media URL (#192/#194). |
| `file` | yes | Package-relative path of the file. Must exist. |
| `sizeBytes` | no | Declared size; a mismatch with the file on disk is a warning. |
| `contentType` | no | MIME as SharePoint reported it. Advisory: the importer sniffs the bytes (#194). |
| `title`, `altText`, `author`, `modified` | no | Carried to the media asset. Missing `altText` on an image is a warning at import — alt text is required before the asset can be used. |

**`users[]`**

| Field | Required | Meaning |
|---|---|---|
| `login` | yes | Claims login, e.g. `i:0#.w|VA\jsmith`, unique. |
| `upn` | no | UPN from AD when the exporter could resolve it. |
| `email` | no | SharePoint e-mail column. |
| `displayName` | no | |

A user with neither `upn` nor `email` cannot be matched to a CMS account and is reported as a
warning (`user-principal-missing`); #195 adds `--user-map` and `--default-owner` to resolve those.

### Validation

`vacms migrate sharepoint --package <dir> --dry-run` reports every problem in one pass, errors first:

| Code | Severity | Cause |
|---|---|---|
| `package-missing`, `manifest-missing`, `manifest-invalid` | error | Folder or `manifest.json` missing, or not valid JSON. |
| `format-unsupported` | error | `format` is not `vacms-sharepoint-export/1`. |
| `source-web-missing` | error | `sourceWeb.url` missing. |
| `path-escapes-package` | error | A `contentFile` / `file` is rooted or resolves outside the package. |
| `page-id-missing`, `page-id-duplicate`, `page-url-missing`, `page-content-missing` | error | Page identity or body problems; the offending page is dropped, the rest is still inventoried. |
| `document-id-missing`, `document-id-duplicate`, `document-url-missing`, `document-file-missing` | error | Document identity or file problems. |
| `user-login-missing`, `user-login-duplicate` | error | |
| `page-title-missing`, `page-layout-unknown`, `page-status-unknown`, `page-author-unknown` | warning | Defaults applied; recorded for the report. |
| `document-size-mismatch` | warning | Possible partial copy — worth checking before import. |
| `user-principal-missing` | warning | Needs `--user-map` / `--default-owner` (#195). |

Any error → exit 1 and "package is NOT importable". Warnings alone → exit 0.

### Sample package

`src/api/VA.CMS.Tests/Fixtures/SharePoint/sample-export/` is a small but realistic SP2016 site
(five publishing pages, two wiki pages, one web part page, five documents across two libraries,
four users, one of them with no e-mail). It is the input for the reader tests (`Issue191AcceptanceTests`)
and for the epic's exit check once the import lands. Its page bodies carry real SharePoint markup
(`ms-rte*` classes, inline styles, `<font>`, `<o:p>`, layout tables, `_layouts` and `javascript:`
links) so the normaliser (#192) has something honest to chew on.

```
$ vacms migrate sharepoint --package src/api/VA.CMS.Tests/Fixtures/SharePoint/sample-export --dry-run
SharePoint export package: …/sample-export
  Format:      vacms-sharepoint-export/1   exported 2026-09-18 13:05 UTC   exporter Export-VacmsSharePoint.ps1/1.0
  Source web:  https://intranet.example.va.gov/sites/vba-example   "VBA Example Regional Office"

Pages: 8
  by layout   publishing 5, wiki 2, webpartpage 1
  by status   published 6, checkedout 1, draft 1
Documents: 5 (1 KB)
  by extension  .pdf 2 (701 B), .png 2 (140 B), .csv 1 (194 B)
  by library    Documents 3, Site Assets 2
Users: 4   with UPN/email 3, without 1

Problems: 0 error(s), 1 warning(s)
  WARN   user-principal-missing  user 'i:0#.w|VA\legacyuser' has no UPN or email; it will need a --user-map entry or --default-owner to import (#195)

Result: package is importable.
```

## Page bodies: SharePoint HTML → Markdown (#192, MIG-02)

The CMS stores rich text as CommonMark Markdown and renders it through Markdig with raw HTML
disabled (BRD FR-AUTH-02), so a SharePoint page body cannot be imported as-is — every `<span style>`
would show up as literal text. `SharePointHtmlNormalizer.Normalize(html, pageId, links, pageUrl)`
turns the exported body into Markdown that renders as plain USWDS markup, and returns a
`NormalizedBody { Markdown, Warnings[] }`. It is a pure function: no I/O, no network, no clock;
the same input always gives the same output.

**What survives, and how**

| SharePoint / Word markup | Markdown |
|---|---|
| `h1` | `##` — the page title is the `h1`; body headings start at `h2` |
| `h2`–`h6`, `p`, `br` | `##`…`######`, paragraphs, backslash hard breaks |
| `ul`/`ol`/`li` (nested, `start`) | `-` / `1.` lists |
| `strong`/`b`, `em`/`i`, `s`/`del`, `code`, `pre` | `**`, `*`, `~~`, backticks, fenced blocks |
| `blockquote`, `hr` | `>`, `---` |
| `table` with 2+ rows and columns, no `rowspan`/`colspan`, only inline content in cells | GFM pipe table; the first row is the header (a warning if it was not `th`) |
| any other `table` (layout) | unwrapped — cell contents in reading order, with a warning |
| `img` | `![alt](media url)`; a missing `alt` attribute is a warning, an explicit `alt=""` is decorative |
| `a` | see link rules below |
| Word `MsoListParagraph` runs | real lists; the bullet glyph and `mso-list … levelN` drive ordered/unordered and nesting |
| `span`, `font`, `u`, `div`, `center`, smart tags, unknown elements | unwrapped |
| `style`, `class`, `id`, every other attribute; `<o:p>`, `<xml>`, `<style>`, `<script>`, VML, `&nbsp;` runs | removed silently (nothing a reader saw) |
| web-part zones, embed boxes, placeholders; `iframe`, `object`, `embed`, `video`, form controls | dropped with a warning |

Prose that happens to look like Markdown (`5 * 3`, `snake_case`, a line starting with `1.`) is
escaped so it renders as typed.

**Link rules.** The normaliser never guesses a CMS URL; the importer hands it a
`SharePointLinkResolver` with two callbacks (page path → CMS path, document path → media URL) and
the source web URL:

| href | Result |
|---|---|
| `/sites/<x>/…/Foo.aspx` (also `Foo.aspx`, `../Pages/Foo.aspx`, or absolute on the source host) | page resolver → e.g. `/foo`; fragment kept |
| any other site path (document libraries, Site Assets) | document resolver → media URL |
| resolver returns `null` | link dropped, text kept, `link-unresolved` / `image-unresolved` |
| `/_layouts/…` | dropped, text kept, `link-layouts-dropped` |
| `javascript:`, `vbscript:`, `data:`, any other scheme | removed, text kept, `link-script-removed` |
| `https://` on another host, `mailto:`, `tel:`, `#anchor` | kept verbatim |

**Warnings** carry the page id and the offending source markup so the report (#196) can point a
content owner at the exact element:

| Code | Meaning |
|---|---|
| `image-alt-missing` | Image kept with empty alt; needs alt text in the CMS before publishing (508). |
| `image-unresolved` | Image source not in the package (or `_layouts`/`data:`); image dropped, alt text kept as text. |
| `link-layouts-dropped` | Link to a SharePoint system page; text kept. |
| `link-script-removed` | `javascript:`/other-scheme link removed; text kept. |
| `link-unresolved` | Site link neither resolver knew; text kept. |
| `layout-table-unwrapped` | Table used for layout; check the reading order of the result. |
| `table-header-inferred` | Data table without `th`; first row promoted to header. |
| `webpart-dropped` | A web part; its output is not in the export. |
| `element-dropped` | `iframe`, embedded media, form control …; nothing in Markdown can hold it. |

The golden files under `src/api/VA.CMS.Tests/Fixtures/SharePoint/html/` (`<case>.html` → `<case>.md`)
are the specification: publishing page, wiki page, nested layout table, data table, Word paste,
images, links, structure, web part page. To change the normaliser's output deliberately, run
`VACMS_UPDATE_GOLDENS=1 dotnet test --filter Issue192` and review the diff.

## Code map

| Piece | Where |
|---|---|
| Exporter | `infra/sharepoint/Export-VacmsSharePoint.ps1` |
| Package model | `src/api/VA.CMS.Infrastructure/Migration/SharePoint/SharePointExportPackage.cs` |
| Reader + validation | `…/Migration/SharePoint/SharePointExportReader.cs` |
| Inventory text | `…/Migration/SharePoint/MigrationInventory.cs` |
| HTML → Markdown normaliser | `…/Migration/SharePoint/SharePointHtmlNormalizer.cs`, `NormalizedBody.cs` (AngleSharp, pinned in `Directory.Packages.props`) |
| CLI command | `src/api/VA.CMS.CLI/Program.cs` — `vacms migrate sharepoint` |
| Tests + sample | `src/api/VA.CMS.Tests/Issue191AcceptanceTests.cs`, `…/Fixtures/SharePoint/sample-export/` |
| Normaliser tests + golden files | `src/api/VA.CMS.Tests/Issue192AcceptanceTests.cs`, `…/Fixtures/SharePoint/html/` |

## Remaining stories

| Story | Delivers |
|---|---|
| #193 | Page import to Draft entries, `MigrationSourceMap` for idempotent re-runs, conflict detection (MIG-01) |
| #194 | Document library → media library through `MediaUploadService` (MIG-03) |
| #195 | User → CMS user mapping, `--user-map`, `--default-owner` (MIG-04) |
| #196 | `migration-report.json` / `.md`, exit codes, end-to-end runbook, epic exit measurement (MIG-05) |
