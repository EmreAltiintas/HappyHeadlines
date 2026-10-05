# HappyHeadlines — Article, Comment, Profanity & Draft services

A slice of the **HappyHeadlines** C4 architecture, built with **Clean Architecture** and
demonstrating four resilience/scaling patterns plus centralized observability:

| Pattern | Where | What it looks like here |
| --- | --- | --- |
| **Z-axis split** (data sharding) | ArticleDatabase | 8 databases — one per continent + one `Global` — behind a connection‑string‑per‑shard router. The `Continent` on the request decides which database is used. No cross‑shard queries. |
| **X-axis split** (horizontal scaling) | ArticleService.WebAPI | 3 identical stateless instances behind a **YARP** round‑robin reverse proxy. The gateway is the only entrypoint. |
| **Swimlanes** (fault isolation) | CommentService + ProfanityService + DraftService | Separate host processes, each with its **own database**. CommentService calls ProfanityService **directly over REST**, wrapped in a **Polly circuit breaker + timeout**, so a failure in one swimlane can't cascade into the other. |
| **Central observability** (logging + tracing) | HappyHeadlines.Observability, used by every service | One shared library wires OpenTelemetry logging/tracing/metrics identically everywhere, so a request that crosses services shows up as a single correlated trace. See [Observability](#observability--logging--distributed-tracing) below. |

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
HappyHeadlines.sln
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
the shard from `Continent` via `IArticleDbContextFactory`, opens a short‑lived `ArticleDbContext`,
does the work `async`/`await`, and maps entities to response models. Everything below the controller
lives in Core; the controller "hits the ball" only once the arguments are OK.

### The shard router

`HappyHeadlines.Db/Sharding/ArticleDbContextFactory.cs` reads 8 connection strings from the
`ArticleShards` configuration section (keys `Africa`…`Global`), caches a
`DbContextOptions<ArticleDbContext>` per continent, and returns a fresh `ArticleDbContext` for the
requested `Continent`. Override any shard from the environment with `ArticleShards__<Continent>`.

---

## Run everything with Docker Compose

```bash
docker compose up --build
```

This starts:

* `sqlserver` — one SQL Server 2022 container hosting all 11 databases (8 article shards + comments + profanity + drafts)
* `articleservice1`, `articleservice2`, `articleservice3` — the ArticleService WebAPI, same image, different `INSTANCE_ID`
* `gateway` — YARP load balancer for ArticleService, published on **http://localhost:8088**
* `commentservice` — CommentService WebAPI, published on **http://localhost:8090**
* `profanityservice` — ProfanityService WebAPI, published on **http://localhost:8091**
* `draftservice` — DraftService WebAPI, published on **http://localhost:8092**
* `redis`, `articlecacheworker`, `prometheus`, `grafana` — ArticleCache, its offline filler and the cache hit-ratio dashboard on **http://localhost:3000** (see [Caching](#caching--articlecache--commentcache-w40-assignment))
* `aspire-dashboard` — OTLP sink for logs/traces/metrics from every service above, UI on **http://localhost:18888** (see [Observability](#observability--logging--distributed-tracing))

The gateway fronts ArticleService only. `commentservice`, `profanityservice` and `draftservice` are
published directly (no gateway); commentservice and profanityservice talk to each other over the
Docker network. Stop with `Ctrl+C`; wipe the databases with `docker compose down -v`.

The SA password defaults to `Your_strong_Pass123`. Override it by exporting `MSSQL_SA_PASSWORD`
before `docker compose up` (it is substituted into every connection string).

---

## Migrations — applying the schema to all 8 databases

### Automatic (default)

`ShardMigrator` calls `Database.MigrateAsync()` against **all 8 shards** on startup, creating the
databases if missing and retrying while SQL Server finishes booting. It runs when
`MIGRATE_ON_STARTUP=true` (the default).

In Docker Compose only **articleservice1** migrates; `articleservice2` and `articleservice3` set
`MIGRATE_ON_STARTUP=false` and wait for it. The container health check (`GET /api/health`) reports
`503` while migrations are pending, so Compose starts the services in order — `sqlserver` healthy →
`articleservice1` migrates and turns healthy → `articleservice2/3` start → `gateway` starts once all
three are healthy.

The comment, profanity and draft services do the same thing for their single database on startup
(`MIGRATE_ON_STARTUP=true`), creating `HappyHeadlines_Comments` / `HappyHeadlines_Profanity` /
`HappyHeadlines_Drafts` and, for profanity, seeding the starter word list from the migration.

### Manual (`dotnet ef database update` per shard)

`HappyHeadlines.Db` now holds **four** contexts, so every `dotnet ef` command needs
`--context` (`ArticleDbContext`, `CommentDbContext`, `ProfanityDbContext` or `DraftDbContext`). The
design‑time factories read the `EF_CONNECTION_STRING` environment variable. SQL Server must be
running (`docker compose up sqlserver`):

```powershell
# from repo root; PowerShell — the 8 article shards
$shards = "Africa","Asia","Europe","NorthAmerica","SouthAmerica","Oceania","Antarctica","Global"
foreach ($s in $shards) {
  $env:EF_CONNECTION_STRING = "Server=localhost,1433;Database=HappyHeadlines_$s;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True;Encrypt=False"
  dotnet ef database update --project HappyHeadlines.Db --startup-project HappyHeadlines.Db --context ArticleDbContext
}

# comment + profanity + draft databases
$env:EF_CONNECTION_STRING = "Server=localhost,1433;Database=HappyHeadlines_Comments;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True;Encrypt=False"
dotnet ef database update --project HappyHeadlines.Db --startup-project HappyHeadlines.Db --context CommentDbContext
$env:EF_CONNECTION_STRING = "Server=localhost,1433;Database=HappyHeadlines_Profanity;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True;Encrypt=False"
dotnet ef database update --project HappyHeadlines.Db --startup-project HappyHeadlines.Db --context ProfanityDbContext
$env:EF_CONNECTION_STRING = "Server=localhost,1433;Database=HappyHeadlines_Drafts;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True;Encrypt=False"
dotnet ef database update --project HappyHeadlines.Db --startup-project HappyHeadlines.Db --context DraftDbContext
```

```bash
# bash — the 8 article shards
for s in Africa Asia Europe NorthAmerica SouthAmerica Oceania Antarctica Global; do
  EF_CONNECTION_STRING="Server=localhost,1433;Database=HappyHeadlines_$s;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True;Encrypt=False" \
  dotnet ef database update --project HappyHeadlines.Db --startup-project HappyHeadlines.Db --context ArticleDbContext
done
```

Add a new migration after changing an entity (pick the matching context + output dir):

```bash
dotnet ef migrations add <Name> --project HappyHeadlines.Db --startup-project HappyHeadlines.Db --context ArticleDbContext   --output-dir Migrations
dotnet ef migrations add <Name> --project HappyHeadlines.Db --startup-project HappyHeadlines.Db --context CommentDbContext   --output-dir Migrations/Comments
dotnet ef migrations add <Name> --project HappyHeadlines.Db --startup-project HappyHeadlines.Db --context ProfanityDbContext --output-dir Migrations/Profanity
dotnet ef migrations add <Name> --project HappyHeadlines.Db --startup-project HappyHeadlines.Db --context DraftDbContext      --output-dir Migrations/Drafts
```

---

## API

Base URL through the gateway: `http://localhost:8088`

| Method | Route | Notes |
| --- | --- | --- |
| `POST` | `/api/articles` | Create. `Continent` in the body selects the shard. → `201` |
| `GET` | `/api/articles/{id}?continent={continent}` | Read one. Shard = `continent`; **no fallback** → `404` if absent there. |
| `PUT` | `/api/articles/{id}?continent={continent}` | Update. Body `Continent` must equal `continent`. → `200` / `400` / `404` |
| `DELETE` | `/api/articles/{id}?continent={continent}` | Delete. → `204` / `404` |
| `GET` | `/api/articles?continent={continent}` | List a shard (use `Global` for worldwide). → `200` |
| `GET` | `/api/instance` | Returns this instance's `instanceId` — used to observe load balancing. |
| `GET` | `/api/health` | `200` when the shard schema is reachable and migrated; `503` while migrating / if the DB is down. Used by the container health check. |

`continent` accepts the enum **name** (`Europe`) or number (`2`). Values:
`Africa, Asia, Europe, NorthAmerica, SouthAmerica, Oceania, Antarctica, Global`.
Every response also carries an `X-Instance-Id` header. OpenAPI document: `http://localhost:8088/openapi/v1.json`.

### Example curl requests

Create (returns the new article with its `id`):

```bash
curl -i -X POST http://localhost:8088/api/articles \
  -H "Content-Type: application/json" \
  -d '{
        "title": "Community garden feeds a whole street",
        "content": "Neighbours in Lisbon turned a vacant lot into a shared vegetable garden.",
        "author": "A. Reporter",
        "publishedDate": "2026-09-10T08:00:00Z",
        "continent": "Europe"
      }'
```

Read it back (same continent):

```bash
curl -i http://localhost:8088/api/articles/1?continent=Europe
```

Wrong shard → 404 (no silent fallback):

```bash
curl -i http://localhost:8088/api/articles/1?continent=Asia
```

List a shard:

```bash
curl -s http://localhost:8088/api/articles?continent=Europe
```

Update:

```bash
curl -i -X PUT http://localhost:8088/api/articles/1?continent=Europe \
  -H "Content-Type: application/json" \
  -d '{
        "title": "Community garden now feeds two streets",
        "content": "The Lisbon garden doubled in size after a second lot was donated.",
        "author": "A. Reporter",
        "publishedDate": "2026-09-10T08:00:00Z",
        "continent": "Europe"
      }'
```

Delete:

```bash
curl -i -X DELETE http://localhost:8088/api/articles/1?continent=Europe
```

---

## Observing the X-axis load balancing

Hit the gateway repeatedly and watch the instance id rotate `articleservice1 → articleservice2 → articleservice3 → articleservice1 …`:

```bash
# bash
for i in $(seq 1 9); do curl -s http://localhost:8088/api/instance; echo; done
```

```powershell
# PowerShell
1..9 | ForEach-Object { (Invoke-RestMethod http://localhost:8088/api/instance).instanceId }
```

Or read the header on any endpoint:

```bash
for i in $(seq 1 6); do curl -s -D - -o /dev/null http://localhost:8088/api/articles?continent=Global | grep -i x-instance-id; done
```

Because the ArticleService is stateless, which instance answers never matters — the articles all come
from the same 8 shared databases.

---

## Running locally without Docker

Needs a SQL Server on `localhost,1433` with SA password `Your_strong_Pass123` (e.g.
`docker compose up sqlserver`). Then:

```bash
dotnet run --project HappyHeadlines.ArticleService.WebAPI     # http://localhost:5215, INSTANCE_ID=local-dev
dotnet run --project HappyHeadlines.Gateway                   # http://localhost:8088 (expects the 3 container hostnames)
dotnet run --project HappyHeadlines.ProfanityService.WebAPI   # http://localhost:5217
dotnet run --project HappyHeadlines.CommentService.WebAPI     # http://localhost:5216 (calls ProfanityService at :5217 by default)
dotnet run --project HappyHeadlines.DraftService.WebAPI       # http://localhost:5218
```

The gateway's destinations are container hostnames, so the round‑robin demo is a Docker‑Compose
scenario; a single local ArticleService instance is enough for exercising the CRUD endpoints directly
on port 5215. Run ProfanityService before CommentService, or watch CommentService fast‑fail the
profanity check and store the comment as Pending (`202`).

Outside Docker Compose, `OTEL_EXPORTER_OTLP_ENDPOINT` is unset, so `AddHappyHeadlinesObservability`
has nowhere to export to - every service still runs and logs to the console exactly as before,
it just has no OTLP destination. Run a local Aspire Dashboard container to get one:
`docker run --rm -it -p 18888:18888 -p 18889:18889 -e DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true mcr.microsoft.com/dotnet/aspire-dashboard:latest`,
then set `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:18889` and `OTEL_EXPORTER_OTLP_PROTOCOL=grpc`
before `dotnet run`.

---

## Comment & Profanity services (swimlane fault isolation)

### Why they are separate host projects (but share `Core` / `Db`)

`Core` and `Db` are **compiled class libraries** — sharing them is just code reuse and creates no
runtime coupling. Fault isolation (Nygard's *swimlane* principle) is about the **running system**:

| Shared? | ArticleService | CommentService | ProfanityService | DraftService |
| --- | --- | --- | --- | --- |
| OS process / container | `articleservice1..3` | `commentservice` | `profanityservice` | `draftservice` |
| Database | 8 article shards | `HappyHeadlines_Comments` | `HappyHeadlines_Profanity` | `HappyHeadlines_Drafts` |
| EF `DbContext` type | `ArticleDbContext` | `CommentDbContext` | `ProfanityDbContext` | `DraftDbContext` |
| Connection pool | per shard | own | own | own |
| In‑process call path | — | — | — | — |

(DraftService has no swimlane dependency on any other service - it is plain CRUD against its own
database, listed here only to show it follows the same isolation shape.)

CommentService reaches ProfanityService **only** by an HTTP REST call to
`http://profanityservice:8080/api/profanity/check` — never in‑process, never through
`HappyHeadlines.Gateway`, never through any other middle layer. So if ProfanityService's process
crashes, its database locks up, or it just gets slow, the blast radius stops at that HTTP call:
CommentService's own process, database and pool are untouched, and the circuit breaker turns the
slow/failed call into an immediate, cheap failure.

If they instead shared one process or one database, a lock, a memory leak, a connection‑pool
exhaustion or an unhandled exception in the profanity path could take comments down with it — the
opposite of a swimlane.

### Circuit breaker + timeout configuration

Configured in `HappyHeadlines.CommentService.WebAPI/Program.cs` on the typed `IProfanityClient`
HTTP client via `Microsoft.Extensions.Http.Resilience` (`AddResilienceHandler`). Values come from the
`ProfanityService` config section (`appsettings.json` / env), defaults:

| Setting | Default | Why |
| --- | --- | --- |
| Per‑attempt **timeout** | **2 s** | A profanity check is a single indexed lookup. If it hasn't answered in 2 s it's effectively down — don't let the caller wait on `HttpClient.Timeout` (100 s). |
| Circuit **FailureRatio** | **0.5** | Open once half the recent calls in the window are failing. |
| Circuit **MinimumThroughput** | **4** | Need ≥ 4 calls in the window before the breaker can trip — one stray error won't open it. |
| Circuit **SamplingDuration** | **10 s** | Rolling window the ratio is measured over. |
| Circuit **BreakDuration** | **15 s** | While open, every call fails instantly for 15 s; then one trial call decides re‑open vs. close. |

The breaker is the **outer** strategy and the timeout the **inner** one, so a timed‑out call counts
as a failure toward opening the circuit. No retry is configured on purpose: retries would add latency
and load exactly when ProfanityService is already struggling, working against "fail fast".

The breaker's own state transitions are logged explicitly (category
`HappyHeadlines.CommentService.ProfanityCircuitBreaker`, via `OnOpened`/`OnClosed`/`OnHalfOpened` on
`HttpCircuitBreakerStrategyOptions`) - this is the concrete "what and when to log" example from the
[Observability](#observability--logging--distributed-tracing) section below: a breaker flipping open
is a business event, not just an HTTP status code, so it gets a dedicated `LogWarning`/`LogInformation`
line rather than being inferred from request logs:

| Transition | Level | Message |
| --- | --- | --- |
| Opened | Warning | `ProfanityService circuit breaker OPENED for {BreakDurationSeconds}s - calls will fail fast until it half-opens.` |
| Half-opened | Information | `ProfanityService circuit breaker HALF-OPEN - trialing a single call.` |
| Closed | Information | `ProfanityService circuit breaker CLOSED - calls are flowing normally again.` |

The fallback path itself (`CommentService.CreateAsync` when the profanity check can't complete)
logs a `LogWarning` with the article id and exception type - see `Comments/CommentService.cs`.

### Fallback — the circuit breaker "takes over" by storing the comment as Pending

When the profanity check can't be completed (circuit **open**, timeout, or transport error),
`CommentService.CreateAsync` catches it and stores the comment with `ModerationStatus.Pending`
and returns `CommentModerationOutcome.AcceptedPendingModeration`; `CommentsController` turns that into
`202 Accepted` with the stored comment.

* A **Pending comment is never returned** by `GET /api/articles/{id}/comments` (only `Approved`
  comments are listed) and is not put in the CommentCache - so no unmoderated text is ever shown.
* `PendingCommentModerator` (`CommentService.WebAPI/PendingCommentModerator.cs`, a `BackgroundService`)
  runs every 15 s and calls `CommentService.ReModeratePendingAsync`: oldest 50 pending comments are
  re-checked; clean → `Approved` (article dropped from the CommentCache so the next read reloads it),
  profane → `Rejected`. If ProfanityService is still down the check fails fast and the rest stay pending.
* Tradeoff: `POST` keeps working during a ProfanityService outage, at the cost of temporarily storing
  un-moderated text (hidden) and one extra background job.

### Manually demonstrating fault isolation

```bash
# 1. Everything up
docker compose up --build -d

# 2. A clean comment succeeds (201)
curl -i -X POST http://localhost:8090/api/articles/1/comments \
  -H "Content-Type: application/json" \
  -d '{"author":"Alice","text":"What a lovely, uplifting story."}'

# 3. Kill the ProfanityService swimlane
docker compose stop profanityservice

# 4. Post again a few times. First 1–3 calls: ~2 s then 202 (timeout -> stored as Pending).
#    After MinimumThroughput failures the circuit OPENS and calls return 202 *instantly*.
for i in $(seq 1 8); do
  curl -s -o /dev/null -w "attempt %{http_code}  %{time_total}s\n" \
    -X POST http://localhost:8090/api/articles/1/comments \
    -H "Content-Type: application/json" \
    -d '{"author":"Alice","text":"trying again"}'
done
# => first couple: "202  ~2.0s"  (timeout, breaker still closed)
#    then:          "202  ~0.01s" (breaker OPEN — fast fail, no call attempted)

# 5. CommentService itself stays healthy the whole time (separate swimlane)
curl -s http://localhost:8090/api/health          # 200 {"status":"ok",...}
curl -s http://localhost:8090/api/articles/1/comments   # 200 — the clean comment from step 2 is still served

# 6. Bring ProfanityService back; within ~BreakDuration the circuit half-opens and recovers
docker compose start profanityservice
sleep 20
curl -i -X POST http://localhost:8090/api/articles/1/comments \
  -H "Content-Type: application/json" \
  -d '{"author":"Alice","text":"back to normal now"}'   # 201
# The "trying again" comments from step 4 were Pending (hidden). Within ~15 s the
# PendingCommentModerator approves them, and they now show up in the list:
sleep 20
curl -s http://localhost:8090/api/articles/1/comments
```

### API — ProfanityService (`http://localhost:8091`)

| Method | Route | Notes |
| --- | --- | --- |
| `POST` | `/api/profanity/check` | Body `{ "text": "..." }` → `{ "containsProfanity": bool, "matchedWords": [ ... ] }`. → `200` / `400` |
| `GET` | `/api/profanity/words` | List the backing word list (seeded on migration). → `200` |
| `POST` | `/api/profanity/words` | Body `{ "word": "..." }` — add a word (bonus CRUD). → `201` / `400` / `409` |
| `DELETE` | `/api/profanity/words/{id}` | Remove a word (bonus CRUD). → `204` / `404` |
| `GET` | `/api/instance`, `/api/health` | Same diagnostics shape as ArticleService. |

```bash
curl -s -X POST http://localhost:8091/api/profanity/check \
  -H "Content-Type: application/json" -d '{"text":"this is a damn shame"}'
# {"containsProfanity":true,"matchedWords":["damn"]}

curl -s http://localhost:8091/api/profanity/words
curl -i -X POST http://localhost:8091/api/profanity/words \
  -H "Content-Type: application/json" -d '{"word":"frell"}'
curl -i -X DELETE http://localhost:8091/api/profanity/words/1
```

### API — CommentService (`http://localhost:8090`)

| Method | Route | Notes |
| --- | --- | --- |
| `POST` | `/api/articles/{articleId}/comments` | Body `{ "author": "...", "text": "..." }`. Profanity‑checked first. → `201` clean · `422` profanity (+ `matchedWords`) · `400` invalid · `202` ProfanityService unavailable (stored as Pending, hidden until re-moderated) |
| `GET` | `/api/articles/{articleId}/comments` | All comments for an article, newest first. → `200` |
| `GET` | `/api/instance`, `/api/health` | Same diagnostics shape as ArticleService. `/api/health` does **not** probe ProfanityService. |

```bash
# clean -> 201
curl -i -X POST http://localhost:8090/api/articles/1/comments \
  -H "Content-Type: application/json" \
  -d '{"author":"Bob","text":"Genuinely made my morning."}'

# profanity -> 422 with the matched words
curl -i -X POST http://localhost:8090/api/articles/1/comments \
  -H "Content-Type: application/json" \
  -d '{"author":"Bob","text":"this is crap"}'

# list
curl -s http://localhost:8090/api/articles/1/comments
```

---

## DraftService (unpublished article drafts)

Same shape as CommentService/ProfanityService: its own host process, own database
(`HappyHeadlines_Drafts`), plain CRUD in Core against `DraftDbContext` directly - no repository
layer. **Not** routed through the Gateway (the Gateway fronts only ArticleService's x-axis).
A draft's `continent` is optional (`Continent?`) since it may not be decided yet; `title`/`content`/
`author` are required.

### API — DraftService (`http://localhost:8092`)

| Method | Route | Notes |
| --- | --- | --- |
| `POST` | `/api/drafts` | Body `{ "title", "content", "author", "continent"? }`. → `201` + `Location` |
| `GET` | `/api/drafts/{id}` | Read one. → `200` / `404` |
| `GET` | `/api/drafts?author={author}` | List, optionally filtered to one author, newest first. → `200` |
| `PUT` | `/api/drafts/{id}` | Full replace, same body shape as create. → `200` / `400` / `404` |
| `DELETE` | `/api/drafts/{id}` | → `204` / `404` |
| `GET` | `/api/instance`, `/api/health` | Same diagnostics shape as the other services. |

```bash
curl -i -X POST http://localhost:8092/api/drafts \
  -H "Content-Type: application/json" \
  -d '{"title":"Community garden feeds a whole street","content":"Draft text...","author":"A. Reporter","continent":"Europe"}'

curl -s http://localhost:8092/api/drafts?author=A.+Reporter
```

---

## Observability — logging & distributed tracing

Every service (Gateway, ArticleService, CommentService, ProfanityService, DraftService) wires the
**same** logging + tracing setup by calling `HappyHeadlines.Observability` from its own
`Program.cs` - nothing per-service to configure beyond a `serviceName` string:

```csharp
builder.AddHappyHeadlinesObservability("CommentService");   // logging, tracing, metrics -> OTLP
// ...
app.UseHappyHeadlinesRequestLogging();                       // one structured log line per request
```

`AddHappyHeadlinesObservability` (`HappyHeadlines.Observability/ObservabilityExtensions.cs`) wires
OpenTelemetry logging + ASP.NET Core/HttpClient/EF Core trace and metric instrumentation, tagged
with the service name and the same `INSTANCE_ID` used elsewhere (X-Instance-Id header, `/api/instance`).
The OTLP destination is **never hardcoded** - `UseOtlpExporter()` reads it from the
`OTEL_EXPORTER_OTLP_ENDPOINT` / `OTEL_EXPORTER_OTLP_PROTOCOL` environment variables, set once per
service in `docker-compose.yml`. That is the actual point of centralizing this in one library: same
call, same code, same destination, everywhere - so a request that crosses services (Gateway →
ArticleService, CommentService → ProfanityService, and anything → DraftService in future) is one
correlated trace instead of N unrelated log streams. Confirmed manually: a `POST
/api/articles/{id}/comments` shows up in the dashboard as a single trace containing the CommentService
HTTP span, the outbound call to ProfanityService, and both services' SQL spans underneath.

`UseHappyHeadlinesRequestLogging()` (`RequestLoggingMiddlewareExtensions.cs`) adds one more piece:
a single structured log line per request - HTTP method, path, status code, duration in ms, and
instance id - at `Information`, or `Warning` when the response is `5xx`. It never logs request or
response bodies.

### Viewing it: Aspire Dashboard

`docker compose up` also starts `aspire-dashboard` (`mcr.microsoft.com/dotnet/aspire-dashboard`),
an OTLP collector + viewer with no other dependency on the rest of the stack - no service has a
blocking `depends_on` on it, so if it's down the other services keep serving, they just have
nowhere to send telemetry until it comes back (same philosophy as CommentService not depending on
ProfanityService).

1. Open **http://localhost:18888** after `docker compose up`.
2. **Traces** tab: hit any endpoint (e.g. `curl http://localhost:8090/api/articles/1/comments -d ...`)
   and find the request - expand it to see the CommentService span, the nested ProfanityService
   span, and the EF Core spans underneath both.
3. **Structured logs** tab: filter by `Resource` (service name) or `Level`; each row's `Trace`
   column links back to the trace it happened inside, so a log line and its trace are one click apart.

Services publish to the dashboard's **internal** OTLP endpoint on the Docker network,
`http://aspire-dashboard:18889` (gRPC) - not the host-mapped port, since that's only relevant to
something outside the Docker network sending telemetry directly. The dashboard's own UI is
published to the host on `18888`.

### What to log, and when

Guidelines followed throughout the codebase (see `CommentService.CreateAsync` and the circuit
breaker callbacks in `CommentService.WebAPI/Program.cs` for concrete examples):

| Level | When | Example |
| --- | --- | --- |
| **Information** | Every request/response (via `UseHappyHeadlinesRequestLogging`); business events - draft created/updated/deleted, shard selected, profanity match found, circuit breaker changed state | `Draft {DraftId} created by {Author}.` |
| **Warning** | Degraded-but-handled behaviour - comment stored as Pending because ProfanityService is unavailable, circuit breaker opened | `ProfanityService circuit breaker OPENED for {BreakDurationSeconds}s ...` |
| **Error** | Unexpected exceptions, always logged before an error response is returned to the client | *(none currently expected in the happy paths covered by this brief)* |
| **Debug** (dev only, **never** in production) | Full request/response bodies, if ever needed for local troubleshooting | not currently used - nothing in this codebase logs bodies at any level |

Rules that apply at every level:

- **Never log full article/comment/draft bodies at Information level** - only ids and metadata
  (e.g. `DraftId`, `Author`), never `Title`/`Content`/`Text`. A log stream is not a content store,
  and it is far more widely readable than the database it's describing.
- **Always use structured log parameters**, never string interpolation:
  `_logger.LogInformation("Draft {DraftId} created by {Author}", id, author)` - never
  `$"Draft {id} created by {author}"`. Interpolating the values into the message string defeats the
  structured backend (Aspire Dashboard here; any OTLP-speaking backend in general) - you lose the
  ability to filter/aggregate by `DraftId` or `Author` as fields.
- **Trace correlation across services is automatic** via `AddHttpClientInstrumentation()` /
  `AddAspNetCoreInstrumentation()` - there is no manual correlation-id header to thread through
  `HttpClient` calls; the OTel SDK propagates trace context on outbound requests and picks it back
  up on the receiving side.

---

## Caching — ArticleCache & CommentCache (W40 assignment)

**Problem:** European readers are slow/unavailable because the *global* ArticleDatabase lives in North
America and is not replicated. ARB decided: add a cache layer instead of x-axis replication.

```
 Reader ─► Gateway ─► ArticleService (x3) ──► ArticleCache (Redis) ──miss──► Global ArticleDatabase
                                                    ▲
                                  ArticleCacheWorker (offline, every 30 s: last 14 days)

 Reader ─► CommentService ──► CommentCache (in-process LRU, 30 articles) ──miss──► CommentDatabase
 
 ArticleService x3 + CommentService ──/metrics──► Prometheus ──► Grafana (hit ratio per cache)
```

| | ArticleCache | CommentCache |
|---|---|---|
| Store | Redis (`redis:7-alpine`), shared by the 3 ArticleService instances | In-process `LruCache` inside CommentService (single instance) |
| Filled by | **Offline** `ArticleCacheWorker` (`BackgroundService`, own container) copies the last 14 days of **Global** articles every 30 s | **Cache-miss** approach: a read of an article's comments that misses loads *all* its comments from the DB |
| Read path | `GET /api/articles[/{id}]?continent=Global` → Redis first, DB on miss (never written back on a miss) | `GET /api/articles/{id}/comments` → cache first, DB on miss |
| Size/eviction | TTL = 3 × refresh interval | max **30 articles**; the 31st evicts the **least recently used** article (`LruCache`: dictionary + linked list) |
| Consistency | `PUT`/`DELETE` of a Global article drops its key (+ the list); `POST` drops the list | `POST` comment (after DB commit) is added to the article's cached list if cached; otherwise nothing |
| Redis down | every call is a logged miss → ArticleService keeps serving from the DB | – |

Choices: Redis for ArticleCache because the offline writer and the 3 instances must share it; an own LRU
for CommentCache because "max 30 *articles*, evict per article" needs control that Redis `allkeys-lru`
(memory/key based) does not give, and the class is ~100 lines and easy to unit test. Only the **Global**
shard is cached (continent shards are local); the Global list endpoint always returns the last 14 days
(same answer on a hit and on a miss). Known limits: ArticleCache is eventually consistent (at most one
refresh interval behind for new articles); a comment POST racing with a miss-fill of the same article can
leave a stale list until it is evicted.

### Start the system and see the dashboard

```bash
docker compose up --build -d
```

* **Grafana dashboard** — http://localhost:3000 (anonymous, opens straight on *HappyHeadlines - Cache hit ratio*;
  datasource + dashboard are provisioned from `monitoring/`, nothing to click)
* Prometheus — http://localhost:9090 (query e.g. `cache_hits_total`, `cache_misses_total`, `cache_evictions_total`; label `cache`)
* Raw metrics — `GET /metrics` on each cache-owning service (published for CommentService: http://localhost:8090/metrics)
* CommentCache state (articles in LRU order, hits/misses/evictions) — http://localhost:8090/api/cache
* Redis contents — `docker exec happyheadlines-redis redis-cli keys "articles:*"`
* Logs/traces — Aspire Dashboard http://localhost:18888: cache misses/evictions are logged
  (`ArticleCache MISS …`, `CommentCache full … evicted …`) and the request span carries the tags
  `cache.name` and `cache.hit`.

### Demo: hit ratio rising + LRU eviction

```powershell
powershell -ExecutionPolicy Bypass -File scripts/demo-cache.ps1
```

The script creates a recent and an old (30 days) Global article, waits for the worker refresh, reads them
repeatedly (hits for the recent one, misses for the old one), then reads 31 different articles' comments to
force an LRU eviction and prints hit ratios from Prometheus. Keep Grafana open next to it.

### Tests

```bash
dotnet test HappyHeadlines.Core.Tests
```

`LruCacheTests` (30 articles, eviction of the least recently used, reads protect an entry), `CommentCacheTests`,
`CommentServiceCacheTests` (miss→hit, consistency on new comments, eviction through the service) and
`ArticleCacheTests` (offline refresh of 14 days, hit/miss, DB fallback, Redis down) — in-memory stand-ins, no Docker needed.

---

## Out of scope

Auth, cross‑shard moves, and a background
re‑moderation job for the "pending" degrade strategy are intentionally left out — this is a course
assignment demonstrating the X‑axis, Z‑axis, swimlane and central-observability patterns, not a
production system.
