using HappyHeadlines.Core.Publishing.Models;

namespace HappyHeadlines.Core.Publishing;

public interface IArticlePublisher
{
    Task<PublishArticleResponse> PublishAsync(PublishArticleRequest request, CancellationToken ct = default);
}
