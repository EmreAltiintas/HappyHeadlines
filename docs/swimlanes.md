# Swimlanes: Comment, Profanity and Draft services

### Why they are separate host projects (but share `Core` / `Db`)

`Core` and `Db` are **compiled class libraries** - sharing them is just code reuse and creates no
runtime coupling. Fault isolation (Nygard's *swimlane* principle) is about the **running system**:

| Shared? | ArticleService | CommentService | ProfanityService | DraftService |
| --- | --- | --- | --- | --- |
| OS process / container | `articleservice1..3` | `commentservice` | `profanityservice` | `draftservice` |
| Database | 8 article shards | `HappyHeadlines_Comments` | `HappyHeadlines_Profanity` | `HappyHeadlines_Drafts` |
| EF `DbContext` type | `ArticleDbContext` | `CommentDbContext` | `ProfanityDbContext` | `DraftDbContext` |
| Connection pool | per shard | own | own | own |
| In-process call path | - | - | - | - |

(DraftService has no swimlane dependency on any other service - it is plain CRUD against its own
database, listed here only to show it follows the same isolation shape.)

CommentService reaches ProfanityService **only** by an HTTP REST call to
`http://profanityservice:8080/api/profanity/check` - never in-process, never through
`HappyHeadlines.Gateway`, never through any other middle layer. So if ProfanityService's process
crashes, its database locks up, or it just gets slow, the blast radius stops at that HTTP call:
CommentService's own process, database and pool are untouched, and the circuit breaker turns the
slow/failed call into an immediate, cheap failure.

If they instead shared one process or one database, a lock, a memory leak, a connection-pool
exhaustion or an unhandled exception in the profanity path could take comments down with it - the
opposite of a swimlane.

### Circuit breaker + timeout configuration

Configured in `HappyHeadlines.CommentService.WebAPI/Program.cs` on the typed `IProfanityClient`
HTTP client via `Microsoft.Extensions.Http.Resilience` (`AddResilienceHandler`). Values come from the
`ProfanityService` config section (`appsettings.json` / env), defaults:

| Setting | Default | Why |
| --- | --- | --- |
| Per-attempt **timeout** | **2 s** | A profanity check is a single indexed lookup. If it hasn't answered in 2 s it's effectively down - don't let the caller wait on `HttpClient.Timeout` (100 s). |
| Circuit **FailureRatio** | **0.5** | Open once half the recent calls in the window are failing. |
| Circuit **MinimumThroughput** | **4** | Need ≥ 4 calls in the window before the breaker can trip - one stray error won't open it. |
| Circuit **SamplingDuration** | **10 s** | Rolling window the ratio is measured over. |
| Circuit **BreakDuration** | **15 s** | While open, every call fails instantly for 15 s; then one trial call decides re-open vs. close. |

The breaker is the **outer** strategy and the timeout the **inner** one, so a timed-out call counts
as a failure toward opening the circuit. No retry is configured on purpose: retries would add latency
and load exactly when ProfanityService is already struggling, working against "fail fast".

The breaker's own state transitions are logged explicitly (category
`HappyHeadlines.CommentService.ProfanityCircuitBreaker`, via `OnOpened`/`OnClosed`/`OnHalfOpened` on
`HttpCircuitBreakerStrategyOptions`) - this is the concrete "what and when to log" example from the
[observability doc](observability.md): a breaker flipping open
is a business event, not just an HTTP status code, so it gets a dedicated `LogWarning`/`LogInformation`
line rather than being inferred from request logs:

| Transition | Level | Message |
| --- | --- | --- |
| Opened | Warning | `ProfanityService circuit breaker OPENED for {BreakDurationSeconds}s - calls will fail fast until it half-opens.` |
| Half-opened | Information | `ProfanityService circuit breaker HALF-OPEN - trialing a single call.` |
| Closed | Information | `ProfanityService circuit breaker CLOSED - calls are flowing normally again.` |

The fallback path itself (`CommentService.CreateAsync` when the profanity check can't complete)
logs a `LogWarning` with the article id and exception type - see `Comments/CommentService.cs`.

### Fallback - the circuit breaker "takes over" by storing the comment as Pending

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
#    then:          "202  ~0.01s" (breaker OPEN - fast fail, no call attempted)

# 5. CommentService itself stays healthy the whole time (separate swimlane)
curl -s http://localhost:8090/api/health          # 200 {"status":"ok",...}
curl -s http://localhost:8090/api/articles/1/comments   # 200 - the clean comment from step 2 is still served

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

### API - ProfanityService (`http://localhost:8091`)

| Method | Route | Notes |
| --- | --- | --- |
| `POST` | `/api/profanity/check` | Body `{ "text": "..." }` → `{ "containsProfanity": bool, "matchedWords": [ ... ] }`. → `200` / `400` |
| `GET` | `/api/profanity/words` | List the backing word list (seeded on migration). → `200` |
| `POST` | `/api/profanity/words` | Body `{ "word": "..." }` - add a word (bonus CRUD). → `201` / `400` / `409` |
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

### API - CommentService (`http://localhost:8090`)

| Method | Route | Notes |
| --- | --- | --- |
| `POST` | `/api/articles/{articleId}/comments` | Body `{ "author": "...", "text": "..." }`. Profanity-checked first. → `201` clean · `422` profanity (+ `matchedWords`) · `400` invalid · `202` ProfanityService unavailable (stored as Pending, hidden until re-moderated) |
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

### API - DraftService (`http://localhost:8092`)

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
