namespace HappyHeadlines.CommentService.WebAPI.Middleware;

/// <summary>Identity of this running instance (mirrors the ArticleService.WebAPI convention).</summary>
public static class InstanceInfo
{
    public static string InstanceId { get; } =
        Environment.GetEnvironmentVariable("INSTANCE_ID")
        ?? Environment.MachineName;
}
