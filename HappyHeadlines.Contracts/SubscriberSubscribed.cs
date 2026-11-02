namespace HappyHeadlines.Contracts;

/// <summary>
/// Published by SubscriberService onto SubscriberQueue when someone subscribes. NewsletterService
/// consumes it and sends the welcome mail. Wire format only - one definition shared by producer
/// and consumer, never redeclared per service.
/// </summary>
public record SubscriberSubscribed(
    Guid SubscriberId,
    string Email,
    DateTime SubscribedAt);
