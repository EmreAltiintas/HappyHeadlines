using HappyHeadlines.Db;
using HappyHeadlines.ProfanityService.WebAPI.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HappyHeadlines.ProfanityService.WebAPI.Controllers;

/// <summary>Instance identity + readiness, mirroring the ArticleService.WebAPI convention.</summary>
[ApiController]
[Route("api")]
public class DiagnosticsController : ControllerBase
{
    private readonly ProfanityDbContext _db;

    public DiagnosticsController(ProfanityDbContext db)
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

    /// <summary>Ready only when the profanity database is reachable and fully migrated.</summary>
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
