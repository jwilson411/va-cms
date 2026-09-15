using DbUp;
using DbUp.Engine;
using DbUp.ScriptProviders;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
// Two schemes are registered:
//  1. "AzureAd" (OpenIdConnect/cookie) — handles the OIDC login/callback.
//     Microsoft.Identity.Web validates the Azure AD token in /api/auth/callback.
//  2. "Bearer" (JwtBearer) — protects all other API endpoints.
//     The CMS issues these JWTs after a successful AD login.
//
// The default challenge scheme is JwtBearer so unauthenticated API calls
// get a 401 rather than an OIDC redirect.
// -----------------------------------------------------------------------

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

// All endpoints require a valid JWT by default.
// /api/auth/* and /health are explicitly [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
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
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IJwtService, JwtService>();
builder.Services.AddSingleton<IRefreshTokenService, InMemoryRefreshTokenService>();

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
