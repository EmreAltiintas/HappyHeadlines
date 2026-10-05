using HappyHeadlines.Core.Caching;
using HappyHeadlines.Core.Comments.Models;
using HappyHeadlines.Db;
using Microsoft.Extensions.Logging.Abstractions;

namespace HappyHeadlines.Core.Tests;

public class CommentCacheTests
{
    private static CommentCache NewCache(int max = CommentCache.DefaultMaxArticles) =>
        new(NullLogger<CommentCache>.Instance, max);

    private static CommentResponse Comment(int id, int articleId) => new()
    {
        Id = id,
        ArticleId = articleId,
        Author = "a",
        Text = "t",
        PostedDate = DateTimeOffset.UtcNow,
        ModerationStatus = ModerationStatus.Approved
    };

    [Fact]
    public void Default_capacity_is_30_articles()
    {
        Assert.Equal(30, NewCache().Capacity);
    }

    [Fact]
    public void Miss_then_hit_is_counted()
    {
        var cache = NewCache();

        Assert.Null(cache.TryGet(1));                       // miss
        cache.Set(1, [Comment(1, 1)]);
        Assert.NotNull(cache.TryGet(1));                    // hit
        Assert.NotNull(cache.TryGet(1));                    // hit

        Assert.Equal(2, cache.Metrics.Hits);
        Assert.Equal(1, cache.Metrics.Misses);
    }

    [Fact]
    public void Filling_a_31st_article_evicts_the_least_recently_used_one_and_counts_it()
    {
        var cache = NewCache();
        for (var articleId = 1; articleId <= 30; articleId++)
            cache.Set(articleId, [Comment(articleId, articleId)]);

        cache.TryGet(1);                                    // protect article 1
        cache.Set(31, [Comment(31, 31)]);

        Assert.Equal(30, cache.CachedArticleIds.Count);
        Assert.DoesNotContain(2, cache.CachedArticleIds);   // 2 was the LRU
        Assert.Contains(1, cache.CachedArticleIds);
        Assert.Equal(1, cache.Metrics.Evictions);
    }

    [Fact]
    public void A_new_comment_is_added_to_an_already_cached_article()
    {
        var cache = NewCache();
        cache.Set(7, [Comment(1, 7)]);

        cache.OnCommentCreated(Comment(2, 7));

        var cached = cache.TryGet(7)!;
        Assert.Equal([2, 1], cached.Select(c => c.Id));     // newest first
    }

    [Fact]
    public void A_new_comment_for_an_uncached_article_does_not_fill_the_cache()
    {
        var cache = NewCache();

        cache.OnCommentCreated(Comment(1, 7));

        Assert.Empty(cache.CachedArticleIds);
    }
}
