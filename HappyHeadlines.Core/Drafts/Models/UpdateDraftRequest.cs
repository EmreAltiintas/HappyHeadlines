using System.ComponentModel.DataAnnotations;
using HappyHeadlines.Db;

namespace HappyHeadlines.Core.Drafts.Models;

/// <summary>Input model for <c>PUT /api/drafts/{id}</c>. Full replace, same shape as create.</summary>
public class UpdateDraftRequest
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

    public Continent? Continent { get; set; }
}
