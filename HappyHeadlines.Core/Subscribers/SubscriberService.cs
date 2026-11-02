using HappyHeadlines.Contracts;
using HappyHeadlines.Core.Subscribers.Models;
using HappyHeadlines.Db;
using HappyHeadlines.Db.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HappyHeadlines.Core.Subscribers;

/// <summary>
/// SubscriberService business logic. Subscribe = store in SubscriberDatabase, then put the new
/// subscriber on SubscriberQueue. The database write is the source of truth: if the queue is down
/// the subscriber is still stored and the caller is told the welcome mail was not queued.
/// </summary>
public class SubscriberService : ISubscriberService
{
    /// <summary>Upper bound for the publish, so a dead broker cannot hang the request.</summary>
    public static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(3);

    private readonly SubscriberDbContext _db;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<SubscriberService> _logger;

    public SubscriberService(SubscriberDbContext db, IPublishEndpoint publishEndpoint, ILogger<SubscriberService> logger)
    {
        _db = db;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken ct = default)
    {
        var email = Normalize(request.Email);

        var existing = await _db.Subscribers.AsNoTracking().FirstOrDefaultAsync(s => s.Email == email, ct);
        if (existing is not null)
            return new SubscribeResult(ToResponse(existing), Created: false, WelcomeMailQueued: false);

        var subscriber = new Subscriber { Id = Guid.NewGuid(), Email = email, SubscribedAt = DateTime.UtcNow };
        _db.Subscribers.Add(subscriber);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Subscriber {happyheadlines.subscriber_id} stored", subscriber.Id);

        var queued = await TryPublishAsync(subscriber, ct);
        return new SubscribeResult(ToResponse(subscriber), Created: true, WelcomeMailQueued: queued);
    }

    public async Task<bool> UnsubscribeAsync(string email, CancellationToken ct = default)
    {
        var normalized = Normalize(email);
        var subscriber = await _db.Subscribers.FirstOrDefaultAsync(s => s.Email == normalized, ct);
        if (subscriber is null)
            return false;

        _db.Subscribers.Remove(subscriber);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Subscriber {happyheadlines.subscriber_id} removed", subscriber.Id);
        return true;
    }

    public async Task<IReadOnlyList<SubscriberResponse>> ListAsync(CancellationToken ct = default)
    {
        var subscribers = await _db.Subscribers.AsNoTracking().OrderBy(s => s.SubscribedAt).ToListAsync(ct);
        return subscribers.Select(ToResponse).ToList();
    }

    private async Task<bool> TryPublishAsync(Subscriber subscriber, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PublishTimeout);

        try
        {
            await _publishEndpoint.Publish(
                new SubscriberSubscribed(subscriber.Id, subscriber.Email, subscriber.SubscribedAt), timeout.Token);
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Fault isolation: SubscriberQueue being down must not fail the subscribe itself.
            _logger.LogWarning(ex,
                "Subscriber {happyheadlines.subscriber_id} stored but SubscriberQueue publish failed - welcome mail skipped",
                subscriber.Id);
            return false;
        }
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static SubscriberResponse ToResponse(Subscriber s) => new(s.Id, s.Email, s.SubscribedAt);
}
