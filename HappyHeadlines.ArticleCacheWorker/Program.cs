using HappyHeadlines.ArticleCacheWorker;
using HappyHeadlines.Core.Caching;
using HappyHeadlines.Db.Sharding;
using HappyHeadlines.Observability;

var builder = Host.CreateApplicationBuilder(args);

// Same central logging + tracing as every other HappyHeadlines process.
builder.AddHappyHeadlinesObservability("ArticleCacheWorker");

// Reads the GLOBAL ArticleDatabase (the shard router needs the same ArticleShards__* settings
// as ArticleService) and writes to Redis.
builder.Services.AddSingleton<IArticleDbContextFactory, ArticleDbContextFactory>();
builder.Services.Configure<ArticleCacheOptions>(builder.Configuration.GetSection(ArticleCacheOptions.SectionName));
builder.Services.AddStackExchangeRedisCache(o =>
    o.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false");
builder.Services.AddSingleton<IArticleCache, ArticleCache>();
builder.Services.AddSingleton<ArticleCacheRefresher>();

builder.Services.AddHostedService<ArticleCacheWorker>();

builder.Build().Run();
