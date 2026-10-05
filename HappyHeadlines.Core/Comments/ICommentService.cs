using HappyHeadlines.Core.Comments.Models;

namespace HappyHeadlines.Core.Comments;

/// <summary>
/// Comment business logic. Runs inside HappyHeadlines.CommentService.WebAPI against that
/// service's own database, and calls ProfanityService (through the circuit-breaker-wrapped
/// client) before storing anything.
/// </summary>
public interface ICommentService
{
    /// <summary>Approved comments for an article, newest first (pending/rejected are never shown).</summary>
    Task<IReadOnlyList<CommentResponse>> ListForArticleAsync(int articleId, CancellationToken ct = default);

    /// <summary>
    /// Runs the profanity check, then stores the comment when it is clean. See
    /// <see cref="CreateCommentResult"/> for the possible outcomes (including storing the comment as
    /// Pending when ProfanityService is unavailable).
    /// </summary>
    Task<CreateCommentResult> CreateAsync(int articleId, CreateCommentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Re-checks comments stored as Pending (oldest first, at most <paramref name="batchSize"/>):
    /// clean ones become Approved, profane ones Rejected. Stops at the first check that fails, since
    /// ProfanityService is then still down. Returns how many comments were decided.
    /// </summary>
    Task<int> ReModeratePendingAsync(int batchSize, CancellationToken ct = default);
}
