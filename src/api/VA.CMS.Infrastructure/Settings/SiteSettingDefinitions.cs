using System.Text.Json;

namespace VA.CMS.Infrastructure.Settings;

/// <summary>Value type of a <see cref="SiteSettingDefinition"/>; mirrors CK_SiteSetting_DataType.</summary>
public enum SiteSettingType { String, Int, Bool, Json }

/// <summary>
/// Who may read a setting. Mirrors CK_SiteSetting_Scope.
///   Server — only the API reads it; exposed solely through the SystemAdmin management endpoint.
///   Admin  — also returned by GET /api/v1/settings/client for the authenticated admin SPA.
///   Public — also returned by the anonymous GET /api/v1/settings/public for the Next.js site.
/// </summary>
public enum SiteSettingScope { Server, Admin, Public }

/// <summary>
/// A runtime setting declared in code. The row in [SiteSetting] is created/refreshed from this at
/// startup (usp_SiteSetting_EnsureDefinition); <see cref="Default"/> is also the in-memory fallback
/// when the database has not been read yet or is unreachable.
/// </summary>
public sealed record SiteSettingDefinition(
    string Key,
    SiteSettingType Type,
    string Default,
    string Category,
    SiteSettingScope Scope,
    string Description,
    int SortOrder = 0);

/// <summary>
/// Every key the application reads. Keep this the single source of truth — issue #142 (epic #141):
/// a new setting is added here and nowhere else; no migration, no appsettings entry.
/// </summary>
public static class SiteSettingKeys
{
    // Site chrome (Public)
    public const string SiteTitle           = "site.title";
    public const string SiteMetaTitle       = "site.metaTitle";
    public const string SiteMetaDescription = "site.metaDescription";
    public const string SiteAgencyName      = "site.agencyName";
    public const string SiteAgencyShortName = "site.agencyShortName";
    public const string SiteAgencyHref      = "site.agencyHref";
    public const string SiteAgencyLogoSrc   = "site.agencyLogoSrc";
    public const string SiteBannerLang      = "site.bannerLang";

    // Analytics (Public)
    public const string AnalyticsDapEnabled   = "analytics.dapEnabled";
    public const string AnalyticsDapAgency    = "analytics.dapAgency";
    public const string AnalyticsDapSubagency = "analytics.dapSubagency";

    // Feature flags
    public const string FeatureGraphQl             = "features.graphql";
    public const string FeatureSwaggerUi           = "features.swaggerUi";
    public const string FeatureWebhooks            = "features.webhooks";
    public const string FeatureScheduledPublishing = "features.scheduledPublishing";
    public const string FeatureNotifications       = "features.notifications";
    public const string FeatureWebpVariants        = "features.webpVariants";
    public const string FeatureSearchAnalytics     = "features.searchAnalytics";
    public const string FeatureMediaUpload         = "features.mediaUpload";
    public const string FeaturePublicSearch        = "features.publicSearch";

    // Media (Server)
    public const string MediaMaxUploadBytes  = "media.maxUploadBytes";
    public const string MediaAllowedMimeTypes = "media.allowedMimeTypes";
    public const string MediaImageMaxWidthPx = "media.imageMaxWidthPx";
    public const string MediaWebpQuality     = "media.webpQuality";

    // Auth (Server)
    public const string AuthAccessTokenMinutes  = "auth.accessTokenMinutes";
    public const string AuthRefreshTokenHours   = "auth.refreshTokenHours";
    public const string AuthPreviewTokenMinutes = "auth.previewTokenMinutes";

    // Workflow (Server)
    public const string WorkflowScheduledPublishPollSeconds = "workflow.scheduledPublishPollSeconds";
    public const string WorkflowRequireReturnComment        = "workflow.requireReturnComment";

    // Webhooks (Server)
    public const string WebhooksMaxAttempts        = "webhooks.maxAttempts";
    public const string WebhooksRetryDelaysSeconds = "webhooks.retryDelaysSeconds";
    public const string WebhooksTimeoutSeconds     = "webhooks.timeoutSeconds";

    // Search
    public const string SearchPublicPageSize  = "search.publicPageSize";
    public const string SearchDefaultPageSize = "search.defaultPageSize";
    public const string SearchMaxPageSize     = "search.maxPageSize";

    // API (Server)
    public const string ApiMaxPageSize = "api.maxPageSize";

    // Notifications (Admin)
    public const string NotificationsPollIntervalSeconds = "notifications.pollIntervalSeconds";
    public const string NotificationsPanelLimit          = "notifications.panelLimit";
    // Notifications — email (Server); issue #39. SMTP host/credentials stay in Email:Smtp config.
    public const string NotificationsEmailEnabled        = "notifications.emailEnabled";
    public const string NotificationsEmailFromAddress    = "notifications.emailFromAddress";
    public const string NotificationsEmailFromName       = "notifications.emailFromName";
    public const string NotificationsAdminBaseUrl        = "notifications.adminBaseUrl";

    // Admin SPA (Admin)
    public const string AdminAutoSaveIntervalSeconds  = "admin.autoSaveIntervalSeconds";
    public const string AdminContentListPageSize      = "admin.contentListPageSize";
    public const string AdminAuditLogPageSize         = "admin.auditLogPageSize";
    public const string AdminSearchAnalyticsPageSize  = "admin.searchAnalyticsPageSize";
}

public static class SiteSettingCategories
{
    public const string Site          = "Site";
    public const string Analytics     = "Analytics";
    public const string Features      = "Features";
    public const string Media         = "Media";
    public const string Auth          = "Auth";
    public const string Workflow      = "Workflow";
    public const string Webhooks      = "Webhooks";
    public const string Search        = "Search";
    public const string Api           = "Api";
    public const string Notifications = "Notifications";
    public const string Admin         = "Admin";
}

public static class SiteSettingDefinitions
{
    /// <summary>The seed MIME allow-list (BRD FR-SECURITY-06). Also the fallback in <c>MimeAllowList</c>.</summary>
    public static readonly string[] DefaultAllowedMimeTypes =
    {
        // Images
        "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp", "image/svg+xml", "image/tiff", "image/bmp",
        // Documents
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        // Text
        "text/plain", "text/csv",
        // Archives (limited; virus scan hook is separate)
        "application/zip", "application/x-zip-compressed",
    };

    private static SiteSettingDefinition S(string key, string def, string cat, SiteSettingScope scope, string desc, int order)
        => new(key, SiteSettingType.String, def, cat, scope, desc, order);
    private static SiteSettingDefinition I(string key, long def, string cat, SiteSettingScope scope, string desc, int order)
        => new(key, SiteSettingType.Int, def.ToString(), cat, scope, desc, order);
    private static SiteSettingDefinition B(string key, bool def, string cat, SiteSettingScope scope, string desc, int order)
        => new(key, SiteSettingType.Bool, def ? "true" : "false", cat, scope, desc, order);
    private static SiteSettingDefinition J(string key, object def, string cat, SiteSettingScope scope, string desc, int order)
        => new(key, SiteSettingType.Json, JsonSerializer.Serialize(def), cat, scope, desc, order);

    public static readonly IReadOnlyList<SiteSettingDefinition> All = new[]
    {
        // ── Site ────────────────────────────────────────────────────────────
        S(SiteSettingKeys.SiteTitle, "Department of Veterans Affairs", SiteSettingCategories.Site, SiteSettingScope.Public,
          "Site title shown in the public header masthead and appended to page titles.", 10),
        S(SiteSettingKeys.SiteMetaTitle, "VA CMS Public Site", SiteSettingCategories.Site, SiteSettingScope.Public,
          "Default <title> for the public site home page.", 20),
        S(SiteSettingKeys.SiteMetaDescription, "USWDS-compliant VA content management system public site", SiteSettingCategories.Site, SiteSettingScope.Public,
          "Default <meta name=\"description\"> for the public site.", 30),
        S(SiteSettingKeys.SiteAgencyName, "Department of Veterans Affairs", SiteSettingCategories.Site, SiteSettingScope.Public,
          "Full agency name used in the USWDS footer and identifier.", 40),
        S(SiteSettingKeys.SiteAgencyShortName, "VA", SiteSettingCategories.Site, SiteSettingScope.Public,
          "Short agency name / abbreviation used in the USWDS identifier.", 50),
        S(SiteSettingKeys.SiteAgencyHref, "https://www.va.gov", SiteSettingCategories.Site, SiteSettingScope.Public,
          "Agency parent-domain URL linked from the footer and identifier.", 60),
        S(SiteSettingKeys.SiteAgencyLogoSrc, "", SiteSettingCategories.Site, SiteSettingScope.Public,
          "Optional agency logo image URL for the footer and identifier. Blank hides the logo.", 70),
        S(SiteSettingKeys.SiteBannerLang, "en", SiteSettingCategories.Site, SiteSettingScope.Public,
          "Language of the official government banner: en or es.", 80),

        // ── Analytics ───────────────────────────────────────────────────────
        B(SiteSettingKeys.AnalyticsDapEnabled, false, SiteSettingCategories.Analytics, SiteSettingScope.Public,
          "Load the federal Digital Analytics Program (DAP) script on the public site.", 10),
        S(SiteSettingKeys.AnalyticsDapAgency, "", SiteSettingCategories.Analytics, SiteSettingScope.Public,
          "DAP agency code, e.g. VA. Required when DAP is enabled.", 20),
        S(SiteSettingKeys.AnalyticsDapSubagency, "", SiteSettingCategories.Analytics, SiteSettingScope.Public,
          "Optional DAP sub-agency code, e.g. VHA.", 30),

        // ── Features ────────────────────────────────────────────────────────
        B(SiteSettingKeys.FeatureGraphQl, true, SiteSettingCategories.Features, SiteSettingScope.Server,
          "Serve the GraphQL endpoint at /api/graphql. Off returns 404.", 10),
        B(SiteSettingKeys.FeatureSwaggerUi, false, SiteSettingCategories.Features, SiteSettingScope.Server,
          "Serve Swagger UI at /swagger outside Development (always on in Development).", 20),
        B(SiteSettingKeys.FeatureWebhooks, true, SiteSettingCategories.Features, SiteSettingScope.Server,
          "Deliver registered webhooks for content, media, navigation and settings events.", 30),
        B(SiteSettingKeys.FeatureScheduledPublishing, true, SiteSettingCategories.Features, SiteSettingScope.Server,
          "Run the background worker that publishes and expires scheduled content.", 40),
        B(SiteSettingKeys.FeatureNotifications, true, SiteSettingCategories.Features, SiteSettingScope.Admin,
          "Record in-app workflow notifications and show the bell in the admin top bar.", 50),
        B(SiteSettingKeys.FeatureWebpVariants, true, SiteSettingCategories.Features, SiteSettingScope.Server,
          "Generate a resized WebP variant for uploaded JPEG/PNG/WebP images.", 60),
        B(SiteSettingKeys.FeatureSearchAnalytics, true, SiteSettingCategories.Features, SiteSettingScope.Admin,
          "Log public search queries for the Search Analytics dashboard.", 70),
        B(SiteSettingKeys.FeatureMediaUpload, true, SiteSettingCategories.Features, SiteSettingScope.Admin,
          "Allow new media uploads. Off hides the upload form and rejects POST /api/v1/media/upload.", 80),
        B(SiteSettingKeys.FeaturePublicSearch, true, SiteSettingCategories.Features, SiteSettingScope.Public,
          "Show the search box on the public site and serve the search results page.", 90),

        // ── Media ───────────────────────────────────────────────────────────
        I(SiteSettingKeys.MediaMaxUploadBytes, 104_857_600, SiteSettingCategories.Media, SiteSettingScope.Admin,
          "Maximum upload size in bytes (default 100 MB).", 10),
        J(SiteSettingKeys.MediaAllowedMimeTypes, DefaultAllowedMimeTypes, SiteSettingCategories.Media, SiteSettingScope.Admin,
          "JSON array of MIME types accepted by the upload endpoint (BRD FR-SECURITY-06).", 20),
        I(SiteSettingKeys.MediaImageMaxWidthPx, 1920, SiteSettingCategories.Media, SiteSettingScope.Server,
          "Images wider than this are resized down (aspect ratio preserved) when generating the WebP variant.", 30),
        I(SiteSettingKeys.MediaWebpQuality, 80, SiteSettingCategories.Media, SiteSettingScope.Server,
          "WebP encoder quality, 1–100.", 40),

        // ── Auth ────────────────────────────────────────────────────────────
        I(SiteSettingKeys.AuthAccessTokenMinutes, 15, SiteSettingCategories.Auth, SiteSettingScope.Server,
          "Lifetime of issued access tokens (JWT) in minutes.", 10),
        I(SiteSettingKeys.AuthRefreshTokenHours, 8, SiteSettingCategories.Auth, SiteSettingScope.Server,
          "Lifetime of refresh tokens and the cms_rt cookie in hours.", 20),
        I(SiteSettingKeys.AuthPreviewTokenMinutes, 60, SiteSettingCategories.Auth, SiteSettingScope.Server,
          "Lifetime of shareable preview links in minutes.", 30),

        // ── Workflow ────────────────────────────────────────────────────────
        I(SiteSettingKeys.WorkflowScheduledPublishPollSeconds, 60, SiteSettingCategories.Workflow, SiteSettingScope.Server,
          "How often the scheduler checks for content due to publish or expire, in seconds.", 10),
        B(SiteSettingKeys.WorkflowRequireReturnComment, true, SiteSettingCategories.Workflow, SiteSettingScope.Admin,
          "Require a comment when returning content from In Review to Draft.", 20),

        // ── Webhooks ────────────────────────────────────────────────────────
        I(SiteSettingKeys.WebhooksMaxAttempts, 3, SiteSettingCategories.Webhooks, SiteSettingScope.Server,
          "Delivery attempts per webhook event before giving up.", 10),
        J(SiteSettingKeys.WebhooksRetryDelaysSeconds, new[] { 5, 25 }, SiteSettingCategories.Webhooks, SiteSettingScope.Server,
          "JSON array of seconds to wait before each retry. The last value repeats if attempts exceed the list.", 20),
        I(SiteSettingKeys.WebhooksTimeoutSeconds, 15, SiteSettingCategories.Webhooks, SiteSettingScope.Server,
          "HTTP timeout for a single webhook delivery in seconds.", 30),

        // ── Search ──────────────────────────────────────────────────────────
        I(SiteSettingKeys.SearchPublicPageSize, 10, SiteSettingCategories.Search, SiteSettingScope.Public,
          "Results per page on the public search page.", 10),
        I(SiteSettingKeys.SearchDefaultPageSize, 25, SiteSettingCategories.Search, SiteSettingScope.Server,
          "Default page size for GET /api/v1/search when pageSize is omitted.", 20),
        I(SiteSettingKeys.SearchMaxPageSize, 100, SiteSettingCategories.Search, SiteSettingScope.Server,
          "Largest pageSize GET /api/v1/search will honour.", 30),

        // ── Api ─────────────────────────────────────────────────────────────
        I(SiteSettingKeys.ApiMaxPageSize, 200, SiteSettingCategories.Api, SiteSettingScope.Server,
          "Largest pageSize honoured by admin list endpoints and GraphQL.", 10),

        // ── Notifications ───────────────────────────────────────────────────
        I(SiteSettingKeys.NotificationsPollIntervalSeconds, 30, SiteSettingCategories.Notifications, SiteSettingScope.Admin,
          "How often the admin bell polls for new notifications, in seconds.", 10),
        I(SiteSettingKeys.NotificationsPanelLimit, 20, SiteSettingCategories.Notifications, SiteSettingScope.Admin,
          "Number of notifications shown in the bell panel.", 20),
        B(SiteSettingKeys.NotificationsEmailEnabled, true, SiteSettingCategories.Notifications, SiteSettingScope.Server,
          "Email workflow notifications (review requested, returned, approved, published) in addition to the bell. " +
          "Delivery also needs Email:Smtp:Host configured in the API environment.", 30),
        S(SiteSettingKeys.NotificationsEmailFromAddress, "cms-noreply@va.gov", SiteSettingCategories.Notifications, SiteSettingScope.Server,
          "Sender address for notification emails. Must be a mailbox or address the SMTP connector accepts.", 40),
        S(SiteSettingKeys.NotificationsEmailFromName, "VA CMS", SiteSettingCategories.Notifications, SiteSettingScope.Server,
          "Sender display name for notification emails.", 50),
        S(SiteSettingKeys.NotificationsAdminBaseUrl, "http://localhost:5173", SiteSettingCategories.Notifications, SiteSettingScope.Server,
          "Public origin of the admin site (e.g. https://cms.va.gov). Notification emails link to {origin}/admin/content/{id}/edit.", 60),

        // ── Admin ───────────────────────────────────────────────────────────
        I(SiteSettingKeys.AdminAutoSaveIntervalSeconds, 60, SiteSettingCategories.Admin, SiteSettingScope.Admin,
          "Content editor autosave interval in seconds. 0 disables autosave.", 10),
        I(SiteSettingKeys.AdminContentListPageSize, 25, SiteSettingCategories.Admin, SiteSettingScope.Admin,
          "Rows per page on the content entry list.", 20),
        I(SiteSettingKeys.AdminAuditLogPageSize, 50, SiteSettingCategories.Admin, SiteSettingScope.Admin,
          "Rows per page on the audit log viewer.", 30),
        I(SiteSettingKeys.AdminSearchAnalyticsPageSize, 50, SiteSettingCategories.Admin, SiteSettingScope.Admin,
          "Rows per page on the search analytics page.", 40),
    };

    private static readonly Dictionary<string, SiteSettingDefinition> _byKey =
        All.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

    public static SiteSettingDefinition? Find(string key) =>
        _byKey.TryGetValue(key, out var d) ? d : null;

    public static SiteSettingDefinition Get(string key) =>
        Find(key) ?? throw new KeyNotFoundException($"Setting '{key}' is not declared in SiteSettingDefinitions.");
}
