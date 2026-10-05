using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HappyHeadlines.Observability;

/// <summary>
/// Shared request-logging middleware, reused by every HappyHeadlines service so "what gets
/// logged per request" is defined once instead of N times. Emits exactly one structured log
/// call per request - method, path, status code, duration and instance id - at Information,
/// or Warning when the response is a 5xx. Never logs request/response bodies (see the README's
/// observability section for the full "what to log, and when" guidelines).
/// </summary>
public static class RequestLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseHappyHeadlinesRequestLogging(this IApplicationBuilder app)
    {
        var instanceId = Environment.GetEnvironmentVariable("INSTANCE_ID") ?? Environment.MachineName;
        var logger = app.ApplicationServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("HappyHeadlines.Observability.RequestLogging");

        return app.Use(async (context, next) =>
        {
            var stopwatch = Stopwatch.StartNew();
            await next(context);
            stopwatch.Stop();

            var statusCode = context.Response.StatusCode;
            var level = statusCode >= 500 ? LogLevel.Warning : LogLevel.Information;

            logger.Log(
                level,
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds}ms (instance {InstanceId})",
                context.Request.Method,
                context.Request.Path.Value,
                statusCode,
                stopwatch.Elapsed.TotalMilliseconds,
                instanceId);
        });
    }
}
