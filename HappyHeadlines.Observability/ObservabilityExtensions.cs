using MassTransit.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HappyHeadlines.Observability;

/// <summary>
/// The one, central place logging + distributed tracing are wired up for every HappyHeadlines
/// service. <see cref="AddHappyHeadlinesObservability"/> is called identically from every
/// service's Program.cs (Gateway, ArticleService, CommentService, ProfanityService, DraftService,
/// PublisherService, NewsletterService)
/// so a request that crosses services (Gateway -> ArticleService, CommentService ->
/// ProfanityService, ...) shows up as one correlated trace instead of N unrelated logs.
///
/// The OTLP destination is never hardcoded here - <c>UseOtlpExporter()</c> reads
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> / <c>OTEL_EXPORTER_OTLP_PROTOCOL</c> from the environment,
/// set once per service in docker-compose.yml. That is the actual point of centralizing this:
/// same call, same code, same destination, everywhere.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>Must match <c>CacheMetrics.MeterName</c> in HappyHeadlines.Core (Observability does not reference Core).</summary>
    private const string CacheMeterName = "HappyHeadlines.Cache";

    /// <summary>Meter for circuit-breaker events; public so the service that owns the breaker uses the same name.</summary>
    public const string ResilienceMeterName = "HappyHeadlines.Resilience";

    public static IHostApplicationBuilder AddHappyHeadlinesObservability(
        this IHostApplicationBuilder builder, string serviceName, bool exposePrometheusMetrics = false)
    {
        var instanceId = Environment.GetEnvironmentVariable("INSTANCE_ID") ?? Environment.MachineName;

        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeScopes = true;
            o.IncludeFormattedMessage = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceInstanceId: instanceId))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                // MassTransit publishes its own spans (publish/consume) under this
                // ActivitySource. Without this, a trace that crosses ArticleQueue would break
                // into disconnected pieces at the queue boundary instead of staying one trace.
                .AddSource(DiagnosticHeaders.DefaultListenerName))
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation()
                 .AddHttpClientInstrumentation()
                 // ArticleCache / CommentCache hit + miss counters (Meter defined in Core's CacheMetrics).
                 .AddMeter(CacheMeterName)
                 // Circuit-breaker events of CommentService (see ProfanityCircuitMetrics in its Program.cs).
                 .AddMeter(ResilienceMeterName);

                // Opt-in: the cache-owning services also expose /metrics for Prometheus to scrape
                // (see MapHappyHeadlinesMetrics). OTLP -> Aspire keeps working side by side.
                if (exposePrometheusMetrics)
                    m.AddPrometheusExporter();
            })
            .UseOtlpExporter();

        return builder;
    }

    /// <summary>Maps <c>GET /metrics</c> (Prometheus text format). Needs <c>exposePrometheusMetrics: true</c>.</summary>
    public static IEndpointRouteBuilder MapHappyHeadlinesMetrics(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPrometheusScrapingEndpoint();
        return endpoints;
    }
}
