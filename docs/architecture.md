# Architecture

A slice of the **HappyHeadlines** C4 architecture, built with **Clean Architecture** and
demonstrating four resilience/scaling patterns plus centralized observability:

| Pattern | Where | What it looks like here |
| --- | --- | --- |
| **Z-axis split** (data sharding) | ArticleDatabase | 8 databases - one per continent + one `Global` - behind a connection-string-per-shard router. The `Continent` on the request decides which database is used. No cross-shard queries. |
| **X-axis split** (horizontal scaling) | ArticleService.WebAPI | 3 identical stateless instances behind a **YARP** round-robin reverse proxy. The gateway is the only entrypoint. |
| **Swimlanes** (fault isolation) | CommentService + ProfanityService + DraftService | Separate host processes, each with its **own database**. CommentService calls ProfanityService **directly over REST**, wrapped in a **Polly circuit breaker + timeout**, so a failure in one swimlane can't cascade into the other. |
| **Central observability** (logging + tracing) | HappyHeadlines.Observability, used by every service | One shared library wires OpenTelemetry logging/tracing/metrics identically everywhere, so a request that crosses services shows up as a single correlated trace. See [observability](observability.md). |

## Tech stack

.NET 10 · ASP.NET Core Web API · EF Core + SQL Server · Polly via
`Microsoft.Extensions.Http.Resilience` (circuit breaker + timeout) · YARP (`Yarp.ReverseProxy`,
existing/untouched) · OpenTelemetry (`OpenTelemetry.Extensions.Hosting` +
ASP.NET Core/HttpClient/EF Core instrumentation + OTLP exporter) · Aspire Dashboard (OTLP sink,
Docker Compose only) · Docker Compose

> The assignment brief names .NET 8; this build targets **.NET 10** to match the SDK installed on the
> dev machine. Nothing else about the design changes.

## Solution layout

```
HappyHeadlines.slnx
├─ HappyHeadlines.ArticleService.WebAPI/     Front door for ArticleService. DI wired in Program.cs.
│  ├─ Controllers/            ArticlesController, DiagnosticsController
│  └─ Middleware/             InstanceHeaderMiddleware, InstanceInfo
│
├─ HappyHeadlines.CommentService.WebAPI/     Front door for CommentService (added). Own host process.
│  ├─ Controllers/            CommentsController, DiagnosticsController
│  ├─ Middleware/             InstanceHeaderMiddleware, InstanceInfo
│  └─ Program.cs              also wires the Polly circuit breaker + timeout around the ProfanityService client
│
├─ HappyHeadlines.ProfanityService.WebAPI/   Front door for ProfanityService (added). Own host process.
│  ├─ Controllers/            ProfanityController, DiagnosticsController
│  └─ Middleware/             InstanceHeaderMiddleware, InstanceInfo
│
├─ HappyHeadlines.DraftService.WebAPI/       Front door for DraftService (added). Own host process.
│  ├─ Controllers/            DraftsController, DiagnosticsController
│  └─ Middleware/             InstanceHeaderMiddleware, InstanceInfo
│
├─ HappyHeadlines.ArticleCacheWorker/       Offline worker that fills the Redis ArticleCache.
│
├─ HappyHeadlines.PublisherService.WebAPI/  Publishes ArticlePublished to ArticleQueue (RabbitMQ).
│
├─ HappyHeadlines.NewsletterService.WebAPI/ Daily and immediate newsletter. Consumes ArticleQueue and SubscriberQueue.
│
├─ HappyHeadlines.SubscriberService.WebAPI/ Subscribe and unsubscribe. Own database and own queue.
│
├─ HappyHeadlines.Contracts/                Contracts shared between services.
│
├─ HappyHeadlines.Core.Tests/               Unit tests.
│
├─ HappyHeadlines.Observability/             Central, reusable logging + tracing library (added).
│  ├─ ObservabilityExtensions.cs             AddHappyHeadlinesObservability(serviceName) - OpenTelemetry
│  │                                          logging/tracing/metrics, OTLP exporter from env vars only
│  └─ RequestLoggingMiddlewareExtensions.cs  UseHappyHeadlinesRequestLogging() - one structured log
│                                             line per request (method, path, status, duration, instance)
│
├─ HappyHeadlines.Core/       All core services + business logic. One folder per feature.
│  ├─ Articles/               IArticleService, ArticleService, Models/         (existing, untouched)
│  ├─ Comments/                ICommentService, CommentService,                 (added)
│  │                          IProfanityClient, ProfanityClient (the HTTP call + fail-fast handling),
│  │                          Models/ (CreateCommentRequest, CommentResponse, CreateCommentResult)
│  ├─ Profanity/               IProfanityService, ProfanityService, Models/     (added)
│  └─ Drafts/                  IDraftService, DraftService, Models/             (added)
│
├─ HappyHeadlines.Db/         Database entities + DbContexts.
│  ├─ Continent.cs / ModerationStatus.cs                (enums; ModerationStatus added)
│  ├─ Entities/               Article (existing), Comment + ProfanityWord + Draft (added)
│  ├─ ArticleDbContext.cs     (existing, untouched)
│  ├─ CommentDbContext.cs / ProfanityDbContext.cs / DraftDbContext.cs  (added; ProfanityDbContext seeds a word list)
│  ├─ Sharding/               ArticleService shard plumbing (existing, untouched)
│  ├─ DesignTime*DbContextFactory.cs                    (one per context; three added)
│  └─ Migrations/  Migrations/Comments/  Migrations/Profanity/  Migrations/Drafts/   (one folder per context)
│
├─ HappyHeadlines.Gateway/    YARP reverse proxy for ArticleService's x-axis only (existing). Also wires
│                             central observability - Gateway → ArticleService shows up as one trace.
│
├─ docker-compose.yml
└─ README.md
```

**Dependencies:** `ArticleService.WebAPI → Core → Db`; `CommentService.WebAPI → Core → Db`;
`ProfanityService.WebAPI → Core → Db`; `DraftService.WebAPI → Core → Db`. Every `*.WebAPI`
project (and the Gateway) also references `HappyHeadlines.Observability` for the shared
logging/tracing wiring - that reference is orthogonal to the Clean Architecture layering above,
it never routes through Core or Db. `Gateway` otherwise remains independent and **not** involved
in the Comment/Profanity/Draft flows - it only fronts ArticleService's x-axis.

**Request flow:** `Controller` validates arguments only, then calls `IArticleService`. Core resolves
the shard from `Continent` via `IArticleDbContextFactory`, opens a short-lived `ArticleDbContext`,
does the work `async`/`await`, and maps entities to response models. Everything below the controller
lives in Core; the controller "hits the ball" only once the arguments are OK.

### The shard router

`HappyHeadlines.Db/Sharding/ArticleDbContextFactory.cs` reads 8 connection strings from the
`ArticleShards` configuration section (keys `Africa`…`Global`), caches a
`DbContextOptions<ArticleDbContext>` per continent, and returns a fresh `ArticleDbContext` for the
requested `Continent`. Override any shard from the environment with `ArticleShards__<Continent>`.

---
