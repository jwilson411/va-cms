using VA.CMS.Infrastructure.Data;

namespace VA.CMS.Infrastructure.Services;

/// <summary>
/// Seed service interface: run demo seeding and optional reset.
/// </summary>
public interface ISeedService
{
    /// <summary>
    /// Insert demo content (idempotent — checks slug/name before inserting).
    /// Seeds: 2 content types, 5 standard pages, 3 news articles,
    ///        2 taxonomy terms, and 1 demo user per role.
    /// </summary>
    Task SeedDemoAsync();

    /// <summary>
    /// Drop all demo content (entries, versions, taxonomy, users)
    /// then re-seed via <see cref="SeedDemoAsync"/>.
    /// Does not affect schema, built-in roles, or non-demo data.
    /// </summary>
    Task ResetAndSeedDemoAsync();
}
