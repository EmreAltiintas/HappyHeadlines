using System.Text.Json.Serialization;
using HappyHeadlines.Core.Profanity;
using HappyHeadlines.Db;
using HappyHeadlines.ProfanityService.WebAPI.Middleware;
using HappyHeadlines.Observability;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Central, reusable logging + tracing - same call as every other HappyHeadlines service.
builder.AddHappyHeadlinesObservability("ProfanityService");

// ---------------------------------------------------------------------------
// Dependency injection – wired here in Program.cs, not in per-layer classes.
// ---------------------------------------------------------------------------
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();

// ProfanityService owns its own database – no shared context, schema or connection pool.
var connectionString = builder.Configuration.GetConnectionString("ProfanityDb")
    ?? throw new InvalidOperationException("Missing connection string 'ConnectionStrings:ProfanityDb'.");

builder.Services.AddDbContext<ProfanityDbContext>(o =>
    o.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
        maxRetryCount: 5,
        maxRetryDelay: TimeSpan.FromSeconds(5),
        errorNumbersToAdd: null)));

// Core services + business logic (from HappyHeadlines.Core).
builder.Services.AddScoped<IProfanityService, ProfanityService>();

var app = builder.Build();

app.Logger.LogInformation("HappyHeadlines.ProfanityService.WebAPI instance '{InstanceId}' starting.", InstanceInfo.InstanceId);

// Bring the profanity database up to the latest migration (seeds the starter word list).
if (app.Configuration.GetValue("MIGRATE_ON_STARTUP", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ProfanityDbContext>();
    await MigrateWithRetryAsync(db, app.Logger);
}
else
{
    app.Logger.LogInformation("MIGRATE_ON_STARTUP is false – this instance skips database migration.");
}

app.MapOpenApi();

app.UseHappyHeadlinesRequestLogging();

app.UseMiddleware<InstanceHeaderMiddleware>();

app.MapControllers();

app.Run();

// Retries while the SQL Server container is still booting, then lets a lasting failure crash startup.
static async Task MigrateWithRetryAsync(DbContext db, ILogger logger)
{
    const int maxAttempts = 20;
    var delay = TimeSpan.FromSeconds(3);

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied.");
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogWarning(
                "Migration attempt {Attempt}/{Max} failed ({Message}). Retrying in {Delay}s.",
                attempt, maxAttempts, ex.Message, delay.TotalSeconds);
            await Task.Delay(delay);
        }
    }

    await db.Database.MigrateAsync();
    logger.LogInformation("Database migrations applied.");
}
