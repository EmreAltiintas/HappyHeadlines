namespace HappyHeadlines.NewsletterService.WebAPI;

/// <summary>Config for the outbound call to ArticleService, bound from the <c>ArticleService</c> section.</summary>
public sealed class ArticleServiceOptions
{
    public const string SectionName = "ArticleService";

    /// <summary>
    /// Base address of ArticleService. Points at the Gateway, not an instance directly - the
    /// x-axis ArticleService instances are never published on their own; the Gateway is their
    /// single entrypoint.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:8088";
}
