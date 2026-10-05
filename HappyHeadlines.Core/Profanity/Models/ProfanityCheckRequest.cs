using System.ComponentModel.DataAnnotations;

namespace HappyHeadlines.Core.Profanity.Models;

/// <summary>Input model for <c>POST /api/profanity/check</c>. Also the wire contract the
/// CommentService's profanity client sends.</summary>
public class ProfanityCheckRequest
{
    [Required]
    [StringLength(4000, MinimumLength = 1)]
    public string Text { get; set; } = string.Empty;
}
