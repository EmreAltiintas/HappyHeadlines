using HappyHeadlines.Core.Comments;

namespace HappyHeadlines.CommentService.WebAPI;

/// <summary>
/// Background re-moderation: every few seconds asks <see cref="ICommentService"/> to re-check comments
/// that were stored as Pending while ProfanityService was down. While the circuit is still open the
/// check fails fast and nothing changes; once ProfanityService is back, pending comments are approved
/// or rejected. Runs inside CommentService only, so it adds no coupling to other swimlanes.
/// </summary>
public class PendingCommentModerator : BackgroundService
{
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PendingCommentModerator> _logger;
    private readonly TimeSpan Interval;

    public PendingCommentModerator(IServiceScopeFactory scopes, ILogger<PendingCommentModerator> logger, IConfiguration configuration)
    {
        _scopes = scopes;
        _logger = logger;
        // PendingModeration:IntervalSeconds (env PendingModeration__IntervalSeconds), default 15.
        Interval = TimeSpan.FromSeconds(configuration.GetValue("PendingModeration:IntervalSeconds", 15.0));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var comments = scope.ServiceProvider.GetRequiredService<ICommentService>();
                await comments.ReModeratePendingAsync(BatchSize, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Database not ready yet, etc. - try again at the next tick.
                _logger.LogWarning(ex, "Pending comment re-moderation failed - will retry in {Seconds}s.", Interval.TotalSeconds);
            }
        }
    }
}
