using System.ComponentModel.DataAnnotations;

namespace HappyHeadlines.Core.Subscribers.Models;

public class SubscribeRequest
{
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; set; } = string.Empty;
}

public record SubscriberResponse(Guid Id, string Email, DateTime SubscribedAt);

/// <param name="Subscriber">The stored subscriber.</param>
/// <param name="Created">False when the email was already subscribed (idempotent subscribe).</param>
/// <param name="WelcomeMailQueued">
/// False when SubscriberQueue was unreachable: the subscriber is still stored, only the welcome
/// mail is skipped (SubscriberService does not fail because the queue is down).
/// </param>
public record SubscribeResult(SubscriberResponse Subscriber, bool Created, bool WelcomeMailQueued);
