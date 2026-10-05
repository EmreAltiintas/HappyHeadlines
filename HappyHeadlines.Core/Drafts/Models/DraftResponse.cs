using HappyHeadlines.Db;
using HappyHeadlines.Db.Entities;

namespace HappyHeadlines.Core.Drafts.Models;

/// <summary>Output model for a draft. Never exposes the entity directly.</summary>
public class DraftResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public Continent? Continent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public static DraftResponse FromEntity(Draft d) => new()
    {
        Id = d.Id,
        Title = d.Title,
        Content = d.Content,
        Author = d.Author,
        Continent = d.Continent,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt
    };
}
