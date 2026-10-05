using HappyHeadlines.Core.Comments.Models;
using Microsoft.Extensions.Logging;

namespace HappyHeadlines.Core.Caching;

/// <summary>
/// In-process <see cref="ICommentCache"/>: an <see cref="LruCache{TKey,TValue}"/> keyed by article id
/// whose value is that article's complete comment list, so the limit "30 articles" is enforced per
/// article, not per comment or per byte. In-process is enough because CommentService runs as a single
/// instance; with more instances this would move to a shared store.
/// </summary>
public sealed class CommentCache : ICommentCache
{
    public const string CacheName = "CommentCache";
    public const int DefaultMaxArticles = 30;

    private readonly LruCache<int, IReadOnlyList<CommentResponse>> _lru;
    private readonly ILogger<CommentCache> _logger;

    public CommentCache(ILogger<CommentCache> logger, int maxArticles = DefaultMaxArticles)
    {
        _logger = logger;
        Metrics = new CacheMetrics(CacheName);
        _lru = new LruCache<int, IReadOnlyList<CommentResponse>>(maxArticles, evictedArticleId =>
        {
            Metrics.RecordEviction();
            _logger.LogInformation(
                "CommentCache full ({Capacity} articles) - evicted least recently used article {ArticleId}.",
                maxArticles, evictedArticleId);
        });
    }

    public CacheMetrics Metrics { get; }

    public int Capacity => _lru.Capacity;

    public IReadOnlyList<int> CachedArticleIds => _lru.Keys;

    public IReadOnlyList<CommentResponse>? TryGet(int articleId)
    {
        if (_lru.TryGet(articleId, out var comments))
        {
            Metrics.RecordHit();
            return comments;
        }

        _logger.LogInformation("CommentCache MISS for article {ArticleId} - reading CommentDatabase.", articleId);
        Metrics.RecordMiss();
        return null;
    }

    public void Set(int articleId, IReadOnlyList<CommentResponse> comments) => _lru.Set(articleId, comments);

    public void Invalidate(int articleId) => _lru.Remove(articleId);

    public void OnCommentCreated(CommentResponse comment)
    {
        // New list instance (copy-on-write) so readers holding the old list never see it change.
        var updated = _lru.TryUpdate(comment.ArticleId, existing => new[] { comment }.Concat(existing).ToList());
        _logger.LogDebug(
            "New comment {CommentId} for article {ArticleId}: cache {Action}.",
            comment.Id, comment.ArticleId, updated ? "updated in place" : "not cached (nothing to update)");
    }
}
