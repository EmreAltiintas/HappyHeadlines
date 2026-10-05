using HappyHeadlines.Contracts;
using HappyHeadlines.Core.Publishing.Models;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace HappyHeadlines.Core.Publishing;

/// <summary>
/// PublisherService's side of the fan-out: builds and publishes one <see cref="ArticlePublished"/>
/// event per article. Uses <see cref="IPublishEndpoint"/> (not <c>Send</c>) so MassTransit fans it
/// out to every consuming service's own queue (ArticleService, NewsletterService, ...) instead of
/// delivering it point-to-point to just one of them.
/// </summary>
public class ArticlePublisher : IArticlePublisher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<ArticlePublisher> _logger;

    public ArticlePublisher(IPublishEndpoint publishEndpoint, ILogger<ArticlePublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<PublishArticleResponse> PublishAsync(PublishArticleRequest request, CancellationToken ct = default)
    {
        var articleId = Guid.NewGuid();

        await _publishEndpoint.Publish(new ArticlePublished(
            articleId,
            request.Title.Trim(),
            request.Content,
            request.Author.Trim(),
            request.Continent,
            DateTime.UtcNow), ct);

        _logger.LogInformation(
            "Article {happyheadlines.article_id} published by {happyheadlines.author} to {happyheadlines.continent}",
            articleId, request.Author, request.Continent);

        return new PublishArticleResponse(articleId);
    }
}
