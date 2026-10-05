using HappyHeadlines.CommentService.WebAPI.Middleware;
using HappyHeadlines.Core.Caching;
using HappyHeadlines.Db;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HappyHeadlines.CommentService.WebAPI.Controllers;

/// <summary>Instance identity + readiness, mirroring the ArticleService.WebAPI convention.</summary>
[ApiController]
[Route("api")]
public class DiagnosticsController : ControllerBase
{
    private readonly CommentDbContext _db;
    private readonly ICommentCache _cache;

    public DiagnosticsController(CommentDbContext db, ICommentCache cache)
    {
        _db = db;
        _cache = cache;
    }

    /// <summary>
    /// CommentCache state for the demo: which articles are cached (most recently used first),
    /// capacity, and hit/miss/eviction totals. Evicting the 31st article is visible here.
    /// </summary>
    [HttpGet("cache")]
    public IActionResult CacheState() => Ok(new
    {
        cache = CommentCache.CacheName,
        capacity = _cache.Capacity,
        cachedArticleCount = _cache.CachedArticleIds.Count,
        cachedArticleIds = _cache.CachedArticleIds,
        hits = _cache.Metrics.Hits,
        misses = _cache.Metrics.Misses,
        evictions = _cache.Metrics.Evictions,
        hitRatio = _cache.Metrics.Hits + _cache.Metrics.Misses == 0
            ? 0
            : Math.Round((double)_cache.Metrics.Hits / (_cache.Metrics.Hits + _cache.Metrics.Misses), 3)
    });

    [HttpGet("instance")]
    public IActionResult Instance() => Ok(new
    {
        instanceId = InstanceInfo.InstanceId,
        machineName = Environment.MachineName,
        utc = DateTimeOffset.UtcNow
    });

    /// <summary>
    /// Ready only when the comment database is reachable and fully migrated. Note this does
    /// NOT probe ProfanityService – the two services are separate swimlanes, so CommentService
    /// stays healthy even while ProfanityService is down.
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        try
        {
            var pending = await _db.Database.GetPendingMigrationsAsync(ct);
            if (pending.Any())
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    status = "migrating",
                    instanceId = InstanceInfo.InstanceId,
                    pendingMigrations = pending
                });
            }
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "db-unavailable",
                instanceId = InstanceInfo.InstanceId,
                error = ex.Message
            });
        }

        return Ok(new
        {
            status = "ok",
            instanceId = InstanceInfo.InstanceId
        });
    }
}
