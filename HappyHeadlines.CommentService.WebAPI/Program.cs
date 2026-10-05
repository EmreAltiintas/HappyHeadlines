using System.Text.Json.Serialization;
using HappyHeadlines.CommentService.WebAPI;
using HappyHeadlines.CommentService.WebAPI.Middleware;
using HappyHeadlines.Core.Caching;
using HappyHeadlines.Core.Comments;
using HappyHeadlines.Db;
using HappyHeadlines.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Polly;

// Counts every time the ProfanityService circuit opens; Prometheus alerts on it (monitoring/prometheus/alerts.yml).
var circuitMeter = new System.Diagnostics.Metrics.Meter(ObservabilityExtensions.ResilienceMeterName);
var CircuitOpenedCounter = circuitMeter.CreateCounter<long>(
    "profanity.circuit_opened", description: "Times the CommentService -> ProfanityService circuit breaker opened.");

var builder = WebApplication.CreateBuilder(args);

// Central, reusable logging + tracing - same call as every other HappyHeadlines service. This
// is what lets the CommentService -> ProfanityService HTTP call (below) show up as one
// correlated trace instead of two unrelated sets of logs.
builder.AddHappyHeadlinesObservability("CommentService", exposePrometheusMetrics: true);

// ---------------------------------------------------------------------------
// Dependency injection – wired here in Program.cs, not in per-layer classes.
// ---------------------------------------------------------------------------
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();

// CommentService owns its own database – no shared context, schema or connection pool.
var connectionString = builder.Configuration.GetConnectionString("CommentDb")
    ?? throw new InvalidOperationException("Missing connection string 'ConnectionStrings:CommentDb'.");

builder.Services.AddDbContext<CommentDbContext>(o =>
    o.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
        maxRetryCount: 5,
        maxRetryDelay: TimeSpan.FromSeconds(5),
        errorNumbersToAdd: null)));

// CommentCache: in-process LRU, max 30 articles (singleton - it must outlive the scoped requests).
builder.Services.AddSingleton<ICommentCache>(sp => new CommentCache(
    sp.GetRequiredService<ILogger<CommentCache>>(),
    builder.Configuration.GetValue("CommentCache:MaxArticles", CommentCache.DefaultMaxArticles)));

// Core services + business logic (from HappyHeadlines.Core).
builder.Services.AddScoped<ICommentService, CommentService>();

// Fallback side of the circuit breaker: re-checks comments stored as Pending while ProfanityService was down.
builder.Services.AddHostedService<PendingCommentModerator>();

// ---------------------------------------------------------------------------
// Swimlane: CommentService -> ProfanityService is a direct REST call (NOT via the
// Gateway, NOT via any middle layer), wrapped in a Polly circuit breaker + timeout so a
// slow or dead ProfanityService makes this call fail fast instead of hanging/cascading.
// ---------------------------------------------------------------------------
var profanityOptions = builder.Configuration
    .GetSection(ProfanityServiceOptions.SectionName)
    .Get<ProfanityServiceOptions>() ?? new ProfanityServiceOptions();

builder.Services.AddHttpClient<IProfanityClient, ProfanityClient>(client =>
{
    client.BaseAddress = new Uri(profanityOptions.BaseUrl);
    // Hard ceiling; the per-attempt resilience timeout below is shorter and fires first.
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddResilienceHandler("profanity", (pipeline, context) =>
{
    // Resolved once, when this pipeline is built - not per call - so it is cheap to capture
    // in the closures below.
    var circuitLogger = context.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("HappyHeadlines.CommentService.ProfanityCircuitBreaker");

    // Circuit breaker is added first => it is the OUTER strategy, so it observes the
    // inner timeout's TimeoutRejectedException (and 5xx / transport errors) as failures.
    pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        FailureRatio = profanityOptions.CircuitBreaker.FailureRatio,
        MinimumThroughput = profanityOptions.CircuitBreaker.MinimumThroughput,
        SamplingDuration = TimeSpan.FromSeconds(profanityOptions.CircuitBreaker.SamplingDurationSeconds),
        BreakDuration = TimeSpan.FromSeconds(profanityOptions.CircuitBreaker.BreakDurationSeconds),

        // "What and when to log" example: the breaker's own state transitions are a business
        // event, not just an HTTP detail - worth a dedicated log line at the point they happen.
        OnOpened = args =>
        {
            CircuitOpenedCounter.Add(1);
            circuitLogger.LogWarning(
                "ProfanityService circuit breaker OPENED for {BreakDurationSeconds}s - calls will fail fast until it half-opens.",
                args.BreakDuration.TotalSeconds);
            return ValueTask.CompletedTask;
        },
        OnClosed = _ =>
        {
            circuitLogger.LogInformation("ProfanityService circuit breaker CLOSED - calls are flowing normally again.");
            return ValueTask.CompletedTask;
        },
        OnHalfOpened = _ =>
        {
            circuitLogger.LogInformation("ProfanityService circuit breaker HALF-OPEN - trialing a single call.");
            return ValueTask.CompletedTask;
        }
    });

    // Per-attempt timeout is the INNER strategy.
    pipeline.AddTimeout(TimeSpan.FromSeconds(profanityOptions.TimeoutSeconds));
});

var app = builder.Build();

app.Logger.LogInformation("HappyHeadlines.CommentService.WebAPI instance '{InstanceId}' starting.", InstanceInfo.InstanceId);

// Bring the comment database up to the latest migration.
if (app.Configuration.GetValue("MIGRATE_ON_STARTUP", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CommentDbContext>();
    await MigrateWithRetryAsync(db, app.Logger);
}
else
{
    app.Logger.LogInformation("MIGRATE_ON_STARTUP is false – this instance skips database migration.");
}

app.MapOpenApi();

app.UseHappyHeadlinesRequestLogging();

app.UseMiddleware<InstanceHeaderMiddleware>();

app.MapControllers();
app.MapHappyHeadlinesMetrics();

app.Run();

// Retries while the SQL Server container is still booting, then lets a lasting failure crash startup.
static async Task MigrateWithRetryAsync(DbContext db, ILogger logger)
{
    const int maxAttempts = 20;
    var delay = TimeSpan.FromSeconds(3);

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied.");
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogWarning(
                "Migration attempt {Attempt}/{Max} failed ({Message}). Retrying in {Delay}s.",
                attempt, maxAttempts, ex.Message, delay.TotalSeconds);
            await Task.Delay(delay);
        }
    }

    await db.Database.MigrateAsync();
    logger.LogInformation("Database migrations applied.");
}
