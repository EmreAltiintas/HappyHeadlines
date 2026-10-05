using HappyHeadlines.NewsletterService.WebAPI.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace HappyHeadlines.NewsletterService.WebAPI.Controllers;

/// <summary>Instance identity + readiness, mirroring the ArticleService.WebAPI convention.</summary>
[ApiController]
[Route("api")]
public class DiagnosticsController : ControllerBase
{
    [HttpGet("instance")]
    public IActionResult Instance() => Ok(new
    {
        instanceId = InstanceInfo.InstanceId,
        machineName = Environment.MachineName,
        utc = DateTimeOffset.UtcNow
    });

    /// <summary>NewsletterService owns no database - ready as soon as it is up.</summary>
    [HttpGet("health")]
    public IActionResult Health() => Ok(new
    {
        status = "ok",
        instanceId = InstanceInfo.InstanceId
    });
}
