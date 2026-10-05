using HappyHeadlines.Core.Caching;
using Microsoft.Extensions.Options;

namespace HappyHeadlines.ArticleCacheWorker;

/// <summary>
/// The offline process that fills ArticleCache: wakes up every
/// <see cref="ArticleCacheOptions.RefreshIntervalSeconds"/> seconds and asks
/// <see cref="ArticleCacheRefresher"/> to copy the last 14 days of global articles into Redis.
/// A failed refresh (database or Redis down) is logged and retried at the next tick.
/// </summary>
public class ArticleCacheWorker : BackgroundService
{
    private readonly ArticleCacheRefresher _refresher;
    private readonly ArticleCacheOptions _options;
    private readonly ILogger<ArticleCacheWorker> _logger;

    public ArticleCacheWorker(
        ArticleCacheRefresher refresher,
        IOptions<ArticleCacheOptions> options,
        ILogger<ArticleCacheWorker> logger)
    {
        _refresher = refresher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ArticleCacheWorker started: refreshing every {Seconds}s with the last {Days} days of global articles.",
            _options.RefreshIntervalSeconds, _options.RecentDays);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.RefreshIntervalSeconds));
        do
        {
            try
            {
                await _refresher.RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "ArticleCache refresh failed - will retry in {Seconds}s.", _options.RefreshIntervalSeconds);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
