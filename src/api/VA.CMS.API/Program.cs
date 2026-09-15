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
using VA.CMS.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------
// Configuration
// -----------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is missing. " +
        "Copy appsettings.Development.json.example to appsettings.Development.json and fill in values.");

// Auth options — drives which scheme is active.
var authOptions = builder.Configuration
    .GetSection(AuthOptions.SectionName)
    .Get<AuthOptions>() ?? new AuthOptions();

// JWT options — Jwt:SigningKey must be set via environment variable in production.
var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()
    ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    // In Development, accept a missing signing key with a generated one so the app starts.
    // In Production this is a fatal misconfiguration.
    if (builder.Environment.IsProduction())
        throw new InvalidOperationException("Jwt:SigningKey is required in Production. Set it via environment variable.");

    jwtOptions.SigningKey = Convert.ToBase64String(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    Console.WriteLine("⚠  Jwt:SigningKey not configured — using a randomly generated key. " +
                      "Tokens will not survive a restart. Set Jwt:SigningKey for persistence.");
}

// -----------------------------------------------------------------------
// Authentication
//
// Three modes (selected by Auth:Mode configuration value):
//
//  AzureAd (default):
//    "AzureAd" (OpenIdConnect/cookie) — handles the OIDC login/callback.
//    Microsoft.Identity.Web validates the Azure AD token in /api/auth/callback.
//    "Bearer" (JwtBearer) — protects all other API endpoints.
//
//  WindowsAuth:
//    "Negotiate" — Windows Integrated Auth (Kerberos/NTLM) for IIS intranet.
//    /api/auth/windows-login extracts the Windows identity and issues a CMS JWT.
//    "Bearer" (JwtBearer) — protects all other API endpoints.
//
//  DevBypass:
//    No AD; X-Dev-User header accepted as UPN. Development only.
//
// The default challenge scheme is JwtBearer so unauthenticated API calls
// get a 401 rather than an OIDC redirect.
// -----------------------------------------------------------------------
switch (authOptions.Mode)
{
    case AuthMode.WindowsAuth:
        // Register Negotiate (Windows Integrated Auth) + JwtBearer.
        // Negotiate is used only on /api/auth/windows-login — everything else
        // requires a Bearer JWT.
        //
        // In test environments, the real NegotiateHandler requires Kestrel
        // (IConnectionItemsFeature) which is not available in WebApplicationFactory's
        // in-memory test server. When WINDOWS_AUTH_FAKE_NEGOTIATE=true is set,
        // we use a passthrough handler that trusts the X-Test-Windows-Upn header.
        // This flag must NEVER be set in production environments.
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
            // Test-only: passthrough Negotiate handler. Never in Production.
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
        // Microsoft.Identity.Web registers the AzureAd OIDC scheme (uses cookies internally).
        // Then chain JwtBearer as the second scheme for API endpoint protection.
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

// All endpoints require a valid JWT by default.
// /api/auth/* and /health are explicitly [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // RBAC policies (story #23, BRD FR-USERS-03/04).
    // Policy = minimum required role; higher roles satisfy lower-tier policies.
    // CmsRoleRequirement (not built-in RequireRole) handles both global and section-scoped claims.
    var allRoles = new[]
    {
        VA.CMS.API.Auth.CmsRoles.ContentOwner,
        VA.CMS.API.Auth.CmsRoles.Editor,
        VA.CMS.API.Auth.CmsRoles.SiteAdmin,
        VA.CMS.API.Auth.CmsRoles.Developer,
        VA.CMS.API.Auth.CmsRoles.SystemAdmin,
        VA.CMS.API.Auth.CmsRoles.ReadOnly,
    };

    // AnyRole: any authenticated CMS role
    options.AddPolicy(VA.CMS.API.Auth.CmsRoles.Policies.AnyRole, p =>
        p.RequireAuthenticatedUser()
         .AddRequirements(new VA.CMS.API.Auth.CmsRoleRequirement(allRoles)));

    // CanRead: all roles may read
    options.AddPolicy(VA.CMS.API.Auth.CmsRoles.Policies.CanRead, p =>
        p.RequireAuthenticatedUser()
         .AddRequirements(new VA.CMS.API.Auth.CmsRoleRequirement(allRoles)));

    // CanWrite: ContentOwner (section-scoped check in service layer), Editor, SiteAdmin, SystemAdmin
    options.AddPolicy(VA.CMS.API.Auth.CmsRoles.Policies.CanWrite, p =>
        p.RequireAuthenticatedUser()
         .AddRequirements(new VA.CMS.API.Auth.CmsRoleRequirement(
             VA.CMS.API.Auth.CmsRoles.ContentOwner,
             VA.CMS.API.Auth.CmsRoles.Editor,
             VA.CMS.API.Auth.CmsRoles.SiteAdmin,
             VA.CMS.API.Auth.CmsRoles.SystemAdmin)));

    // CanPublish: Editor, SiteAdmin, SystemAdmin
    options.AddPolicy(VA.CMS.API.Auth.CmsRoles.Policies.CanPublish, p =>
        p.RequireAuthenticatedUser()
         .AddRequirements(new VA.CMS.API.Auth.CmsRoleRequirement(
             VA.CMS.API.Auth.CmsRoles.Editor,
             VA.CMS.API.Auth.CmsRoles.SiteAdmin,
             VA.CMS.API.Auth.CmsRoles.SystemAdmin)));

    // CanManageSite: SiteAdmin, SystemAdmin
    options.AddPolicy(VA.CMS.API.Auth.CmsRoles.Policies.CanManageSite, p =>
        p.RequireAuthenticatedUser()
         .AddRequirements(new VA.CMS.API.Auth.CmsRoleRequirement(
             VA.CMS.API.Auth.CmsRoles.SiteAdmin,
             VA.CMS.API.Auth.CmsRoles.SystemAdmin)));

    // CanDevelop: Developer, SystemAdmin
    options.AddPolicy(VA.CMS.API.Auth.CmsRoles.Policies.CanDevelop, p =>
        p.RequireAuthenticatedUser()
         .AddRequirements(new VA.CMS.API.Auth.CmsRoleRequirement(
             VA.CMS.API.Auth.CmsRoles.Developer,
             VA.CMS.API.Auth.CmsRoles.SystemAdmin)));

    // CanAdminSystem: SystemAdmin only
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
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<INavigationMenuRepository, NavigationMenuRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IDbMonitorRepository, DbMonitorRepository>();

// Auth services
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IJwtService, JwtService>();
builder.Services.AddSingleton<IRefreshTokenService, InMemoryRefreshTokenService>();
builder.Services.AddSingleton<IRbacService, RbacService>();
// Register the CmsRoleHandler for CmsRoleRequirement (handles global + scoped claims).
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, CmsRoleHandler>();

// Seed (demo)
builder.Services.AddScoped<ISeedService, DemoSeedService>();

// -----------------------------------------------------------------------
// Build
// -----------------------------------------------------------------------
var app = builder.Build();

// -----------------------------------------------------------------------
// DbUp: run all pending SQL migrations from the /migrations folder.
// Skipped when SKIP_MIGRATIONS=true (e.g. in integration test hosts).
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

// Authentication MUST run before auth-header redaction so JwtBearer
// can read the original Authorization header value.
app.UseAuthentication();

// Strip Authorization header from request after auth has consumed it,
// so logging middleware never writes the bearer token value to logs.
app.UseAuthHeaderRedaction();

app.UseAuthorization();

app.MapControllers();

// Health probe — no auth required
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
   .AllowAnonymous();

app.Run();

return 0;

// Make Program accessible to WebApplicationFactory<Program> in test projects.
public partial class Program { }
