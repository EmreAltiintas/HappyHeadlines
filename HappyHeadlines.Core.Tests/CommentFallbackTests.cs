using HappyHeadlines.Core.Caching;
using HappyHeadlines.Core.Comments;
using HappyHeadlines.Core.Comments.Models;
using HappyHeadlines.Core.Profanity.Models;
using HappyHeadlines.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HappyHeadlines.Core.Tests;

/// <summary>Circuit-breaker fallback: comments are stored as Pending while ProfanityService is down.</summary>
public class CommentFallbackTests
{
    private sealed class SwitchableProfanityClient : IProfanityClient
    {
        public bool Down { get; set; } = true;
        public bool Profane { get; set; }

        public Task<ProfanityCheckResponse> CheckAsync(string text, CancellationToken ct = default)
        {
            if (Down)
                throw new HttpRequestException("ProfanityService is down");
            return Task.FromResult(new ProfanityCheckResponse { ContainsProfanity = Profane });
        }
    }

    private static (CommentService Service, CommentDbContext Db, CommentCache Cache, SwitchableProfanityClient Client) Create()
    {
        var options = new DbContextOptionsBuilder<CommentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CommentDbContext(options);
        var cache = new CommentCache(NullLogger<CommentCache>.Instance);
        var client = new SwitchableProfanityClient();
        var service = new CommentService(db, client, cache, NullLogger<CommentService>.Instance);
        return (service, db, cache, client);
    }

    [Fact]
    public async Task When_profanityservice_is_down_the_comment_is_stored_as_pending_and_not_shown()
    {
        var (service, db, _, _) = Create();

        var result = await service.CreateAsync(1, new CreateCommentRequest { Author = "a", Text = "hello" });

        Assert.Equal(CommentModerationOutcome.AcceptedPendingModeration, result.Outcome);
        Assert.Equal(ModerationStatus.Pending, (await db.Comments.SingleAsync()).ModerationStatus);
        Assert.Empty(await service.ListForArticleAsync(1));
    }

    [Fact]
    public async Task Re_moderation_does_nothing_while_profanityservice_is_still_down()
    {
        var (service, db, _, _) = Create();
        await service.CreateAsync(1, new CreateCommentRequest { Author = "a", Text = "hello" });

        var decided = await service.ReModeratePendingAsync(50);

        Assert.Equal(0, decided);
        Assert.Equal(ModerationStatus.Pending, (await db.Comments.SingleAsync()).ModerationStatus);
    }

    [Fact]
    public async Task Re_moderation_approves_clean_comments_and_makes_them_visible()
    {
        var (service, _, cache, client) = Create();
        await service.CreateAsync(1, new CreateCommentRequest { Author = "a", Text = "hello" });
        Assert.Empty(await service.ListForArticleAsync(1));          // caches the empty list
        client.Down = false;

        var decided = await service.ReModeratePendingAsync(50);

        Assert.Equal(1, decided);
        Assert.DoesNotContain(1, cache.CachedArticleIds);            // invalidated
        Assert.Single(await service.ListForArticleAsync(1));
    }

    [Fact]
    public async Task Re_moderation_rejects_profane_comments()
    {
        var (service, db, _, client) = Create();
        await service.CreateAsync(1, new CreateCommentRequest { Author = "a", Text = "damn" });
        client.Down = false;
        client.Profane = true;

        await service.ReModeratePendingAsync(50);

        Assert.Equal(ModerationStatus.Rejected, (await db.Comments.SingleAsync()).ModerationStatus);
        Assert.Empty(await service.ListForArticleAsync(1));
    }
}
