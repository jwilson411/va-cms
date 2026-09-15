using Microsoft.AspNetCore.Mvc;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Controllers.Admin;

/// <summary>
/// GET /api/v1/admin/health/db — Returns combined database health metrics.
/// Requires SystemAdmin role (auth middleware enforces this in production;
/// DevBypass mode accepts any X-Dev-User header).
/// </summary>
[ApiController]
[Route("api/v1/admin/health")]
public class DbHealthController : ControllerBase
{
    private readonly IDbMonitorRepository _monitor;

    public DbHealthController(IDbMonitorRepository monitor)
    {
        _monitor = monitor;
    }

    /// <summary>
    /// Returns combined database health: index fragmentation, table sizes,
    /// and any queries running longer than 5 seconds.
    /// Requires SystemAdmin role.
    /// </summary>
    [HttpGet("db")]
    [ProducesResponseType(typeof(DbHealthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDbHealth()
    {
        var fragmentation   = await _monitor.GetIndexFragmentationAsync();
        var tableSizes      = await _monitor.GetTableSizesAsync();
        var longRunning     = await _monitor.GetLongRunningQueriesAsync();

        var response = new DbHealthResponse
        {
            IndexFragmentation  = fragmentation.ToList(),
            TableSizes          = tableSizes.ToList(),
            LongRunningQueries  = longRunning.ToList(),
            CollectedAt         = DateTime.UtcNow,
        };

        return Ok(response);
    }
}

/// <summary>Combined JSON response for GET /api/v1/admin/health/db.</summary>
public sealed class DbHealthResponse
{
    public List<IndexFragmentationRow> IndexFragmentation  { get; set; } = [];
    public List<TableSizeRow>          TableSizes          { get; set; } = [];
    public List<LongRunningQueryRow>   LongRunningQueries  { get; set; } = [];
    public DateTime                    CollectedAt         { get; set; }
}
