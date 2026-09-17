using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
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
using VA.CMS.Infrastructure.Services;
using VA.CMS.Infrastructure.Settings;
using VA.CMS.Infrastructure.Storage;
using VA.CMS.API.Webhooks;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------
// Configuration
// -----------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is missing. " +
        "Copy appsettings.Development.json.example to appsettings.Development.json and fill in values.");

var authOptions = builder.Configuration
    .GetSection(AuthOptions.SectionName)
    .Get<AuthOptions>() ?? new AuthOptions();

var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()
    ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    if (builder.Environment.IsProduction())
        throw new InvalidOperationException("Jwt:SigningKey is required in Production. Set it via environment variable.");

    jwtOptions.SigningKey = Convert.ToBase64String(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    Console.WriteLine("⚠  Jwt:SigningKey not configured — using a randomly generated key. " +
                      "Tokens will not survive a restart. Set Jwt:SigningKey for persistence.");
}

// -----------------------------------------------------------------------
// Authentication
// -----------------------------------------------------------------------
// -----------------------------------------------------------------------
// DevBypass Production guard (AC: refuses to start in Production)
// -----------------------------------------------------------------------
if (authOptions.Mode == AuthMode.DevBypass && builder.Environment.IsProduction())
{
    throw new InvalidOperationException(
        "Auth:Mode=DevBypass must not be used in Production. " +
        "Set Auth:Mode=AzureAd (or WindowsAuth) and configure real AD credentials.");
}

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
                options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme    = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var jwtSvc = new JwtService(jwtOptions);
                options.TokenValidationParameters = jwtSvc.GetValidationParameters();
            });
        break;

    case AuthMode.WindowsAuth:
        var useFakeNegotiate = builder.Configuration["WINDOWS_AUTH_FAKE_NEGOTIATE"] == "true";
        if (useFakeNegotiate && builder.Environment.IsProduction())
            throw new InvalidOperationException(
                "WINDOWS_AUTH_FAKE_NEGOTIATE must not be used in Production.");

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

        authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var jwtSvc = new JwtService(jwtOptions);
            options.TokenValidationParameters = jwtSvc.GetValidationParameters();
        });
        break;

    case AuthMode.AzureAd:
    default:
        // The OIDC redirect URI (AzureAd:CallbackPath, default /signin-oidc) is owned by the
        // OIDC handler. /api/auth/callback is the app's own post-login action, so the two must
        // never coincide — the handler would swallow the second GET with "state is null" (#154).
        var aadCallbackPath = builder.Configuration["AzureAd:CallbackPath"];
        if (!string.IsNullOrEmpty(aadCallbackPath)
            && aadCallbackPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"AzureAd:CallbackPath '{aadCallbackPath}' collides with the API routes. Leave it unset " +
                $"(defaults to {AzureAdSchemes.CallbackPath}) and register that path as the redirect URI " +
                "in the app registration.");
        }

        var useFakeOidc = builder.Configuration["AZUREAD_FAKE_OIDC"] == "true";
        if (useFakeOidc && builder.Environment.IsProduction())
            throw new InvalidOperationException("AZUREAD_FAKE_OIDC must not be used in Production.");

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
            o.Cookie.SecurePolicy = builder.Environment.IsProduction()
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan      = TimeSpan.FromHours(8);
            o.SlidingExpiration   = false;
        });

        aadBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var jwtSvc = new JwtService(jwtOptions);
            options.TokenValidationParameters = jwtSvc.GetValidationParameters();
        });
        break;
}

// Policies (default deny — see CmsAuthorizationExtensions, #155)
builder.Services.AddCmsAuthorization();

// -----------------------------------------------------------------------
// Services
// -----------------------------------------------------------------------
builder.Services.AddControllers();

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

// PetaPoco database
builder.Services.AddScoped<CmsDatabase>(_ => new CmsDatabase(connectionString));

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
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IDbMonitorRepository, DbMonitorRepository>();

// Story #67: AD group role mapping repository and resolver
builder.Services.AddScoped<IAdGroupMappingRepository, AdGroupMappingRepository>();
builder.Services.AddScoped<IAdGroupRoleResolver, AdGroupRoleResolver>();
builder.Services.AddScoped<VA.CMS.API.Services.IMediaUsageSyncService, VA.CMS.API.Services.MediaUsageSyncService>();

// Issue #49: Full-text search repository (FR-SEARCH-02)
builder.Services.AddScoped<ISearchRepository, SearchRepository>();

// Issue #51: Search analytics repository (FR-SEARCH-06)
builder.Services.AddScoped<ISearchAnalyticsRepository, SearchAnalyticsRepository>();

// Issue #52: Search pins repository (FR-SEARCH-04)
builder.Services.AddScoped<ISearchPinRepository, SearchPinRepository>();

// Issue #53: Taxonomy repository (needed for GraphQL)
builder.Services.AddScoped<ITaxonomyRepository, TaxonomyRepository>();

// Auth services
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IJwtService, JwtService>();
builder.Services.AddSingleton<IRefreshTokenService, InMemoryRefreshTokenService>();
builder.Services.AddSingleton<IRbacService, RbacService>();

// -----------------------------------------------------------------------
// Storage backend (issue #40: BRD FR-MEDIA-07 / FR-SECURITY-06)
// -----------------------------------------------------------------------
var storageOptions = builder.Configuration
    .GetSection(StorageOptions.SectionName)
    .Get<StorageOptions>() ?? new StorageOptions();
builder.Services.AddSingleton(storageOptions);

IStorageBackend storageBackend = storageOptions.Backend?.ToLowerInvariant() switch
{
    "unc"        => new UncStorageBackend(storageOptions),
    "azure_blob" => new AzureBlobStorageBackend(storageOptions),
    _            => new LocalStorageBackend(storageOptions),   // default: local
};
builder.Services.AddSingleton<IStorageBackend>(storageBackend);
builder.Services.AddSingleton<IImageProcessingService, ImageProcessingService>();
// Issue #45: virus scan hook — default no-op; VA teams replace with real AV implementation via DI
builder.Services.AddSingleton<IVirusScanService, NoOpVirusScanService>();
builder.Services.AddScoped<IMediaExtendedRepository, MediaExtendedRepository>();
builder.Services.AddScoped<IMediaUploadService, MediaUploadService>();

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
// Per-delivery timeout is webhooks.timeoutSeconds (applied in WebhookDispatcher); this is only a ceiling.
builder.Services.AddHttpClient("WebhookClient")
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddScoped<IWebhookDispatcher, WebhookDispatcher>();
builder.Services.AddSingleton<IWebhookBackgroundDispatcher, WebhookBackgroundDispatcher>();

// Issue #38: In-app notification center for workflow events (BRD FR-WORKFLOW-02/03)
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<IWorkflowNotifier, WorkflowNotifier>();

// Issue #39: Workflow emails over SMTP (BRD FR-WORKFLOW-02). The relay and its credentials
// come from the Email:Smtp section (Email__Smtp__Host etc.); the switch, sender and link
// origin are site settings (notifications.email*). Without a host, messages are logged.
var emailOptions = builder.Configuration
    .GetSection(EmailOptions.SectionName)
    .Get<EmailOptions>() ?? new EmailOptions();
emailOptions.Validate();
builder.Services.AddSingleton(emailOptions);
if (emailOptions.IsEnabled)
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
else
{
    builder.Services.AddSingleton<IEmailSender, DisabledEmailSender>();
    Console.WriteLine("ℹ  Email:Smtp:Host not configured — workflow emails will be logged, not sent.");
}
builder.Services.AddSingleton<IEmailDispatcher, BackgroundEmailDispatcher>();

// Issue #35: Scheduled publish / expiry background worker (BRD FR-AUTH-04)
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
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Migration failed: {result.Error}");
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Database migrations applied successfully.");
        Console.ResetColor();
    }
    else
    {
        var status = await VA.CMS.Infrastructure.Data.Migrations.MigrationRunner.CheckAsync(connectionString, migrationsPath);
        if (!status.IsUpToDate)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(status.Error ?? "The database is behind the deployed migration set.");
            Console.Error.WriteLine($"Pending migrations ({status.Pending.Count}): {string.Join(", ", status.Pending)}");
            Console.Error.WriteLine("Run `vacms db migrate` with the deployment account, or set Database:MigrateOnStartup=true.");
            Console.ResetColor();
            return 1;
        }

        Console.WriteLine($"Database schema is current ({status.Applied.Count} migrations applied).");
    }
}

// -----------------------------------------------------------------------
// HTTP pipeline
// -----------------------------------------------------------------------
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

// -----------------------------------------------------------------------
// Site setting gates (epic #141): features.graphql / features.swaggerUi answer 404 while
// off, and media.maxUploadBytes sets the upload body limit. Must run before Swagger,
// routing and the form reader.
// -----------------------------------------------------------------------
app.UseSiteSettingGates(app.Environment.IsDevelopment());

// -----------------------------------------------------------------------
// Swagger UI — always on in Development (AC: Issue #55); elsewhere the middleware above
// serves it only while the features.swaggerUi site setting is on.
// -----------------------------------------------------------------------
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

// DevBypass: inject a JWT from the X-Dev-User header BEFORE the auth pipeline runs.
// Must be placed before UseAuthentication so the injected token is visible to JWT bearer.
// The middleware is a no-op in AzureAd and WindowsAuth modes (guard in Program.cs).
if (authOptions.Mode == AuthMode.DevBypass)
    app.UseDevBypassAuth();

app.UseAuthentication();
app.UseAuthHeaderRedaction();
app.UseAuthorization();

app.MapControllers();

// Issue #53: GraphQL endpoint + playground
// Endpoint:  /api/graphql
// Playground: /api/graphql/ui (Development only — HC disables Banana Cake Pop in non-dev by default)
app.MapGraphQL("/api/graphql")
   .AllowAnonymous();   // #156: anonymous = Published-only surface; CanRead JWT = full surface (see GraphQLAudience)

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
   .AllowAnonymous();

app.Run();

return 0;

public partial class Program { }
