namespace HappyHeadlines.NewsletterService.WebAPI.Middleware;

/// <summary>Identity of this running instance.</summary>
public static class InstanceInfo
{
    public static string InstanceId { get; } =
        Environment.GetEnvironmentVariable("INSTANCE_ID")
        ?? Environment.MachineName;
}
