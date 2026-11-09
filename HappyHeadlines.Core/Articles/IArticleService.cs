using HappyHeadlines.Core.Articles.Models;
using HappyHeadlines.Db;

namespace HappyHeadlines.Core.Articles;

/// <summary>
/// All article business logic. The controller only calls in once arguments are valid;
/// shard selection, persistence and mapping all happen here.
/// </summary>
public interface IArticleService
{
    const int DefaultPageSize = 20;
    const int MaxPageSize = 100;

    /// <summary>One page of article summaries (no <c>Content</c>), newest first. <paramref name="page"/> starts at 1.</summary>
    Task<IReadOnlyList<ArticleSummaryResponse>> ListAsync(Continent continent, int page = 1, int pageSize = DefaultPageSize, CancellationToken ct = default);

    /// <summary>Returns null when no article with that id exists in the continent's shard (no fallback).</summary>
    Task<ArticleResponse?> GetAsync(Continent continent, int id, CancellationToken ct = default);

    Task<ArticleResponse> CreateAsync(CreateArticleRequest request, CancellationToken ct = default);

    /// <summary>Returns null when the article does not exist in the continent's shard.</summary>
    Task<ArticleResponse?> UpdateAsync(Continent continent, int id, UpdateArticleRequest request, CancellationToken ct = default);

    Task<bool> DeleteAsync(Continent continent, int id, CancellationToken ct = default);
}
