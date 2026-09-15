namespace VA.CMS.Infrastructure.Data.Pocos;

/// <summary>Row returned by usp_Monitor_IndexFragmentation.</summary>
public class IndexFragmentationRow
{
    public string TableName { get; set; } = string.Empty;
    public string IndexName { get; set; } = string.Empty;
    public double FragmentationPct { get; set; }
    public long PageCount { get; set; }
}

/// <summary>Row returned by usp_Monitor_TableSizes.</summary>
public class TableSizeRow
{
    public string TableName { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public long TotalSizeMB { get; set; }
    public long UsedSizeMB { get; set; }
}

/// <summary>Row returned by usp_Monitor_LongRunningQueries.</summary>
public class LongRunningQueryRow
{
    public int SessionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public int DurationSec { get; set; }
    public string Command { get; set; } = string.Empty;
    public string? QueryText { get; set; }
    public string? WaitType { get; set; }
    public int? BlockingSessionId { get; set; }
}
