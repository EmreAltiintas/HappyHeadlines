using HappyHeadlines.Core.Caching;
using HappyHeadlines.Core.Comments;
using HappyHeadlines.Core.Comments.Models;
using HappyHeadlines.Core.Profanity.Models;
using HappyHeadlines.Db;
using HappyHeadlines.Db.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HappyHeadlines.Core.Tests;

/// <summary>CommentService + CommentCache against an in-memory CommentDbContext.</summary>
public class CommentServiceCacheTests
{
    private sealed class CleanProfanityClient : IProfanityClient
    {
        public Task<ProfanityCheckResponse> CheckAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new ProfanityCheckResponse { ContainsProfanity = false });
    }

    private static (CommentService Service, CommentDbContext Db, CommentCache Cache) Create()
    {
        var options = new DbContextOptionsBuilder<CommentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CommentDbContext(options);
        var cache = new CommentCache(NullLogger<CommentCache>.Instance);
        var service = new CommentService(db, new CleanProfanityClient(), cache, NullLogger<CommentService>.Instance);
        return (service, db, cache);
    }

    private static Comment DbComment(int articleId, string text) => new()
    {
        ArticleId = articleId,
        Author = "a",
        Text = text,
        PostedDate = DateTimeOffset.UtcNow,
        ModerationStatus = ModerationStatus.Approved
    };

    [Fact]
    public async Task First_read_is_a_miss_that_fills_the_cache_and_the_second_read_is_a_hit()
    {
        var (service, db, cache) = Create();
        db.Comments.Add(DbComment(5, "t"));
        await db.SaveChangesAsync();

        var first = await service.ListForArticleAsync(5);
        var second = await service.ListForArticleAsync(5);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(1, cache.Metrics.Misses);
        Assert.Equal(1, cache.Metrics.Hits);
        Assert.Contains(5, cache.CachedArticleIds);
    }

    [Fact]
    public async Task A_cached_read_is_served_from_the_cache_not_the_database()
    {
        var (service, db, _) = Create();
        db.Comments.Add(DbComment(5, "t"));
        await db.SaveChangesAsync();
        await service.ListForArticleAsync(5);               // fills the cache

        // Change the DB behind the cache's back: a cache hit must not see it.
        db.Comments.Add(DbComment(5, "u"));
        await db.SaveChangesAsync();

        Assert.Single(await service.ListForArticleAsync(5));
    }

    [Fact]
    public async Task Comment_created_through_the_service_keeps_a_cached_article_consistent()
    {
        var (service, _, cache) = Create();
        await service.ListForArticleAsync(9);               // miss: caches the (empty) list for article 9

        var result = await service.CreateAsync(9, new CreateCommentRequest { Author = "emre", Text = "fresh comment" });

        Assert.Equal(CommentModerationOutcome.Accepted, result.Outcome);
        var list = await service.ListForArticleAsync(9);    // must be a hit AND contain the new comment
        Assert.Single(list);
        Assert.Equal("fresh comment", list[0].Text);
        Assert.Equal(1, cache.Metrics.Hits);
    }

    [Fact]
    public async Task Reading_31_articles_evicts_the_least_recently_used_one()
    {
        var (service, _, cache) = Create();

        for (var articleId = 1; articleId <= 31; articleId++)
            await service.ListForArticleAsync(articleId);

        Assert.Equal(30, cache.CachedArticleIds.Count);
        Assert.DoesNotContain(1, cache.CachedArticleIds);
        Assert.Equal(31, cache.CachedArticleIds[0]);        // most recently used first
        Assert.Equal(1, cache.Metrics.Evictions);

        await service.ListForArticleAsync(1);               // evicted -> miss again
        Assert.Equal(32, cache.Metrics.Misses);
    }
}
