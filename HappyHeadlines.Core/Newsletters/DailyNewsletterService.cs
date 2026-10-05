using HappyHeadlines.Core.Newsletters.Models;
using HappyHeadlines.Db;
using Microsoft.Extensions.Logging;

namespace HappyHeadlines.Core.Newsletters;

/// <summary>
/// Daily-newsletter trigger: fetches every article ArticleService has for a continent over plain
/// HTTP (through the Gateway) and stubs compiling/sending the digest.
/// </summary>
public class DailyNewsletterService : IDailyNewsletterService
{
    private readonly IArticleClient _articles;
    private readonly ILogger<DailyNewsletterService> _logger;

    public DailyNewsletterService(IArticleClient articles, ILogger<DailyNewsletterService> logger)
    {
        _articles = articles;
        _logger = logger;
    }

    public async Task<DailyNewsletterResult> SendAsync(Continent continent, CancellationToken ct = default)
    {
        var articles = await _articles.ListAsync(continent, ct);

        _logger.LogInformation(
            "Would compile daily newsletter for {happyheadlines.continent} from {happyheadlines.article_count} articles",
            continent, articles.Count);

        return new DailyNewsletterResult(continent, articles.Count);
    }
}
