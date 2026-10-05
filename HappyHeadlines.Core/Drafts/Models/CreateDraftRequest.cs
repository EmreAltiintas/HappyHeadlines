using System.ComponentModel.DataAnnotations;
using HappyHeadlines.Db;

namespace HappyHeadlines.Core.Drafts.Models;

/// <summary>Input model for <c>POST /api/drafts</c>.</summary>
public class CreateDraftRequest
{
    [Required]
    [StringLength(300, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(int.MaxValue, MinimumLength = 1)]
    public string Content { get; set; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Author { get; set; } = string.Empty;

    /// <summary>Target continent, if already decided. Optional at draft stage.</summary>
    public Continent? Continent { get; set; }
}
