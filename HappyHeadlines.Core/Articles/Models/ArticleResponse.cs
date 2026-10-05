using HappyHeadlines.Db;
using HappyHeadlines.Db.Entities;

namespace HappyHeadlines.Core.Articles.Models;

/// <summary>Output model returned by every article operation. Never exposes the entity directly.</summary>
public class ArticleResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTimeOffset PublishedDate { get; set; }
    public Continent Continent { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public static ArticleResponse FromEntity(Article a) => new()
    {
        Id = a.Id,
        Title = a.Title,
        Content = a.Content,
        Author = a.Author,
        PublishedDate = a.PublishedDate,
        Continent = a.Continent,
        CreatedAtUtc = a.CreatedAtUtc,
        UpdatedAtUtc = a.UpdatedAtUtc
    };
}
