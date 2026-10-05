using System.ComponentModel.DataAnnotations;

namespace HappyHeadlines.Core.Profanity.Models;

/// <summary>Input model for adding a word to the profanity list (<c>POST /api/profanity/words</c>).</summary>
public class AddProfanityWordRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Word { get; set; } = string.Empty;
}
