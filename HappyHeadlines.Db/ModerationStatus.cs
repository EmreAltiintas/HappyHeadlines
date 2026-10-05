namespace HappyHeadlines.Db;

/// <summary>
/// Where a comment sits in moderation. Lives in the Db project next to
/// <see cref="Continent"/> because the <see cref="Entities.Comment"/> entity depends on it
/// and Core builds on top of Db.
/// </summary>
public enum ModerationStatus
{
    /// <summary>Awaiting a moderation decision (used only by the "store as pending" degrade strategy).</summary>
    Pending = 0,

    /// <summary>Passed the profanity check and is publicly visible.</summary>
    Approved = 1,

    /// <summary>Rejected because the profanity check found banned words.</summary>
    Rejected = 2
}
