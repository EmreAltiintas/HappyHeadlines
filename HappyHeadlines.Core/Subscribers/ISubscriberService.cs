using HappyHeadlines.Core.Subscribers.Models;

namespace HappyHeadlines.Core.Subscribers;

public interface ISubscriberService
{
    /// <summary>Stores the subscriber (SubscriberDatabase) and publishes to SubscriberQueue.</summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken ct = default);

    /// <summary>Removes the subscriber from SubscriberDatabase. False when the email was not subscribed.</summary>
    Task<bool> UnsubscribeAsync(string email, CancellationToken ct = default);

    Task<IReadOnlyList<SubscriberResponse>> ListAsync(CancellationToken ct = default);
}
