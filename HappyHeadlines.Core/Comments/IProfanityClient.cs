using HappyHeadlines.Core.Profanity.Models;

namespace HappyHeadlines.Core.Comments;

/// <summary>
/// The CommentService's view of ProfanityService: one direct REST call. The circuit breaker
/// and timeout are attached to this client's <see cref="System.Net.Http.HttpClient"/> in
/// HappyHeadlines.CommentService.WebAPI's Program.cs.
/// </summary>
public interface IProfanityClient
{
    /// <summary>
    /// Calls <c>POST /api/profanity/check</c> on ProfanityService. Throws (broken circuit,
    /// timeout, transport error) when the service is unavailable – callers treat that as a
    /// signal to degrade gracefully.
    /// </summary>
    Task<ProfanityCheckResponse> CheckAsync(string text, CancellationToken ct = default);
}
