using HappyHeadlines.Db.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyHeadlines.Db;

/// <summary>
/// EF Core context for SubscriberService. Backed by its own database
/// (<c>HappyHeadlines_Subscribers</c>) on its own SQL Server container - never shares a
/// database, schema or connection pool with any other service's context.
/// </summary>
public class SubscriberDbContext : DbContext
{
    public SubscriberDbContext(DbContextOptions<SubscriberDbContext> options) : base(options)
    {
    }

    public DbSet<Subscriber> Subscribers => Set<Subscriber>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var subscriber = modelBuilder.Entity<Subscriber>();

        subscriber.ToTable("Subscribers");
        subscriber.HasKey(s => s.Id);

        subscriber.Property(s => s.Email).IsRequired().HasMaxLength(320);
        subscriber.Property(s => s.SubscribedAt).IsRequired();

        subscriber.HasIndex(s => s.Email).IsUnique();
    }
}
