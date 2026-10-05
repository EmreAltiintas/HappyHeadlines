using HappyHeadlines.Db.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyHeadlines.Db;

/// <summary>
/// EF Core context for DraftService. Backed by its own database
/// (<c>HappyHeadlines_Drafts</c>) - never shares a database, schema or connection pool with
/// <see cref="ArticleDbContext"/>, <see cref="CommentDbContext"/> or <see cref="ProfanityDbContext"/>.
/// </summary>
public class DraftDbContext : DbContext
{
    public DraftDbContext(DbContextOptions<DraftDbContext> options) : base(options)
    {
    }

    public DbSet<Draft> Drafts => Set<Draft>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var draft = modelBuilder.Entity<Draft>();

        draft.ToTable("Drafts");
        draft.HasKey(d => d.Id);

        draft.Property(d => d.Title).IsRequired().HasMaxLength(300);
        draft.Property(d => d.Content).IsRequired();
        draft.Property(d => d.Author).IsRequired().HasMaxLength(200);
        draft.Property(d => d.Continent).HasConversion<int?>();
        draft.Property(d => d.CreatedAt).IsRequired();

        draft.HasIndex(d => d.Author);
    }
}
