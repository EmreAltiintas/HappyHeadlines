using HappyHeadlines.Core.Comments;
using HappyHeadlines.Core.Comments.Models;
using Microsoft.AspNetCore.Mvc;

namespace HappyHeadlines.CommentService.WebAPI.Controllers;

/// <summary>
/// Thin controller: validates arguments, then hands off to <see cref="ICommentService"/>.
/// The profanity check (over HTTP, circuit-breaker-wrapped) and persistence live in Core.
/// </summary>
[ApiController]
[Route("api/articles/{articleId:int}/comments")]
[Produces("application/json")]
public class CommentsController : ControllerBase
{
    private readonly ICommentService _comments;

    public CommentsController(ICommentService comments)
    {
        _comments = comments;
    }

    /// <summary>All comments for an article, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CommentResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CommentResponse>>> List(int articleId, CancellationToken ct)
    {
        return Ok(await _comments.ListForArticleAsync(articleId, ct));
    }

    /// <summary>
    /// Post a comment. The text is checked by ProfanityService first:
    /// clean → <c>201</c>; profanity → <c>422</c>; ProfanityService unavailable → <c>202</c>
    /// (fallback: stored as Pending, re-checked later; not visible until approved).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CommentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CommentResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CommentResponse>> Create(
        int articleId,
        [FromBody] CreateCommentRequest request,
        CancellationToken ct)
    {
        var result = await _comments.CreateAsync(articleId, request, ct);

        switch (result.Outcome)
        {
            case CommentModerationOutcome.Accepted:
                return CreatedAtAction(nameof(List), new { articleId }, result.Comment);

            case CommentModerationOutcome.RejectedForProfanity:
                var rejected = new ProblemDetails
                {
                    Title = "Comment rejected",
                    Detail = $"The comment contains profanity: {string.Join(", ", result.MatchedWords)}.",
                    Status = StatusCodes.Status422UnprocessableEntity
                };
                rejected.Extensions["matchedWords"] = result.MatchedWords;
                return UnprocessableEntity(rejected);

            case CommentModerationOutcome.AcceptedPendingModeration:
            default:
                // Fallback: ProfanityService is down, the comment is saved as Pending and not yet visible.
                return Accepted(result.Comment);
        }
    }
}
