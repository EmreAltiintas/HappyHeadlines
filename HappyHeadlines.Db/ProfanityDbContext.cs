using HappyHeadlines.Db.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyHeadlines.Db;

/// <summary>
/// EF Core context for the ProfanityService. Backed by its own database
/// (<c>HappyHeadlines_Profanity</c>) – never shares a database, schema or connection pool
/// with <see cref="ArticleDbContext"/> or <see cref="CommentDbContext"/>.
/// The word list is seeded into the schema via <see cref="ModelBuilder"/> data so a fresh
/// migration brings a usable starter list with it.
/// </summary>
public class ProfanityDbContext : DbContext
{
    /// <summary>Starter word list baked into the InitialCreate migration.</summary>
    public static readonly string[] StarterWords =
    {
        "damn", "hell", "crap", "bloody", "arse", "bugger",
        "shit", "piss", "dick", "bastard", "asshole", "bollocks"
    };

    public ProfanityDbContext(DbContextOptions<ProfanityDbContext> options) : base(options)
    {
    }

    public DbSet<ProfanityWord> ProfanityWords => Set<ProfanityWord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var word = modelBuilder.Entity<ProfanityWord>();

        word.ToTable("ProfanityWords");
        word.HasKey(w => w.Id);

        word.Property(w => w.Word).IsRequired().HasMaxLength(100);
        word.HasIndex(w => w.Word).IsUnique();

        // Seed the starter list. Fixed ids keep the migration deterministic.
        word.HasData(StarterWords.Select((w, i) => new ProfanityWord { Id = i + 1, Word = w }));
    }
}
