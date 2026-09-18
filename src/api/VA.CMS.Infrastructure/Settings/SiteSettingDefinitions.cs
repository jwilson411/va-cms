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
    public const string AuthAzureAdSignOut      = "auth.azureAdSignOut";
    public const string AuthAutoProvisionUsers  = "auth.autoProvisionUsers";

    // Session policy (VA 6500 AC-8/AC-11/AC-12) — #163/#164
    public const string AuthIdleTimeoutMinutes          = "auth.idleTimeoutMinutes";
    public const string AuthAbsoluteSessionHours        = "auth.absoluteSessionHours";
    public const string AuthSystemUseNotice             = "auth.systemUseNotice";
    public const string AuthRevocationCheckSeconds      = "auth.revocationCheckSeconds";
    public const string AuthRefreshRotationGraceSeconds = "auth.refreshRotationGraceSeconds";

    // Navigation / redirects (Server)
    public const string RedirectsAllowedExternalHosts = "redirects.allowedExternalHosts";
    public const string RedirectsCacheSeconds         = "redirects.cacheSeconds";

    // Security headers (Server) — #162
    public const string SecurityCspReportOnly = "security.cspReportOnly";
    public const string SecurityHstsPreload   = "security.hstsPreload";

    // Workflow (Server)
    public const string WorkflowScheduledPublishPollSeconds = "workflow.scheduledPublishPollSeconds";
    public const string WorkflowRequireReturnComment        = "workflow.requireReturnComment";

    // Webhooks (Server)
    public const string WebhooksMaxAttempts        = "webhooks.maxAttempts";
    public const string WebhooksRetryDelaysSeconds = "webhooks.retryDelaysSeconds";
    public const string WebhooksTimeoutSeconds     = "webhooks.timeoutSeconds";
    public const string WebhooksAllowedHosts       = "webhooks.allowedHosts";
    public const string WebhooksAllowPrivateNetworks = "webhooks.allowPrivateNetworks";

    // Search
    public const string SearchPublicPageSize  = "search.publicPageSize";
    public const string SearchDefaultPageSize = "search.defaultPageSize";
    public const string SearchMaxPageSize     = "search.maxPageSize";
    public const string SearchMaxQueryLength  = "search.maxQueryLength";
    public const string SearchLogQueueCapacity = "search.logQueueCapacity";

    // API (Server)
    public const string ApiMaxPageSize         = "api.maxPageSize";
    public const string ApiMaxRequestBodyBytes = "api.maxRequestBodyBytes";

    // API rate limits (Server) — #167. Per-minute allowances per client (IP for anonymous
    // surfaces, user id for authenticated ones); 0 disables that one policy.
    public const string ApiRateLimitsEnabled                 = "api.rateLimits.enabled";
    public const string ApiRateLimitsPublicReadPerMinute     = "api.rateLimits.publicReadPerMinute";
    public const string ApiRateLimitsAuthPerMinute           = "api.rateLimits.authPerMinute";
    public const string ApiRateLimitsAnalyticsWritePerMinute = "api.rateLimits.analyticsWritePerMinute";
    public const string ApiRateLimitsAdminPerMinute          = "api.rateLimits.adminPerMinute";

    // Notifications (Admin)
    public const string NotificationsPollIntervalSeconds = "notifications.pollIntervalSeconds";
    public const string NotificationsPanelLimit          = "notifications.panelLimit";
    // Notifications — email (Server); issue #39. SMTP host/credentials stay in Email:Smtp config.
    public const string NotificationsEmailEnabled        = "notifications.emailEnabled";
    public const string NotificationsEmailFromAddress    = "notifications.emailFromAddress";
    public const string NotificationsEmailFromName       = "notifications.emailFromName";
    public const string NotificationsAdminBaseUrl        = "notifications.adminBaseUrl";
    // Email delivery retry policy (Server); issue #171 — emails go through the outbox.
    public const string NotificationsEmailMaxAttempts        = "notifications.emailMaxAttempts";
    public const string NotificationsEmailRetryDelaysSeconds = "notifications.emailRetryDelaysSeconds";

    // Outbox (Server); issue #171. The OutboxDispatcherWorker on every node reads these per poll.
    public const string OutboxPollSeconds    = "outbox.pollSeconds";
    public const string OutboxBatchSize      = "outbox.batchSize";
    public const string OutboxLeaseSeconds   = "outbox.leaseSeconds";
    public const string OutboxRetentionDays  = "outbox.retentionDays";
    public const string OutboxStaleAfterSeconds = "outbox.staleAfterSeconds";

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
    public const string Navigation    = "Navigation";
    public const string Security      = "Security";
    public const string Search        = "Search";
    public const string Api           = "Api";
    public const string Notifications = "Notifications";
    public const string Admin         = "Admin";
    public const string Outbox        = "Outbox";
}

public static class SiteSettingDefinitions
{
    /// <summary>The seed MIME allow-list (BRD FR-SECURITY-06). Also the fallback in <c>MimeAllowList</c>.</summary>
    public static readonly string[] DefaultAllowedMimeTypes =
    {
        // Images
        // image/svg+xml is deliberately absent (#158): SVG can carry scripts. An administrator may
        // add it to media.allowedMimeTypes; uploads are then sanitized and served sandboxed.
        "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp", "image/tiff", "image/bmp",
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

    /// <summary>
    /// VA-standard system-use notification (VA Handbook 6500 / NIST AC-8). Editable as
    /// auth.systemUseNotice; kept here so a fresh database shows the approved wording.
    /// </summary>
    public const string SystemUseNoticeDefault =
        "This is a U.S. Government computer system, which may be accessed and used only for authorized Government " +
        "business by authorized personnel. Unauthorized access or use of this computer system may subject violators " +
        "to criminal, civil, and/or administrative action. All information on this computer system may be " +
        "intercepted, recorded, read, copied, and disclosed by and to authorized personnel for official purposes, " +
        "including criminal investigations. Such information includes sensitive data encrypted to comply with " +
        "confidentiality and privacy requirements. Access or use of this computer system by any person, whether " +
        "authorized or unauthorized, constitutes consent to these terms. There is no right of privacy in this system.";

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
        B(SiteSettingKeys.FeatureGraphQl, false, SiteSettingCategories.Features, SiteSettingScope.Server,
          "Serve the GraphQL endpoint at /api/graphql (off by default — enable when a consumer needs it). " +
          "Anonymous callers see Published content only; a CanRead JWT sees everything. Off returns 404.", 10),
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
        B(SiteSettingKeys.AuthAzureAdSignOut, true, SiteSettingCategories.Auth, SiteSettingScope.Server,
          "On logout in AzureAd mode, also send the browser to the Azure AD end-session endpoint so the AAD session " +
          "is cleared (VA 6500 AC-12). Off clears only the CMS cookies; the next login may sign in silently.", 40),
        B(SiteSettingKeys.AuthAutoProvisionUsers, false, SiteSettingCategories.Auth, SiteSettingScope.Server,
          "Create a CMS user row for any tenant identity on its first AzureAd/WindowsAuth login. Off rejects unknown " +
          "identities until an administrator has created the user (DevBypass logins are governed by DevBypassAllowedUsers).", 50),
        I(SiteSettingKeys.AuthIdleTimeoutMinutes, 15, SiteSettingCategories.Auth, SiteSettingScope.Admin,
          "Admin session inactivity limit in minutes (VA 6500 AC-11: 15 for privileged users). The admin SPA warns two " +
          "minutes before and signs out; the API refuses to refresh a session unused for longer than this " +
          "(never less than auth.accessTokenMinutes + 1, so the SPA's silent refresh always fits).", 60),
        I(SiteSettingKeys.AuthAbsoluteSessionHours, 8, SiteSettingCategories.Auth, SiteSettingScope.Server,
          "Hard cap on a session measured from login, in hours (1–12), regardless of activity (VA 6500 AC-12). " +
          "Refresh stops working at the cap and the user must sign in again.", 70),
        S(SiteSettingKeys.AuthSystemUseNotice, SystemUseNoticeDefault, SiteSettingCategories.Auth, SiteSettingScope.Public,
          "System-use notification shown on the admin sign-in page (NIST AC-8). The user must acknowledge it before " +
          "the sign-in buttons enable; the acknowledgement is recorded on the Logon audit row.", 80),
        I(SiteSettingKeys.AuthRevocationCheckSeconds, 30, SiteSettingCategories.Auth, SiteSettingScope.Server,
          "How long (seconds) each API node caches a user's session version before re-reading it. Bounds the delay " +
          "between deactivating a user or changing their roles and their existing access tokens being refused.", 90),
        I(SiteSettingKeys.AuthRefreshRotationGraceSeconds, 30, SiteSettingCategories.Auth, SiteSettingScope.Server,
          "After a refresh token is rotated, the old value stays accepted for this many seconds so two browser tabs " +
          "refreshing at once do not trip replay detection. Reuse after the grace revokes the whole session chain.", 100),

        // ── Security headers ────────────────────────────────────────────────
        B(SiteSettingKeys.SecurityCspReportOnly, true, SiteSettingCategories.Security, SiteSettingScope.Server,
          "Send the API's Content-Security-Policy as Report-Only (violations logged at POST /api/v1/security/csp-report, " +
          "nothing blocked). Turn off to enforce once the report log is quiet.", 10),
        B(SiteSettingKeys.SecurityHstsPreload, false, SiteSettingCategories.Security, SiteSettingScope.Server,
          "Add 'preload' to Strict-Transport-Security (1 year, includeSubDomains). Only after the host name has been " +
          "submitted to hstspreload.org — it cannot be undone quickly.", 20),

        // ── Navigation / redirects ──────────────────────────────────────────
        J(SiteSettingKeys.RedirectsAllowedExternalHosts, Array.Empty<string>(), SiteSettingCategories.Navigation, SiteSettingScope.Server,
          "JSON array of host names a redirect ToPath may point at (e.g. [\"www.va.gov\", \"*.va.gov\"]). " +
          "Empty means redirects may only target site-relative paths.", 10),
        I(SiteSettingKeys.RedirectsCacheSeconds, 60, SiteSettingCategories.Navigation, SiteSettingScope.Server,
          "How long a resolved redirect (or a miss) may be cached by the API and the public site, in seconds " +
          "(Cache-Control max-age on GET /api/v1/redirects/resolve). 0 disables caching.", 20),

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
        J(SiteSettingKeys.WebhooksAllowedHosts, Array.Empty<string>(), SiteSettingCategories.Webhooks, SiteSettingScope.Server,
          "JSON array of host names webhooks may be registered for and delivered to (\"www.va.gov\" or \"*.va.gov\"). " +
          "Empty means no deliveries outside Development. Checked at registration and again on every delivery.", 40),
        B(SiteSettingKeys.WebhooksAllowPrivateNetworks, false, SiteSettingCategories.Webhooks, SiteSettingScope.Server,
          "Allow allow-listed webhook hosts to resolve to RFC 1918 / CGNAT addresses (an on-prem subscriber inside the VA network). " +
          "Loopback, link-local and multicast destinations are always refused outside Development.", 50),

        // ── Search ──────────────────────────────────────────────────────────
        I(SiteSettingKeys.SearchPublicPageSize, 10, SiteSettingCategories.Search, SiteSettingScope.Public,
          "Results per page on the public search page.", 10),
        I(SiteSettingKeys.SearchDefaultPageSize, 25, SiteSettingCategories.Search, SiteSettingScope.Server,
          "Default page size for GET /api/v1/search when pageSize is omitted.", 20),
        I(SiteSettingKeys.SearchMaxPageSize, 100, SiteSettingCategories.Search, SiteSettingScope.Server,
          "Largest pageSize GET /api/v1/search will honour.", 30),
        I(SiteSettingKeys.SearchMaxQueryLength, 200, SiteSettingCategories.Search, SiteSettingScope.Public,
          "Longest search query (characters) GET /api/v1/search and POST /api/v1/search/click accept; longer is 400.", 40),
        I(SiteSettingKeys.SearchLogQueueCapacity, 10_000, SiteSettingCategories.Search, SiteSettingScope.Server,
          "Search/click analytics rows buffered in memory before the oldest are dropped (applied when the queue is first used after a restart).", 50),

        // ── Api ─────────────────────────────────────────────────────────────
        I(SiteSettingKeys.ApiMaxPageSize, 200, SiteSettingCategories.Api, SiteSettingScope.Server,
          "Largest pageSize honoured by admin list endpoints and GraphQL.", 10),
        I(SiteSettingKeys.ApiMaxRequestBodyBytes, 1_048_576, SiteSettingCategories.Api, SiteSettingScope.Server,
          "Request body limit in bytes for every endpoint except media upload (which uses media.maxUploadBytes); larger bodies are 413.", 20),
        B(SiteSettingKeys.ApiRateLimitsEnabled, true, SiteSettingCategories.Api, SiteSettingScope.Server,
          "Master switch for API rate limiting (#167). Off means no policy applies; use only while diagnosing.", 30),
        I(SiteSettingKeys.ApiRateLimitsPublicReadPerMinute, 300, SiteSettingCategories.Api, SiteSettingScope.Server,
          "Anonymous reads (public content, search, navigation, media serve, GraphQL, health) allowed per client IP per minute; 0 = unlimited.", 31),
        I(SiteSettingKeys.ApiRateLimitsAuthPerMinute, 30, SiteSettingCategories.Api, SiteSettingScope.Server,
          "/api/auth/* calls (login, callback, refresh, logout) allowed per client IP per minute; 0 = unlimited.", 32),
        I(SiteSettingKeys.ApiRateLimitsAnalyticsWritePerMinute, 60, SiteSettingCategories.Api, SiteSettingScope.Server,
          "Anonymous writes (search click tracking, CSP reports) allowed per client IP per minute (token bucket); 0 = unlimited.", 33),
        I(SiteSettingKeys.ApiRateLimitsAdminPerMinute, 600, SiteSettingCategories.Api, SiteSettingScope.Server,
          "Authenticated admin/API calls allowed per user per minute; 0 = unlimited.", 34),

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
        I(SiteSettingKeys.NotificationsEmailMaxAttempts, 5, SiteSettingCategories.Notifications, SiteSettingScope.Server,
          "SMTP delivery attempts per queued email before it is marked failed in the outbox.", 70),
        J(SiteSettingKeys.NotificationsEmailRetryDelaysSeconds, new[] { 30, 120, 600 }, SiteSettingCategories.Notifications, SiteSettingScope.Server,
          "JSON array of seconds to wait before each email retry. The last value repeats if attempts exceed the list.", 80),

        // ── Outbox (#171) ───────────────────────────────────────────────────
        I(SiteSettingKeys.OutboxPollSeconds, 5, SiteSettingCategories.Outbox, SiteSettingScope.Server,
          "How often each API node checks the outbox for due webhook deliveries and emails, in seconds (minimum 1).", 10),
        I(SiteSettingKeys.OutboxBatchSize, 20, SiteSettingCategories.Outbox, SiteSettingScope.Server,
          "Rows one node claims per poll (1–500). A full batch is followed immediately by another poll.", 20),
        I(SiteSettingKeys.OutboxLeaseSeconds, 300, SiteSettingCategories.Outbox, SiteSettingScope.Server,
          "How long a claimed row stays owned by a node before another node may take it over (minimum 10). " +
          "Must exceed the longest single delivery (webhooks.timeoutSeconds, one SMTP session).", 30),
        I(SiteSettingKeys.OutboxRetentionDays, 14, SiteSettingCategories.Outbox, SiteSettingScope.Server,
          "Days to keep delivered and failed outbox rows before the hourly purge removes them.", 40),
        I(SiteSettingKeys.OutboxStaleAfterSeconds, 600, SiteSettingCategories.Outbox, SiteSettingScope.Server,
          "/health/ready reports Degraded when the oldest due outbox row has waited longer than this. 0 disables the check.", 50),

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
