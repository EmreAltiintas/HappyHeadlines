using HappyHeadlines.DraftService.WebAPI.Middleware;
using HappyHeadlines.Db;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HappyHeadlines.DraftService.WebAPI.Controllers;

/// <summary>Instance identity + readiness, mirroring the ArticleService.WebAPI convention.</summary>
[ApiController]
[Route("api")]
public class DiagnosticsController : ControllerBase
{
    private readonly DraftDbContext _db;

    public DiagnosticsController(DraftDbContext db)
    {
        _db = db;
    }

    [HttpGet("instance")]
    public IActionResult Instance() => Ok(new
    {
        instanceId = InstanceInfo.InstanceId,
        machineName = Environment.MachineName,
        utc = DateTimeOffset.UtcNow
    });

    /// <summary>Ready only when the draft database is reachable and fully migrated.</summary>
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
