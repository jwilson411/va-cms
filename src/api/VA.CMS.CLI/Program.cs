using Microsoft.Extensions.Configuration;
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
    Console.WriteLine("  health --url <url>           HTTP health probe");
    Console.WriteLine();
    Console.WriteLine("Connection string resolution order:");
    Console.WriteLine("  1. VACMS_CONNECTION_STRING environment variable");
    Console.WriteLine("  2. appsettings[.Development].json DefaultConnection");
    Console.WriteLine("  3. Local dev default (localhost:14333, VACMS_Dev, sa)");
}
