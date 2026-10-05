namespace HappyHeadlines.Core.Publishing.Models;

/// <summary>
/// Acknowledges that the article was published to ArticleQueue - not that it has been persisted
/// yet (that happens asynchronously once ArticleService's consumer processes the event).
/// </summary>
public record PublishArticleResponse(Guid ArticleId);
