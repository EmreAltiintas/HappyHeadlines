using HappyHeadlines.Core.Profanity.Models;

namespace HappyHeadlines.Core.Profanity;

/// <summary>
/// Profanity-filtering business logic. Runs inside HappyHeadlines.ProfanityService.WebAPI
/// against that service's own database; nothing else shares this code path.
/// </summary>
public interface IProfanityService
{
    /// <summary>Check whether <paramref name="text"/> contains any banned word.</summary>
    Task<ProfanityCheckResponse> CheckAsync(string text, CancellationToken ct = default);

    /// <summary>List every word currently in the profanity database.</summary>
    Task<IReadOnlyList<ProfanityWordResponse>> ListWordsAsync(CancellationToken ct = default);

    /// <summary>Add a word to the list. Returns null when the word already exists.</summary>
    Task<ProfanityWordResponse?> AddWordAsync(string word, CancellationToken ct = default);

    /// <summary>Remove a word by id. Returns false when it does not exist.</summary>
    Task<bool> DeleteWordAsync(int id, CancellationToken ct = default);
}
