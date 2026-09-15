using Microsoft.Extensions.Configuration;
using VA.CMS.Infrastructure.ContentTypes;
using VA.CMS.Infrastructure.Data;
using VA.CMS.Infrastructure.Services;

// ---------------------------------------------------------------------------
// VA CMS CLI — vacms
//
// Usage:
//   vacms db seed --demo              Seed demo content (idempotent)
//   vacms db seed --demo --reset      Drop all demo content and re-seed
//   vacms db migrate                  Run pending DbUp migrations
//   vacms health --url <url>          HTTP health check (stub)
//   vacms content-type scaffold <Name>   Scaffold a new content type definition
//   vacms content-type --help         Show content-type command help
//
// Connection string is resolved from:
//   1. VACMS_CONNECTION_STRING environment variable
//   2. appsettings.json ConnectionStrings:DefaultConnection
//   3. Hardcoded local dev default
// ---------------------------------------------------------------------------

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0)
    {
        PrintHelp();
        return 0;
    }

    // Parse top-level command
    if (args[0] == "db" && args.Length >= 2)
    {
        if (args[1] == "seed")
        {
            bool isDemo  = args.Contains("--demo");
            bool isReset = args.Contains("--reset");

            if (!isDemo)
            {
                Console.Error.WriteLine("Error: 'vacms db seed' requires the --demo flag.");
                Console.Error.WriteLine("Usage: vacms db seed --demo [--reset]");
                return 1;
            }

            var connStr = ResolveConnectionString();
            var db = new CmsDatabase(connStr);
            var service = new DemoSeedService(db);

            if (isReset)
            {
                Console.WriteLine("Resetting demo data and re-seeding...");
                await service.ResetAndSeedDemoAsync();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Demo data reset and re-seeded successfully.");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine("Seeding demo data (idempotent)...");
                await service.SeedDemoAsync();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Demo data seeded successfully.");
                Console.ResetColor();
            }

            return 0;
        }

        if (args[1] == "migrate")
        {
            Console.Error.WriteLine("Use the API startup to run migrations (DbUp runs on startup).");
            Console.Error.WriteLine("Or run the API with --migrate-only for an explicit migration run.");
            return 1;
        }
    }

    // ── content-type commands ────────────────────────────────────────────────
    if (args[0] == "content-type")
    {
        // vacms content-type --help / -h / help
        if (args.Length == 1
            || args[1] == "--help"
            || args[1] == "-h"
            || args[1] == "help")
        {
            PrintContentTypeHelp();
            return 0;
        }

        // vacms content-type scaffold <Name>
        if (args[1] == "scaffold")
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Error: 'vacms content-type scaffold' requires a <Name> argument.");
                Console.Error.WriteLine("Usage: vacms content-type scaffold <Name>");
                Console.Error.WriteLine("       <Name> must be a PascalCase identifier, e.g. NewsArticle");
                return 1;
            }

            var typeName = args[2];

            try
            {
                var outputDir = args.Length >= 5 && args[3] == "--output"
                    ? args[4]
                    : null;

                var filePath = ScaffoldService.Scaffold(typeName, outputDir);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Scaffolded: {filePath}");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("Next steps:");
                Console.WriteLine("  1. Move the file to your project's ContentTypes/ folder.");
                Console.WriteLine("  2. Register it in Program.cs:");
                Console.WriteLine($"       builder.Services.AddContentType<{typeName}TypeDefinition>();");
                Console.WriteLine("  3. Customise the field definitions as needed.");
                return 0;
            }
            catch (ArgumentException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        Console.Error.WriteLine($"Unknown content-type sub-command: {args[1]}");
        PrintContentTypeHelp();
        return 1;
    }

    if (args[0] == "health")
    {
        Console.WriteLine("Health check not yet implemented. Run the API and call /health.");
        return 0;
    }

    if (args[0] == "--help" || args[0] == "-h" || args[0] == "help")
    {
        PrintHelp();
        return 0;
    }

    Console.Error.WriteLine($"Unknown command: {string.Join(' ', args)}");
    PrintHelp();
    return 1;
}

static string ResolveConnectionString()
{
    // 1. Environment variable (highest priority — CI/CD / staging)
    var env = Environment.GetEnvironmentVariable("VACMS_CONNECTION_STRING");
    if (!string.IsNullOrWhiteSpace(env))
        return env;

    // 2. appsettings.json in the working directory
    var config = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile("appsettings.Development.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    var fromConfig = config.GetConnectionString("DefaultConnection");
    if (!string.IsNullOrWhiteSpace(fromConfig))
        return fromConfig;

    // 3. Local dev default (matches the dev container defined in the tech stack)
    return "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;" +
           "Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=15;";
}

static void PrintHelp()
{
    Console.WriteLine("VA CMS CLI");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  db seed --demo              Seed demo content (idempotent)");
    Console.WriteLine("  db seed --demo --reset       Drop all demo content and re-seed");
    Console.WriteLine("  content-type scaffold <Name> Scaffold a new content type definition file");
    Console.WriteLine("  content-type --help          Show content-type command details");
    Console.WriteLine("  health --url <url>           HTTP health probe");
    Console.WriteLine();
    Console.WriteLine("Connection string resolution order:");
    Console.WriteLine("  1. VACMS_CONNECTION_STRING environment variable");
    Console.WriteLine("  2. appsettings[.Development].json DefaultConnection");
    Console.WriteLine("  3. Local dev default (localhost:14333, VACMS_Dev, sa)");
}

static void PrintContentTypeHelp()
{
    Console.WriteLine("vacms content-type — Content type scaffolding commands");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  vacms content-type scaffold <Name>            Scaffold a new type definition");
    Console.WriteLine("  vacms content-type scaffold <Name> --output <dir>   Write to a specific directory");
    Console.WriteLine("  vacms content-type --help                     Show this help");
    Console.WriteLine();
    Console.WriteLine("Arguments:");
    Console.WriteLine("  <Name>   PascalCase type name, e.g. NewsArticle, BenefitsPage, EventListing");
    Console.WriteLine();
    Console.WriteLine("The scaffold command creates:");
    Console.WriteLine("  <output>/ContentTypes/<Name>TypeDefinition.cs");
    Console.WriteLine();
    Console.WriteLine("The generated file:");
    Console.WriteLine("  - Derives from ContentTypeDefinitionBase");
    Console.WriteLine("  - Includes starter ShortText/LongText/RichText fields");
    Console.WriteLine("  - Compiles without modification");
    Console.WriteLine("  - Must be registered via builder.Services.AddContentType<T>()");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  vacms content-type scaffold NewsArticle");
    Console.WriteLine("  vacms content-type scaffold BenefitsPage --output ./src/api/MyProject");
}
