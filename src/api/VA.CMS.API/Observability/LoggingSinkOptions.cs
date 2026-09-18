using System.ComponentModel.DataAnnotations;

namespace VA.CMS.API.Observability;

/// <summary>
/// Where structured logs go — the "Logging:Sinks" configuration section (#166, BRD NFR-OPS-02).
/// Sinks are deployment wiring (paths, hosts, tokens), so like the SMTP relay they stay in
/// configuration rather than the settings table. Levels still come from the standard
/// "Logging:LogLevel" section, which Serilog honours as its minimum-level overrides.
///
///   Logging__Sinks__Console__Enabled=true              # JSON outside Development, text inside
///   Logging__Sinks__File__Enabled=true
///   Logging__Sinks__File__Path=D:\logs\vacms\api-.json  # date is inserted before the extension
///   Logging__Sinks__EventLog__Enabled=true              # Windows only; Warning and above by default
///   Logging__Sinks__Splunk__Enabled=true
///   Logging__Sinks__Splunk__HecUrl=https://splunk-hec.va.gov:8088
///   Logging__Sinks__Splunk__Token=(hec token)            # a secret: inject it, never commit it
/// </summary>
public sealed class LoggingSinkOptions
{
    public const string SectionName = "Logging:Sinks";

    public ConsoleSinkOptions  Console  { get; set; } = new();
    public FileSinkOptions     File     { get; set; } = new();
    public EventLogSinkOptions EventLog { get; set; } = new();
    public SplunkSinkOptions   Splunk   { get; set; } = new();

    /// <summary>Configuration problems, in the StartupValidation list format (#173). Empty when fine.</summary>
    public IEnumerable<string> Validate(bool isDevelopment)
    {
        if (File.Enabled && string.IsNullOrWhiteSpace(File.Path))
            yield return "Logging:Sinks:File:Path is required when the file sink is enabled (e.g. D:\\logs\\vacms\\api-.json).";

        if (EventLog.Enabled && !OperatingSystem.IsWindows())
            yield return "Logging:Sinks:EventLog is enabled but this host is not Windows; the Windows Event Log sink cannot run here.";

        if (Splunk.Enabled)
        {
            if (!Uri.TryCreate(Splunk.HecUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                yield return "Logging:Sinks:Splunk:HecUrl must be an absolute http(s) URL of the HTTP Event Collector (e.g. https://splunk-hec.va.gov:8088).";
            else if (!isDevelopment && uri.Scheme != Uri.UriSchemeHttps)
                yield return "Logging:Sinks:Splunk:HecUrl must use https:// outside Development; the HEC token would otherwise travel in clear text.";

            if (string.IsNullOrWhiteSpace(Splunk.Token))
                yield return "Logging:Sinks:Splunk:Token is required when the Splunk sink is enabled.";
        }
    }
}

public sealed class ConsoleSinkOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>"Json" (compact JSON, one event per line) or "Text". Null resolves to Text in Development, Json elsewhere.</summary>
    public string? Format { get; set; }
}

public sealed class FileSinkOptions
{
    public bool Enabled { get; set; }

    /// <summary>Rolling log path; the date is inserted before the extension (api-20260917.json).</summary>
    public string? Path { get; set; }

    [Range(1, 3650)]
    public int RetainedFileCountLimit { get; set; } = 31;

    [Range(1_048_576, long.MaxValue)]
    public long FileSizeLimitBytes { get; set; } = 100 * 1024 * 1024;

    /// <summary>Set when more than one process writes the same file (two app pools on one host).</summary>
    public bool Shared { get; set; }
}

public sealed class EventLogSinkOptions
{
    public bool Enabled { get; set; }

    [Required, MinLength(1)]
    public string Source { get; set; } = "VA CMS API";

    [Required, MinLength(1)]
    public string LogName { get; set; } = "Application";

    /// <summary>Warning by default: the event log is for operators, not for request traces.</summary>
    public string MinimumLevel { get; set; } = "Warning";

    /// <summary>Creating an event source needs local administrator rights; the deployment script does that, not the app.</summary>
    public bool ManageEventSource { get; set; }
}

public sealed class SplunkSinkOptions
{
    public bool Enabled { get; set; }

    /// <summary>Scheme, host and port of the HTTP Event Collector; the path defaults to services/collector/event.</summary>
    public string? HecUrl { get; set; }

    public string? Token { get; set; }

    public string? Index { get; set; }

    public string Source { get; set; } = "va-cms-api";

    public string SourceType { get; set; } = "_json";

    public string MinimumLevel { get; set; } = "Information";

    [Range(1, 300)]
    public int BatchIntervalSeconds { get; set; } = 5;

    [Range(1, 10_000)]
    public int BatchSizeLimit { get; set; } = 100;

    /// <summary>Events buffered while the collector is unreachable; older events are dropped beyond this.</summary>
    [Range(100, 1_000_000)]
    public int QueueLimit { get; set; } = 10_000;
}
