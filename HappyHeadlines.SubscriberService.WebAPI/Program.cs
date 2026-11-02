using System.Text.Json.Serialization;
using HappyHeadlines.Core.Subscribers;
using HappyHeadlines.Db;
using HappyHeadlines.Observability;
using HappyHeadlines.SubscriberService.WebAPI.Middleware;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Central, reusable logging + tracing - same call as every other HappyHeadlines service. This
// is what lets subscribe -> SubscriberQueue -> NewsletterService show up as one trace.
builder.AddHappyHeadlinesObservability("SubscriberService");

// ---------------------------------------------------------------------------
// Release toggle: a JSON file loaded with reloadOnChange. Edit the file and the next request
// sees the new value (IOptionsMonitor) - no redeploy, no restart. In Docker the file's
// directory is a mounted volume (FeatureFlags__File=/config/featureflags.json).
// ---------------------------------------------------------------------------
var featureFlagsFile = builder.Configuration["FeatureFlags:File"] ?? "featureflags.json";
if (!Path.IsPathRooted(featureFlagsFile))
    featureFlagsFile = Path.Combine(AppContext.BaseDirectory, featureFlagsFile);

builder.Configuration.AddJsonFile(featureFlagsFile, optional: true, reloadOnChange: true);
builder.Services.Configure<SubscriberFeatureFlags>(
    builder.Configuration.GetSection(SubscriberFeatureFlags.SectionName));

// ---------------------------------------------------------------------------
// Dependency injection - wired here in Program.cs, not in per-layer classes.
// ---------------------------------------------------------------------------
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();

// SubscriberService owns SubscriberDatabase - no shared context, schema or connection pool.
var connectionString = builder.Configuration.GetConnectionString("SubscriberDb")
    ?? throw new InvalidOperationException("Missing connection string 'ConnectionStrings:SubscriberDb'.");

builder.Services.AddDbContext<SubscriberDbContext>(o =>
    o.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
        maxRetryCount: 5,
        maxRetryDelay: TimeSpan.FromSeconds(5),
        errorNumbersToAdd: null)));

builder.Services.AddScoped<ISubscriberService, SubscriberService>();

// ---------------------------------------------------------------------------
// SubscriberQueue producer side (its own RabbitMQ broker, separate from ArticleQueue):
// SubscriberService only publishes, so no ConfigureEndpoints call is needed.
// ---------------------------------------------------------------------------
var queueConnectionString = builder.Configuration.GetConnectionString("SubscriberQueue")
    ?? throw new InvalidOperationException("Missing connection string 'ConnectionStrings:SubscriberQueue'.");

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((_, cfg) => ConfigureRabbitMqHost(cfg, queueConnectionString));
});

var app = builder.Build();

app.Logger.LogInformation("HappyHeadlines.SubscriberService.WebAPI instance '{InstanceId}' starting.", InstanceInfo.InstanceId);

// Bring the subscriber database up to the latest migration.
if (app.Configuration.GetValue("MIGRATE_ON_STARTUP", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SubscriberDbContext>();
    await MigrateWithRetryAsync(db, app.Logger);
}
else
{
    app.Logger.LogInformation("MIGRATE_ON_STARTUP is false - this instance skips database migration.");
}

app.MapOpenApi();

app.UseHappyHeadlinesRequestLogging();

app.UseMiddleware<InstanceHeaderMiddleware>();

app.MapControllers();

app.Run();

// Parses "amqp://user:pass@host:port" into a MassTransit host - same small per-service helper
// as PublisherService/NewsletterService.
static void ConfigureRabbitMqHost(IRabbitMqBusFactoryConfigurator cfg, string connectionString)
{
    var uri = new Uri(connectionString);
    var port = (ushort)(uri.IsDefaultPort ? 5672 : uri.Port);

    cfg.Host(uri.Host, port, "/", h =>
    {
        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length == 2)
        {
            h.Username(Uri.UnescapeDataString(userInfo[0]));
            h.Password(Uri.UnescapeDataString(userInfo[1]));
        }
    });
}

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
