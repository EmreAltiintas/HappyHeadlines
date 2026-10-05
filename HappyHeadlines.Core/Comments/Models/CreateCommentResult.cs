namespace HappyHeadlines.Core.Comments.Models;

/// <summary>The three outcomes of trying to post a comment.</summary>
public enum CommentModerationOutcome
{
    /// <summary>Text was clean; the comment was stored.</summary>
    Accepted,

    /// <summary>ProfanityService found banned words; the comment was not stored.</summary>
    RejectedForProfanity,

    /// <summary>
    /// ProfanityService was slow/unreachable or the circuit breaker was open. The comment is stored
    /// as <c>Pending</c> (not publicly visible) and re-checked later by the background moderator.
    /// </summary>
    AcceptedPendingModeration
}

/// <summary>Outcome of <see cref="ICommentService.CreateAsync"/>.</summary>
public sealed record CreateCommentResult(
    CommentModerationOutcome Outcome,
    CommentResponse? Comment,
    IReadOnlyList<string> MatchedWords)
{
    public static CreateCommentResult Accepted(CommentResponse comment) =>
        new(CommentModerationOutcome.Accepted, comment, []);

    public static CreateCommentResult RejectedForProfanity(IReadOnlyList<string> matchedWords) =>
        new(CommentModerationOutcome.RejectedForProfanity, null, matchedWords);

    public static CreateCommentResult AcceptedPending(CommentResponse comment) =>
        new(CommentModerationOutcome.AcceptedPendingModeration, comment, []);
}
