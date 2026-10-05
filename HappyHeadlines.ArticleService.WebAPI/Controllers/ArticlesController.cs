using HappyHeadlines.Core.Articles;
using HappyHeadlines.Core.Articles.Models;
using HappyHeadlines.Db;
using Microsoft.AspNetCore.Mvc;

namespace HappyHeadlines.ArticleService.WebAPI.Controllers;

/// <summary>
/// Thin controller: it only checks that the arguments are valid, then hands off to
/// <see cref="IArticleService"/>. All shard routing, persistence and mapping live in Core.
/// </summary>
[ApiController]
[Route("api/articles")]
[Produces("application/json")]
public class ArticlesController : ControllerBase
{
    private readonly IArticleService _articles;

    public ArticlesController(IArticleService articles)
    {
        _articles = articles;
    }

    /// <summary>List every article in a continent's shard (use <c>Global</c> for worldwide).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ArticleResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ArticleResponse>>> List(
        [FromQuery] Continent continent,
        CancellationToken ct)
    {
        return Ok(await _articles.ListAsync(continent, ct));
    }

    /// <summary>Read one article. The shard is chosen from <paramref name="continent"/>; no fallback.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ArticleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArticleResponse>> Get(
        int id,
        [FromQuery] Continent continent,
        CancellationToken ct)
    {
        var article = await _articles.GetAsync(continent, id, ct);
        return article is null
            ? NotFound(Problem($"Article {id} was not found in the {continent} shard.", statusCode: StatusCodes.Status404NotFound))
            : Ok(article);
    }

    /// <summary>Create an article. Its <c>Continent</c> decides which shard it is written to.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ArticleResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ArticleResponse>> Create(
        [FromBody] CreateArticleRequest request,
        CancellationToken ct)
    {
        var created = await _articles.CreateAsync(request, ct);
        return CreatedAtAction(
            nameof(Get),
            new { id = created.Id, continent = created.Continent },
            created);
    }

    /// <summary>Update an article in the continent's shard. Body continent must match the route.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ArticleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArticleResponse>> Update(
        int id,
        [FromQuery] Continent continent,
        [FromBody] UpdateArticleRequest request,
        CancellationToken ct)
    {
        if (request.Continent != continent)
        {
            ModelState.AddModelError(nameof(request.Continent),
                "Body 'Continent' must match the 'continent' the article is routed to. Cross-shard moves are out of scope.");
            return ValidationProblem(ModelState);
        }

        var updated = await _articles.UpdateAsync(continent, id, request, ct);
        return updated is null
            ? NotFound(Problem($"Article {id} was not found in the {continent} shard.", statusCode: StatusCodes.Status404NotFound))
            : Ok(updated);
    }

    /// <summary>Delete an article from the continent's shard.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        int id,
        [FromQuery] Continent continent,
        CancellationToken ct)
    {
        var deleted = await _articles.DeleteAsync(continent, id, ct);
        return deleted
            ? NoContent()
            : NotFound(Problem($"Article {id} was not found in the {continent} shard.", statusCode: StatusCodes.Status404NotFound));
    }
}
