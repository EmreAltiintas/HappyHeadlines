using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HappyHeadlines.Db.Sharding;

/// <summary>
/// Applies EF Core migrations to every one of the 8 shard databases on startup.
/// Retries for a while because the SQL Server container usually is not accepting
/// connections yet when the API process starts.
/// </summary>
public sealed class ShardMigrator
{
    private readonly IArticleDbContextFactory _contextFactory;
    private readonly ILogger<ShardMigrator> _logger;

    public ShardMigrator(IArticleDbContextFactory contextFactory, ILogger<ShardMigrator> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task MigrateAllAsync(CancellationToken ct = default)
    {
        foreach (var continent in Enum.GetValues<Continent>())
        {
            await MigrateOneAsync(continent, ct);
        }
    }

    private async Task MigrateOneAsync(Continent continent, CancellationToken ct)
    {
        const int maxAttempts = 20;
        var delay = TimeSpan.FromSeconds(3);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var db = _contextFactory.Create(continent);
                await db.Database.MigrateAsync(ct);
                _logger.LogInformation("Shard {Continent}: migrations applied.", continent);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(
                    "Shard {Continent}: migration attempt {Attempt}/{Max} failed ({Message}). Retrying in {Delay}s.",
                    continent, attempt, maxAttempts, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }

        // Final attempt outside the catch so a lasting failure crashes startup.
        await using var final = _contextFactory.Create(continent);
        await final.Database.MigrateAsync(ct);
        _logger.LogInformation("Shard {Continent}: migrations applied.", continent);
    }
}
