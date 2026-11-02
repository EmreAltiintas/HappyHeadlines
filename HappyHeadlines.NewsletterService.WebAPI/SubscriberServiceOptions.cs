namespace HappyHeadlines.NewsletterService.WebAPI;

/// <summary>
/// Config for the outbound call to SubscriberService, bound from the <c>SubscriberService</c>
/// section. Same shape as CommentService's ProfanityServiceOptions.
/// </summary>
public sealed class SubscriberServiceOptions
{
    public const string SectionName = "SubscriberService";

    /// <summary>Base address of SubscriberService (Docker network name in Compose).</summary>
    public string BaseUrl { get; set; } = "http://localhost:5221";

    /// <summary>Per-attempt timeout.</summary>
    public double TimeoutSeconds { get; set; } = 2;

    /// <summary>Retries after the first attempt (transient errors and 503).</summary>
    public int RetryCount { get; set; } = 2;

    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();

    public sealed class CircuitBreakerOptions
    {
        public double FailureRatio { get; set; } = 0.5;
        public int MinimumThroughput { get; set; } = 4;
        public double SamplingDurationSeconds { get; set; } = 10;
        public double BreakDurationSeconds { get; set; } = 15;
    }
}
