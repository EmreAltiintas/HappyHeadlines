# Observability

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
