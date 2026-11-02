using HappyHeadlines.Core.Subscribers;
using HappyHeadlines.SubscriberService.WebAPI.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HappyHeadlines.Core.Tests;

/// <summary>Release toggle: whole-service and per-operation switches, and reload without restart.</summary>
public class SubscriberReleaseToggleTests
{
    [Theory]
    [InlineData(true, true, SubscriberOperation.Subscribe, true)]
    [InlineData(false, true, SubscriberOperation.Subscribe, false)]   // master switch off => everything off
    [InlineData(false, true, SubscriberOperation.List, false)]
    [InlineData(true, false, SubscriberOperation.Subscribe, false)]   // partial: only Subscribe off
    [InlineData(true, false, SubscriberOperation.Unsubscribe, true)]
    [InlineData(true, false, SubscriberOperation.List, true)]
    public void Flags_combine_master_and_per_operation_switches(
        bool enabled, bool subscribe, SubscriberOperation operation, bool expected)
    {
        var flags = new SubscriberFeatureFlags { Enabled = enabled, Subscribe = subscribe };

        Assert.Equal(expected, flags.IsEnabled(operation));
    }

    private static string WriteFlags(string path, bool enabled) =>
        Write(path, $$"""{ "FeatureFlags": { "SubscriberService": { "Enabled": {{enabled.ToString().ToLowerInvariant()}} } } }""");

    private static string Write(string path, string json)
    {
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task Filter_returns_503_when_off_and_passes_through_after_the_file_changes_without_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"featureflags-{Guid.NewGuid():N}.json");
        WriteFlags(path, enabled: false);
        try
        {
            var config = new ConfigurationBuilder().AddJsonFile(path, optional: false, reloadOnChange: true).Build();
            var services = new ServiceCollection();
            services.Configure<SubscriberFeatureFlags>(config.GetSection(SubscriberFeatureFlags.SectionName));
            using var provider = services.BuildServiceProvider();

            // Same running "process" (same provider/monitor) for both calls.
            var off = await Run(provider);
            Assert.Equal(503, off.StatusCode);

            WriteFlags(path, enabled: true);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && !provider.GetRequiredService<IOptionsMonitor<SubscriberFeatureFlags>>().CurrentValue.Enabled)
                await Task.Delay(100);

            var on = await Run(provider);
            Assert.Null(on.Result);          // filter let the action run
            Assert.True(on.NextCalled);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<(int? StatusCode, IActionResult? Result, bool NextCalled)> Run(IServiceProvider provider)
    {
        var http = new DefaultHttpContext { RequestServices = provider };
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(),
            new Dictionary<string, object?>(), controller: new object());
        var nextCalled = false;

        await new ReleaseToggleAttribute(SubscriberOperation.Subscribe).OnActionExecutionAsync(executing, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        });

        return ((executing.Result as ObjectResult)?.StatusCode, executing.Result, nextCalled);
    }
}
