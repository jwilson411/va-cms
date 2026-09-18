using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace VA.CMS.API.Observability;

/// <summary>
/// Builds the Serilog pipeline from configuration (#166, NFR-OPS-02).
///
///   Levels     — "Logging:LogLevel" (Default + per-namespace), the section every ASP.NET
///                operator already knows; mapped onto Serilog's minimum-level overrides.
///   Enrichers  — Application, Environment, MachineName on every event; CorrelationId and
///                UserId are pushed per request by <see cref="Middleware.CorrelationIdMiddleware"/> and
///                the request-logging enricher (LogContext), never the UPN or e-mail.
///   Sinks      — <see cref="LoggingSinkOptions"/>: Console (compact JSON outside Development),
///                rolling File, Windows Event Log, Splunk HTTP Event Collector. All on-prem.
///
/// docs/LOGGING.md is the operator-facing description of levels, fields and PII rules.
/// </summary>
public static class SerilogSetup
{
    public const string ApplicationName = "va-cms-api";

    /// <summary>Bootstrap logger for the few lines emitted before the host is built (StartupValidation, key warnings).</summary>
    public static Serilog.ILogger CreateBootstrapLogger(bool isDevelopment)
    {
        var cfg = new LoggerConfiguration().MinimumLevel.Information().Enrich.WithProperty("Application", ApplicationName);
        return (isDevelopment ? cfg.WriteTo.Console() : cfg.WriteTo.Console(new CompactJsonFormatter())).CreateBootstrapLogger();
    }

    public static void Configure(LoggerConfiguration logger, IConfiguration configuration, IHostEnvironment environment)
    {
        var isDevelopment = environment.IsDevelopment();
        var sinks = configuration.GetSection(LoggingSinkOptions.SectionName).Get<LoggingSinkOptions>() ?? new LoggingSinkOptions();

        ApplyLevels(logger, configuration.GetSection("Logging:LogLevel"));

        logger.Enrich.FromLogContext()
              .Enrich.WithProperty("Application", ApplicationName)
              .Enrich.WithProperty("Environment", environment.EnvironmentName)
              .Enrich.WithProperty("MachineName", Environment.MachineName);

        if (sinks.Console.Enabled)
        {
            var json = string.Equals(sinks.Console.Format ?? (isDevelopment ? "Text" : "Json"), "Json", StringComparison.OrdinalIgnoreCase);
            if (json)
                logger.WriteTo.Console(new CompactJsonFormatter());
            else
                logger.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
        }

        if (sinks.File.Enabled && !string.IsNullOrWhiteSpace(sinks.File.Path))
        {
            logger.WriteTo.File(
                new CompactJsonFormatter(),
                sinks.File.Path,
                rollingInterval:        RollingInterval.Day,
                rollOnFileSizeLimit:    true,
                fileSizeLimitBytes:     sinks.File.FileSizeLimitBytes,
                retainedFileCountLimit: sinks.File.RetainedFileCountLimit,
                shared:                 sinks.File.Shared);
        }

        if (sinks.EventLog.Enabled && OperatingSystem.IsWindows())
        {
            logger.WriteTo.EventLog(
                source:                   sinks.EventLog.Source,
                logName:                  sinks.EventLog.LogName,
                manageEventSource:        sinks.EventLog.ManageEventSource,
                restrictedToMinimumLevel: ParseLevel(sinks.EventLog.MinimumLevel, LogEventLevel.Warning));
        }

        if (sinks.Splunk.Enabled && !string.IsNullOrWhiteSpace(sinks.Splunk.HecUrl) && !string.IsNullOrWhiteSpace(sinks.Splunk.Token))
        {
            logger.WriteTo.EventCollector(
                splunkHost:               sinks.Splunk.HecUrl,
                eventCollectorToken:      sinks.Splunk.Token,
                index:                    sinks.Splunk.Index,
                source:                   sinks.Splunk.Source,
                sourceType:               sinks.Splunk.SourceType,
                host:                     Environment.MachineName,
                restrictedToMinimumLevel: ParseLevel(sinks.Splunk.MinimumLevel, LogEventLevel.Information),
                batchIntervalInSeconds:   sinks.Splunk.BatchIntervalSeconds,
                batchSizeLimit:           sinks.Splunk.BatchSizeLimit,
                queueLimit:               sinks.Splunk.QueueLimit);
        }
    }

    /// <summary>Maps Microsoft.Extensions.Logging level names ("Logging:LogLevel") onto Serilog's minimum level and overrides.</summary>
    public static void ApplyLevels(LoggerConfiguration logger, IConfigurationSection logLevels)
    {
        logger.MinimumLevel.Is(ParseLevel(logLevels["Default"], LogEventLevel.Information));

        foreach (var child in logLevels.GetChildren())
        {
            if (child.Key == "Default" || string.IsNullOrWhiteSpace(child.Value)) continue;
            logger.MinimumLevel.Override(child.Key, ParseLevel(child.Value, LogEventLevel.Information));
        }
    }

    /// <summary>Accepts both Microsoft ("Trace", "Critical", "None") and Serilog ("Verbose", "Fatal") spellings.</summary>
    public static LogEventLevel ParseLevel(string? name, LogEventLevel fallback) => name?.Trim().ToLowerInvariant() switch
    {
        "trace" or "verbose"  => LogEventLevel.Verbose,
        "debug"               => LogEventLevel.Debug,
        "information" or "info" => LogEventLevel.Information,
        "warning" or "warn"   => LogEventLevel.Warning,
        "error"               => LogEventLevel.Error,
        "critical" or "fatal" => LogEventLevel.Fatal,
        "none"                => LevelAlias.Off,
        _                     => fallback,
    };
}
