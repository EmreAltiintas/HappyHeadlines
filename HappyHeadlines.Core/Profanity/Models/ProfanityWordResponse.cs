using HappyHeadlines.Db.Entities;

namespace HappyHeadlines.Core.Profanity.Models;

/// <summary>Output model for a stored profanity word. Never exposes the entity directly.</summary>
public class ProfanityWordResponse
{
    public int Id { get; set; }
    public string Word { get; set; } = string.Empty;

    public static ProfanityWordResponse FromEntity(ProfanityWord w) => new()
    {
        Id = w.Id,
        Word = w.Word
    };
}
