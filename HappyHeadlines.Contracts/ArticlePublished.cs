using HappyHeadlines.Db;

namespace HappyHeadlines.Contracts;

/// <summary>
/// Fan-out event published by PublisherService when an article is finalized. MassTransit's
/// Publish/Subscribe model gives every consumer (ArticleService, NewsletterService, ...) its own
/// queue bound to this message type - see IConsumer&lt;ArticlePublished&gt; in each service.
/// One definition, referenced by every producer/consumer - never redeclared per service.
/// </summary>
public record ArticlePublished(
    Guid ArticleId,
    string Title,
    string Content,
    string Author,
    Continent Continent,
    DateTime PublishedAt);
