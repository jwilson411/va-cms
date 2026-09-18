using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using VA.CMS.API.Observability;
using VA.CMS.API.Navigation;
using VA.CMS.API.RateLimiting;
using VA.CMS.API.Search;
using VA.CMS.API;
using VA.CMS.API.Auth;
using VA.CMS.API.GraphQL;
using VA.CMS.API.Middleware;
using VA.CMS.Infrastructure.Data;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.ContentTypes;
using VA.CMS.Infrastructure.ContentTypes.BuiltIn;
using VA.CMS.Infrastructure.ContentTypes.CustomFields;
using VA.CMS.Infrastructure.Email;
using VA.CMS.Infrastructure.Notifications;
using VA.CMS.Infrastructure.Outbox;
using VA.CMS.Infrastructure.Services;
using VA.CMS.Infrastructure.Settings;
using VA.CMS.Infrastructure.Storage;
using VA.CMS.API.Webhooks;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------
// Logging (#166, NFR-OPS-02): Serilog with compact JSON, sinks from Logging:Sinks
// (console, rolling file, Windows Event Log, Splunk HEC), levels from Logging:LogLevel.
// A bootstrap logger covers the lines emitted before the host exists.
// -----------------------------------------------------------------------
Log.Logger = SerilogSetup.CreateBootstrapLogger(builder.Environment.IsDevelopment());
builder.Host.UseSerilog((context, services, configuration) =>
    SerilogSetup.Configure(configuration, context.Configuration, context.HostingEnvironment));

// -----------------------------------------------------------------------
// Fail-fast configuration validation (#173)
// Every environment/secret rule is evaluated here, before a single service is
// registered, and reported as one numbered list. See StartupValidation for the
// rules and docs/DEPLOYMENT.md "Startup validation" for the operator view.
// -----------------------------------------------------------------------
try
{
    StartupValidation.Run(builder.Configuration, builder.Environment);
}
catch (InvalidOperationException ex)
{
    Log.Fatal("{StartupProblems}", ex.Message);
    throw;
}

// -----------------------------------------------------------------------
// Configuration
// -----------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;

var authOptions = builder.Configuration
    .GetSection(AuthOptions.SectionName)
    .Get<AuthOptions>() ?? new AuthOptions();

var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()
    ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    // Development only — StartupValidation refuses an empty key everywhere else.
    jwtOptions.SigningKey = Convert.ToBase64String(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    Log.Warning("Jwt:SigningKey not configured; using a randomly generated key. Tokens will not survive a restart. Set Jwt:SigningKey for persistence.");
}

// Options classes are also registered through the options pipeline with
// ValidateDataAnnotations().ValidateOnStart() (#173). StartupValidation has already
// evaluated the same annotations; this keeps IOptions<T> consumers and the host's own
// start-up check on one source of truth.
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .PostConfigure(o => { if (string.IsNullOrWhiteSpace(o.SigningKey)) o.SigningKey = jwtOptions.SigningKey; })
    .ValidateDataAnnotations()
    .Validate(o => StartupValidation.ValidateSigningKey(o.SigningKey, isDevelopment: false) is null,
              "Jwt:SigningKey is missing, shorter than 32 bytes or an example placeholder.")
    .ValidateOnStart();
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<StorageOptions>()
    .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => o.Validate(builder.Environment.IsDevelopment() ? null : builder.Environment.ContentRootPath) is null,
              "Storage options are invalid (see StorageOptions.Validate).")
    .ValidateOnStart();
builder.Services.AddOptions<MediaScannerOptions>()
    .Bind(builder.Configuration.GetSection(MediaScannerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
    .Validate(o => StartupValidation.ValidateSmtp(o, builder.Environment.IsDevelopment()) is null,
              "Email:Smtp options are invalid (see StartupValidation.ValidateSmtp).")
    .ValidateOnStart();
builder.Services.AddOptions<LoggingSinkOptions>()
    .Bind(builder.Configuration.GetSection(LoggingSinkOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => !o.Validate(builder.Environment.IsDevelopment()).Any(), "Logging:Sinks options are invalid (see LoggingSinkOptions.Validate).")
    .ValidateOnStart();
builder.Services.AddOptions<KeyRingOptions>()
    .Bind(builder.Configuration.GetSection(KeyRingOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => o.Validate(builder.Environment.IsDevelopment()) is null, "DataProtection options are invalid (see KeyRingOptions.Validate).")
    .ValidateOnStart();

// -----------------------------------------------------------------------
// Data Protection key ring (#168): persisted where DataProtection:KeysPath points so
// webhook secrets encrypted by one node/app-pool identity can be read by the next.
// -----------------------------------------------------------------------
(builder.Configuration.GetSection(KeyRingOptions.SectionName).Get<KeyRingOptions>() ?? new KeyRingOptions())
    .Apply(builder.Services.AddDataProtection());

// -----------------------------------------------------------------------
// Host hardening (#162): explicit AllowedHosts outside Development; forwarded
// headers only from configured proxies; CORS only when a front end is cross-origin.
// -----------------------------------------------------------------------
var forwardedHeaders = VA.CMS.API.HostHardeningOptions.BuildForwardedHeaders(builder.Configuration);
var corsOrigins      = VA.CMS.API.HostHardeningOptions.CorsAllowedOrigins(builder.Configuration);
if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));
}

// -----------------------------------------------------------------------
// Authentication
// -----------------------------------------------------------------------
// Session policy guards (#164, VA 6500): DevBypass and the fake AD handlers exist for
// local development only. StartupValidation has already refused them outside
// ASPNETCORE_ENVIRONMENT=Development, so the switch below only wires what was allowed.

// JWT bearer validation shared by every auth mode. OnTokenValidated consults the
// session revocation guard (#163) so a token minted before a deactivation or role
// change is refused even though its signature and lifetime are fine.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IJwtService>((options, jwt) =>
    {
        options.TokenValidationParameters = jwt.GetValidationParameters();
        options.Events = SessionRevocationJwtEvents.Build();
    });

switch (authOptions.Mode)
{
    case AuthMode.DevBypass:
        // Development-only JWT bypass: no AD required.
        // The DevBypassMiddleware injects a JWT derived from the X-Dev-User header.
        // Register JWT bearer so that the middleware-injected tokens are validated
        // by the standard auth pipeline.
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });
        break;

    case AuthMode.WindowsAuth:
        var useFakeNegotiate = builder.Configuration["WINDOWS_AUTH_FAKE_NEGOTIATE"] == "true";

        var authBuilder = builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
            });

        if (useFakeNegotiate)
        {
            authBuilder.AddScheme<AuthenticationSchemeOptions, FakeNegotiateHandler>(
                NegotiateDefaults.AuthenticationScheme, _ => { });
        }
        else
        {
            authBuilder.AddNegotiate();
        }

        authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });
        break;

    case AuthMode.AzureAd:
    default:
        // The OIDC redirect URI (AzureAd:CallbackPath, default /signin-oidc) is owned by the
        // OIDC handler; StartupValidation refuses a CallbackPath under /api/ (#154).
        var useFakeOidc = builder.Configuration["AZUREAD_FAKE_OIDC"] == "true";

        var aadBuilder = builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
            });

        if (useFakeOidc)
        {
            aadBuilder.AddCookie(AzureAdSchemes.Cookie);
            aadBuilder.AddScheme<AuthenticationSchemeOptions, FakeAzureAdHandler>(
                AzureAdSchemes.OpenIdConnect, _ => { });
        }
        else
        {
            aadBuilder.AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"),
                openIdConnectScheme: AzureAdSchemes.OpenIdConnect,
                cookieScheme:        AzureAdSchemes.Cookie);
        }

        // The AAD session cookie only has to survive the hop from /signin-oidc to
        // /api/auth/callback and be present for sign-out; keep it tight.
        builder.Services.Configure<CookieAuthenticationOptions>(AzureAdSchemes.Cookie, o =>
        {
            o.Cookie.Name         = AzureAdSchemes.CookieName;
            o.Cookie.HttpOnly     = true;
            o.Cookie.SameSite     = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            o.ExpireTimeSpan      = TimeSpan.FromHours(8);
            o.SlidingExpiration   = false;
        });

        aadBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });
        break;
}

// Policies (default deny — see CmsAuthorizationExtensions, #155)
builder.Services.AddCmsAuthorization();
// #165: a refused policy is an AuditLog row (AuthorizationDenied) before the 403 goes out.
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuditingAuthorizationResultHandler>();

// -----------------------------------------------------------------------
// Request bounds (#167): header/line limits on Kestrel explicitly (IIS in-process
// applies its own requestLimits from web.config — see DEPLOYMENT.md); the body limit
// is per request from api.maxRequestBodyBytes / media.maxUploadBytes in
// UseSiteSettingGates so it can change without a restart.
// -----------------------------------------------------------------------
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
    kestrel.Limits.MaxRequestHeaderCount      = 100;
    kestrel.Limits.MaxRequestLineSize         = 8 * 1024;
    kestrel.AddServerHeader                   = false;
});

// -----------------------------------------------------------------------
// Services
// -----------------------------------------------------------------------
// Every controller action gets a rate-limit policy by convention (#167) unless it
// declares one; see RateLimitPolicyConvention for the mapping.
builder.Services.AddControllers(options => options.Conventions.Add(new RateLimitPolicyConvention()));
builder.Services.AddCmsRateLimiting();

// -----------------------------------------------------------------------
// Errors and health (#166, NFR-OPS-01/02)
// Unhandled exceptions become RFC 7807 ProblemDetails carrying the correlation id
// and nothing else (the exception itself is logged with the same id); exception
// text is only added to the body in Development. Health checks: see HealthEndpoints.
// -----------------------------------------------------------------------
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["correlationId"] = ctx.HttpContext.GetCorrelationId();
    // .NET 8's exception handler exposes the error via the feature, not ProblemDetailsContext.Exception.
    var error = ctx.Exception ?? ctx.HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (error is not null && builder.Environment.IsDevelopment())
    {
        ctx.ProblemDetails.Detail = error.Message;
        ctx.ProblemDetails.Extensions["exception"] = error.ToString();
    }
});
builder.Services.AddCmsHealthChecks();

// -----------------------------------------------------------------------
// OpenAPI / Swagger (Issue #55 — BRD FR-DEV-01)
// -----------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "VA CMS REST API",
        Version     = "v1",
        Description = "Headless content management API for VA CMS. " +
                      "BRD FR-DEV-01: OpenAPI 3.0 spec auto-generated from controllers.",
        Contact = new OpenApiContact
        {
            Name  = "VA CMS Team",
            Email = "cms@va.gov",
        },
    });

    // JWT Bearer security scheme so Swagger UI can authenticate
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Enter your JWT access token (issued by /api/auth/login).",
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer",
                }
            },
            Array.Empty<string>()
        }
    });

    // Include XML doc comments from controllers (all documented with <summary> tags)
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = System.IO.Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (System.IO.File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

// Who/where for audit rows (#165): filled per request by UseAuditContext(); CmsDatabase
// stamps it onto SESSION_CONTEXT so stored procedures audit with the right actor.
builder.Services.AddScoped<AuditContext>();
builder.Services.AddScoped<IAuditContext>(sp => sp.GetRequiredService<AuditContext>());

// PetaPoco database
builder.Services.AddScoped<CmsDatabase>(sp => new CmsDatabase(connectionString, sp.GetRequiredService<IAuditContext>()));

// -----------------------------------------------------------------------
// Site settings (epic #141): runtime configuration + feature flags from [SiteSetting].
// One singleton serves typed reads from an in-memory snapshot; as a hosted service it
// syncs the C# definitions into the table, loads at startup, and refreshes on a timer.
// Reads never block on the database — code defaults apply until the first load.
// -----------------------------------------------------------------------
builder.Services.AddSingleton<ISiteSettingRepository>(_ => new SiteSettingRepository(connectionString));
builder.Services.AddSingleton<SiteSettingsService>(sp => new SiteSettingsService(
    sp.GetRequiredService<ISiteSettingRepository>(),
    sp.GetRequiredService<ILogger<SiteSettingsService>>()));
builder.Services.AddSingleton<ISiteSettingsService>(sp => sp.GetRequiredService<SiteSettingsService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<SiteSettingsService>());

// Repositories
builder.Services.AddScoped<IContentEntryRepository, ContentEntryRepository>();
builder.Services.AddScoped<IContentTypeRepository, ContentTypeRepository>();
builder.Services.AddScoped<IContentVersionRepository, ContentVersionRepository>();
builder.Services.AddScoped<IMediaAssetRepository, MediaAssetRepository>();
builder.Services.AddScoped<IMediaExtendedRepository, MediaExtendedRepository>();
builder.Services.AddScoped<IMediaAltTextGuardRepository, MediaAltTextGuardRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserRoleRepository, UserRoleRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<INavigationMenuRepository, NavigationMenuRepository>();
builder.Services.AddScoped<INavigationRepository, NavigationRepository>();
builder.Services.AddSingleton<RedirectResolveCache>();                      // #169: process-wide resolve cache
builder.Services.AddScoped<IRedirectResolver, RedirectResolver>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IDbMonitorRepository, DbMonitorRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

// Story #67: AD group role mapping repository and resolver
builder.Services.AddScoped<IAdGroupMappingRepository, AdGroupMappingRepository>();
builder.Services.AddScoped<IAdGroupRoleResolver, AdGroupRoleResolver>();
builder.Services.AddScoped<VA.CMS.API.Services.IMediaUsageSyncService, VA.CMS.API.Services.MediaUsageSyncService>();

// Issue #49: Full-text search repository (FR-SEARCH-02)
builder.Services.AddScoped<ISearchRepository, SearchRepository>();
// #167: search/click analytics rows go through a bounded channel + hosted writer,
// never on the request's own (scoped, soon disposed) CmsDatabase.
builder.Services.AddSearchLogQueue();

// Issue #51: Search analytics repository (FR-SEARCH-06)
builder.Services.AddScoped<ISearchAnalyticsRepository, SearchAnalyticsRepository>();

// Issue #52: Search pins repository (FR-SEARCH-04)
builder.Services.AddScoped<ISearchPinRepository, SearchPinRepository>();

// Issue #53: Taxonomy repository (needed for GraphQL)
builder.Services.AddScoped<ITaxonomyRepository, TaxonomyRepository>();

// Auth services
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<AuthOptions>>().Value);
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<JwtOptions>>().Value);
builder.Services.AddSingleton<IJwtService, JwtService>();
// #163: refresh tokens are rows in [RefreshToken] (rotation, replay detection, revocation);
// InMemoryRefreshTokenService exists for tests only.
builder.Services.AddScoped<IRefreshTokenService, DbRefreshTokenService>();
builder.Services.AddSingleton<ISessionRevocationGuard, SessionRevocationGuard>();
builder.Services.AddSingleton<IRbacService, RbacService>();

// -----------------------------------------------------------------------
// Storage backend (issue #40: BRD FR-MEDIA-07 / FR-SECURITY-06)
// -----------------------------------------------------------------------
// On-prem only: local disk or a UNC share. Anything else (the former azure_blob stub)
// was refused by StartupValidation rather than failing on the first upload (#170).
var storageOptions = builder.Configuration
    .GetSection(StorageOptions.SectionName)
    .Get<StorageOptions>() ?? new StorageOptions();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<StorageOptions>>().Value);

IStorageBackend storageBackend = storageOptions.Backend.Trim().ToLowerInvariant() switch
{
    "unc" => new UncStorageBackend(storageOptions),
    _     => new LocalStorageBackend(storageOptions),   // default: local
};
builder.Services.AddSingleton<IStorageBackend>(storageBackend);
builder.Services.AddSingleton<IImageProcessingService, ImageProcessingService>();
// Virus scanning (#159, BRD FR-MEDIA-04, NIST SI-3): Media:Scanner selects ICAP (enterprise
// engines), ClamAV (dev/CI) or Disabled. Disabled is refused in Production by
// StartupValidation; FailClosed defaults to true outside Development so an unreachable
// engine rejects uploads.
var scannerOptions = builder.Configuration
    .GetSection(MediaScannerOptions.SectionName)
    .Get<MediaScannerOptions>() ?? new MediaScannerOptions();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<MediaScannerOptions>>().Value);
builder.Services.AddSingleton<IVirusScanService>(scannerOptions.Mode switch
{
    MediaScannerMode.Icap   => new IcapVirusScanService(scannerOptions),
    MediaScannerMode.ClamAv => new ClamAvVirusScanService(scannerOptions),
    _                       => new NoOpVirusScanService(),
});
builder.Services.AddScoped<IMediaExtendedRepository, MediaExtendedRepository>();
builder.Services.AddScoped<IMediaUploadService>(sp => new MediaUploadService(
    sp.GetRequiredService<IStorageBackend>(),
    sp.GetRequiredService<IMediaAssetRepository>(),
    sp.GetRequiredService<IImageProcessingService>(),
    sp.GetRequiredService<IVirusScanService>(),
    sp.GetRequiredService<IMediaExtendedRepository>(),
    sp.GetRequiredService<ISiteSettingsService>(),
    failClosed: scannerOptions.ResolveFailClosed(builder.Environment.IsDevelopment()),
    audit: sp.GetRequiredService<IAuditLogRepository>(),
    logger: sp.GetRequiredService<ILogger<MediaUploadService>>()));

// Preview token service — issue #34 (BRD FR-AUTH-08)
builder.Services.AddSingleton<IPreviewTokenService, PreviewTokenService>();

// Markdown renderer (shared by preview and publish pipelines)
// Issue #66: register both IMarkdownRenderer and IUswdsMarkdownRenderer from the same singleton.
builder.Services.AddSingleton<VA.CMS.Infrastructure.Markdown.UswdsMarkdownRenderer>();
builder.Services.AddSingleton<VA.CMS.Infrastructure.Markdown.IUswdsMarkdownRenderer>(
    sp => sp.GetRequiredService<VA.CMS.Infrastructure.Markdown.UswdsMarkdownRenderer>());
builder.Services.AddSingleton<VA.CMS.Infrastructure.Markdown.IMarkdownRenderer>(
    sp => sp.GetRequiredService<VA.CMS.Infrastructure.Markdown.UswdsMarkdownRenderer>());

// Seed (demo)
builder.Services.AddScoped<ISeedService, DemoSeedService>();

// Issue #54: Webhook registration and delivery (BRD FR-DEV-07)
builder.Services.AddScoped<IWebhookRepository, WebhookRepository>();
// #168: destination policy (allow-list + address classes), DNS-checked connect, no redirects/proxy,
// TLS 1.2+, capped response. Per-delivery timeout is webhooks.timeoutSeconds (WebhookDispatcher).
builder.Services.AddSingleton<WebhookDestinationPolicy>();
builder.Services.AddSingleton<IWebhookDnsResolver, DnsWebhookDnsResolver>();
builder.Services.AddSingleton<IWebhookSecretProtector, WebhookSecretProtector>();
builder.Services.AddHostedService<WebhookSecretRekeyService>();
builder.Services.AddHttpClient(WebhookHttpHandler.ClientName)
    .ConfigureHttpClient(WebhookHttpHandler.ConfigureClient)
    .ConfigurePrimaryHttpMessageHandler(sp => WebhookHttpHandler.Create(
        sp.GetRequiredService<WebhookDestinationPolicy>(), sp.GetRequiredService<IWebhookDnsResolver>()));
builder.Services.AddScoped<IWebhookDispatcher, WebhookDispatcher>();
// #171: events are queued to the transactional outbox and delivered by OutboxDispatcherWorker.
builder.Services.AddScoped<IWebhookBackgroundDispatcher, OutboxWebhookDispatcher>();
builder.Services.AddScoped<IOutboxConsumer, OutboxWebhookConsumer>();

// Issue #38: In-app notification center for workflow events (BRD FR-WORKFLOW-02/03)
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<IWorkflowNotifier, WorkflowNotifier>();

// Issue #39: Workflow emails over SMTP (BRD FR-WORKFLOW-02). The relay and its credentials
// come from the Email:Smtp section (Email__Smtp__Host etc.); the switch, sender and link
// origin are site settings (notifications.email*). Without a host, messages are logged.
var emailOptions = builder.Configuration
    .GetSection(EmailOptions.SectionName)
    .Get<EmailOptions>() ?? new EmailOptions();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<EmailOptions>>().Value);
if (emailOptions.IsEnabled)
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
else
{
    builder.Services.AddSingleton<IEmailSender, DisabledEmailSender>();
    Log.Information("Email:Smtp:Host not configured; workflow emails will be logged, not sent.");
}
builder.Services.AddScoped<IEmailDispatcher, OutboxEmailDispatcher>();
builder.Services.AddScoped<IOutboxConsumer, OutboxEmailConsumer>();

// -----------------------------------------------------------------------
// Issue #171 (NFR-OPS-04): transactional outbox. Webhook deliveries and workflow emails are
// [OutboundEvent] rows written on the caller's connection; OutboxDispatcherWorker runs on
// every node and claims batches with UPDLOCK/READPAST, so N nodes share the work and a
// recycle loses nothing. Knobs are the outbox.* site settings.
// -----------------------------------------------------------------------
builder.Services.AddSingleton<IOutboxRepository>(_ => new OutboxRepository(connectionString));
builder.Services.AddHostedService<OutboxDispatcherWorker>();

// Issue #35: Scheduled publish / expiry background worker (BRD FR-AUTH-04). Safe to run on
// every node since #171: the claim SPs hand each due entry to exactly one sweep.
builder.Services.AddHostedService<VA.CMS.Infrastructure.Services.ScheduledPublishWorker>();

// -----------------------------------------------------------------------
// Content Type Registry (FR-SCHEMA-01)
// -----------------------------------------------------------------------
builder.Services.AddContentType<StandardPageTypeDefinition>();
builder.Services.AddContentType<NewsArticleTypeDefinition>();

// -----------------------------------------------------------------------
// Issue #53: Hot Chocolate GraphQL (FR-DEV-02)
// - Endpoint: /api/graphql
// - Playground: /api/graphql/ui (Development only)
// - Types: ContentEntry, MediaAsset, NavigationMenu, TaxonomyTerm
// - DataLoader prevents N+1 on relation loads
// -----------------------------------------------------------------------
// #156: two audiences on one schema. The endpoint stays anonymous so the public
// site can query Published content; field-level [Authorize] and IGraphQLAudience
// gate everything else on the JWT bearer identity. Depth, cost and timeout limits
// bound what an anonymous caller can make the database do; introspection is a
// Development-only convenience.
builder.Services.AddScoped<IGraphQLAudience, GraphQLAudience>();
builder.Services
    .AddGraphQLServer()
    .AddAuthorization()
    .AddQueryType<Query>()
    .AddDataLoader<VA.CMS.API.GraphQL.DataLoaders.ContentEntryByIdDataLoader>()
    .AddDataLoader<VA.CMS.API.GraphQL.DataLoaders.MediaAssetByIdDataLoader>()
    .AddMaxExecutionDepthRule(GraphQLLimits.MaxExecutionDepth, skipIntrospectionFields: true)
    .ModifyCostOptions(o =>
    {
        o.MaxFieldCost = GraphQLLimits.MaxFieldCost;
        o.MaxTypeCost  = GraphQLLimits.MaxTypeCost;
    })
    .ModifyRequestOptions(o =>
    {
        o.ExecutionTimeout       = GraphQLLimits.ExecutionTimeout;
        o.IncludeExceptionDetails = builder.Environment.IsDevelopment();
    })
    .DisableIntrospection(!builder.Environment.IsDevelopment());

// -----------------------------------------------------------------------
// Custom Field Type Plugins (FR-DEV-05 / issue #27)
// -----------------------------------------------------------------------
// GeoPoint is the reference implementation; additional plugins follow the
// same pattern: builder.Services.AddCustomFieldType<MyCustomFieldType>();
builder.Services.AddCustomFieldType<GeoPointFieldType>();

// -----------------------------------------------------------------------
// Build
// -----------------------------------------------------------------------
var app = builder.Build();

// -----------------------------------------------------------------------
// Database migrations (#157)
// Deployments run `vacms db migrate` with an elevated connection; the API's own
// login is EXECUTE-only. Startup therefore only *checks* that nothing is pending
// and refuses to serve a database that is behind, unless Database:MigrateOnStartup
// opts in (default: Development only). SKIP_MIGRATIONS=true skips both (tests).
// -----------------------------------------------------------------------
var skipMigrations = builder.Configuration["SKIP_MIGRATIONS"] == "true";

if (!skipMigrations)
{
    var migrateOnStartup = builder.Configuration.GetValue<bool?>("Database:MigrateOnStartup")
                           ?? builder.Environment.IsDevelopment();
    var migrationsPath = VA.CMS.Infrastructure.Data.Migrations.MigrationRunner.FindMigrationsPath();

    if (migrateOnStartup)
    {
        var result = VA.CMS.Infrastructure.Data.Migrations.MigrationRunner.Upgrade(connectionString, migrationsPath);
        if (!result.Successful)
        {
            app.Logger.LogCritical("Migration failed: {Error}", result.Error);
            return 1;
        }

        app.Logger.LogInformation("Database migrations applied successfully.");
    }
    else
    {
        var status = await VA.CMS.Infrastructure.Data.Migrations.MigrationRunner.CheckAsync(connectionString, migrationsPath);
        if (!status.IsUpToDate)
        {
            app.Logger.LogCritical(
                "{Error} Pending migrations ({PendingCount}): {Pending}. Run `vacms db migrate` with the deployment account, or set Database:MigrateOnStartup=true.",
                status.Error ?? "The database is behind the deployed migration set.", status.Pending.Count, string.Join(", ", status.Pending));
            return 1;
        }

        app.Logger.LogInformation("Database schema is current ({AppliedCount} migrations applied).", status.Applied.Count);
    }
}

// -----------------------------------------------------------------------
// HTTP pipeline
// -----------------------------------------------------------------------
// Correlation id first (#166) so the exception handler, the request log line and
// every audit row of a request share one id; then the exception handler so any
// failure below becomes ProblemDetails instead of an empty 500.
app.UseCorrelationId();
app.UseExceptionHandler();

// Forwarded headers next so Request.Scheme / RemoteIpAddress are right for
// everything below (HTTPS redirect, Secure cookies, HSTS, audit source IPs, request log).
if (forwardedHeaders is not null)
    app.UseForwardedHeaders(forwardedHeaders);

app.UseSecurityHeaders();   // #162: on every response, including errors

// One structured line per request (#166). The enricher runs at completion, so the
// user id is available even though authentication happens further down; UPN/e-mail
// are deliberately not logged (docs/LOGGING.md). Liveness polls are demoted to Debug.
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.GetLevel = (httpContext, elapsed, ex) =>
        ex is not null || httpContext.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
        : HealthEndpoints.Prefixes.Any(p => httpContext.Request.Path.StartsWithSegments(p)) ? Serilog.Events.LogEventLevel.Debug
        : Serilog.Events.LogEventLevel.Information;
    options.EnrichDiagnosticContext = (diagnostics, httpContext) =>
    {
        diagnostics.Set("UserId",   long.TryParse(httpContext.User.FindFirst("cms_user_id")?.Value, out var id) ? id : null);
        diagnostics.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());
        diagnostics.Set("Scheme",   httpContext.Request.Scheme);
        diagnostics.Set("Host",     httpContext.Request.Host.Value);
    };
});

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

// -----------------------------------------------------------------------
// Site setting gates (epic #141): features.graphql / features.swaggerUi answer 404 while
// off, and media.maxUploadBytes sets the upload body limit. Must run before Swagger,
// routing and the form reader.
// -----------------------------------------------------------------------
app.UseSiteSettingGates(app.Environment.IsDevelopment());

// -----------------------------------------------------------------------
// Swagger — always on in Development (AC: Issue #55); elsewhere the site-setting gate
// above answers 404 while features.swaggerUi is off, and (#162) a CanDevelop bearer
// token is required to reach the UI or swagger.json. Stays ahead of UseRouting so the
// fallback authorization policy (which also covers non-endpoint requests) does not apply.
// -----------------------------------------------------------------------
app.UseSwaggerAccessGate(app.Environment.IsDevelopment());
app.UseSwagger(c =>
{
    c.RouteTemplate = "swagger/{documentName}/swagger.json";
});
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "VA CMS REST API v1");
    c.RoutePrefix = "swagger";
    c.DocumentTitle = "VA CMS API";
});

app.UseRouting();

if (corsOrigins.Length > 0)
    app.UseCors();

// DevBypass: inject a JWT from the X-Dev-User header BEFORE the auth pipeline runs.
// Must be placed before UseAuthentication so the injected token is visible to JWT bearer.
// The middleware is a no-op in AzureAd and WindowsAuth modes (guard in Program.cs).
if (authOptions.Mode == AuthMode.DevBypass)
    app.UseDevBypassAuth();

app.UseAuthentication();
app.UseRateLimiter();       // #167: after authentication so the admin policy can key on the user id
app.UseAuthHeaderRedaction();
app.UseAuditContext();      // #165: actor / IP / agent / correlation id for every audit row
app.UseAuthorization();

app.MapControllers();

// Issue #53: GraphQL endpoint + playground
// Endpoint:  /api/graphql
// Playground: /api/graphql/ui (Development only — HC disables Banana Cake Pop in non-dev by default)
app.MapGraphQL("/api/graphql")
   .AllowAnonymous()    // #156: anonymous = Published-only surface; CanRead JWT = full surface (see GraphQLAudience)
   .RequireRateLimiting(RateLimitPolicies.PublicRead);   // #167: on top of the depth/cost limits

app.MapCmsHealthChecks();   // #166: /health(/live) liveness, /health/ready readiness (+ /api/health aliases)

try
{
    app.Run();
    return 0;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
