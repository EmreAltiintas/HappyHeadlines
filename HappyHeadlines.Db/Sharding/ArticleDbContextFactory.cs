using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HappyHeadlines.Db.Sharding;

/// <summary>
/// Builds (and caches) one set of <see cref="DbContextOptions{ArticleDbContext}"/> per
/// continent from the 8 connection strings in the <c>ArticleShards</c> configuration
/// section. A fresh <see cref="ArticleDbContext"/> is returned on every call so callers
/// can dispose it per unit of work.
/// </summary>
public sealed class ArticleDbContextFactory : IArticleDbContextFactory
{
    public const string ConfigSection = "ArticleShards";

    private readonly Dictionary<Continent, string> _connectionStrings = new();
    private readonly Dictionary<Continent, DbContextOptions<ArticleDbContext>> _options = new();

    public ArticleDbContextFactory(IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigSection);

        foreach (var continent in Enum.GetValues<Continent>())
        {
            var connectionString = section[continent.ToString()];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Missing connection string for shard '{continent}'. " +
                    $"Set configuration key '{ConfigSection}:{continent}' " +
                    $"(environment variable '{ConfigSection}__{continent}').");
            }

            _connectionStrings[continent] = connectionString;
            _options[continent] = new DbContextOptionsBuilder<ArticleDbContext>()
                .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null))
                .Options;
        }
    }

    public IReadOnlyDictionary<Continent, string> ConnectionStrings => _connectionStrings;

    public ArticleDbContext Create(Continent continent)
    {
        if (!_options.TryGetValue(continent, out var options))
            throw new ArgumentOutOfRangeException(nameof(continent), continent, "Unknown continent / shard.");

        return new ArticleDbContext(options);
    }
}
