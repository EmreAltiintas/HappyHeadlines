using System.Text.Json.Serialization;
using HappyHeadlines.Core.Newsletters;
using HappyHeadlines.Core.Subscribers;
using HappyHeadlines.NewsletterService.WebAPI;
using HappyHeadlines.NewsletterService.WebAPI.Consumers;
using HappyHeadlines.NewsletterService.WebAPI.Middleware;
using HappyHeadlines.Observability;
using MassTransit;
using Microsoft.Extensions.Http.Resilience;
using Polly;

var builder = WebApplication.CreateBuilder(args);

// Central, reusable logging + tracing - same call as every other HappyHeadlines service. This is
// what lets the ArticleQueue consume span AND the NewsletterService -> ArticleService HTTP call
// each correlate into their own complete traces.
builder.AddHappyHeadlinesObservability("NewsletterService");

// ---------------------------------------------------------------------------
// Dependency injection – wired here in Program.cs, not in per-layer classes.
// ---------------------------------------------------------------------------
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();

// ---------------------------------------------------------------------------
// Flow b) daily digest: plain instrumented HttpClient call to ArticleService via the Gateway.
// ---------------------------------------------------------------------------
var articleServiceOptions = builder.Configuration
    .GetSection(ArticleServiceOptions.SectionName)
    .Get<ArticleServiceOptions>() ?? new ArticleServiceOptions();

builder.Services.AddHttpClient<IArticleClient, ArticleClient>(client =>
{
    client.BaseAddress = new Uri(articleServiceOptions.BaseUrl);
});

// ---------------------------------------------------------------------------
// Diagram: NewsletterService -> SubscriberService "Sends request to get subscribers" (sync REST).
// Swimlane fault isolation: per-attempt timeout, retry and a circuit breaker, so a slow, dead or
// toggled-off (503) SubscriberService fails fast and DailyNewsletterService falls back to
// "0 subscribers" instead of taking NewsletterService down. No depends_on in compose either.
// ---------------------------------------------------------------------------
var subscriberOptions = builder.Configuration
    .GetSection(SubscriberServiceOptions.SectionName)
    .Get<SubscriberServiceOptions>() ?? new SubscriberServiceOptions();

builder.Services.AddHttpClient<ISubscriberClient, SubscriberClient>(client =>
{
    client.BaseAddress = new Uri(subscriberOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30); // hard ceiling; the resilience timeout fires first
})
.AddResilienceHandler("subscriberservice", (pipeline, context) =>
{
    var logger = context.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("HappyHeadlines.NewsletterService.SubscriberCircuitBreaker");

    // Outer -> inner: retry, circuit breaker, per-attempt timeout.
    pipeline.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = subscriberOptions.RetryCount,
        Delay = TimeSpan.FromMilliseconds(200),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true
    });

    pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        FailureRatio = subscriberOptions.CircuitBreaker.FailureRatio,
        MinimumThroughput = subscriberOptions.CircuitBreaker.MinimumThroughput,
        SamplingDuration = TimeSpan.FromSeconds(subscriberOptions.CircuitBreaker.SamplingDurationSeconds),
        BreakDuration = TimeSpan.FromSeconds(subscriberOptions.CircuitBreaker.BreakDurationSeconds),
        OnOpened = args =>
        {
            logger.LogWarning("SubscriberService circuit breaker OPENED for {BreakDurationSeconds}s - calls fail fast.",
                args.BreakDuration.TotalSeconds);
            return ValueTask.CompletedTask;
        },
        OnClosed = _ =>
        {
            logger.LogInformation("SubscriberService circuit breaker CLOSED - calls flowing again.");
            return ValueTask.CompletedTask;
        }
    });

    pipeline.AddTimeout(TimeSpan.FromSeconds(subscriberOptions.TimeoutSeconds));
});

// Same toggle file as SubscriberService; changes apply without a restart.
var featureFlagsFile = builder.Configuration["FeatureFlags:File"] ?? "featureflags.json";
if (!Path.IsPathRooted(featureFlagsFile))
    featureFlagsFile = Path.Combine(AppContext.BaseDirectory, featureFlagsFile);

builder.Configuration.AddJsonFile(featureFlagsFile, optional: true, reloadOnChange: true);
builder.Services.Configure<SubscriberFeatureFlags>(
    builder.Configuration.GetSection(SubscriberFeatureFlags.SectionName));

builder.Services.AddScoped<IDailyNewsletterService, DailyNewsletterService>();

// ---------------------------------------------------------------------------
// Flow a) immediate newsletter: NewsletterService's own independent subscriber to
// ArticlePublished (separate queue from ArticleService's, via ConfigureEndpoints below).
// ---------------------------------------------------------------------------
var rabbitMqConnectionString = builder.Configuration.GetConnectionString("RabbitMq")
    ?? throw new InvalidOperationException("Missing connection string 'ConnectionStrings:RabbitMq'.");

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ImmediateNewsletterConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        ConfigureRabbitMqHost(cfg, rabbitMqConnectionString);
        cfg.ConfigureEndpoints(context);
    });
});

// ---------------------------------------------------------------------------
// Diagram: NewsletterService consumes SubscriberQueue ("Send a welcome mail to new subscribers").
// A second, separate bus on its own broker: if SubscriberQueue is down, ArticleQueue consumption
// (and the REST endpoints) keep working, and vice versa. Messages wait durably in the queue
// while this service is down; failed consumes are retried with backoff.
// ---------------------------------------------------------------------------
var subscriberQueueConnectionString = builder.Configuration.GetConnectionString("SubscriberQueue")
    ?? throw new InvalidOperationException("Missing connection string 'ConnectionStrings:SubscriberQueue'.");

builder.Services.AddMassTransit<ISubscriberQueueBus>(x =>
{
    x.AddConsumer<WelcomeMailConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        ConfigureRabbitMqHost(cfg, subscriberQueueConnectionString);
        cfg.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.Logger.LogInformation("HappyHeadlines.NewsletterService.WebAPI instance '{InstanceId}' starting.", InstanceInfo.InstanceId);

app.MapOpenApi();

app.UseHappyHeadlinesRequestLogging();

app.UseMiddleware<InstanceHeaderMiddleware>();

app.MapControllers();

app.Run();

// Parses "amqp://user:pass@host:port" (ConnectionStrings:RabbitMq) into a MassTransit host -
// duplicated identically in PublisherService/ArticleService rather than shared, matching this
// codebase's convention of small per-service Program.cs helpers over a shared infra abstraction.
static void ConfigureRabbitMqHost(IRabbitMqBusFactoryConfigurator cfg, string connectionString)
{
    var uri = new Uri(connectionString);
    var port = (ushort)(uri.IsDefaultPort ? 5672 : uri.Port);

    cfg.Host(uri.Host, port, "/", h =>
    {
        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length == 2)
        {
            h.Username(Uri.UnescapeDataString(userInfo[0]));
            h.Password(Uri.UnescapeDataString(userInfo[1]));
        }
    });
}
