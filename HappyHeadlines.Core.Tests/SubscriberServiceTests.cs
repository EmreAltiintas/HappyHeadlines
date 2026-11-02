using System.Reflection;
using HappyHeadlines.Contracts;
using HappyHeadlines.Core.Subscribers;
using HappyHeadlines.Core.Subscribers.Models;
using HappyHeadlines.Db;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SubscriberSvc = HappyHeadlines.Core.Subscribers.SubscriberService;

namespace HappyHeadlines.Core.Tests;

/// <summary>Subscribe / unsubscribe against an in-memory SubscriberDbContext + a recording fake queue.</summary>
public class SubscriberServiceTests
{
    /// <summary>IPublishEndpoint stand-in (DispatchProxy: no mocking library needed).</summary>
    public class FakeQueue : DispatchProxy
    {
        public List<object> Published { get; } = new();
        public bool Down { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == "Publish" && args is { Length: > 0 })
            {
                if (Down)
                    return Task.FromException(new InvalidOperationException("SubscriberQueue is down"));
                Published.Add(args[0]!);
            }
            return targetMethod.ReturnType == typeof(Task) ? Task.CompletedTask : null;
        }
    }

    private static (SubscriberSvc Service, SubscriberDbContext Db, FakeQueue Queue) Create()
    {
        var db = new SubscriberDbContext(new DbContextOptionsBuilder<SubscriberDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var proxy = DispatchProxy.Create<IPublishEndpoint, FakeQueue>();
        var queue = (FakeQueue)(object)proxy;
        return (new SubscriberSvc(db, proxy, NullLogger<SubscriberSvc>.Instance), db, queue);
    }

    [Fact]
    public async Task Subscribe_stores_the_subscriber_and_publishes_to_the_queue()
    {
        var (service, db, queue) = Create();

        var result = await service.SubscribeAsync(new SubscribeRequest { Email = "  Ann@Example.com " });

        Assert.True(result.Created);
        Assert.True(result.WelcomeMailQueued);
        Assert.Equal("ann@example.com", (await db.Subscribers.SingleAsync()).Email);
        var message = Assert.IsType<SubscriberSubscribed>(Assert.Single(queue.Published));
        Assert.Equal(result.Subscriber.Id, message.SubscriberId);
        Assert.Equal("ann@example.com", message.Email);
    }

    [Fact]
    public async Task Subscribing_twice_is_idempotent_and_does_not_publish_again()
    {
        var (service, db, queue) = Create();
        await service.SubscribeAsync(new SubscribeRequest { Email = "ann@example.com" });

        var second = await service.SubscribeAsync(new SubscribeRequest { Email = "ANN@example.com" });

        Assert.False(second.Created);
        Assert.Equal(1, await db.Subscribers.CountAsync());
        Assert.Single(queue.Published);
    }

    [Fact]
    public async Task When_the_queue_is_down_the_subscriber_is_still_stored()
    {
        var (service, db, queue) = Create();
        queue.Down = true;

        var result = await service.SubscribeAsync(new SubscribeRequest { Email = "ann@example.com" });

        Assert.True(result.Created);
        Assert.False(result.WelcomeMailQueued);
        Assert.Equal(1, await db.Subscribers.CountAsync());
        Assert.Empty(queue.Published);
    }

    [Fact]
    public async Task Unsubscribe_removes_the_subscriber_and_reports_unknown_emails()
    {
        var (service, db, _) = Create();
        await service.SubscribeAsync(new SubscribeRequest { Email = "ann@example.com" });

        Assert.True(await service.UnsubscribeAsync("Ann@example.com"));
        Assert.Empty(db.Subscribers);
        Assert.False(await service.UnsubscribeAsync("ann@example.com"));
    }

    [Fact]
    public async Task List_returns_all_subscribers()
    {
        var (service, _, _) = Create();
        await service.SubscribeAsync(new SubscribeRequest { Email = "a@example.com" });
        await service.SubscribeAsync(new SubscribeRequest { Email = "b@example.com" });

        var list = await service.ListAsync();

        Assert.Equal(new[] { "a@example.com", "b@example.com" }, list.Select(s => s.Email));
    }
}
