namespace HappyHeadlines.CommentService.WebAPI;

/// <summary>
/// Config for the outbound call to ProfanityService, bound from the <c>ProfanityService</c>
/// section. Defaults are tuned so the circuit trips quickly in a demo (stop profanityservice,
/// fire ~5 requests) rather than the library defaults (100 requests / 30s window).
/// </summary>
public sealed class ProfanityServiceOptions
{
    public const string SectionName = "ProfanityService";

    /// <summary>Base address of ProfanityService (Docker network name in Compose).</summary>
    public string BaseUrl { get; set; } = "http://localhost:5217";

    /// <summary>Per-attempt timeout. A profanity check is a trivial lookup; >2s means trouble.</summary>
    public double TimeoutSeconds { get; set; } = 2;

    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();

    public sealed class CircuitBreakerOptions
    {
        /// <summary>Fraction of failed calls in the sampling window that opens the circuit (0-1].</summary>
        public double FailureRatio { get; set; } = 0.5;

        /// <summary>Minimum calls in the window before the breaker can open.</summary>
        public int MinimumThroughput { get; set; } = 4;

        /// <summary>Rolling window the failure ratio is measured over.</summary>
        public double SamplingDurationSeconds { get; set; } = 10;

        /// <summary>How long the circuit stays open (fast-failing) before it tries a probe call.</summary>
        public double BreakDurationSeconds { get; set; } = 15;
    }
}
