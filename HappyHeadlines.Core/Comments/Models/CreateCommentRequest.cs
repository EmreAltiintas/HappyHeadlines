using System.ComponentModel.DataAnnotations;

namespace HappyHeadlines.Core.Comments.Models;

/// <summary>Input model for <c>POST /api/articles/{articleId}/comments</c>. The article id comes from the route.</summary>
public class CreateCommentRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Author { get; set; } = string.Empty;

    [Required]
    [StringLength(4000, MinimumLength = 1)]
    public string Text { get; set; } = string.Empty;
}
