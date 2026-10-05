namespace HappyHeadlines.Db.Sharding;

/// <summary>
/// The Z-axis shard router. Given a <see cref="Continent"/> it returns an
/// <see cref="ArticleDbContext"/> wired to that continent's own database.
/// </summary>
public interface IArticleDbContextFactory
{
    ArticleDbContext Create(Continent continent);

    /// <summary>All configured shards, exposed for the migrate-all-shards startup step.</summary>
    IReadOnlyDictionary<Continent, string> ConnectionStrings { get; }
}
