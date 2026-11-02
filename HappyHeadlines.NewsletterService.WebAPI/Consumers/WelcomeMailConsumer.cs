using HappyHeadlines.Contracts;
using MassTransit;

namespace HappyHeadlines.NewsletterService.WebAPI.Consumers;

/// <summary>
/// NewsletterService consuming SubscriberQueue: sends a welcome mail to each new subscriber
/// (diagram relation "Send a welcome mail to new subscribers"). Like the immediate newsletter,
/// the actual mail send is stubbed to a log line. Named distinctly from every other consumer
/// (MassTransit derives the queue name from the class name - see ImmediateNewsletterConsumer).
/// The message waits durably in SubscriberQueue while this service is down.
/// </summary>
public class WelcomeMailConsumer : IConsumer<SubscriberSubscribed>
{
    private readonly ILogger<WelcomeMailConsumer> _logger;

    public WelcomeMailConsumer(ILogger<WelcomeMailConsumer> logger) => _logger = logger;

    public Task Consume(ConsumeContext<SubscriberSubscribed> context)
    {
        _logger.LogInformation(
            "Would send welcome mail to new subscriber {happyheadlines.subscriber_id}",
            context.Message.SubscriberId);

        return Task.CompletedTask;
    }
}
