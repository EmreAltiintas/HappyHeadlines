using HappyHeadlines.Core.Articles;
using HappyHeadlines.Core.Articles.Models;
using HappyHeadlines.Core.Caching;
using HappyHeadlines.Db;
using HappyHeadlines.Db.Entities;
using HappyHeadlines.Db.Sharding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HappyHeadlines.Core.Tests;

/// <summary>ArticleCache + ArticleCacheRefresher + ArticleService fallback, with in-memory stand-ins for Redis and SQL Server.</summary>
public class ArticleCacheTests
{
    /// <summary>One shared in-memory database per continent, so the refresher and the service see the same "Global" shard.</summary>
    private sealed class InMemoryShards : IArticleDbContextFactory
    {
        private readonly string _name = Guid.NewGuid().ToString();

        public IReadOnlyDictionary<Continent, string> ConnectionStrings { get; } = new Dictionary<Continent, string>();

        public ArticleDbContext Create(Continent continent) => new(
            new DbContextOptionsBuilder<ArticleDbContext>().UseInMemoryDatabase($"{_name}-{continent}").Options);
    }

    private sealed class DownCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("Redis is down");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("Redis is down");
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
    }

    private static readonly IOptions<ArticleCacheOptions> Options = Microsoft.Extensions.Options.Options.Create(new ArticleCacheOptions());

    private static ArticleCache NewCache(IDistributedCache? backing = null) => new(
        backing ?? new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions())),
        Options,
        NullLogger<ArticleCache>.Instance);

    private static async Task<int> AddArticle(InMemoryShards shards, Continent continent, DateTimeOffset published, string title = "t")
    {
        await using var db = shards.Create(continent);
        var article = new Article
        {
            Title = title, Content = "c", Author = "a",
            PublishedDate = published, Continent = continent, CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Articles.Add(article);
        await db.SaveChangesAsync();
        return article.Id;
    }

    [Fact]
    public async Task Empty_cache_is_a_miss_and_a_filled_cache_is_a_hit()
    {
        var cache = NewCache();

        Assert.Null(await cache.GetAsync(1));               // miss
        await cache.StoreAsync([new ArticleResponse { Id = 1, Title = "x", Continent = Continent.Global }], TimeSpan.FromMinutes(1));
        var hit = await cache.GetAsync(1);                  // hit

        Assert.NotNull(hit);
        Assert.Equal("x", hit.Title);
        Assert.Equal(Continent.Global, hit.Continent);      // enum survives the JSON round trip
        Assert.Equal(1, cache.Metrics.Hits);
        Assert.Equal(1, cache.Metrics.Misses);
    }

    [Fact]
    public async Task Refresher_caches_only_global_articles_from_the_last_14_days()
    {
        var shards = new InMemoryShards();
        var recent = await AddArticle(shards, Continent.Global, DateTimeOffset.UtcNow.AddDays(-3));
        var old = await AddArticle(shards, Continent.Global, DateTimeOffset.UtcNow.AddDays(-20));
        await AddArticle(shards, Continent.Europe, DateTimeOffset.UtcNow);   // other shard: ignored
        var cache = NewCache();
        var refresher = new ArticleCacheRefresher(shards, cache, Options, NullLogger<ArticleCacheRefresher>.Instance);

        var count = await refresher.RefreshAsync();

        Assert.Equal(1, count);
        Assert.NotNull(await cache.GetAsync(recent));
        Assert.Null(await cache.GetAsync(old));
        var list = await cache.GetRecentAsync();
        Assert.Equal([recent], list!.Select(a => a.Id));
    }

    [Fact]
    public async Task ArticleService_reads_global_from_the_cache_first_and_falls_back_to_the_database_on_a_miss()
    {
        var shards = new InMemoryShards();
        var cached = await AddArticle(shards, Continent.Global, DateTimeOffset.UtcNow.AddDays(-1), "recent");
        var old = await AddArticle(shards, Continent.Global, DateTimeOffset.UtcNow.AddDays(-30), "old");
        var cache = NewCache();
        await new ArticleCacheRefresher(shards, cache, Options, NullLogger<ArticleCacheRefresher>.Instance).RefreshAsync();
        var service = new ArticleService(shards, cache);

        var hit = await service.GetAsync(Continent.Global, cached);
        var miss = await service.GetAsync(Continent.Global, old);      // not cached -> database

        Assert.Equal("recent", hit!.Title);
        Assert.Equal("old", miss!.Title);                              // fallback still answers
        Assert.Equal(1, cache.Metrics.Hits);
        Assert.Equal(1, cache.Metrics.Misses);
    }

    [Fact]
    public async Task Continent_shards_bypass_the_cache()
    {
        var shards = new InMemoryShards();
        var id = await AddArticle(shards, Continent.Europe, DateTimeOffset.UtcNow);
        var cache = NewCache();
        var service = new ArticleService(shards, cache);

        Assert.NotNull(await service.GetAsync(Continent.Europe, id));

        Assert.Equal(0, cache.Metrics.Hits + cache.Metrics.Misses);
    }

    [Fact]
    public async Task Global_list_is_the_same_14_day_window_on_a_miss_as_on_a_hit()
    {
        var shards = new InMemoryShards();
        await AddArticle(shards, Continent.Global, DateTimeOffset.UtcNow.AddDays(-1));
        await AddArticle(shards, Continent.Global, DateTimeOffset.UtcNow.AddDays(-30));
        var cache = NewCache();
        var service = new ArticleService(shards, cache);

        var onMiss = await service.ListAsync(Continent.Global);
        await new ArticleCacheRefresher(shards, cache, Options, NullLogger<ArticleCacheRefresher>.Instance).RefreshAsync();
        var onHit = await service.ListAsync(Continent.Global);

        Assert.Single(onMiss);
        Assert.Single(onHit);
    }

    [Fact]
    public async Task Writing_a_global_article_invalidates_its_cache_entry()
    {
        var shards = new InMemoryShards();
        var id = await AddArticle(shards, Continent.Global, DateTimeOffset.UtcNow, "before");
        var cache = NewCache();
        await new ArticleCacheRefresher(shards, cache, Options, NullLogger<ArticleCacheRefresher>.Instance).RefreshAsync();
        var service = new ArticleService(shards, cache);

        await service.UpdateAsync(Continent.Global, id, new UpdateArticleRequest
        {
            Title = "after", Content = "c", Author = "a", PublishedDate = DateTimeOffset.UtcNow, Continent = Continent.Global
        });

        Assert.Equal("after", (await service.GetAsync(Continent.Global, id))!.Title);
    }

    [Fact]
    public async Task When_the_cache_is_down_ArticleService_still_answers_from_the_database()
    {
        var shards = new InMemoryShards();
        var id = await AddArticle(shards, Continent.Global, DateTimeOffset.UtcNow, "db");
        var cache = NewCache(new DownCache());
        var service = new ArticleService(shards, cache);

        var article = await service.GetAsync(Continent.Global, id);

        Assert.Equal("db", article!.Title);
        Assert.Equal(1, cache.Metrics.Misses);
    }
}
