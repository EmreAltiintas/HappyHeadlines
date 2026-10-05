using HappyHeadlines.Core.Drafts;
using HappyHeadlines.Core.Drafts.Models;
using Microsoft.AspNetCore.Mvc;

namespace HappyHeadlines.DraftService.WebAPI.Controllers;

/// <summary>
/// Thin controller: validates arguments, then hands off to <see cref="IDraftService"/>.
/// All persistence lives in Core.
/// </summary>
[ApiController]
[Route("api/drafts")]
[Produces("application/json")]
public class DraftsController : ControllerBase
{
    private readonly IDraftService _drafts;

    public DraftsController(IDraftService drafts)
    {
        _drafts = drafts;
    }

    /// <summary>All drafts, newest first. Optionally filtered to one author.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DraftResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DraftResponse>>> List([FromQuery] string? author, CancellationToken ct)
    {
        return Ok(await _drafts.ListAsync(author, ct));
    }

    /// <summary>A single draft.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DraftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DraftResponse>> Get(Guid id, CancellationToken ct)
    {
        var draft = await _drafts.GetAsync(id, ct);
        return draft is null
            ? NotFound(Problem($"Draft {id} was not found.", statusCode: StatusCodes.Status404NotFound))
            : Ok(draft);
    }

    /// <summary>Create a draft.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(DraftResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DraftResponse>> Create([FromBody] CreateDraftRequest request, CancellationToken ct)
    {
        var draft = await _drafts.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = draft.Id }, draft);
    }

    /// <summary>Full replace of an existing draft.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(DraftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DraftResponse>> Update(Guid id, [FromBody] UpdateDraftRequest request, CancellationToken ct)
    {
        var updated = await _drafts.UpdateAsync(id, request, ct);
        return updated is null
            ? NotFound(Problem($"Draft {id} was not found.", statusCode: StatusCodes.Status404NotFound))
            : Ok(updated);
    }

    /// <summary>Delete a draft.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _drafts.DeleteAsync(id, ct);
        return deleted
            ? NoContent()
            : NotFound(Problem($"Draft {id} was not found.", statusCode: StatusCodes.Status404NotFound));
    }
}
