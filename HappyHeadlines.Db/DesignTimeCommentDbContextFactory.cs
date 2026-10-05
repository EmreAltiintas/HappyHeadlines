using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HappyHeadlines.Db;

/// <summary>
/// Used only by the EF Core CLI (<c>dotnet ef migrations add --context CommentDbContext</c> /
/// <c>database update --context CommentDbContext</c>). Override the connection string with the
/// <c>EF_CONNECTION_STRING</c> environment variable when applying migrations from the command line.
/// </summary>
public sealed class DesignTimeCommentDbContextFactory : IDesignTimeDbContextFactory<CommentDbContext>
{
    public CommentDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("EF_CONNECTION_STRING")
            ?? "Server=localhost,1433;Database=HappyHeadlines_Comments_DesignTime;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<CommentDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new CommentDbContext(options);
    }
}
