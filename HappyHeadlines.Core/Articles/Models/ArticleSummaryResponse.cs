using HappyHeadlines.Db;

namespace HappyHeadlines.Core.Articles.Models;

/// <summary>
/// Light version of <see cref="ArticleResponse"/> used by the list endpoint: everything except
/// <c>Content</c>. The full text is only sent by <c>GET /api/articles/{id}</c>.
/// </summary>
public class ArticleSummaryResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTimeOffset PublishedDate { get; set; }
    public Continent Continent { get; set; }

    public static ArticleSummaryResponse FromResponse(ArticleResponse a) => new()
    {
        Id = a.Id,
        Title = a.Title,
        Author = a.Author,
        PublishedDate = a.PublishedDate,
        Continent = a.Continent
    };
}
