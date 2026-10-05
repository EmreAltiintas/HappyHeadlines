namespace HappyHeadlines.Db.Entities;

/// <summary>
/// A single banned word backing the profanity filter. Lives in the ProfanityService's own
/// database (<see cref="ProfanityDbContext"/>).
/// </summary>
public class ProfanityWord
{
    public int Id { get; set; }

    /// <summary>The banned word, stored lower-case. Matched case-insensitively as a substring.</summary>
    public string Word { get; set; } = string.Empty;
}
