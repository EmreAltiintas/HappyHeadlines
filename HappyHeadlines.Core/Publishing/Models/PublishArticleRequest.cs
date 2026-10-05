using System.ComponentModel.DataAnnotations;
using HappyHeadlines.Db;

namespace HappyHeadlines.Core.Publishing.Models;

/// <summary>Input model for finalizing and publishing an article via ArticleQueue.</summary>
public class PublishArticleRequest
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

    /// <summary>Also decides which ArticleService shard the article is eventually written to.</summary>
    [Required]
    [EnumDataType(typeof(Continent))]
    public Continent Continent { get; set; }
}
