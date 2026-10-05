using HappyHeadlines.Core.Caching;
using HappyHeadlines.Core.Comments.Models;
using HappyHeadlines.Db;
using HappyHeadlines.Db.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace HappyHeadlines.Core.Comments;

/// <summary>
/// Comment logic. Before storing a comment it asks ProfanityService (over HTTP, through the
/// circuit-breaker-wrapped <see cref="IProfanityClient"/>) whether the text is clean.
///
/// Graceful degradation (swimlane fault isolation): if ProfanityService is slow, unreachable
/// or the circuit breaker is open, the client throws fast and the fallback takes over: the comment
/// is stored as <see cref="ModerationStatus.Pending"/> (never shown to readers) and returned as
/// <see cref="CommentModerationOutcome.AcceptedPendingModeration"/> (controller: <c>202</c>).
/// <see cref="ReModeratePendingAsync"/> decides pending comments once ProfanityService is back.
/// </summary>
public class CommentService : ICommentService
{
    private readonly CommentDbContext _db;
    private readonly IProfanityClient _profanityClient;
    private readonly ICommentCache _cache;
    private readonly ILogger<CommentService> _logger;

    public CommentService(CommentDbContext db, IProfanityClient profanityClient, ICommentCache cache, ILogger<CommentService> logger)
    {
        _db = db;
        _profanityClient = profanityClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CommentResponse>> ListForArticleAsync(int articleId, CancellationToken ct = default)
    {
        // Cache-miss approach: look in the CommentCache first; only on a miss read the database
        // and fill the cache with ALL of the article's comments (LRU-evicting another article if full).
        var cached = _cache.TryGet(articleId);
        if (cached is not null)
            return cached;

        var comments = await _db.Comments
            .AsNoTracking()
            .Where(c => c.ArticleId == articleId && c.ModerationStatus == ModerationStatus.Approved)
            .OrderByDescending(c => c.PostedDate)
            .ToListAsync(ct);

        var result = comments.Select(CommentResponse.FromEntity).ToList();
        _cache.Set(articleId, result);
        return result;
    }

    public async Task<CreateCommentResult> CreateAsync(int articleId, CreateCommentRequest request, CancellationToken ct = default)
    {
        ProfanityCheck check;
        try
        {
            var response = await _profanityClient.CheckAsync(request.Text, ct);
            check = new ProfanityCheck(response.ContainsProfanity, response.MatchedWords);
        }
        catch (Exception ex) when (ex is BrokenCircuitException or TimeoutRejectedException or HttpRequestException)
        {
            // Circuit open / timeout / transport failure – the ProfanityService swimlane is
            // degraded. The call already failed fast; the fallback takes over by storing the comment
            // as Pending (invisible to readers) so it can be re-checked once ProfanityService is back.
            _logger.LogWarning(ex,
                "ProfanityService unavailable ({ExceptionType}) – storing comment for article {ArticleId} as Pending.",
                ex.GetType().Name, articleId);

            var pending = new Comment
            {
                ArticleId = articleId,
                Author = request.Author.Trim(),
                Text = request.Text,
                PostedDate = DateTimeOffset.UtcNow,
                ModerationStatus = ModerationStatus.Pending
            };
            _db.Comments.Add(pending);
            await _db.SaveChangesAsync(ct);

            // Not added to the CommentCache: pending comments are not publicly visible.
            return CreateCommentResult.AcceptedPending(CommentResponse.FromEntity(pending));
        }

        if (check.ContainsProfanity)
        {
            _logger.LogInformation(
                "Comment for article {ArticleId} rejected: profanity {Words}.",
                articleId, string.Join(", ", check.MatchedWords));
            return CreateCommentResult.RejectedForProfanity(check.MatchedWords);
        }

        var comment = new Comment
        {
            ArticleId = articleId,
            Author = request.Author.Trim(),
            Text = request.Text,
            PostedDate = DateTimeOffset.UtcNow,
            ModerationStatus = ModerationStatus.Approved
        };

        _db.Comments.Add(comment);
        await _db.SaveChangesAsync(ct);

        // Keep the CommentCache consistent: only after the DB commit succeeded.
        var created = CommentResponse.FromEntity(comment);
        _cache.OnCommentCreated(created);

        return CreateCommentResult.Accepted(created);
    }

    public async Task<int> ReModeratePendingAsync(int batchSize, CancellationToken ct = default)
    {
        var pending = await _db.Comments
            .Where(c => c.ModerationStatus == ModerationStatus.Pending)
            .OrderBy(c => c.PostedDate)
            .Take(batchSize)
            .ToListAsync(ct);

        var decided = 0;
        foreach (var comment in pending)
        {
            try
            {
                var response = await _profanityClient.CheckAsync(comment.Text, ct);
                comment.ModerationStatus = response.ContainsProfanity ? ModerationStatus.Rejected : ModerationStatus.Approved;
            }
            catch (Exception ex) when (ex is BrokenCircuitException or TimeoutRejectedException or HttpRequestException)
            {
                // Still down: keep the rest pending and try again on the next tick.
                _logger.LogInformation("Re-moderation paused: ProfanityService still unavailable ({ExceptionType}).", ex.GetType().Name);
                break;
            }

            decided++;
            _logger.LogInformation(
                "Pending comment {CommentId} on article {ArticleId} re-moderated: {Status}.",
                comment.Id, comment.ArticleId, comment.ModerationStatus);
        }

        if (decided > 0)
        {
            await _db.SaveChangesAsync(ct);

            // Approved comments become visible: drop the article from the cache so the next read
            // reloads it from the DB in the right order.
            foreach (var articleId in pending.Where(c => c.ModerationStatus == ModerationStatus.Approved).Select(c => c.ArticleId).Distinct())
                _cache.Invalidate(articleId);
        }

        return decided;
    }

    private readonly record struct ProfanityCheck(bool ContainsProfanity, IReadOnlyList<string> MatchedWords);
}
