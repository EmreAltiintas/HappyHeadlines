using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HappyHeadlines.Db;

/// <summary>
/// Used only by the EF Core CLI (<c>dotnet ef migrations add --context DraftDbContext</c> /
/// <c>database update --context DraftDbContext</c>). Override the connection string with the
/// <c>EF_CONNECTION_STRING</c> environment variable when applying migrations from the command line.
/// </summary>
public sealed class DesignTimeDraftDbContextFactory : IDesignTimeDbContextFactory<DraftDbContext>
{
    public DraftDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("EF_CONNECTION_STRING")
            ?? "Server=localhost,1433;Database=HappyHeadlines_Drafts_DesignTime;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<DraftDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new DraftDbContext(options);
    }
}
