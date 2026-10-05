# Demo: watch the cache hit ratio rise and the CommentCache LRU eviction happen.
# Prereq: `docker compose up --build -d` (Grafana: http://localhost:3000, shows the same numbers).
# Usage:  powershell -ExecutionPolicy Bypass -File scripts/demo-cache.ps1
$ErrorActionPreference = "Stop"
$gateway  = "http://localhost:8088"   # ArticleService (3 instances behind the gateway)
$comments = "http://localhost:8090"   # CommentService
$prom     = "http://localhost:9090"

function Show-Ratio([string]$title) {
    Start-Sleep -Seconds 10   # Prometheus scrapes every 5 s
    Write-Host "`n--- $title ---" -ForegroundColor Cyan
    foreach ($cache in "ArticleCache", "CommentCache") {
        $q = "sum(cache_hits_total{cache=`"$cache`"}) / (sum(cache_hits_total{cache=`"$cache`"}) + sum(cache_misses_total{cache=`"$cache`"}))"
        $r = Invoke-RestMethod "$prom/api/v1/query" -Body @{ query = $q }
        $v = if ($r.data.result.Count) { "{0:P0}" -f [double]$r.data.result[0].value[1] } else { "n/a" }
        Write-Host ("{0,-13} hit ratio (all time): {1}" -f $cache, $v)
    }
}

function New-Article([int]$daysAgo, [string]$title) {
    $body = @{
        title = $title; content = "demo"; author = "demo"; continent = "Global"
        publishedDate = (Get-Date).ToUniversalTime().AddDays(-$daysAgo).ToString("o")
    } | ConvertTo-Json
    (Invoke-RestMethod "$gateway/api/articles" -Method Post -ContentType "application/json" -Body $body).id
}

# ---------------------------------------------------------------- ArticleCache
Write-Host "== 1. ArticleCache (Redis, filled offline by ArticleCacheWorker) ==" -ForegroundColor Yellow
$recent = New-Article 1 "Demo: recent article (inside 14 days)"
$old    = New-Article 30 "Demo: old article (outside 14 days)"
Write-Host "Created recent article $recent and old article $old in the Global shard."
Write-Host "Waiting 35 s for the worker's next refresh to cache the recent article..."
Start-Sleep -Seconds 35

1..20 | ForEach-Object { Invoke-RestMethod "$gateway/api/articles/${recent}?continent=Global" | Out-Null }
Write-Host "20 reads of the recent article -> served from Redis (hits)."
Show-Ratio "After cache hits"

1..10 | ForEach-Object { Invoke-RestMethod "$gateway/api/articles/${old}?continent=Global" | Out-Null }
Write-Host "10 reads of the old article -> miss, fall back to the database every time."
Show-Ratio "After misses on the old article (ratio drops)"

# ---------------------------------------------------------------- CommentCache
Write-Host "`n== 2. CommentCache (LRU, max 30 articles) ==" -ForegroundColor Yellow
$base = Get-Random -Minimum 1000 -Maximum 900000   # fresh article ids, so every first read is a miss
$ids  = $base..($base + 29)
Invoke-RestMethod "$comments/api/articles/$($ids[0])/comments" -Method Post -ContentType "application/json" `
    -Body (@{ author = "demo"; text = "first comment" } | ConvertTo-Json) | Out-Null

foreach ($id in $ids) { Invoke-RestMethod "$comments/api/articles/$id/comments" | Out-Null }
Write-Host "Read comments of 30 different articles (misses) -> cache is full (earlier runs may already have caused evictions)."
$state = Invoke-RestMethod "$comments/api/cache"
Write-Host "Cached articles: $($state.cachedArticleCount)/$($state.capacity), evictions: $($state.evictions)"

1..20 | ForEach-Object { Invoke-RestMethod "$comments/api/articles/$($ids[0])/comments" | Out-Null }
Write-Host "20 repeat reads of article $($ids[0]) -> hits, and it becomes the MOST recently used."

$next = $base + 30
Invoke-RestMethod "$comments/api/articles/$next/comments" | Out-Null
$state = Invoke-RestMethod "$comments/api/cache"
$lru = $ids[1]
Write-Host "Read a 31st article ($next) -> evictions: $($state.evictions)." -ForegroundColor Green
Write-Host "Article $lru (least recently used) evicted: $($state.cachedArticleIds -notcontains $lru)"
Write-Host "Article $($ids[0]) (kept alive by the hits) still cached: $($state.cachedArticleIds -contains $ids[0])"
Write-Host "Cache now (most recent first): $($state.cachedArticleIds -join ', ')"

$list = Invoke-RestMethod "$comments/api/articles/$($ids[0])/comments"
Write-Host "`nConsistency: posting a comment to a cached article updates the cache in place."
Invoke-RestMethod "$comments/api/articles/$($ids[0])/comments" -Method Post -ContentType "application/json" `
    -Body (@{ author = "demo"; text = "second comment" } | ConvertTo-Json) | Out-Null
$after = Invoke-RestMethod "$comments/api/articles/$($ids[0])/comments"
Write-Host "Comments before: $($list.Count), after POST (served from cache): $($after.Count)"

Show-Ratio "Final"
Write-Host "`nOpen http://localhost:3000 to see the same data in Grafana." -ForegroundColor Green
