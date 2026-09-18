<#
.SYNOPSIS
    Exports a SharePoint 2016 site to a VA CMS migration package (epic #13, BRD MIG-01).

.DESCRIPTION
    Runs ON a SharePoint 2016 web front end (server object model, Microsoft.SharePoint.PowerShell
    snap-in) as a farm administrator who has read access to the site. Writes a folder that
    `vacms migrate sharepoint` reads offline:

        <OutputPath>\
          manifest.json                  format "vacms-sharepoint-export/1"
          pages\<item UniqueId>.html      raw body of every publishing / wiki / web part page
          documents\<Library>\<path>      every file in every visible document library

    Nothing leaves the network: the package is a plain folder the office copies to the CMS host.
    Full field reference: docs/MIGRATION.md in the va-cms repository.

    The script never modifies SharePoint. Re-running against the same output folder replaces the
    package (use -Force when the folder is not empty).

.PARAMETER SiteUrl
    URL of the web (site) to export, e.g. https://intranet.va.gov/sites/vba-example

.PARAMETER OutputPath
    Folder to write the package into. Created if missing.

.PARAMETER Recurse
    Also export every sub-web under -SiteUrl (pages and documents are prefixed with the sub-web's
    server-relative URL; one manifest covers all of them).

.PARAMETER ExcludeLibrary
    Document-library titles to skip (wildcards allowed). System libraries (Pages, Site Pages,
    Master Page Gallery, Style Library, Form Templates, …) are always skipped.

.PARAMETER MaxFileSizeMB
    Files larger than this are listed in the manifest with "skipped": "too-large" and not copied.
    Default 100 (the CMS media.maxUploadBytes default).

.PARAMETER ResolveUpnFromAd
    Look each site user up in Active Directory (System.DirectoryServices.AccountManagement) to
    fill users[].upn. Without it, upn is null and the CMS matches on the SharePoint e-mail column.

.PARAMETER Force
    Overwrite a non-empty -OutputPath.

.EXAMPLE
    .\Export-VacmsSharePoint.ps1 -SiteUrl https://intranet.va.gov/sites/vba-example -OutputPath D:\exports\vba-example -Recurse -ResolveUpnFromAd

.EXAMPLE
    .\Export-VacmsSharePoint.ps1 -SiteUrl https://intranet.va.gov/sites/vba-example -OutputPath D:\exports\vba-example -WhatIf
    Lists every page, document and user that would be exported; writes nothing.

.NOTES
    Version 1.0 — exporterVersion "Export-VacmsSharePoint.ps1/1.0". Requires Windows PowerShell 5.1
    on the SharePoint server; the SharePoint snap-in is not available in PowerShell 7.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)] [string]   $SiteUrl,
    [Parameter(Mandatory = $true)] [string]   $OutputPath,
    [switch]   $Recurse,
    [string[]] $ExcludeLibrary = @(),
    [int]      $MaxFileSizeMB  = 100,
    [switch]   $ResolveUpnFromAd,
    [switch]   $Force
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

$script:ExporterVersion = 'Export-VacmsSharePoint.ps1/1.0'
$script:Format          = 'vacms-sharepoint-export/1'
# Structural libraries that are never content. "Site Assets", "Images" and "Site Collection Images"
# hold the pictures pages embed, so they ARE exported.
$script:SystemLibraries = @(
    'Pages', 'Site Pages', 'Master Page Gallery', 'Style Library', 'Form Templates', 'Site Collection Documents',
    'List Template Gallery', 'Web Part Gallery', 'Theme Gallery', 'Solution Gallery', 'Converted Forms',
    'Customized Reports', 'wfpub', 'Reusable Content', 'Content and Structure Reports'
)

# ── SharePoint snap-in ────────────────────────────────────────────────────────────────────────
if (-not (Get-PSSnapin -Name Microsoft.SharePoint.PowerShell -ErrorAction SilentlyContinue)) {
    Add-PSSnapin Microsoft.SharePoint.PowerShell
}
Add-Type -AssemblyName System.Web   # MimeMapping

# ── helpers ───────────────────────────────────────────────────────────────────────────────────
function Format-Utc([object] $value) {
    if ($null -eq $value) { return $null }
    return ([datetime]$value).ToUniversalTime().ToString('o')
}

function Get-SafeName([string] $name) {
    $invalid = [System.IO.Path]::GetInvalidFileNameChars() -join ''
    $pattern = '[{0}]' -f [regex]::Escape($invalid)
    return ($name -replace $pattern, '_')
}

function Get-PageStatus([Microsoft.SharePoint.SPFile] $file) {
    switch ($file.Level) {
        'Published' { return 'published' }
        'Draft'     { return 'draft' }
        'Checkout'  { return 'checkedout' }
        default     { return 'draft' }
    }
}

# Author/Editor columns hold "12;#Smith, Jane"; resolve to the claims login the users[] list uses.
function Get-UserLogin([Microsoft.SharePoint.SPWeb] $web, [object] $fieldValue) {
    if ([string]::IsNullOrEmpty([string]$fieldValue)) { return $null }
    try {
        $uv = New-Object Microsoft.SharePoint.SPFieldUserValue($web, [string]$fieldValue)
        if ($null -ne $uv.User) { return $uv.User.LoginName }
        return $uv.LookupValue
    } catch { return $null }
}

$script:Users = @{}
function Add-User([Microsoft.SharePoint.SPUser] $user) {
    if ($null -eq $user -or $user.IsDomainGroup) { return }
    $login = $user.LoginName
    if ($login -match '^(SHAREPOINT\\|NT AUTHORITY\\|c:0|i:0#\.w\|nt authority)' ) { return }
    if ($script:Users.ContainsKey($login)) { return }

    $upn = $null
    if ($ResolveUpnFromAd) {
        $upn = Resolve-Upn $login
    }
    $email = if ([string]::IsNullOrWhiteSpace($user.Email)) { $null } else { $user.Email }

    $script:Users[$login] = [ordered]@{
        login       = $login
        upn         = $upn
        email       = $email
        displayName = $user.Name
    }
}

function Resolve-Upn([string] $login) {
    # i:0#.w|VA\jsmith  →  VA \ jsmith
    if ($login -notmatch '\|?([^|\\]+)\\([^|\\]+)$') { return $null }
    $domain = $Matches[1]; $sam = $Matches[2]
    try {
        Add-Type -AssemblyName System.DirectoryServices.AccountManagement
        $ctx = New-Object System.DirectoryServices.AccountManagement.PrincipalContext('Domain', $domain)
        $p   = [System.DirectoryServices.AccountManagement.UserPrincipal]::FindByIdentity($ctx, 'SamAccountName', $sam)
        if ($null -ne $p -and -not [string]::IsNullOrWhiteSpace($p.UserPrincipalName)) { return $p.UserPrincipalName }
    } catch {
        Write-Warning "AD lookup failed for $login : $($_.Exception.Message)"
    }
    return $null
}

# ── output folder ─────────────────────────────────────────────────────────────────────────────
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if ((Test-Path $OutputPath) -and (Get-ChildItem $OutputPath -Force | Select-Object -First 1) -and -not $Force) {
    throw "Output folder '$OutputPath' is not empty. Pass -Force to overwrite it."
}
$pagesDir = Join-Path $OutputPath 'pages'
$docsDir  = Join-Path $OutputPath 'documents'
if ($PSCmdlet.ShouldProcess($OutputPath, 'Create package folder')) {
    if ($Force -and (Test-Path $OutputPath)) {
        Remove-Item (Join-Path $OutputPath '*') -Recurse -Force
    }
    New-Item -ItemType Directory -Path $pagesDir -Force | Out-Null
    New-Item -ItemType Directory -Path $docsDir  -Force | Out-Null
}

# ── walk the web(s) ───────────────────────────────────────────────────────────────────────────
$pages     = New-Object System.Collections.Generic.List[object]
$documents = New-Object System.Collections.Generic.List[object]
$maxBytes  = [long]$MaxFileSizeMB * 1024 * 1024

$rootWeb = Get-SPWeb $SiteUrl
$webs    = @($rootWeb)
if ($Recurse) {
    $prefix = $rootWeb.ServerRelativeUrl.TrimEnd('/') + '/'
    $webs  += @($rootWeb.Site.AllWebs | Where-Object { $_.ServerRelativeUrl.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) })
}

foreach ($web in $webs) {
    Write-Verbose "Web: $($web.Url)"
    foreach ($u in $web.SiteUsers) { Add-User $u }

    # ── pages: Publishing "Pages" (PublishingPageContent), wiki "Site Pages" (WikiField), else web part page ──
    foreach ($listTitle in @('Pages', 'Site Pages')) {
        $list = $web.Lists.TryGetList($listTitle)
        if ($null -eq $list) { continue }

        foreach ($item in $list.Items) {
            if ($item.FileSystemObjectType -ne [Microsoft.SharePoint.SPFileSystemObjectType]::File) { continue }
            $file = $item.File
            if ($null -eq $file -or -not $file.Name.EndsWith('.aspx', [System.StringComparison]::OrdinalIgnoreCase)) { continue }

            $layout = $null; $body = $null
            if ($list.Fields.ContainsField('PublishingPageContent') -and $listTitle -eq 'Pages') {
                $layout = 'publishing'; $body = [string]$item['PublishingPageContent']
            } elseif ($list.Fields.ContainsField('WikiField') -and -not [string]::IsNullOrEmpty([string]$item['WikiField'])) {
                $layout = 'wiki'; $body = [string]$item['WikiField']
            } else {
                $layout = 'webpartpage'
                $body   = [System.Text.Encoding]::UTF8.GetString($file.OpenBinary())
            }

            $id          = $item.UniqueId.ToString()
            $contentFile = "pages/$id.html"
            $description = $null
            foreach ($f in @('Comments', 'Description', 'PublishingPageDescription')) {
                if ($list.Fields.ContainsField($f) -and -not [string]::IsNullOrWhiteSpace([string]$item[$f])) { $description = [string]$item[$f]; break }
            }

            $pages.Add([ordered]@{
                id          = $id
                url         = $file.ServerRelativeUrl
                title       = [string]$item['Title']
                layout      = $layout
                contentFile = $contentFile
                status      = Get-PageStatus $file
                description = $description
                author      = Get-UserLogin $web $item['Author']
                editor      = Get-UserLogin $web $item['Editor']
                created     = Format-Utc $item['Created']
                modified    = Format-Utc $item['Modified']
            })

            if ($PSCmdlet.ShouldProcess($file.ServerRelativeUrl, "Export $layout page")) {
                [System.IO.File]::WriteAllText((Join-Path $OutputPath ($contentFile -replace '/', '\')), [string]$body, [System.Text.Encoding]::UTF8)
            }
        }
    }

    # ── document libraries ──
    foreach ($list in $web.Lists) {
        if ($list.BaseType -ne [Microsoft.SharePoint.SPBaseType]::DocumentLibrary) { continue }
        if ($list.Hidden -or $list.IsCatalog) { continue }
        if ($list.Title -in $script:SystemLibraries) { continue }
        if ($ExcludeLibrary | Where-Object { $list.Title -like $_ }) { Write-Verbose "Skipping library $($list.Title)"; continue }

        $libraryDir = Get-SafeName $list.Title
        $rootUrl    = $list.RootFolder.ServerRelativeUrl.TrimEnd('/')

        foreach ($item in $list.Items) {
            if ($item.FileSystemObjectType -ne [Microsoft.SharePoint.SPFileSystemObjectType]::File) { continue }
            $file = $item.File
            if ($null -eq $file) { continue }

            $relative = $file.ServerRelativeUrl.Substring($rootUrl.Length).TrimStart('/')
            if ($web.ID -ne $rootWeb.ID) {
                # sub-web files keep the sub-web path so two libraries with the same title cannot collide
                $relative = ($web.ServerRelativeUrl.Substring($rootWeb.ServerRelativeUrl.Length).Trim('/') + '/' + $relative)
            }
            $packageFile = "documents/$libraryDir/$relative"

            $altText = $null
            foreach ($f in @('ImageAlternateText', 'AlternateText', '_Comments', 'Description')) {
                if ($list.Fields.ContainsField($f) -and -not [string]::IsNullOrWhiteSpace([string]$item[$f])) { $altText = [string]$item[$f]; break }
            }

            $doc = [ordered]@{
                id          = $item.UniqueId.ToString()
                library     = $list.Title
                url         = $file.ServerRelativeUrl
                file        = $packageFile
                sizeBytes   = [long]$file.Length
                contentType = [System.Web.MimeMapping]::GetMimeMapping($file.Name)
                title       = if ([string]::IsNullOrWhiteSpace([string]$item['Title'])) { $file.Name } else { [string]$item['Title'] }
                altText     = $altText
                author      = Get-UserLogin $web $item['Author']
                modified    = Format-Utc $item['Modified']
            }

            if ($file.Length -gt $maxBytes) {
                Write-Warning "Skipping $($file.ServerRelativeUrl): $([math]::Round($file.Length / 1MB, 1)) MB exceeds -MaxFileSizeMB $MaxFileSizeMB"
                continue
            }

            $documents.Add($doc)
            if ($PSCmdlet.ShouldProcess($file.ServerRelativeUrl, 'Export document')) {
                $target = Join-Path $OutputPath ($packageFile -replace '/', '\')
                New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
                [System.IO.File]::WriteAllBytes($target, $file.OpenBinary())
            }
        }
    }
}

# Authors/editors who are no longer site users still need a users[] row so the CMS can map or default them.
foreach ($p in $pages)     { foreach ($k in 'author', 'editor') { $l = $p[$k]; if ($l -and -not $script:Users.ContainsKey($l)) { try { Add-User $rootWeb.EnsureUser($l) } catch { $script:Users[$l] = [ordered]@{ login = $l; upn = $null; email = $null; displayName = $l } } } } }
foreach ($d in $documents) { $l = $d['author']; if ($l -and -not $script:Users.ContainsKey($l)) { $script:Users[$l] = [ordered]@{ login = $l; upn = $null; email = $null; displayName = $l } } }

# ── manifest ──────────────────────────────────────────────────────────────────────────────────
$manifest = [ordered]@{
    format          = $script:Format
    exportedAt      = (Get-Date).ToUniversalTime().ToString('o')
    exporterVersion = $script:ExporterVersion
    sourceWeb       = [ordered]@{ url = $rootWeb.Url; title = $rootWeb.Title; id = $rootWeb.ID.ToString() }
    pages           = @($pages)
    documents       = @($documents)
    users           = @($script:Users.Values | Sort-Object { $_.login })
}

if ($PSCmdlet.ShouldProcess((Join-Path $OutputPath 'manifest.json'), 'Write manifest')) {
    $json = $manifest | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText((Join-Path $OutputPath 'manifest.json'), $json, (New-Object System.Text.UTF8Encoding($false)))
}
foreach ($web in $webs) { $web.Dispose() }

Write-Host ("Exported {0} page(s), {1} document(s), {2} user(s) from {3}" -f $pages.Count, $documents.Count, $script:Users.Count, $SiteUrl)
Write-Host "Package: $OutputPath"
Write-Host 'Next: copy the folder to the CMS host and run  vacms migrate sharepoint --package <folder> --dry-run'
