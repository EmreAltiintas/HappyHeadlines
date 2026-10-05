using System.Text.Json.Serialization;
using HappyHeadlines.Core.Articles;
using HappyHeadlines.Core.Caching;
using HappyHeadlines.Db.Sharding;
using HappyHeadlines.ArticleService.WebAPI.Consumers;
using HappyHeadlines.ArticleService.WebAPI.Middleware;
using HappyHeadlines.Observability;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

// Central, reusable logging + tracing - same call as every other HappyHeadlines service.
builder.AddHappyHeadlinesObservability("ArticleService", exposePrometheusMetrics: true);

// ---------------------------------------------------------------------------
// Dependency injection – wired here in Program.cs, not in per-layer classes.
// ---------------------------------------------------------------------------
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();

// Z-axis shard router + startup migrator (from HappyHeadlines.Db).
builder.Services.AddSingleton<IArticleDbContextFactory, ArticleDbContextFactory>();
builder.Services.AddSingleton<ShardMigrator>();

// ArticleCache in front of the GLOBAL shard: Redis, filled offline by ArticleCacheWorker.
// abortConnect=false + short timeouts: if Redis is down, ArticleService keeps serving from the DB.
builder.Services.Configure<ArticleCacheOptions>(builder.Configuration.GetSection(ArticleCacheOptions.SectionName));
builder.Services.AddStackExchangeRedisCache(o =>
    o.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false");
builder.Services.AddSingleton<IArticleCache, ArticleCache>();

// Core services + business logic (from HappyHeadlines.Core).
builder.Services.AddScoped<IArticleService, ArticleService>();

// ---------------------------------------------------------------------------
// ArticleQueue consumer side: PublisherService publishes ArticlePublished, MassTransit's
// ConfigureEndpoints(context) below gives ArticleService its own queue bound to it (fan-out -
// NewsletterService gets a separate queue for the same event). Trace context (W3C traceparent)
// travels in the message headers automatically; nothing manual is needed here for that.
// ---------------------------------------------------------------------------
var rabbitMqConnectionString = builder.Configuration.GetConnectionString("RabbitMq")
    ?? throw new InvalidOperationException("Missing connection string 'ConnectionStrings:RabbitMq'.");

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ArticlePublishedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        ConfigureRabbitMqHost(cfg, rabbitMqConnectionString);
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.Logger.LogInformation("HappyHeadlines.ArticleService.WebAPI instance '{InstanceId}' starting.", InstanceInfo.InstanceId);

// Bring every one of the 8 shard databases up to the latest migration.
if (app.Configuration.GetValue("MIGRATE_ON_STARTUP", true))
{
    using var scope = app.Services.CreateScope();
    var migrator = scope.ServiceProvider.GetRequiredService<ShardMigrator>();
    await migrator.MigrateAllAsync();
}
else
{
    app.Logger.LogInformation("MIGRATE_ON_STARTUP is false – this instance skips shard migration.");
}

app.MapOpenApi();

app.UseHappyHeadlinesRequestLogging();

app.UseMiddleware<InstanceHeaderMiddleware>();

app.MapControllers();
app.MapHappyHeadlinesMetrics();

app.Run();

// Parses "amqp://user:pass@host:port" (ConnectionStrings:RabbitMq) into a MassTransit host -
// duplicated identically in PublisherService/NewsletterService rather than shared, matching this
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
