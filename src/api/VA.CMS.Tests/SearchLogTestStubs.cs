using VA.CMS.API.Search;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.Tests;

/// <summary>
/// Test doubles for the #167 search-analytics queue. Controller unit tests that used to
/// observe the fire-and-forget write against a real database now get an inline queue
/// that writes synchronously through the given repositories, so the row is there when
/// the action returns; host tests use the real SearchLogQueue + SearchLogWriter.
/// </summary>
internal sealed class InlineSearchLogQueue(ISearchRepository? search = null, ISearchAnalyticsRepository? analytics = null) : ISearchLogQueue
{
    public List<SearchLogItem> Items { get; } = [];

    public int  Pending => 0;
    public long Dropped => 0;

    public bool TryEnqueue(SearchLogItem item)
    {
        Items.Add(item);
        switch (item)
        {
            case SearchQueryLogItem q when search is not null:
                search.LogQueryAsync(q.Query, q.ResultCount, q.UserId).GetAwaiter().GetResult();
                break;
            case SearchClickLogItem c when analytics is not null:
                analytics.LogClickAsync(c.Query, c.ClickedSlug, c.ResultRank, c.UserId).GetAwaiter().GetResult();
                break;
        }
        return true;
    }
}

/// <summary>Content repository stub for which every slug is a published entry (click-tracking slug check, #167).</summary>
internal sealed class AnySlugPublishedStub : Issue23ContentEntryStub
{
    public override Task<PublishedContentEntry?> GetPublishedBySlugAsync(string slug, string locale = "en-US")
        => Task.FromResult<PublishedContentEntry?>(new PublishedContentEntry { Id = 1, ContentTypeId = 1, Slug = slug, Locale = locale });
}
