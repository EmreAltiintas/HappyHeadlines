using HappyHeadlines.Db;
using HappyHeadlines.Db.Entities;

namespace HappyHeadlines.Core.Comments.Models;

/// <summary>Output model for a comment. Never exposes the entity directly.</summary>
public class CommentResponse
{
    public int Id { get; set; }
    public int ArticleId { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset PostedDate { get; set; }
    public ModerationStatus ModerationStatus { get; set; }

    public static CommentResponse FromEntity(Comment c) => new()
    {
        Id = c.Id,
        ArticleId = c.ArticleId,
        Author = c.Author,
        Text = c.Text,
        PostedDate = c.PostedDate,
        ModerationStatus = c.ModerationStatus
    };
}
