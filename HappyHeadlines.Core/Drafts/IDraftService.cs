using HappyHeadlines.Core.Drafts.Models;

namespace HappyHeadlines.Core.Drafts;

/// <summary>
/// Draft business logic. Runs inside HappyHeadlines.DraftService.WebAPI against that
/// service's own database.
/// </summary>
public interface IDraftService
{
    /// <summary>All drafts, optionally filtered to one author, newest first.</summary>
    Task<IReadOnlyList<DraftResponse>> ListAsync(string? author, CancellationToken ct = default);

    /// <summary>A single draft, or null if it does not exist.</summary>
    Task<DraftResponse?> GetAsync(Guid id, CancellationToken ct = default);

    Task<DraftResponse> CreateAsync(CreateDraftRequest request, CancellationToken ct = default);

    /// <summary>Full replace of an existing draft, or null if it does not exist.</summary>
    Task<DraftResponse?> UpdateAsync(Guid id, UpdateDraftRequest request, CancellationToken ct = default);

    /// <summary>True if a draft was deleted; false if it did not exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
