using HappyHeadlines.Core.Articles.Models;
using HappyHeadlines.Db;
using HappyHeadlines.Db.Sharding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HappyHeadlines.Core.Caching;

/// <summary>
/// The offline fill process: reads the last <see cref="ArticleCacheOptions.RecentDays"/> days of
/// articles from the GLOBAL ArticleDatabase and writes them to the <see cref="IArticleCache"/>.
/// The ArticleCacheWorker calls this on a timer; keeping the logic here (not in the worker) makes it
/// unit-testable. Entries get a TTL of 3 refresh intervals so deleted articles disappear if the
/// worker keeps running, and nothing stale lives forever if it stops.
/// </summary>
public class ArticleCacheRefresher
{
    private readonly IArticleDbContextFactory _contextFactory;
    private readonly IArticleCache _cache;
    private readonly ArticleCacheOptions _options;
    private readonly ILogger<ArticleCacheRefresher> _logger;

    public ArticleCacheRefresher(
        IArticleDbContextFactory contextFactory,
        IArticleCache cache,
        IOptions<ArticleCacheOptions> options,
        ILogger<ArticleCacheRefresher> logger)
    {
        _contextFactory = contextFactory;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Returns the number of articles cached.</summary>
    public async Task<int> RefreshAsync(CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-_options.RecentDays);

        await using var db = _contextFactory.Create(Continent.Global);
        var articles = (await db.Articles
                .AsNoTracking()
                .Where(a => a.PublishedDate >= since)
                .OrderByDescending(a => a.PublishedDate)
                .ToListAsync(ct))
            .Select(ArticleResponse.FromEntity)
            .ToList();

        await _cache.StoreAsync(articles, TimeSpan.FromSeconds(_options.RefreshIntervalSeconds * 3), ct);

        _logger.LogInformation(
            "ArticleCache refreshed: {Count} global articles from the last {Days} days.",
            articles.Count, _options.RecentDays);
        return articles.Count;
    }
}
