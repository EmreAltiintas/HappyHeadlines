using HappyHeadlines.Core.Articles.Models;

namespace HappyHeadlines.Core.Caching;

/// <summary>
/// Cache in front of the GLOBAL ArticleDatabase. Filled only by the offline ArticleCacheWorker
/// (periodic refresh of the last 14 days); ArticleService only reads it and falls back to the
/// database on a miss. It never writes on a miss.
/// </summary>
public interface IArticleCache
{
    /// <summary>The window of recent articles the cache covers (days).</summary>
    int RecentDays { get; }

    /// <summary>Recent-articles list; null on a miss (including when Redis is unreachable).</summary>
    Task<IReadOnlyList<ArticleResponse>?> GetRecentAsync(CancellationToken ct = default);

    /// <summary>One article; null on a miss (including when Redis is unreachable).</summary>
    Task<ArticleResponse?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Used by the offline refresher.</summary>
    Task StoreAsync(IReadOnlyList<ArticleResponse> recentArticles, TimeSpan timeToLive, CancellationToken ct = default);

    /// <summary>Drops one article and the list so a write in ArticleService is not served stale.</summary>
    Task InvalidateAsync(int? id, CancellationToken ct = default);
}
