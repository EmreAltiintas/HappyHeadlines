using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HappyHeadlines.Core.Articles;
using HappyHeadlines.Core.Articles.Models;
using HappyHeadlines.Db;

namespace HappyHeadlines.Core.Newsletters;

/// <summary>
/// Typed <see cref="HttpClient"/> wrapper for the single REST call NewsletterService makes to
/// ArticleService (through the Gateway, ArticleService's x-axis instances are never published
/// directly). Plain HTTP, no MassTransit involved - already covered end-to-end by the shared
/// AddHttpClientInstrumentation, so this trace correlates with no extra code.
/// </summary>
public sealed class ArticleClient : IArticleClient
{
    // ArticleService's controllers serialize with ASP.NET's Web defaults (camelCase, Continent
    // as a string via JsonStringEnumConverter) - GetFromJsonAsync's own defaults match neither,
    // so without this it fails to deserialize the response at all.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;

    public ArticleClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<ArticleSummaryResponse>> ListAsync(Continent continent, CancellationToken ct = default)
    {
        // The newsletter only needs the newest articles, not every article with its full text.
        var articles = await _http.GetFromJsonAsync<List<ArticleSummaryResponse>>(
            $"/api/articles?continent={continent}&pageSize={IArticleService.MaxPageSize}", JsonOptions, ct);

        return articles ?? new List<ArticleSummaryResponse>();
    }
}
