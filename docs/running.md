# Running and API reference

## Run everything with Docker Compose

```bash
docker compose up --build
```

This starts:

* `sqlserver` - one SQL Server 2022 container hosting all 11 databases (8 article shards + comments + profanity + drafts)
* `articleservice1`, `articleservice2`, `articleservice3` - the ArticleService WebAPI, same image, different `INSTANCE_ID`
* `gateway` - YARP load balancer for ArticleService, published on **http://localhost:8088**
* `commentservice` - CommentService WebAPI, published on **http://localhost:8090**
* `profanityservice` - ProfanityService WebAPI, published on **http://localhost:8091**
* `draftservice` - DraftService WebAPI, published on **http://localhost:8092**
* `redis`, `articlecacheworker`, `prometheus`, `grafana` - ArticleCache, its offline filler and the cache hit-ratio dashboard on **http://localhost:3000** (see [caching](caching.md))
* `aspire-dashboard` - OTLP sink for logs/traces/metrics from every service above, UI on **http://localhost:18888** (see [observability](observability.md))

The gateway fronts ArticleService only. `commentservice`, `profanityservice` and `draftservice` are
published directly (no gateway); commentservice and profanityservice talk to each other over the
Docker network. Stop with `Ctrl+C`; wipe the databases with `docker compose down -v`.

The SA password defaults to `Your_strong_Pass123`. Override it by exporting `MSSQL_SA_PASSWORD`
before `docker compose up` (it is substituted into every connection string).

---

## Migrations - applying the schema to all 8 databases

### Automatic (default)

`ShardMigrator` calls `Database.MigrateAsync()` against **all 8 shards** on startup, creating the
databases if missing and retrying while SQL Server finishes booting. It runs when
`MIGRATE_ON_STARTUP=true` (the default).

In Docker Compose only **articleservice1** migrates; `articleservice2` and `articleservice3` set
`MIGRATE_ON_STARTUP=false` and wait for it. The container health check (`GET /api/health`) reports
`503` while migrations are pending, so Compose starts the services in order - `sqlserver` healthy →
`articleservice1` migrates and turns healthy → `articleservice2/3` start → `gateway` starts once all
three are healthy.

The comment, profanity and draft services do the same thing for their single database on startup
(`MIGRATE_ON_STARTUP=true`), creating `HappyHeadlines_Comments` / `HappyHeadlines_Profanity` /
`HappyHeadlines_Drafts` and, for profanity, seeding the starter word list from the migration.

### Manual (`dotnet ef database update` per shard)

`HappyHeadlines.Db` now holds **four** contexts, so every `dotnet ef` command needs
`--context` (`ArticleDbContext`, `CommentDbContext`, `ProfanityDbContext` or `DraftDbContext`). The
design-time factories read the `EF_CONNECTION_STRING` environment variable. SQL Server must be
running (`docker compose up sqlserver`):

```powershell
# from repo root; PowerShell - the 8 article shards
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
# bash - the 8 article shards
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
| `GET` | `/api/instance` | Returns this instance's `instanceId` - used to observe load balancing. |
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

Because the ArticleService is stateless, which instance answers never matters - the articles all come
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

The gateway's destinations are container hostnames, so the round-robin demo is a Docker-Compose
scenario; a single local ArticleService instance is enough for exercising the CRUD endpoints directly
on port 5215. Run ProfanityService before CommentService, or watch CommentService fast-fail the
profanity check and store the comment as Pending (`202`).

Outside Docker Compose, `OTEL_EXPORTER_OTLP_ENDPOINT` is unset, so `AddHappyHeadlinesObservability`
has nowhere to export to - every service still runs and logs to the console exactly as before,
it just has no OTLP destination. Run a local Aspire Dashboard container to get one:
`docker run --rm -it -p 18888:18888 -p 18889:18889 -e DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true mcr.microsoft.com/dotnet/aspire-dashboard:latest`,
then set `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:18889` and `OTEL_EXPORTER_OTLP_PROTOCOL=grpc`
before `dotnet run`.

---
