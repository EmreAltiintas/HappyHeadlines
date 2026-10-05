using HappyHeadlines.Core.Publishing;
using HappyHeadlines.Core.Publishing.Models;
using Microsoft.AspNetCore.Mvc;

namespace HappyHeadlines.PublisherService.WebAPI.Controllers;

/// <summary>
/// Thin controller: validates the request, then hands off to <see cref="IArticlePublisher"/>.
/// All ArticleQueue publishing lives in Core.
/// </summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class PublishController : ControllerBase
{
    private readonly IArticlePublisher _publisher;

    public PublishController(IArticlePublisher publisher)
    {
        _publisher = publisher;
    }

    /// <summary>Finalizes and publishes an article. Persistence happens asynchronously via ArticleQueue.</summary>
    [HttpPost("publish")]
    [ProducesResponseType(typeof(PublishArticleResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PublishArticleResponse>> Publish(
        [FromBody] PublishArticleRequest request,
        CancellationToken ct)
    {
        var result = await _publisher.PublishAsync(request, ct);
        return Accepted(result);
    }
}
