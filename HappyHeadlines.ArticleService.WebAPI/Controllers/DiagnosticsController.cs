using HappyHeadlines.Db;
using HappyHeadlines.Db.Sharding;
using HappyHeadlines.ArticleService.WebAPI.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HappyHeadlines.ArticleService.WebAPI.Controllers;

/// <summary>
/// Helpers for verifying the X-axis split. Hit these through the gateway repeatedly and
/// watch <c>instanceId</c> rotate across instance 1/2/3.
/// </summary>
[ApiController]
[Route("api")]
public class DiagnosticsController : ControllerBase
{
    private readonly IArticleDbContextFactory _shards;

    public DiagnosticsController(IArticleDbContextFactory shards)
    {
        _shards = shards;
    }

    [HttpGet("instance")]
    public IActionResult Instance() => Ok(new
    {
        instanceId = InstanceInfo.InstanceId,
        machineName = Environment.MachineName,
        utc = DateTimeOffset.UtcNow
    });

    /// <summary>
    /// Ready only when the shard schema is reachable and fully migrated. Container health
    /// checks use this so instances 2 &amp; 3 wait for instance 1 to finish migrating.
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        await using var db = _shards.Create(Continent.Global);
        try
        {
            var pending = await db.Database.GetPendingMigrationsAsync(ct);
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
            instanceId = InstanceInfo.InstanceId,
            shards = _shards.ConnectionStrings.Keys.Select(c => c.ToString())
        });
    }
}
