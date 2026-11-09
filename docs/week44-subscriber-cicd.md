# SubscriberService, release toggle and CI/CD (week 44)

SubscriberService lets users subscribe and unsubscribe. It stores subscribers in its own database and puts each new subscriber
on SubscriberQueue. NewsletterService reads subscribers from SubscriberService over REST and consumes SubscriberQueue to send
a welcome mail (the mail itself is only a log line, like the other newsletter sends). The Website is not built.

### Swimlane and fault isolation

* Containers: `subscriberservice` (port 8095), `subscriberdb` (its own SQL Server, port 1434) and `subscriberqueue` (its own RabbitMQ, port 5673, management UI 15673).
* They share the network `subscriber-lane`. Only `subscriberservice` and `newsletterservice` are also on `happyheadlines`. Only SubscriberService can reach the database.
* NewsletterService calls SubscriberService with a Polly retry, circuit breaker and timeout (`SubscriberServiceOptions`). If the call fails, the daily newsletter runs with 0 subscribers and returns `subscribersAvailable: false`. There is no `depends_on` between the lanes.
* Subscribe saves to the database first and then publishes to the queue (3 second limit). If the queue is down it still returns `201`, with `welcomeMailQueued: false`.
* NewsletterService consumes SubscriberQueue on a second MassTransit bus (`ISubscriberQueueBus`) with retry. Messages wait in the queue while NewsletterService is down.

### API: SubscriberService (`http://localhost:8095`)

```bash
curl -X POST localhost:8095/api/subscribers -H "Content-Type: application/json" -d '{"email":"anna@example.com"}'   # subscribe
curl localhost:8095/api/subscribers                                                                                  # list
curl -X DELETE localhost:8095/api/subscribers/anna@example.com                                                       # unsubscribe
curl -X POST "localhost:8094/api/newsletters/daily?continent=Europe"                                                 # NewsletterService reads subscribers
```

### Release toggle

`featureflags/featureflags.json` is mounted into the container and reloaded when it changes, so the toggle can be flipped while
the stack runs, without a redeploy or restart. It can take 5 to 10 seconds to be picked up on Docker Desktop.

```json
{ "FeatureFlags": { "SubscriberService": { "Enabled": false, "Subscribe": true, "Unsubscribe": true, "List": true } } }
```

`Enabled: false` turns the whole service off. The other flags turn off single operations. A switched-off call returns `503` with
`Retry-After` and a message. `/api/health` still returns `200`, so the container is not restarted. NewsletterService treats the
503 like an outage and uses the fallback above.

NewsletterService reads the same `featureflags.json`. When the toggle is off it does not call SubscriberService. It logs a
line and continues with an empty subscriber list.

### CI/CD

`.github/workflows/ci.yml` (GitHub Actions) restores, builds and tests the solution, builds a Docker image for each of the 9
services, and validates `docker-compose.yml`.

`.github/workflows/cd.yml` runs when CI succeeds on `master`. It pushes one image per service to GHCR (tags: commit SHA and
`latest`) and deploys over SSH with `docker compose pull` and `up -d`. Secrets in the `production` environment:
`DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_SSH_KEY`. Without `DEPLOY_HOST` the deploy steps are skipped.

`docker-compose.yml` uses the GHCR images. `docker-compose.override.yml` adds `build:` for local development.

---
