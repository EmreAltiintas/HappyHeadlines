using HappyHeadlines.Core.Articles.Models;
using HappyHeadlines.Core.Caching;
using HappyHeadlines.Db;
using HappyHeadlines.Db.Entities;
using HappyHeadlines.Db.Sharding;
using Microsoft.EntityFrameworkCore;

namespace HappyHeadlines.Core.Articles;

/// <summary>
/// Article business logic. Resolves the shard from the <see cref="Continent"/>, opens a
/// short-lived <see cref="ArticleDbContext"/> for it, and maps entities to/from models.
/// No cross-shard queries.
/// </summary>
public class ArticleService : IArticleService
{
    private readonly IArticleDbContextFactory _contextFactory;
    private readonly IArticleCache _cache;

    public ArticleService(IArticleDbContextFactory contextFactory, IArticleCache cache)
    {
        _contextFactory = contextFactory;
        _cache = cache;
    }

    /// <summary>
    /// The Global shard (hosted in North America) is fronted by the ArticleCache: its list covers
    /// the last <see cref="IArticleCache.RecentDays"/> days only, on a hit AND on a miss, so the
    /// answer does not depend on cache state. The 7 continent shards are not cached.
    /// </summary>
    public async Task<IReadOnlyList<ArticleResponse>> ListAsync(Continent continent, CancellationToken ct = default)
    {
        if (continent == Continent.Global)
        {
            var cached = await _cache.GetRecentAsync(ct);
            if (cached is not null)
                return cached;
        }

        await using var db = _contextFactory.Create(continent);

        var query = db.Articles.AsNoTracking();
        if (continent == Continent.Global)
        {
            var since = DateTimeOffset.UtcNow.AddDays(-_cache.RecentDays);
            query = query.Where(a => a.PublishedDate >= since);
        }

        var articles = await query
            .OrderByDescending(a => a.PublishedDate)
            .ToListAsync(ct);

        return articles.Select(ArticleResponse.FromEntity).ToList();
    }

    public async Task<ArticleResponse?> GetAsync(Continent continent, int id, CancellationToken ct = default)
    {
        if (continent == Continent.Global)
        {
            var cached = await _cache.GetAsync(id, ct);
            if (cached is not null)
                return cached;
        }

        await using var db = _contextFactory.Create(continent);

        var article = await db.Articles
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        return article is null ? null : ArticleResponse.FromEntity(article);
    }

    public async Task<ArticleResponse> CreateAsync(CreateArticleRequest request, CancellationToken ct = default)
    {
        await using var db = _contextFactory.Create(request.Continent);

        var article = new Article
        {
            Title = request.Title.Trim(),
            Content = request.Content,
            Author = request.Author.Trim(),
            PublishedDate = request.PublishedDate,
            Continent = request.Continent,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        db.Articles.Add(article);
        await db.SaveChangesAsync(ct);
        if (request.Continent == Continent.Global)
            await _cache.InvalidateAsync(null, ct);

        return ArticleResponse.FromEntity(article);
    }

    public async Task<ArticleResponse?> UpdateAsync(Continent continent, int id, UpdateArticleRequest request, CancellationToken ct = default)
    {
        await using var db = _contextFactory.Create(continent);

        var article = await db.Articles.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (article is null)
            return null;

        article.Title = request.Title.Trim();
        article.Content = request.Content;
        article.Author = request.Author.Trim();
        article.PublishedDate = request.PublishedDate;
        article.Continent = request.Continent;
        article.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        if (continent == Continent.Global)
            await _cache.InvalidateAsync(id, ct);

        return ArticleResponse.FromEntity(article);
    }

    public async Task<bool> DeleteAsync(Continent continent, int id, CancellationToken ct = default)
    {
        await using var db = _contextFactory.Create(continent);

        var article = await db.Articles.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (article is null)
            return false;

        db.Articles.Remove(article);
        await db.SaveChangesAsync(ct);
        if (continent == Continent.Global)
            await _cache.InvalidateAsync(id, ct);
        return true;
    }
}
