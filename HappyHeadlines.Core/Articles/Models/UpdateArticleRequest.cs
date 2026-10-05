using System.ComponentModel.DataAnnotations;
using HappyHeadlines.Db;

namespace HappyHeadlines.Core.Articles.Models;

/// <summary>Input model for updating an article.</summary>
public class UpdateArticleRequest
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

    /// <summary>
    /// Must match the continent the article is routed to. Cross-shard moves are out of
    /// scope, so a mismatch is rejected by the controller before Core is called.
    /// </summary>
    [Required]
    [EnumDataType(typeof(Continent))]
    public Continent Continent { get; set; }
}
