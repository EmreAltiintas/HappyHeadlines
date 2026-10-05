namespace HappyHeadlines.Db.Entities;

/// <summary>
/// Database entity for a reader comment on an article. Lives in the CommentService's own
/// database (<see cref="CommentDbContext"/>) – never shared with articles or profanity.
/// </summary>
public class Comment
{
    public int Id { get; set; }

    /// <summary>Id of the article this comment belongs to (owned by ArticleService – no FK across services).</summary>
    public int ArticleId { get; set; }

    public string Author { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset PostedDate { get; set; }

    public ModerationStatus ModerationStatus { get; set; }
}
