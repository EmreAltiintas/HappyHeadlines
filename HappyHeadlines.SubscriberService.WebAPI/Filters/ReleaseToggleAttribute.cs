using HappyHeadlines.Core.Subscribers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace HappyHeadlines.SubscriberService.WebAPI.Filters;

/// <summary>
/// Release toggle gate. Reads <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> on every
/// request, so a change to the feature-flag file takes effect on the next call without a
/// redeploy or restart. When the operation is switched off it answers 503 + Retry-After with a
/// clear message (before any database or queue work happens) - consumers such as
/// NewsletterService treat that like any other "SubscriberService unavailable".
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ReleaseToggleAttribute : Attribute, IAsyncActionFilter
{
    private readonly SubscriberOperation _operation;

    public ReleaseToggleAttribute(SubscriberOperation operation) => _operation = operation;

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var flags = context.HttpContext.RequestServices
            .GetRequiredService<IOptionsMonitor<SubscriberFeatureFlags>>().CurrentValue;

        if (flags.IsEnabled(_operation))
            return next();

        context.HttpContext.Response.Headers["Retry-After"] = "30";
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "SubscriberService feature is switched off",
            Detail = $"The release toggle for '{_operation}' is currently disabled. Try again later."
        })
        { StatusCode = StatusCodes.Status503ServiceUnavailable };

        return Task.CompletedTask;
    }
}
