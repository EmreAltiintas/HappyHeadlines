using HappyHeadlines.Core.Newsletters;
using HappyHeadlines.Core.Newsletters.Models;
using HappyHeadlines.Db;
using Microsoft.AspNetCore.Mvc;

namespace HappyHeadlines.NewsletterService.WebAPI.Controllers;

/// <summary>
/// Thin controller: validates the request, then hands off to <see cref="IDailyNewsletterService"/>.
/// The actual ArticleService call (plain HTTP, through the Gateway) lives in Core.
/// </summary>
[ApiController]
[Route("api/newsletters")]
[Produces("application/json")]
public class NewslettersController : ControllerBase
{
    private readonly IDailyNewsletterService _dailyNewsletters;

    public NewslettersController(IDailyNewsletterService dailyNewsletters)
    {
        _dailyNewsletters = dailyNewsletters;
    }

    /// <summary>Manual trigger for the daily digest of one continent's articles.</summary>
    [HttpPost("daily")]
    [ProducesResponseType(typeof(DailyNewsletterResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<DailyNewsletterResult>> Daily(
        [FromQuery] Continent continent,
        CancellationToken ct)
    {
        return Ok(await _dailyNewsletters.SendAsync(continent, ct));
    }
}
