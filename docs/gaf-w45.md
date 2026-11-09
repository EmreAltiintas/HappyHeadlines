# Week 45: Sustainability with the Green Architecture Framework (GAF)

Assignment: pick three GAF tactics we have not worked with yet, apply them to HappyHeadlines and
document the process with before and after code.

Already used in earlier weeks (so not chosen here): Independent Scaling of Modular Architecture
(3 ArticleService instances), Utilize Geographical Sharding (8 databases), Cache Data Closer to the
User (W40, Redis + LRU).

| # | Tactic | GAF category |
| --- | --- | --- |
| 1 | Defer Fetching (pagination + summaries) | Avoid Unnecessary Resources |
| 2 | Reduce Logging (Accept Lower Fidelity) | Avoid Unnecessary Resources |
| 3 | Reduce Bundle Size | Avoid Unnecessary Resources |

## How the numbers were measured

Stack started with `docker compose up -d --build`. Test data: 500 articles of about 5 KB text each in
the Europe shard and 500 in the Global shard, created through the gateway (`POST /api/articles`).
All requests below go through the gateway on `http://localhost:8088`.

---

## 1. Defer Fetching (pagination and summaries)

### Why it fits HappyHeadlines
`GET /api/articles?continent=X` returned **every** article of a shard including the full `Content`
text. A reader of a front page only needs title, author and date. NewsletterService even called this
endpoint just to read `articles.Count`. Every list request therefore moved megabytes through SQL
Server, ArticleService, the gateway (and Redis for the Global shard) for data nobody looked at.

Now the list returns one page of summaries without `Content`. The full text is fetched only when an
article is opened (`GET /api/articles/{id}`).

### Before

`ArticleService.ListAsync` (Core/Articles/ArticleService.cs): loads every column of every row.

```csharp
var articles = await query
    .OrderByDescending(a => a.PublishedDate)
    .ToListAsync(ct);

return articles.Select(ArticleResponse.FromEntity).ToList();
```

`ArticlesController.List`: no paging parameters.

```csharp
public async Task<ActionResult<IReadOnlyList<ArticleResponse>>> List(
    [FromQuery] Continent continent,
    CancellationToken ct)
{
    return Ok(await _articles.ListAsync(continent, ct));
}
```

`ArticleCache.StoreAsync`: the cached Global list contained all article texts.

```csharp
await _cache.SetStringAsync(ListKey, JsonSerializer.Serialize(recentArticles, Json), options, ct);
```

### After

New model `ArticleSummaryResponse` (Core/Articles/Models): same as `ArticleResponse` without `Content`.

```csharp
public class ArticleSummaryResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTimeOffset PublishedDate { get; set; }
    public Continent Continent { get; set; }
    // ...
}
```

`ArticleService.ListAsync`: `Select` makes SQL Server return only the five columns, `Skip`/`Take` the one page.

```csharp
return await query
    .OrderByDescending(a => a.PublishedDate)
    .ThenByDescending(a => a.Id)
    .Skip(skip)
    .Take(pageSize)
    .Select(a => new ArticleSummaryResponse
    {
        Id = a.Id,
        Title = a.Title,
        Author = a.Author,
        PublishedDate = a.PublishedDate,
        Continent = a.Continent
    })
    .ToListAsync(ct);
```

`ArticlesController.List`: `page` (default 1) and `pageSize` (default 20, max 100). Invalid values
give `400` through `[ApiController]` validation.

```csharp
public async Task<ActionResult<IReadOnlyList<ArticleSummaryResponse>>> List(
    [FromQuery] Continent continent,
    [FromQuery, Range(1, int.MaxValue)] int page = 1,
    [FromQuery, Range(1, IArticleService.MaxPageSize)] int pageSize = IArticleService.DefaultPageSize,
    CancellationToken ct = default)
{
    return Ok(await _articles.ListAsync(continent, page, pageSize, ct));
}
```

`ArticleCache.StoreAsync`: the list key now only holds summaries. The full text stays in the
per-article keys, which `GET /api/articles/{id}` uses.

```csharp
var summaries = recentArticles.Select(ArticleSummaryResponse.FromResponse).ToList();
await _cache.SetStringAsync(ListKey, JsonSerializer.Serialize(summaries, Json), options, ct);
```

`ArticleClient` (NewsletterService) asks for the newest 100 summaries instead of everything.

```csharp
var articles = await _http.GetFromJsonAsync<List<ArticleSummaryResponse>>(
    $"/api/articles?continent={continent}&pageSize={IArticleService.MaxPageSize}", JsonOptions, ct);
```

### Measurement

| What | Before | After |
| --- | --- | --- |
| `GET /api/articles?continent=Europe` (500+ articles) | 2,602,826 bytes | 2,439 bytes (20 articles) |
| `GET /api/articles?continent=Global` | 2,602,342 bytes | 2,439 bytes (20 articles) |
| Redis value for the cached Global list (`HSTRLEN articles:global:recent data`) | 2,602,342 bytes | 60,393 bytes (all 500 recent, no text) |
| NewsletterService download from ArticleService | 502 articles with text (about 2.6 MB) | 100 summaries (about 12 KB) |
| `GET /api/articles/{id}` | full article | unchanged, still has `content` |

Commands: `curl -s -o /dev/null -w "%{size_download}" "http://localhost:8088/api/articles?continent=Europe"`
and `docker exec happyheadlines-redis redis-cli hstrlen articles:global:recent data`.

### Trade-offs
- Breaking change for clients of the list endpoint: no `content` and only 20 items by default.
  NewsletterService, the cache and the tests were updated.
- The daily newsletter now covers at most the 100 newest articles of a continent.
- We did not add a `Summary` column to the database, so a "summary" is title, author and date.
  A real teaser text would need an EF migration on all 8 shards.
- Paging uses `Skip`/`Take`. That is fine here; for very deep pages keyset paging would be cheaper.

---

## 2. Reduce Logging (Accept Lower Fidelity)

### Why it fits HappyHeadlines
Every service sends logs and traces to the Aspire Dashboard over OTLP. Most of that is repetitive:
each service is health-checked every 5 seconds, Prometheus scrapes `/metrics` every few seconds, and
`HttpClient` writes 4 Information lines for each outbound call. Sending, storing and showing all of
that costs CPU, network and storage while telling us nothing new. We accept a lower level of detail.

Three small changes:
1. Keep only 25 % of the traces (configurable). The decision is parent-based, so a trace is either
   complete or not recorded at all, also across RabbitMQ.
2. Do not log successful health checks and `/metrics` scrapes, and do not trace `/metrics`.
3. Log `HttpClient` only at Warning or above (the trace span already shows the call).

### Before

`ObservabilityExtensions.cs`: every trace was recorded (default sampler).

```csharp
.WithTracing(t => t
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddEntityFrameworkCoreInstrumentation()
    .AddSource(DiagnosticHeaders.DefaultListenerName))
```

`RequestLoggingMiddlewareExtensions.cs`: one log line for every request, health checks included.

```csharp
var statusCode = context.Response.StatusCode;
var level = statusCode >= 500 ? LogLevel.Warning : LogLevel.Information;

logger.Log(level, "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds}ms (instance {InstanceId})", ...);
```

### After

`ObservabilityExtensions.cs`:

```csharp
var samplingRatio = builder.Configuration.GetValue("TRACE_SAMPLING_RATIO", 1.0);

builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

// ...
.WithTracing(t => t
    .SetSampler(new ParentBasedSampler(new RootTraceSampler(samplingRatio)))
    .AddAspNetCoreInstrumentation(o =>
        o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/metrics"))
    // ...
```

`RequestLoggingMiddlewareExtensions.cs`:

```csharp
var path = context.Request.Path;
var isPolling = path.StartsWithSegments("/api/health") || path.StartsWithSegments("/metrics");
if (isPolling && statusCode < 400)
    return;
```

New file `RootTraceSampler.cs`. `ParentBasedSampler` only calls it for the first span of a trace.

```csharp
public override SamplingResult ShouldSample(in SamplingParameters parameters)
{
    if (parameters.Kind == ActivityKind.Consumer)
        return new SamplingResult(SamplingDecision.Drop);

    return _ratioSampler.ShouldSample(parameters);
}
```

`docker-compose.yml` (`x-otel-env`, shared by all services). Default is 0.25 in Docker and 1.0 (keep
everything) when the variable is not set, for example with `dotnet run`.

```yaml
TRACE_SAMPLING_RATIO: "${TRACE_SAMPLING_RATIO:-0.25}"
```

Set `TRACE_SAMPLING_RATIO=1` before `docker compose up` to see every trace while debugging.

#### Why the `Consumer` rule is needed (found while testing)
With only `ParentBasedSampler(TraceIdRatioBasedSampler(0.25))` the traces over RabbitMQ broke. When
PublisherService drops a trace, MassTransit sends no trace context. The consumers in ArticleService
and NewsletterService then saw "no parent", started a new trace and kept 25 % of them by chance.
In the first test run that gave 17 + 21 stray one-service traces next to 23 complete
Publisher, ArticleService, NewsletterService traces. A consumer span without a parent is now always
dropped. Consumers whose publisher kept the trace still get the parent and are recorded.

### Measurement

Idle stack, 60 seconds, counting `HTTP ... responded` log lines in all containers (each is one log
record and one server span):

| | Before | After |
| --- | --- | --- |
| Request log records per minute, idle | 158 | 0 |

Fixed workload (`workload.sh`: 50 list requests through the gateway plus 50 comment posts, where
CommentService calls ProfanityService), console log lines over the same period:

| | Before | After |
| --- | --- | --- |
| `System.Net.Http.HttpClient` lines | 200 | 0 |
| Total log lines | 523 | 301 |

Traces, 250 requests (100 list requests via gateway, 50 comments, 100 publishes through RabbitMQ),
read from the Aspire Dashboard telemetry API (`/api/telemetry/spans`) and grouped by trace id:

| | Ratio 1 (everything) | Ratio 0.25 |
| --- | --- | --- |
| Spans | 1,440 | 318 to 410 (two runs) |
| Traces | 280 | 59 to 78 |
| Gateway, ArticleService traces | 100 | 14 to 22 |
| Publisher, ArticleService, NewsletterService traces (over RabbitMQ) | 100 | 27 to 32 |
| Traces with exactly one root and no missing parent span | 254 of 254 | 48 of 48, 67 of 67 |
| Stray one-service traces from consumers | 0 | 0 |

So about 75 % fewer spans, and the traces that are kept are still whole, over HTTP and over RabbitMQ.

### Trade-offs
- At 0.25 a single request has a 75 % chance of having no trace. Rare errors can be missed. For
  debugging set `TRACE_SAMPLING_RATIO=1`. Failed requests are still visible in the log lines
  (Warning for 5xx, health checks with 4xx/5xx are still logged).
- Health-check traces are only sampled, not removed. We did not filter them out completely because
  the ArticleService health check runs an EF query, which would then show up as separate traces.
- Metrics (Prometheus, Grafana) are not sampled. The cache hit ratio dashboard is unchanged.
- One extra class (`RootTraceSampler`) to maintain.

---

## 3. Reduce Bundle Size

### Why it fits HappyHeadlines
We run 9 container images. A smaller image is faster to build, push, pull and start, and uses less
disk and registry storage on every machine and every CI run. The Dockerfiles were already multi-stage
(SDK image for build, ASP.NET image for runtime), so the remaining waste was the Debian runtime
image, `apt` for curl and `.pdb` debug symbols in the published output.

### Before (ArticleService, the other services are the same)

```dockerfile
RUN dotnet publish HappyHeadlines.ArticleService.WebAPI/HappyHeadlines.ArticleService.WebAPI.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# curl is used by the container HEALTHCHECK in docker-compose.yml.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
```

### After

```dockerfile
RUN dotnet publish HappyHeadlines.ArticleService.WebAPI/HappyHeadlines.ArticleService.WebAPI.csproj -c Release -o /app --no-restore -p:DebugType=None -p:DebugSymbols=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
# curl is used by the container HEALTHCHECK in docker-compose.yml.
RUN apk add --no-cache curl
# SqlClient does not work in invariant globalization mode, which the alpine image turns on.
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
```

The `icu-libs` and `ENV` lines are only in the services that talk to SQL Server (ArticleService,
CommentService, DraftService, ProfanityService, SubscriberService, ArticleCacheWorker). Gateway,
PublisherService and NewsletterService do not need them. The first attempt without ICU failed:
ArticleService could not migrate the shards (`Globalization Invariant Mode is not supported`) and
the container never became healthy.

### Measurement (`docker images`)

| Image | Before | After |
| --- | --- | --- |
| articleservice-webapi | 329 MB | 226 MB |
| commentservice-webapi | 328 MB | 225 MB |
| draftservice-webapi | 327 MB | 224 MB |
| profanityservice-webapi | 327 MB | 224 MB |
| subscriberservice-webapi | 328 MB | 225 MB |
| publisherservice-webapi | 328 MB | 217 MB |
| newsletterservice-webapi | 329 MB | 218 MB |
| articlecacheworker | 321 MB | 219 MB |
| gateway | 237 MB | 128 MB |

About 100 MB less per image, 948 MB less in total for the nine images (2,854 MB before, 1,906 MB after).

### Trade-offs
- Alpine uses musl instead of glibc. Everything in this solution ran fine (EF Core, SqlClient,
  MassTransit, Redis, OpenTelemetry), but native libraries can behave differently.
- Services with SQL Server need ICU, which adds back some size. Trimming (`PublishTrimmed`) would save
  more but is risky with EF Core and MassTransit, so we left it out.
- No `.pdb` files means stack traces have no line numbers in the containers.

---

## Check after all three changes

- `dotnet build HappyHeadlines.slnx`: succeeds.
- `dotnet test HappyHeadlines.Core.Tests`: 45 of 45 pass.
- `docker compose up -d --build`: all containers with a health check are healthy.
- List, detail, daily newsletter and SubscriberService endpoints answer `200`.
- Publisher, ArticleService, NewsletterService traces over RabbitMQ and Gateway, ArticleService
  traces over HTTP are complete (see the table in tactic 2).
