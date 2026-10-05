namespace HappyHeadlines.DraftService.WebAPI.Middleware;

/// <summary>Identity of this running instance, for the X-axis load-balancing demo.</summary>
public static class InstanceInfo
{
    public static string InstanceId { get; } =
        Environment.GetEnvironmentVariable("INSTANCE_ID")
        ?? Environment.MachineName;
}
