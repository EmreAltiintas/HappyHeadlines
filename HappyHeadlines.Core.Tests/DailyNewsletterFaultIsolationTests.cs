using HappyHeadlines.Core.Articles.Models;
using HappyHeadlines.Core.Newsletters;
using HappyHeadlines.Core.Subscribers.Models;
using HappyHeadlines.Db;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace HappyHeadlines.Core.Tests;

/// <summary>NewsletterService degrades gracefully when SubscriberService is down / off / circuit-open.</summary>
public class DailyNewsletterFaultIsolationTests
{
    private sealed class FakeArticles : IArticleClient
    {
        public Task<IReadOnlyList<ArticleResponse>> ListAsync(Continent continent, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ArticleResponse>>(new List<ArticleResponse> { new(), new() });
    }

    private sealed class FakeSubscribers : ISubscriberClient
    {
        public Exception? Throw { get; set; }

        public Task<IReadOnlyList<SubscriberResponse>> ListAsync(CancellationToken ct = default)
        {
            if (Throw is not null) throw Throw;
            return Task.FromResult<IReadOnlyList<SubscriberResponse>>(new List<SubscriberResponse>
            {
                new(Guid.NewGuid(), "a@example.com", DateTime.UtcNow),
                new(Guid.NewGuid(), "b@example.com", DateTime.UtcNow)
            });
        }
    }

    private static (DailyNewsletterService Service, FakeSubscribers Subscribers) Create()
    {
        var subscribers = new FakeSubscribers();
        return (new DailyNewsletterService(new FakeArticles(), subscribers, NullLogger<DailyNewsletterService>.Instance), subscribers);
    }

    [Fact]
    public async Task Healthy_subscriberservice_gives_subscriber_count()
    {
        var (service, _) = Create();

        var result = await service.SendAsync(Continent.Europe);

        Assert.Equal(2, result.SubscriberCount);
        Assert.True(result.SubscribersAvailable);
    }

    public static IEnumerable<object[]> Failures() => new[]
    {
        new object[] { new HttpRequestException("503 - toggled off", null, System.Net.HttpStatusCode.ServiceUnavailable) },
        new object[] { new HttpRequestException("connection refused") },
        new object[] { new BrokenCircuitException("circuit open") },
        new object[] { new TimeoutRejectedException("timed out") },
        new object[] { new TaskCanceledException("http timeout") }
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Unavailable_subscriberservice_falls_back_to_zero_subscribers_without_failing(Exception failure)
    {
        var (service, subscribers) = Create();
        subscribers.Throw = failure;

        var result = await service.SendAsync(Continent.Europe);

        Assert.Equal(2, result.ArticleCount);       // the rest of the run still works
        Assert.Equal(0, result.SubscriberCount);
        Assert.False(result.SubscribersAvailable);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed()
    {
        var (service, subscribers) = Create();
        subscribers.Throw = new TaskCanceledException();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => service.SendAsync(Continent.Europe, cts.Token));
    }
}
