namespace HappyHeadlines.NewsletterService.WebAPI;

/// <summary>
/// Second MassTransit bus (multi-bus): SubscriberQueue lives on its own RabbitMQ broker, separate
/// from ArticleQueue, so each can be down without affecting the other. Marker type only.
/// </summary>
public interface ISubscriberQueueBus : MassTransit.IBus
{
}
