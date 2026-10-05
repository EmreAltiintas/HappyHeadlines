namespace HappyHeadlines.Db.Entities;

/// <summary>
/// Database entity for an unpublished article draft. Lives in DraftService's own database
/// (<see cref="DraftDbContext"/>) - never shared with articles, comments or profanity.
/// </summary>
public class Draft
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    /// <summary>Target continent, if already decided. Optional - a draft may not be assigned yet.</summary>
    public Continent? Continent { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
