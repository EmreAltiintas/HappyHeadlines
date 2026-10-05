using System.Net.Http.Json;
using HappyHeadlines.Core.Profanity.Models;

namespace HappyHeadlines.Core.Comments;

/// <summary>
/// Typed <see cref="HttpClient"/> wrapper for the single REST call CommentService makes to
/// ProfanityService. Deliberately thin: no retry, no fallback here – the resilience pipeline
/// (timeout + circuit breaker) is layered onto the injected <see cref="HttpClient"/> by DI,
/// and the fail-fast handling lives in <see cref="CommentService"/>.
/// </summary>
public sealed class ProfanityClient : IProfanityClient
{
    private readonly HttpClient _http;

    public ProfanityClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<ProfanityCheckResponse> CheckAsync(string text, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(
            "/api/profanity/check",
            new ProfanityCheckRequest { Text = text },
            ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ProfanityCheckResponse>(ct);
        return result ?? new ProfanityCheckResponse();
    }
}
