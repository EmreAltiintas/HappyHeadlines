using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HappyHeadlines.Db;

/// <summary>
/// Used only by the EF Core CLI (<c>dotnet ef migrations add</c> / <c>database update</c>).
/// The schema is identical across shards, so any valid SQL Server connection string works;
/// override it with the <c>EF_CONNECTION_STRING</c> environment variable when applying
/// migrations to a specific shard from the command line.
/// </summary>
public sealed class DesignTimeArticleDbContextFactory : IDesignTimeDbContextFactory<ArticleDbContext>
{
    public ArticleDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("EF_CONNECTION_STRING")
            ?? "Server=localhost,1433;Database=HappyHeadlines_DesignTime;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<ArticleDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ArticleDbContext(options);
    }
}
