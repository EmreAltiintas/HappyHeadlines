namespace HappyHeadlines.Core.Caching;

/// <summary>Bound from the <c>ArticleCache</c> configuration section (ArticleService + ArticleCacheWorker).</summary>
public class ArticleCacheOptions
{
    public const string SectionName = "ArticleCache";

    /// <summary>The offline process caches articles published within this many days.</summary>
    public int RecentDays { get; set; } = 14;

    /// <summary>How often the ArticleCacheWorker refreshes the cache.</summary>
    public int RefreshIntervalSeconds { get; set; } = 30;
}
