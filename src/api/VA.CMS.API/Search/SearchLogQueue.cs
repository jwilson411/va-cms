using System.Diagnostics.Metrics;
using System.Threading.Channels;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Search;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Search;

/// <summary>A search-analytics row waiting to be written (#167).</summary>
public abstract record SearchLogItem;

/// <summary>usp_Search_LogQuery — one row per GET /api/v1/search.</summary>
public sealed record SearchQueryLogItem(string Query, int ResultCount, long? UserId) : SearchLogItem;

/// <summary>usp_Search_LogClick — one row per POST /api/v1/search/click.</summary>
public sealed record SearchClickLogItem(string Query, string ClickedSlug, int ResultRank, long? UserId) : SearchLogItem;

/// <summary>
/// Bounded in-memory queue between the anonymous search endpoints and the database
/// (#167). The controllers used to fire-and-forget the write on their request-scoped
/// <c>CmsDatabase</c>, which the framework disposes when the response completes — the
/// insert raced disposal and rows were silently lost. Now the controller enqueues and
/// returns; <see cref="SearchLogWriter"/> drains the queue in its own DI scope.
///
/// The channel drops the oldest item when full (capacity: search.logQueueCapacity,
/// read once when the queue is first used) so a flood of searches can never exhaust
/// memory; every drop is counted on the <c>vacms.search.log.dropped</c> meter and the
/// first plus every thousandth is logged.
///
/// Query text is redacted on the way in (#175, <see cref="ISearchQueryRedactor"/>): the
/// raw string never sits in the buffer, so neither the writer, a heap dump nor the
/// database sees an SSN a visitor typed into the search box.
/// </summary>
public interface ISearchLogQueue
{
    /// <summary>Queues an item. Never blocks; returns false only when the queue is closed (shutdown).</summary>
    bool TryEnqueue(SearchLogItem item);

    /// <summary>Items enqueued but not yet written. Tests poll this to know a row has landed.</summary>
    int Pending { get; }

    /// <summary>Items discarded because the queue was full, since start-up.</summary>
    long Dropped { get; }
}

public sealed class SearchLogQueue : ISearchLogQueue
{
    public const string MeterName    = "VA.CMS.Search";
    public const int    DefaultCapacity = 10_000;

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> DroppedCounter = Meter.CreateCounter<long>("vacms.search.log.dropped",
        description: "Search analytics rows discarded because the in-memory queue was full.");

    private readonly ISiteSettingsService     _settings;
    private readonly ISearchQueryRedactor     _redactor;
    private readonly ILogger<SearchLogQueue>  _logger;
    private readonly object                   _gate = new();
    private Channel<SearchLogItem>?           _channel;
    private int                               _pending;
    private long                              _dropped;

    public SearchLogQueue(ISiteSettingsService settings, ISearchQueryRedactor redactor, ILogger<SearchLogQueue> logger)
    {
        _settings = settings;
        _redactor = redactor;
        _logger   = logger;
    }

    public int  Pending => Volatile.Read(ref _pending);
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>The reader side for <see cref="SearchLogWriter"/>.</summary>
    public ChannelReader<SearchLogItem> Reader => Channel.Reader;

    private Channel<SearchLogItem> Channel
    {
        get
        {
            if (_channel is { } existing) return existing;
            lock (_gate)
            {
                if (_channel is not null) return _channel;
                var capacity = (int)Math.Clamp(_settings.GetLong(SiteSettingKeys.SearchLogQueueCapacity), 100, 1_000_000);
                _channel = System.Threading.Channels.Channel.CreateBounded<SearchLogItem>(
                    new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
                    itemDropped: _ => OnDropped());
                return _channel;
            }
        }
    }

    public bool TryEnqueue(SearchLogItem item)
    {
        item = Redact(item);
        Interlocked.Increment(ref _pending);
        if (Channel.Writer.TryWrite(item)) return true;
        Interlocked.Decrement(ref _pending);
        return false;
    }

    /// <summary>Applies search.analytics.redactionPatterns to the query text of an item (#175).</summary>
    private SearchLogItem Redact(SearchLogItem item) => item switch
    {
        SearchQueryLogItem q => q with { Query = _redactor.Redact(q.Query) },
        SearchClickLogItem c => c with { Query = _redactor.Redact(c.Query) },
        _ => item,
    };

    /// <summary>Called by the writer once an item has been handled (written or failed).</summary>
    internal void Completed() => Interlocked.Decrement(ref _pending);

    internal void Close() => Channel.Writer.TryComplete();

    private void OnDropped()
    {
        Interlocked.Decrement(ref _pending);
        var n = Interlocked.Increment(ref _dropped);
        DroppedCounter.Add(1);
        if (n == 1 || n % 1000 == 0)
            _logger.LogWarning("Search analytics queue full; {Dropped} rows dropped since start-up (search.logQueueCapacity).", n);
    }
}

/// <summary>
/// Drains <see cref="SearchLogQueue"/> to the stored procedures, one DI scope per item so
/// the repositories get a live <c>CmsDatabase</c>. A failed write is logged and skipped —
/// analytics must never take the search endpoint down. On shutdown it stops accepting new
/// items and gives the remaining ones a few seconds to land.
/// </summary>
public sealed class SearchLogWriter : BackgroundService
{
    public static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(5);

    private readonly SearchLogQueue          _queue;
    private readonly IServiceScopeFactory    _scopes;
    private readonly ILogger<SearchLogWriter> _logger;

    public SearchLogWriter(SearchLogQueue queue, IServiceScopeFactory scopes, ILogger<SearchLogWriter> logger)
    {
        _queue  = queue;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Runs until the channel completes (Close() on shutdown), not until the token fires,
        // so items already queued get their grace period.
        using var grace = new CancellationTokenSource();
        using var _ = stoppingToken.Register(() =>
        {
            _queue.Close();
            grace.CancelAfter(ShutdownGrace);
        });

        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(grace.Token))
            {
                try
                {
                    await WriteAsync(item, grace.Token);
                }
                catch (OperationCanceledException) when (grace.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Search analytics row could not be written ({ItemType}).", item.GetType().Name);
                }
                finally
                {
                    _queue.Completed();
                }
            }
        }
        catch (OperationCanceledException) when (grace.IsCancellationRequested)
        {
            _logger.LogWarning("Search analytics writer stopped with {Pending} rows unwritten.", _queue.Pending);
        }
    }

    private async Task WriteAsync(SearchLogItem item, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        switch (item)
        {
            case SearchQueryLogItem q:
                await scope.ServiceProvider.GetRequiredService<ISearchRepository>().LogQueryAsync(q.Query, q.ResultCount, q.UserId);
                break;
            case SearchClickLogItem c:
                await scope.ServiceProvider.GetRequiredService<ISearchAnalyticsRepository>().LogClickAsync(c.Query, c.ClickedSlug, c.ResultRank, c.UserId);
                break;
        }
    }
}

public static class SearchLogQueueExtensions
{
    public static IServiceCollection AddSearchLogQueue(this IServiceCollection services)
    {
        services.AddSingleton<ISearchQueryRedactor, SearchQueryRedactor>();
        services.AddSingleton<SearchLogQueue>();
        services.AddSingleton<ISearchLogQueue>(sp => sp.GetRequiredService<SearchLogQueue>());
        services.AddHostedService<SearchLogWriter>();
        return services;
    }
}
