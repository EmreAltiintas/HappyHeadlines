using HappyHeadlines.Core.Subscribers;
using HappyHeadlines.Core.Subscribers.Models;
using HappyHeadlines.SubscriberService.WebAPI.Filters;
using Microsoft.AspNetCore.Mvc;

namespace HappyHeadlines.SubscriberService.WebAPI.Controllers;

/// <summary>
/// Thin controller: release toggle gate, argument validation, then hand off to
/// <see cref="ISubscriberService"/> in Core.
/// </summary>
[ApiController]
[Route("api/subscribers")]
[Produces("application/json")]
public class SubscribersController : ControllerBase
{
    private readonly ISubscriberService _subscribers;

    public SubscribersController(ISubscriberService subscribers)
    {
        _subscribers = subscribers;
    }

    /// <summary>Subscribe: stores the subscriber and puts them on SubscriberQueue.</summary>
    [HttpPost]
    [ReleaseToggle(SubscriberOperation.Subscribe)]
    [ProducesResponseType(typeof(SubscribeResult), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(SubscribeResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<SubscribeResult>> Subscribe([FromBody] SubscribeRequest request, CancellationToken ct)
    {
        var result = await _subscribers.SubscribeAsync(request, ct);
        return result.Created ? StatusCode(StatusCodes.Status201Created, result) : Ok(result);
    }

    /// <summary>Unsubscribe: removes the subscriber from the database.</summary>
    [HttpDelete("{email}")]
    [ReleaseToggle(SubscriberOperation.Unsubscribe)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unsubscribe(string email, CancellationToken ct)
    {
        return await _subscribers.UnsubscribeAsync(email, ct) ? NoContent() : NotFound();
    }

    /// <summary>All subscribers - called by NewsletterService.</summary>
    [HttpGet]
    [ReleaseToggle(SubscriberOperation.List)]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriberResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SubscriberResponse>>> List(CancellationToken ct)
    {
        return Ok(await _subscribers.ListAsync(ct));
    }
}
