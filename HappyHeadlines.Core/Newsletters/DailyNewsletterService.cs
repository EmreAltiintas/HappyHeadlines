using HappyHeadlines.Core.Newsletters.Models;
using HappyHeadlines.Core.Subscribers.Models;
using HappyHeadlines.Db;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace HappyHeadlines.Core.Newsletters;

/// <summary>
/// Daily-newsletter trigger: fetches every article ArticleService has for a continent over plain
/// HTTP (through the Gateway), asks SubscriberService (REST) who to send it to, and stubs
/// compiling/sending the digest.
/// </summary>
public class DailyNewsletterService : IDailyNewsletterService
{
    private readonly IArticleClient _articles;
    private readonly ISubscriberClient _subscribers;
    private readonly ILogger<DailyNewsletterService> _logger;

    public DailyNewsletterService(
        IArticleClient articles,
        ISubscriberClient subscribers,
        ILogger<DailyNewsletterService> logger)
    {
        _articles = articles;
        _subscribers = subscribers;
        _logger = logger;
    }

    public async Task<DailyNewsletterResult> SendAsync(Continent continent, CancellationToken ct = default)
    {
        var articles = await _articles.ListAsync(continent, ct);
        var (subscribers, available) = await GetSubscribersAsync(ct);

        _logger.LogInformation(
            "Would compile daily newsletter for {happyheadlines.continent} from {happyheadlines.article_count} articles to {happyheadlines.subscriber_count} subscribers",
            continent, articles.Count, subscribers.Count);

        return new DailyNewsletterResult(continent, articles.Count, subscribers.Count, available);
    }

    /// <summary>
    /// Fault isolation towards SubscriberService: when it is down, timing out, circuit-open or
    /// switched off by its release toggle (503), NewsletterService degrades to "no recipients"
    /// instead of failing the whole run.
    /// </summary>
    private async Task<(IReadOnlyList<SubscriberResponse> Subscribers, bool Available)> GetSubscribersAsync(CancellationToken ct)
    {
        try
        {
            return (await _subscribers.ListAsync(ct), true);
        }
        catch (Exception ex) when (ex is HttpRequestException or BrokenCircuitException or TimeoutRejectedException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogWarning(ex,
                "SubscriberService unavailable (down, timed out, circuit open or toggled off) - continuing with 0 subscribers");
            return (Array.Empty<SubscriberResponse>(), false);
        }
    }
}
