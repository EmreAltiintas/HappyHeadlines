using HappyHeadlines.Core.Drafts.Models;
using HappyHeadlines.Db;
using HappyHeadlines.Db.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HappyHeadlines.Core.Drafts;

/// <summary>
/// Draft logic: plain CRUD against <see cref="DraftDbContext"/>, no repository abstraction -
/// same convention as <see cref="Comments.CommentService"/> and <see cref="Profanity.ProfanityService"/>.
/// Business events (created/updated/deleted) are logged at Information with structured
/// parameters - id and author only, never the title or content (see README's "what to log"
/// guidelines: never log full draft bodies at Information level).
/// </summary>
public class DraftService : IDraftService
{
    private readonly DraftDbContext _db;
    private readonly ILogger<DraftService> _logger;

    public DraftService(DraftDbContext db, ILogger<DraftService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DraftResponse>> ListAsync(string? author, CancellationToken ct = default)
    {
        var query = _db.Drafts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(author))
        {
            query = query.Where(d => d.Author == author);
        }

        var drafts = await query
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        return drafts.Select(DraftResponse.FromEntity).ToList();
    }

    public async Task<DraftResponse?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var draft = await _db.Drafts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        return draft is null ? null : DraftResponse.FromEntity(draft);
    }

    public async Task<DraftResponse> CreateAsync(CreateDraftRequest request, CancellationToken ct = default)
    {
        var draft = new Draft
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Content = request.Content,
            Author = request.Author.Trim(),
            Continent = request.Continent,
            CreatedAt = DateTime.UtcNow
        };

        _db.Drafts.Add(draft);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Draft {DraftId} created by {Author}.", draft.Id, draft.Author);

        return DraftResponse.FromEntity(draft);
    }

    public async Task<DraftResponse?> UpdateAsync(Guid id, UpdateDraftRequest request, CancellationToken ct = default)
    {
        var draft = await _db.Drafts.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (draft is null)
        {
            return null;
        }

        draft.Title = request.Title.Trim();
        draft.Content = request.Content;
        draft.Author = request.Author.Trim();
        draft.Continent = request.Continent;
        draft.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Draft {DraftId} updated by {Author}.", draft.Id, draft.Author);

        return DraftResponse.FromEntity(draft);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var draft = await _db.Drafts.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (draft is null)
        {
            return false;
        }

        _db.Drafts.Remove(draft);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Draft {DraftId} deleted.", id);

        return true;
    }
}
