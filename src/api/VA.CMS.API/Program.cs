using DbUp;
using DbUp.Engine;
using DbUp.ScriptProviders;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using VA.CMS.API.Auth;
using VA.CMS.API.Middleware;
using VA.CMS.Infrastructure.Data;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.ContentTypes;
using VA.CMS.Infrastructure.ContentTypes.BuiltIn;
using VA.CMS.Infrastructure.ContentTypes.CustomFields;
using VA.CMS.Infrastructure.Services;
using VA.CMS.Infrastructure.Storage;

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
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"),
                openIdConnectScheme: "AzureAd",
                cookieScheme:        "AzureAdCookies")
            .Services
            .AddAuthentication()
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var jwtSvc = new JwtService(jwtOptions);
                options.TokenValidationParameters = jwtSvc.GetValidationParameters();
            });
        break;
}

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    var allRoles = new[]
    {
        VA.CMS.API.Auth.CmsRoles.ContentOwner,
        VA.CMS.API.Auth.CmsRoles.Editor,
        VA.CMS.API.Auth.CmsRoles.SiteAdmin,
        VA.CMS.API.Auth.CmsRoles.Developer,
        VA.CMS.API.Auth.CmsRoles.SystemAdmin,
        VA.CMS.API.Auth.CmsRoles.ReadOnly,
    };

    options.AddPolicy(VA.CMS.API.Auth.CmsRoles.Policies.AnyRole, p =>
        p.RequireAuthenticatedUser()
         .AddRequirements(new VA.CMS.API.Auth.CmsRoleRequirement(allRoles)));

    options.AddPolicy(VA.CMS.API.Auth.CmsRoles.Policies.CanRead, p =>
        p.RequireAuthenticatedUser()
         .AddRequirements(new VA.CMS.API.Auth.CmsRoleRequirement(allRoles)));

    options.AddPolicy(VA.CMS.API.Auth.CmsRoles.Policies.CanWrite, p =>
        p.RequireAuthenticatedUser()
         .AddRequirements(new VA.CMS.API.Auth.CmsRoleRequirement(
             VA.CMS.API.Auth.CmsRoles.ContentOwner,
             VA.CMS.API.Auth.CmsRoles.Editor,
             VA.CMS.API.Auth.CmsRoles.SiteAdmin,
             VA.CMS.API.Auth.CmsRoles.SystemAdmin)));

    options.AddPolicy(VA.CMS.API.Auth.CmsRoles.Policies.CanPublish, p =>
        p.RequireAuthenticatedUser()
         .AddRequirements(new VA.CMS.API.Auth.CmsRoleRequirement(
             VA.CMS.API.Auth.CmsRoles.Editor,
             VA.CMS.API.Auth.CmsRoles.SiteAdmin,
             VA.CMS.API.Auth.CmsRoles.SystemAdmin)));

    options.AddPolicy(VA.CMS.API.Auth.CmsRoles.Policies.CanManageSite, p =>
        p.RequireAuthenticatedUser()
         .AddRequirements(new VA.CMS.API.Auth.CmsRoleRequirement(
             VA.CMS.API.Auth.CmsRoles.SiteAdmin,
             VA.CMS.API.Auth.CmsRoles.SystemAdmin)));

    options.AddPolicy(VA.CMS.API.Auth.CmsRoles.Policies.CanDevelop, p =>
        p.RequireAuthenticatedUser()
         .AddRequirements(new VA.CMS.API.Auth.CmsRoleRequirement(
             VA.CMS.API.Auth.CmsRoles.Developer,
             VA.CMS.API.Auth.CmsRoles.SystemAdmin)));

    options.AddPolicy(VA.CMS.API.Auth.CmsRoles.Policies.CanAdminSystem, p =>
        p.RequireAuthenticatedUser()
         .AddRequirements(new VA.CMS.API.Auth.CmsRoleRequirement(
             VA.CMS.API.Auth.CmsRoles.SystemAdmin)));
});

// -----------------------------------------------------------------------
// Services
// -----------------------------------------------------------------------
builder.Services.AddControllers();

// PetaPoco database
builder.Services.AddScoped<CmsDatabase>(_ => new CmsDatabase(connectionString));

// Repositories
builder.Services.AddScoped<IContentEntryRepository, ContentEntryRepository>();
builder.Services.AddScoped<IContentVersionRepository, ContentVersionRepository>();
builder.Services.AddScoped<IMediaAssetRepository, MediaAssetRepository>();
builder.Services.AddScoped<IMediaExtendedRepository, MediaExtendedRepository>();
builder.Services.AddScoped<IMediaAltTextGuardRepository, MediaAltTextGuardRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<INavigationMenuRepository, NavigationMenuRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IDbMonitorRepository, DbMonitorRepository>();

// Story #67: AD group role mapping repository and resolver
builder.Services.AddScoped<IAdGroupMappingRepository, AdGroupMappingRepository>();
builder.Services.AddScoped<IAdGroupRoleResolver, AdGroupRoleResolver>();

// Auth services
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IJwtService, JwtService>();
builder.Services.AddSingleton<IRefreshTokenService, InMemoryRefreshTokenService>();
builder.Services.AddSingleton<IRbacService, RbacService>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, CmsRoleHandler>();

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
builder.Services.AddScoped<IMediaUploadService, MediaUploadService>();

// Preview token service — issue #34 (BRD FR-AUTH-08)
builder.Services.AddSingleton<IPreviewTokenService, PreviewTokenService>();

// Markdown renderer (shared by preview and publish pipelines)
builder.Services.AddSingleton<VA.CMS.Infrastructure.Markdown.IUswdsMarkdownRenderer,
    VA.CMS.Infrastructure.Markdown.UswdsMarkdownRenderer>();

// Seed (demo)
builder.Services.AddScoped<ISeedService, DemoSeedService>();

// Issue #35: Scheduled publish / expiry background worker (BRD FR-AUTH-04)
builder.Services.AddHostedService<VA.CMS.Infrastructure.Services.ScheduledPublishWorker>();

// -----------------------------------------------------------------------
// Content Type Registry (FR-SCHEMA-01)
// -----------------------------------------------------------------------
builder.Services.AddContentType<StandardPageTypeDefinition>();

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
// DbUp migrations
// -----------------------------------------------------------------------
var skipMigrations = builder.Configuration["SKIP_MIGRATIONS"] == "true";

if (!skipMigrations)
{
var migrationsPath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "migrations"));

if (!Directory.Exists(migrationsPath))
    migrationsPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "migrations"));

EnsureDatabase.For.SqlDatabase(connectionString);

var upgrader = DeployChanges.To
    .SqlDatabase(connectionString)
    .WithScriptsFromFileSystem(
        migrationsPath,
        new FileSystemScriptOptions { IncludeSubDirectories = false })
    .WithTransactionPerScript()
    .LogToConsole()
    .Build();

DatabaseUpgradeResult result = upgrader.PerformUpgrade();
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

} // end if (!skipMigrations)

// -----------------------------------------------------------------------
// HTTP pipeline
// -----------------------------------------------------------------------
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

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

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
   .AllowAnonymous();

app.Run();

return 0;

public partial class Program { }
