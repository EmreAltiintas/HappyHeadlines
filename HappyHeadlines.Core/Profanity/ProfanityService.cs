using HappyHeadlines.Core.Profanity.Models;
using HappyHeadlines.Db;
using HappyHeadlines.Db.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyHeadlines.Core.Profanity;

/// <summary>
/// Profanity-filtering logic. Reads the banned-word list from <see cref="ProfanityDbContext"/>
/// (this service's own database) and does a case-insensitive substring match.
/// </summary>
public class ProfanityService : IProfanityService
{
    private readonly ProfanityDbContext _db;

    public ProfanityService(ProfanityDbContext db)
    {
        _db = db;
    }

    public async Task<ProfanityCheckResponse> CheckAsync(string text, CancellationToken ct = default)
    {
        var haystack = (text ?? string.Empty).ToLowerInvariant();

        var words = await _db.ProfanityWords
            .AsNoTracking()
            .Select(w => w.Word)
            .ToListAsync(ct);

        var matched = words
            .Where(w => haystack.Contains(w.ToLowerInvariant()))
            .Distinct()
            .OrderBy(w => w)
            .ToList();

        return new ProfanityCheckResponse
        {
            ContainsProfanity = matched.Count > 0,
            MatchedWords = matched
        };
    }

    public async Task<IReadOnlyList<ProfanityWordResponse>> ListWordsAsync(CancellationToken ct = default)
    {
        var words = await _db.ProfanityWords
            .AsNoTracking()
            .OrderBy(w => w.Word)
            .ToListAsync(ct);

        return words.Select(ProfanityWordResponse.FromEntity).ToList();
    }

    public async Task<ProfanityWordResponse?> AddWordAsync(string word, CancellationToken ct = default)
    {
        var normalized = word.Trim().ToLowerInvariant();

        var exists = await _db.ProfanityWords.AnyAsync(w => w.Word == normalized, ct);
        if (exists)
            return null;

        var entity = new ProfanityWord { Word = normalized };
        _db.ProfanityWords.Add(entity);
        await _db.SaveChangesAsync(ct);

        return ProfanityWordResponse.FromEntity(entity);
    }

    public async Task<bool> DeleteWordAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.ProfanityWords.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (entity is null)
            return false;

        _db.ProfanityWords.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
