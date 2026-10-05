namespace HappyHeadlines.Core.Profanity.Models;

/// <summary>Result of a profanity check. Also the wire contract the CommentService's client reads.</summary>
public class ProfanityCheckResponse
{
    public bool ContainsProfanity { get; set; }

    /// <summary>The banned words found in the text (lower-case, distinct). Empty when clean.</summary>
    public IReadOnlyList<string> MatchedWords { get; set; } = [];
}
