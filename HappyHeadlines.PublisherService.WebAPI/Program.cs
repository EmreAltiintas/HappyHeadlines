using System.Text.Json.Serialization;
using HappyHeadlines.Core.Publishing;
using HappyHeadlines.Observability;
using HappyHeadlines.PublisherService.WebAPI.Middleware;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

// Central, reusable logging + tracing - same call as every other HappyHeadlines service. This is
// what lets a request that starts here and crosses ArticleQueue show up as one correlated trace.
builder.AddHappyHeadlinesObservability("PublisherService");

// ---------------------------------------------------------------------------
// Dependency injection – wired here in Program.cs, not in per-layer classes.
// ---------------------------------------------------------------------------
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();

// Core services + business logic (from HappyHeadlines.Core).
builder.Services.AddScoped<IArticlePublisher, ArticlePublisher>();

// ---------------------------------------------------------------------------
// ArticleQueue producer side: PublisherService only publishes - it has no consumers of its own,
// so no ConfigureEndpoints(context) call is needed here (there is nothing to bind a queue to).
// ---------------------------------------------------------------------------
var rabbitMqConnectionString = builder.Configuration.GetConnectionString("RabbitMq")
    ?? throw new InvalidOperationException("Missing connection string 'ConnectionStrings:RabbitMq'.");

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((_, cfg) =>
    {
        ConfigureRabbitMqHost(cfg, rabbitMqConnectionString);
    });
});

var app = builder.Build();

app.Logger.LogInformation("HappyHeadlines.PublisherService.WebAPI instance '{InstanceId}' starting.", InstanceInfo.InstanceId);

app.MapOpenApi();

app.UseHappyHeadlinesRequestLogging();

app.UseMiddleware<InstanceHeaderMiddleware>();

app.MapControllers();

app.Run();

// Parses "amqp://user:pass@host:port" (ConnectionStrings:RabbitMq) into a MassTransit host -
// duplicated identically in ArticleService/NewsletterService rather than shared, matching this
// codebase's convention of small per-service Program.cs helpers over a shared infra abstraction.
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
