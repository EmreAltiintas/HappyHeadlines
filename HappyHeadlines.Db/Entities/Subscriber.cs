namespace HappyHeadlines.Db.Entities;

/// <summary>
/// Database entity for a newsletter subscriber. Lives in SubscriberService's own database
/// (<see cref="SubscriberDbContext"/>, "SubscriberDatabase" in the C4 diagram) - owned solely by
/// SubscriberService, never read directly by another service.
/// </summary>
public class Subscriber
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public DateTime SubscribedAt { get; set; }
}
