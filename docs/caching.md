# Caching

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

* **Grafana dashboard** - http://localhost:3000 (anonymous, opens straight on *HappyHeadlines - Cache hit ratio*;
  datasource + dashboard are provisioned from `monitoring/`, nothing to click)
* Prometheus - http://localhost:9090 (query e.g. `cache_hits_total`, `cache_misses_total`, `cache_evictions_total`; label `cache`)
* Raw metrics - `GET /metrics` on each cache-owning service (published for CommentService: http://localhost:8090/metrics)
* CommentCache state (articles in LRU order, hits/misses/evictions) - http://localhost:8090/api/cache
* Redis contents - `docker exec happyheadlines-redis redis-cli keys "articles:*"`
* Logs/traces - Aspire Dashboard http://localhost:18888: cache misses/evictions are logged
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
`ArticleCacheTests` (offline refresh of 14 days, hit/miss, DB fallback, Redis down) - in-memory stand-ins, no Docker needed.

---
