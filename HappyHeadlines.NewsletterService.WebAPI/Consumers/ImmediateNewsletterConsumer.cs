using HappyHeadlines.Contracts;
using MassTransit;

namespace HappyHeadlines.NewsletterService.WebAPI.Consumers;

/// <summary>
/// NewsletterService's own independent subscriber to the same <see cref="ArticlePublished"/>
/// event ArticleService consumes - MassTransit's ConfigureEndpoints (Program.cs) gives this
/// service its own queue bound to it, so both consumers receive every publish (fan-out).
/// Named distinctly from ArticleService's consumer: MassTransit's default endpoint name
/// formatter derives the queue name from the consumer class name alone (not its namespace), so
/// two differently-named services with an identically-named consumer class collide onto one
/// shared queue instead of getting one each - which silently turns the fan-out into competing
/// consumers. SubscriberService doesn't exist yet, so the actual send is stubbed to a log line;
/// the point this week is that this consume span still lands in the same trace as the publish
/// that triggered it.
/// </summary>
public class ImmediateNewsletterConsumer : IConsumer<ArticlePublished>
{
    private readonly ILogger<ImmediateNewsletterConsumer> _logger;

    public ImmediateNewsletterConsumer(ILogger<ImmediateNewsletterConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<ArticlePublished> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Would send immediate newsletter for article {happyheadlines.article_id} ({happyheadlines.title}) to subscribers in {happyheadlines.continent}",
            message.ArticleId, message.Title, message.Continent);

        return Task.CompletedTask;
    }
}
