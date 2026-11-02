using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HappyHeadlines.Db;

/// <summary>
/// Used only by the EF Core CLI (<c>dotnet ef migrations add --context SubscriberDbContext</c>).
/// Override the connection string with the <c>EF_CONNECTION_STRING</c> environment variable.
/// </summary>
public sealed class DesignTimeSubscriberDbContextFactory : IDesignTimeDbContextFactory<SubscriberDbContext>
{
    public SubscriberDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("EF_CONNECTION_STRING")
            ?? "Server=localhost,1434;Database=HappyHeadlines_Subscribers_DesignTime;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<SubscriberDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new SubscriberDbContext(options);
    }
}
