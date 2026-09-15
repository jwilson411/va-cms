namespace VA.CMS.Infrastructure.Data.Pocos;

/// <summary>
/// A single row from usp_Search_GetTopQueries or the dashboard widget.
/// Issue #51 — Search analytics dashboard widget.
/// </summary>
public sealed class SearchQueryStat
{
    public string Query { get; set; } = string.Empty;
    public int SearchCount { get; set; }
    public int ZeroResultCount { get; set; }
    public decimal AvgResultCount { get; set; }
    public DateTime LastSearchedAt { get; set; }
}

/// <summary>
/// A single row from usp_Search_GetZeroResultQueries.
/// Issue #51.
/// </summary>
public sealed class SearchZeroResultStat
{
    public string Query { get; set; } = string.Empty;
    public int ZeroResultCount { get; set; }
    public DateTime LastSearchedAt { get; set; }
}

/// <summary>
/// A single row from usp_Search_GetAnalyticsFull (full analytics table).
/// Issue #51.
/// </summary>
public sealed class SearchAnalyticsRow
{
    public string Query { get; set; } = string.Empty;
    public int SearchCount { get; set; }
    public int ZeroResultCount { get; set; }
    public decimal AvgResultCount { get; set; }
    public DateTime LastSearchedAt { get; set; }
    public int ClickCount { get; set; }
    public decimal ClickThroughRate { get; set; }
}
