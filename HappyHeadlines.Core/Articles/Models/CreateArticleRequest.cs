using System.ComponentModel.DataAnnotations;
using HappyHeadlines.Db;

namespace HappyHeadlines.Core.Articles.Models;

/// <summary>Input model for creating an article.</summary>
public class CreateArticleRequest
{
    [Required]
    [StringLength(300, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public string Content { get; set; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Author { get; set; } = string.Empty;

    [Required]
    public DateTimeOffset PublishedDate { get; set; }

    /// <summary>Also decides which shard database the article is written to.</summary>
    [Required]
    [EnumDataType(typeof(Continent))]
    public Continent Continent { get; set; }
}
