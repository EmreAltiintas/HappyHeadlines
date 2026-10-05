using HappyHeadlines.Core.Profanity;
using HappyHeadlines.Core.Profanity.Models;
using Microsoft.AspNetCore.Mvc;

namespace HappyHeadlines.ProfanityService.WebAPI.Controllers;

/// <summary>
/// Thin controller: validates arguments, then hands off to <see cref="IProfanityService"/>.
/// All matching and persistence live in Core.
/// </summary>
[ApiController]
[Route("api/profanity")]
[Produces("application/json")]
public class ProfanityController : ControllerBase
{
    private readonly IProfanityService _profanity;

    public ProfanityController(IProfanityService profanity)
    {
        _profanity = profanity;
    }

    /// <summary>Check whether a piece of text contains profanity.</summary>
    [HttpPost("check")]
    [ProducesResponseType(typeof(ProfanityCheckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProfanityCheckResponse>> Check(
        [FromBody] ProfanityCheckRequest request,
        CancellationToken ct)
    {
        return Ok(await _profanity.CheckAsync(request.Text, ct));
    }

    /// <summary>List every word in the profanity database (seeded on migration).</summary>
    [HttpGet("words")]
    [ProducesResponseType(typeof(IReadOnlyList<ProfanityWordResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProfanityWordResponse>>> ListWords(CancellationToken ct)
    {
        return Ok(await _profanity.ListWordsAsync(ct));
    }

    /// <summary>Add a word to the profanity list. Bonus CRUD – not required by the brief.</summary>
    [HttpPost("words")]
    [ProducesResponseType(typeof(ProfanityWordResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProfanityWordResponse>> AddWord(
        [FromBody] AddProfanityWordRequest request,
        CancellationToken ct)
    {
        var added = await _profanity.AddWordAsync(request.Word, ct);
        return added is null
            ? Conflict(Problem($"'{request.Word.Trim().ToLowerInvariant()}' is already on the list.", statusCode: StatusCodes.Status409Conflict))
            : CreatedAtAction(nameof(ListWords), new { }, added);
    }

    /// <summary>Remove a word from the profanity list. Bonus CRUD – not required by the brief.</summary>
    [HttpDelete("words/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteWord(int id, CancellationToken ct)
    {
        var deleted = await _profanity.DeleteWordAsync(id, ct);
        return deleted
            ? NoContent()
            : NotFound(Problem($"Profanity word {id} was not found.", statusCode: StatusCodes.Status404NotFound));
    }
}
