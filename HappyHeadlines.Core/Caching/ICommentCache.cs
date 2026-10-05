using HappyHeadlines.Core.Comments.Models;

namespace HappyHeadlines.Core.Caching;

/// <summary>
/// Cache-aside (cache-miss) cache in front of CommentDatabase: holds ALL comments of an article,
/// for at most <see cref="Capacity"/> articles, evicting the least recently used article when full.
/// </summary>
public interface ICommentCache
{
    int Capacity { get; }

    /// <summary>Cached comments of the article (newest first), or null on a miss. Counts as a use.</summary>
    IReadOnlyList<CommentResponse>? TryGet(int articleId);

    /// <summary>Fills the cache after a miss; may evict the LRU article.</summary>
    void Set(int articleId, IReadOnlyList<CommentResponse> comments);

    /// <summary>
    /// Keeps the cache consistent after a new comment is stored: if the article is cached the
    /// comment is added to it; otherwise nothing happens (the next read fills it from the DB).
    /// </summary>
    void OnCommentCreated(CommentResponse comment);

    /// <summary>Drops an article from the cache (its next read is a miss that reloads from the DB).</summary>
    void Invalidate(int articleId);

    /// <summary>Cached article ids, most recently used first (for diagnostics / the demo).</summary>
    IReadOnlyList<int> CachedArticleIds { get; }

    CacheMetrics Metrics { get; }
}
