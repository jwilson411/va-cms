using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Infrastructure.Search;

/// <summary>
/// Strips identifiers out of a search query before it is stored for analytics (#175).
/// </summary>
public interface ISearchQueryRedactor
{
    /// <summary>
    /// Returns <paramref name="query"/> with every match of search.analytics.redactionPatterns
    /// replaced by <see cref="SearchQueryRedactor.Token"/>. Never throws: a pattern that does
    /// not compile is skipped, and a pattern that times out redacts the whole query.
    /// </summary>
    string Redact(string query);
}

/// <summary>
/// Regex-based redaction driven by the search.analytics.redactionPatterns site setting.
///
/// Anonymous visitors type SSNs, claim numbers, phone numbers and e-mail addresses into site
/// search. The analytics tables must never hold them, so every query passes through here on
/// its way to usp_Search_LogQuery / usp_Search_LogClick. The compiled set is rebuilt only when
/// the setting's value changes (compared as a sequence), so a request pays one list compare.
///
/// Patterns are operator-supplied, so each one runs with a match timeout; a timeout is treated
/// as "could not prove this query is clean" and the whole query becomes the token rather than
/// the raw text reaching the database.
/// </summary>
public sealed class SearchQueryRedactor : ISearchQueryRedactor
{
    public const string Token = "[redacted]";

    /// <summary>Upper bound on any single pattern's work per query; well above what the defaults need.</summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex Whitespace = new(@"\s{2,}", RegexOptions.Compiled);

    private readonly ISiteSettingsService _settings;
    private readonly ILogger<SearchQueryRedactor> _logger;
    private readonly object _gate = new();
    private State? _state;

    /// <summary>Source patterns and what they compiled to, swapped as one so a reader never mixes generations.</summary>
    private sealed record State(string[] Source, Regex[] Compiled);

    public SearchQueryRedactor(ISiteSettingsService settings, ILogger<SearchQueryRedactor>? logger = null)
    {
        _settings = settings;
        _logger   = logger ?? NullLogger<SearchQueryRedactor>.Instance;
    }

    /// <summary>The patterns currently in effect (after dropping any that failed to compile).</summary>
    public IReadOnlyList<string> ActivePatterns
    {
        get => EnsureCompiled().Select(r => r.ToString()).ToArray();
    }

    public string Redact(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return query;

        var patterns = EnsureCompiled();
        var text = query;
        foreach (var regex in patterns)
        {
            try
            {
                text = regex.Replace(text, Token);
            }
            catch (RegexMatchTimeoutException)
            {
                _logger.LogWarning("Search redaction pattern {Pattern} timed out; the query was redacted in full.", regex);
                return Token;
            }
        }

        if (!ReferenceEquals(text, query))
            text = Whitespace.Replace(text, " ").Trim();
        return text;
    }

    private Regex[] EnsureCompiled()
    {
        var wanted = _settings.GetStringList(SiteSettingKeys.SearchAnalyticsRedactionPatterns);
        if (_state is { } current && SameAs(current, wanted)) return current.Compiled;

        lock (_gate)
        {
            if (_state is { } again && SameAs(again, wanted)) return again.Compiled;

            var compiled = new List<Regex>(wanted.Count);
            foreach (var pattern in wanted)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                try
                {
                    compiled.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout));
                }
                catch (ArgumentException ex)
                {
                    _logger.LogError(ex, "Search redaction pattern {Pattern} does not compile and is ignored (search.analytics.redactionPatterns).", pattern);
                }
            }

            var next = new State(wanted.ToArray(), compiled.ToArray());
            _state = next;
            return next.Compiled;
        }
    }

    private static bool SameAs(State state, IReadOnlyList<string> wanted)
    {
        var source = state.Source;
        if (source.Length != wanted.Count) return false;
        for (var i = 0; i < source.Length; i++)
            if (!string.Equals(source[i], wanted[i], StringComparison.Ordinal)) return false;
        return true;
    }
}
