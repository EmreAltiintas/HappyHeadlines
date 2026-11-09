using HappyHeadlines.Core.Articles.Models;
using HappyHeadlines.Db;

namespace HappyHeadlines.Core.Newsletters;

public interface IArticleClient
{
    Task<IReadOnlyList<ArticleSummaryResponse>> ListAsync(Continent continent, CancellationToken ct = default);
}
