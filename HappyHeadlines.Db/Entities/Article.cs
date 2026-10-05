namespace HappyHeadlines.Db.Entities;

/// <summary>Database entity. The schema is identical in every one of the 8 shard databases.</summary>
public class Article
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public DateTimeOffset PublishedDate { get; set; }

    /// <summary>
    /// Persisted on the row so the data is self-describing, even though this value also
    /// decides which database the row lives in.
    /// </summary>
    public Continent Continent { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
