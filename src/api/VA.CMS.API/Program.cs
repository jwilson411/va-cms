using DbUp;
using DbUp.Engine;
using DbUp.ScriptProviders;
using VA.CMS.Infrastructure.Data;
using VA.CMS.Infrastructure.Data.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is missing. " +
        "Copy appsettings.Development.json.example to appsettings.Development.json and fill in values.");

// -----------------------------------------------------------------------
// PetaPoco: register CmsDatabase as a scoped service
// All DB queries go through repositories — no raw calls in controllers.
// -----------------------------------------------------------------------
builder.Services.AddScoped<CmsDatabase>(_ => new CmsDatabase(connectionString));

// Repository registrations
builder.Services.AddScoped<IContentEntryRepository, ContentEntryRepository>();
builder.Services.AddScoped<IContentVersionRepository, ContentVersionRepository>();
builder.Services.AddScoped<IMediaAssetRepository, MediaAssetRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<INavigationMenuRepository, NavigationMenuRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();

var app = builder.Build();

// -----------------------------------------------------------------------
// DbUp: run all pending SQL migrations from the /migrations folder.
// Migrations are plain SQL files (V{NNN}__description.sql) and run
// exactly once per environment in version order.
// The API aborts startup if any migration fails — no partial state.
// -----------------------------------------------------------------------

// Resolve the migrations directory relative to the application base
var migrationsPath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "migrations"));

if (!Directory.Exists(migrationsPath))
{
    // Fallback: look for migrations/ two levels up from the solution root
    migrationsPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "migrations"));
}

// Ensure the target database exists (creates it if absent)
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

// -----------------------------------------------------------------------
// HTTP pipeline
// -----------------------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseAuthorization();
app.MapControllers();

// Simple health probe (no auth required)
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();

return 0;
