using HappyHeadlines.Core.Subscribers.Models;

namespace HappyHeadlines.Core.Newsletters;

/// <summary>NewsletterService's synchronous REST call to SubscriberService ("get subscribers").</summary>
public interface ISubscriberClient
{
    Task<IReadOnlyList<SubscriberResponse>> ListAsync(CancellationToken ct = default);
}
