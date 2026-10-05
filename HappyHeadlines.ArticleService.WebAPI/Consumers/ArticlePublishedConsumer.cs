using HappyHeadlines.Contracts;
using HappyHeadlines.Core.Articles;
using HappyHeadlines.Core.Articles.Models;
using MassTransit;

namespace HappyHeadlines.ArticleService.WebAPI.Consumers;

/// <summary>
/// The ArticleQueue side of the fan-out: PublisherService publishes one <see cref="ArticlePublished"/>
/// event, MassTransit's ConfigureEndpoints (Program.cs) gives this service its own queue bound to
/// it, and this consumer reuses the exact same <see cref="IArticleService"/> the REST endpoints use
/// to pick the right shard (from <see cref="ArticlePublished.Continent"/>) and persist the article.
/// No sharding/x-axis code is touched - this only calls into the existing Core service.
/// </summary>
public class ArticlePublishedConsumer : IConsumer<ArticlePublished>
{
    private readonly IArticleService _articles;
    private readonly ILogger<ArticlePublishedConsumer> _logger;

    public ArticlePublishedConsumer(IArticleService articles, ILogger<ArticlePublishedConsumer> logger)
    {
        _articles = articles;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ArticlePublished> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Article {happyheadlines.article_id} received from queue, persisting to shard {happyheadlines.continent}",
            message.ArticleId, message.Continent);

        await _articles.CreateAsync(new CreateArticleRequest
        {
            Title = message.Title,
            Content = message.Content,
            Author = message.Author,
            PublishedDate = new DateTimeOffset(message.PublishedAt, TimeSpan.Zero),
            Continent = message.Continent
        }, context.CancellationToken);
    }
}
