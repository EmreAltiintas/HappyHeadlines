using System.Text.Json.Serialization;
using HappyHeadlines.Core.Newsletters;
using HappyHeadlines.NewsletterService.WebAPI;
using HappyHeadlines.NewsletterService.WebAPI.Consumers;
using HappyHeadlines.NewsletterService.WebAPI.Middleware;
using HappyHeadlines.Observability;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

// Central, reusable logging + tracing - same call as every other HappyHeadlines service. This is
// what lets the ArticleQueue consume span AND the NewsletterService -> ArticleService HTTP call
// each correlate into their own complete traces.
builder.AddHappyHeadlinesObservability("NewsletterService");

// ---------------------------------------------------------------------------
// Dependency injection – wired here in Program.cs, not in per-layer classes.
// ---------------------------------------------------------------------------
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();

// ---------------------------------------------------------------------------
// Flow b) daily digest: plain instrumented HttpClient call to ArticleService via the Gateway.
// ---------------------------------------------------------------------------
var articleServiceOptions = builder.Configuration
    .GetSection(ArticleServiceOptions.SectionName)
    .Get<ArticleServiceOptions>() ?? new ArticleServiceOptions();

builder.Services.AddHttpClient<IArticleClient, ArticleClient>(client =>
{
    client.BaseAddress = new Uri(articleServiceOptions.BaseUrl);
});

builder.Services.AddScoped<IDailyNewsletterService, DailyNewsletterService>();

// ---------------------------------------------------------------------------
// Flow a) immediate newsletter: NewsletterService's own independent subscriber to
// ArticlePublished (separate queue from ArticleService's, via ConfigureEndpoints below).
// ---------------------------------------------------------------------------
var rabbitMqConnectionString = builder.Configuration.GetConnectionString("RabbitMq")
    ?? throw new InvalidOperationException("Missing connection string 'ConnectionStrings:RabbitMq'.");

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ImmediateNewsletterConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        ConfigureRabbitMqHost(cfg, rabbitMqConnectionString);
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.Logger.LogInformation("HappyHeadlines.NewsletterService.WebAPI instance '{InstanceId}' starting.", InstanceInfo.InstanceId);

app.MapOpenApi();

app.UseHappyHeadlinesRequestLogging();

app.UseMiddleware<InstanceHeaderMiddleware>();

app.MapControllers();

app.Run();

// Parses "amqp://user:pass@host:port" (ConnectionStrings:RabbitMq) into a MassTransit host -
// duplicated identically in PublisherService/ArticleService rather than shared, matching this
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
