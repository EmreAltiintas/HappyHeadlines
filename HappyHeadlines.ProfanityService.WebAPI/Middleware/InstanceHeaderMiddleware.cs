namespace HappyHeadlines.ProfanityService.WebAPI.Middleware;

/// <summary>Stamps every response with the serving instance id (mirrors ArticleService.WebAPI).</summary>
public sealed class InstanceHeaderMiddleware
{
    private readonly RequestDelegate _next;

    public InstanceHeaderMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Instance-Id"] = InstanceInfo.InstanceId;
            return Task.CompletedTask;
        });
        return _next(context);
    }
}
