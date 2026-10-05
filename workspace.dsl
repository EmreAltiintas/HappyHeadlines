workspace "HappyHeadlines" "C4 model: services, caches and monitoring. Not built, so not drawn: Webapp, Website, SubscriberService + SubscriberDatabase + SubscriberQueue (NewsletterService only logs 'would send')." {

    !identifiers hierarchical

    model {
        reader = person "Reader" "Reads articles and posts comments."
        editor = person "Editor" "Writes drafts and publishes articles."

        happyHeadlines = softwareSystem "HappyHeadlines" "News platform." {

            gateway = container "Gateway" "YARP load balancer, round-robin over the 3 ArticleService instances." "ASP.NET Core / YARP"

            articleService = container "ArticleService (x3)" "Article CRUD. Global shard is read through ArticleCache first." "ASP.NET Core Web API (x-axis: 3 instances)"
            articleCache = container "ArticleCache" "Last 14 days of Global articles. BOUGHT (Redis, non-core)." "Redis" "Cache"
            articleCacheWorker = container "ArticleCacheWorker" "Offline: every 30 s copies the last 14 days of Global articles into ArticleCache." ".NET BackgroundService"
            articleDb = container "ArticleDatabase" "8 shards (7 continents + Global), z-axis split." "SQL Server" "Database"

            commentService = container "CommentService" "Comments. Reads through CommentCache. If ProfanityService is down, comments are stored as Pending." "ASP.NET Core Web API" {
                commentCache = component "CommentCache" "In-process LRU: all comments of max 30 articles, evicts the least recently used article." "LruCache"
            }
            commentDb = container "CommentDatabase" "Comments." "SQL Server" "Database"
            profanityService = container "ProfanityService" "Profanity check." "ASP.NET Core Web API"
            profanityDb = container "ProfanityDatabase" "Banned words." "SQL Server" "Database"

            draftService = container "DraftService" "Unpublished drafts." "ASP.NET Core Web API"
            draftDb = container "DraftDatabase" "Drafts." "SQL Server" "Database"
            publisherService = container "PublisherService" "Publishes articles onto ArticleQueue." "ASP.NET Core Web API"
            newsletterService = container "NewsletterService" "Immediate + daily newsletters." "ASP.NET Core Web API"
            articleQueue = container "ArticleQueue" "Article fan-out. BOUGHT (RabbitMQ, non-core)." "RabbitMQ / MassTransit" "Queue"

            # Monitoring is non-core, so it is BOUGHT (off-the-shelf), not built. Every service sends
            # logs/traces to Aspire over OTLP (one shared library, HappyHeadlines.Observability); those
            # eight identical arrows are left out of the diagram on purpose and stated here instead.
            aspire = container "Aspire Dashboard" "BOUGHT. Receives logs and traces from ALL services (OTLP)." "Aspire Dashboard" "Monitoring"
            prometheus = container "Prometheus" "BOUGHT. Scrapes cache + circuit breaker metrics. Alert rules." "Prometheus" "Monitoring"
            grafana = container "Grafana" "BOUGHT. Cache hit ratio per cache + firing alerts." "Grafana" "Monitoring"
        }

        reader -> happyHeadlines.gateway "Reads articles"
        reader -> happyHeadlines.commentService "Reads / posts comments"
        editor -> happyHeadlines.draftService "Writes drafts"
        editor -> happyHeadlines.publisherService "Publishes"
        editor -> happyHeadlines.grafana "Views dashboard"

        happyHeadlines.gateway -> happyHeadlines.articleService "Round-robin"
        happyHeadlines.articleService -> happyHeadlines.articleCache "1. Cache first"
        happyHeadlines.articleService -> happyHeadlines.articleDb "2. On miss"
        happyHeadlines.articleCacheWorker -> happyHeadlines.articleDb "Reads 14 days"
        happyHeadlines.articleCacheWorker -> happyHeadlines.articleCache "Fills"

        happyHeadlines.commentService.commentCache -> happyHeadlines.commentDb "On miss"
        happyHeadlines.commentService -> happyHeadlines.commentDb "Writes"
        happyHeadlines.commentService -> happyHeadlines.profanityService "Check (circuit breaker)"
        happyHeadlines.profanityService -> happyHeadlines.profanityDb "Reads"

        happyHeadlines.draftService -> happyHeadlines.draftDb "Reads / writes"
        happyHeadlines.publisherService -> happyHeadlines.articleQueue "Publishes"
        happyHeadlines.articleQueue -> happyHeadlines.articleService "Delivers"
        happyHeadlines.articleQueue -> happyHeadlines.newsletterService "Delivers"
        happyHeadlines.newsletterService -> happyHeadlines.gateway "Reads articles"

        happyHeadlines.prometheus -> happyHeadlines.articleService "Scrapes /metrics"
        happyHeadlines.prometheus -> happyHeadlines.commentService "Scrapes /metrics"
        happyHeadlines.grafana -> happyHeadlines.prometheus "Queries"
    }

    views {
        systemContext happyHeadlines "SystemContext" {
            include *
            autolayout lr
        }

        container happyHeadlines "Containers" {
            include *
            autolayout lr 250 150
        }

        component happyHeadlines.commentService "CommentServiceComponents" {
            include *
            autolayout lr
        }

        styles {
            element "Person" {
                shape person
                background #08427b
                color #ffffff
            }
            element "Container" {
                background #438dd5
                color #ffffff
            }
            element "Component" {
                background #85bbf0
                color #000000
            }
            element "Database" {
                shape cylinder
            }
            element "Cache" {
                shape cylinder
                background #d9822b
            }
            element "Queue" {
                shape pipe
            }
            element "Monitoring" {
                background #2e8b57
            }
            relationship "Relationship" {
                thickness 3
                color #555555
                fontSize 22
                routing Orthogonal
            }
        }
    }
}
