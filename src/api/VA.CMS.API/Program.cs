using DbUp;
using DbUp.Engine;
using DbUp.ScriptProviders;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

var app = builder.Build();

// -----------------------------------------------------------------------
// DbUp: run all pending SQL migrations from the /migrations folder.
// Migrations are plain SQL files (V{NNN}__description.sql) and run
// exactly once per environment in version order.
// The API aborts startup if any migration fails — no partial state.
// -----------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is missing. " +
        "Copy appsettings.Development.json.example to appsettings.Development.json and fill in values.");

// Ensure the target database exists (creates it if absent)
EnsureDatabase.For.SqlDatabase(connectionString);

// Resolve the migrations directory relative to the application base
var migrationsPath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "migrations"));

if (!Directory.Exists(migrationsPath))
{
    // Fallback: look for migrations/ two levels up from the solution root
    migrationsPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "migrations"));
}

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
