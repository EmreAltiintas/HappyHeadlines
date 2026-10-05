using HappyHeadlines.Db.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyHeadlines.Db;

/// <summary>
/// One DbContext type, used against every shard. The connection string it is handed
/// decides which continent database it talks to; the schema is identical everywhere.
/// </summary>
public class ArticleDbContext : DbContext
{
    public ArticleDbContext(DbContextOptions<ArticleDbContext> options) : base(options)
    {
    }

    public DbSet<Article> Articles => Set<Article>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var article = modelBuilder.Entity<Article>();

        article.ToTable("Articles");
        article.HasKey(a => a.Id);

        article.Property(a => a.Title).IsRequired().HasMaxLength(300);
        article.Property(a => a.Content).IsRequired();
        article.Property(a => a.Author).IsRequired().HasMaxLength(200);
        article.Property(a => a.PublishedDate).IsRequired();
        article.Property(a => a.Continent).IsRequired().HasConversion<int>();
        article.Property(a => a.CreatedAtUtc).IsRequired();

        article.HasIndex(a => a.PublishedDate);
    }
}
