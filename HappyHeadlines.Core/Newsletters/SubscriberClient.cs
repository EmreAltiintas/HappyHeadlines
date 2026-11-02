using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HappyHeadlines.Core.Subscribers.Models;

namespace HappyHeadlines.Core.Newsletters;

/// <summary>
/// Typed <see cref="HttpClient"/> for NewsletterService -> SubscriberService. Deliberately thin:
/// the timeout / retry / circuit-breaker pipeline is layered on by DI (NewsletterService
/// Program.cs) and the fallback lives in <see cref="DailyNewsletterService"/>. A 503 from the
/// release toggle surfaces as <see cref="HttpRequestException"/> just like an outage.
/// </summary>
public sealed class SubscriberClient : ISubscriberClient
{
    // Same explicit Web + enum options every typed client here needs (see ArticleClient).
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;

    public SubscriberClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<SubscriberResponse>> ListAsync(CancellationToken ct = default)
    {
        var subscribers = await _http.GetFromJsonAsync<List<SubscriberResponse>>("/api/subscribers", JsonOptions, ct);
        return subscribers ?? new List<SubscriberResponse>();
    }
}
