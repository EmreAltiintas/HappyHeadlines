using System.Text.Json;
using System.Text.Json.Serialization;
using HappyHeadlines.Core.Articles.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HappyHeadlines.Core.Caching;

/// <summary>
/// <see cref="IArticleCache"/> on top of <see cref="IDistributedCache"/> (Redis in Docker). Redis is
/// shared, so the 3 ArticleService instances and the offline worker see the same data. If Redis is
/// down every call degrades to a miss, so ArticleService simply reads the database (availability
/// first); that miss is logged as a warning.
/// </summary>
public sealed class ArticleCache : IArticleCache
{
    public const string CacheName = "ArticleCache";
    private const string ListKey = "articles:global:recent";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IDistributedCache _cache;
    private readonly ILogger<ArticleCache> _logger;
    private readonly CacheMetrics _metrics = new(CacheName);

    public ArticleCache(IDistributedCache cache, IOptions<ArticleCacheOptions> options, ILogger<ArticleCache> logger)
    {
        _cache = cache;
        _logger = logger;
        RecentDays = options.Value.RecentDays;
    }

    public int RecentDays { get; }

    public CacheMetrics Metrics => _metrics;

    public Task<IReadOnlyList<ArticleResponse>?> GetRecentAsync(CancellationToken ct = default) =>
        ReadAsync<IReadOnlyList<ArticleResponse>>(ListKey, ct);

    public Task<ArticleResponse?> GetAsync(int id, CancellationToken ct = default) =>
        ReadAsync<ArticleResponse>(ItemKey(id), ct);

    public async Task StoreAsync(IReadOnlyList<ArticleResponse> recentArticles, TimeSpan timeToLive, CancellationToken ct = default)
    {
        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = timeToLive };

        foreach (var article in recentArticles)
            await _cache.SetStringAsync(ItemKey(article.Id), JsonSerializer.Serialize(article, Json), options, ct);

        await _cache.SetStringAsync(ListKey, JsonSerializer.Serialize(recentArticles, Json), options, ct);
    }

    public async Task InvalidateAsync(int? id, CancellationToken ct = default)
    {
        try
        {
            if (id is not null)
                await _cache.RemoveAsync(ItemKey(id.Value), ct);
            await _cache.RemoveAsync(ListKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ArticleCache invalidation failed; entries expire on their own at the next refresh.");
        }
    }

    private async Task<T?> ReadAsync<T>(string key, CancellationToken ct) where T : class
    {
        string? json;
        try
        {
            json = await _cache.GetStringAsync(key, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ArticleCache unavailable - treating {Key} as a miss and reading the database.", key);
            _metrics.RecordMiss();
            return null;
        }

        if (json is null)
        {
            _logger.LogInformation("ArticleCache MISS for {Key} - falling back to the global ArticleDatabase.", key);
            _metrics.RecordMiss();
            return null;
        }

        _metrics.RecordHit();
        return JsonSerializer.Deserialize<T>(json, Json);
    }

    private static string ItemKey(int id) => $"articles:global:{id}";
}
