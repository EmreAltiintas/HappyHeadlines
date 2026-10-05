using HappyHeadlines.Observability;

var builder = WebApplication.CreateBuilder(args);

// Central, reusable logging + tracing - same call as every other HappyHeadlines service, so a
// request that fans out through the gateway to ArticleService shows up as one correlated trace.
builder.AddHappyHeadlinesObservability("Gateway");

// YARP reverse proxy / load balancer. Routes + cluster + round-robin policy come from
// the "ReverseProxy" section of configuration (appsettings.json, overridable via env).
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseHappyHeadlinesRequestLogging();

// Gateway-level health endpoint (not proxied).
app.MapGet("/", () => Results.Ok(new { service = "HappyHeadlines.Gateway", status = "ok" }));

app.MapReverseProxy();

app.Run();
