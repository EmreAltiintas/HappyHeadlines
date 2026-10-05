using HappyHeadlines.Db.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyHeadlines.Db;

/// <summary>
/// EF Core context for the CommentService. Backed by its own database
/// (<c>HappyHeadlines_Comments</c>) – never shares a database, schema or connection pool
/// with <see cref="ArticleDbContext"/> or <see cref="ProfanityDbContext"/>.
/// </summary>
public class CommentDbContext : DbContext
{
    public CommentDbContext(DbContextOptions<CommentDbContext> options) : base(options)
    {
    }

    public DbSet<Comment> Comments => Set<Comment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var comment = modelBuilder.Entity<Comment>();

        comment.ToTable("Comments");
        comment.HasKey(c => c.Id);

        comment.Property(c => c.ArticleId).IsRequired();
        comment.Property(c => c.Author).IsRequired().HasMaxLength(200);
        comment.Property(c => c.Text).IsRequired().HasMaxLength(4000);
        comment.Property(c => c.PostedDate).IsRequired();
        comment.Property(c => c.ModerationStatus).IsRequired().HasConversion<int>();

        comment.HasIndex(c => c.ArticleId);
    }
}
