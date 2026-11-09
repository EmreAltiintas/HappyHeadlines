# HappyHeadlines

.NET 10 microservices implementation of the HappyHeadlines C4 architecture, built for the semester project at EASV. Clean Architecture, EF Core with SQL Server, and everything runs in Docker Compose.

## Run it

```
docker compose up --build
```

Wipe the databases with `docker compose down -v`. The SQL Server SA password has a development default in `docker-compose.yml`; set `MSSQL_SA_PASSWORD` to override it.

## Services

| Service | Port | What it does |
| --- | --- | --- |
| Gateway | 8088 | YARP round-robin in front of 3 ArticleService instances (the only way in) |
| CommentService | 8090 | Comments, profanity-checked through ProfanityService |
| ProfanityService | 8091 | Profanity check and word list |
| DraftService | 8092 | Unpublished article drafts |
| PublisherService | 8093 | Publishes articles to ArticleQueue |
| NewsletterService | 8094 | Daily and immediate newsletters |
| SubscriberService | 8095 | Subscribe and unsubscribe, own database and queue |
| Aspire Dashboard | 18888 | Logs and traces from all services |
| Grafana | 3000 | Cache hit ratio |
| Prometheus | 9090 | Metrics |

## Patterns

Z-axis split (8 article databases, one per continent plus Global), X-axis split (3 ArticleService instances behind the gateway), swimlanes (Comment, Profanity, Draft and Subscriber each have their own process and database), central observability with OpenTelemetry, caching (Redis ArticleCache and an in-process LRU CommentCache), and a release toggle for SubscriberService.

## Docs

- [Architecture](docs/architecture.md): pattern overview, tech stack, solution layout, shard router
- [Running and API reference](docs/running.md): Docker Compose, migrations, endpoints, curl examples
- [Swimlanes](docs/swimlanes.md): Comment, Profanity and Draft services, circuit breaker, fault isolation demo
- [Observability](docs/observability.md): logging, tracing, Aspire Dashboard
- [Caching](docs/caching.md): ArticleCache and CommentCache (week 40)
- [SubscriberService, release toggle and CI/CD](docs/week44-subscriber-cicd.md) (week 44)
- [Sustainability tactics](docs/gaf-w45.md): pagination, trace sampling, alpine images (week 45)

## Tests

```
dotnet test HappyHeadlines.slnx
```

## Not implemented

Authentication and moving articles between shards.
