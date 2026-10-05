using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HappyHeadlines.Core.Caching;

/// <summary>
/// Hit / miss / eviction counters for one named cache (<c>ArticleCache</c> or
/// <c>CommentCache</c>). The counters are shared instruments on the
/// <c>HappyHeadlines.Cache</c> meter, tagged with <c>cache=&lt;name&gt;</c>; OpenTelemetry exports
/// them to Prometheus as <c>cache_hits_total{cache="..."}</c> and <c>cache_misses_total{cache="..."}</c>,
/// which is what the Grafana hit-ratio dashboard divides. The same call also tags the current
/// trace span (<c>cache.name</c>, <c>cache.hit</c>) so a single request's trace shows hit vs miss.
/// </summary>
public sealed class CacheMetrics
{
    public const string MeterName = "HappyHeadlines.Cache";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> HitCounter = Meter.CreateCounter<long>("cache.hits", description: "Cache lookups answered from the cache.");
    private static readonly Counter<long> MissCounter = Meter.CreateCounter<long>("cache.misses", description: "Cache lookups that fell through to the database.");
    private static readonly Counter<long> EvictionCounter = Meter.CreateCounter<long>("cache.evictions", description: "Entries evicted because the cache was full.");

    private readonly string _name;
    private readonly KeyValuePair<string, object?> _tag;
    private long _hits;
    private long _misses;
    private long _evictions;

    public CacheMetrics(string cacheName)
    {
        _name = cacheName;
        _tag = new KeyValuePair<string, object?>("cache", cacheName);
    }

    // Per-instance totals; mainly so tests and diagnostics endpoints can read them directly.
    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);
    public long Evictions => Interlocked.Read(ref _evictions);

    public void RecordHit()
    {
        Interlocked.Increment(ref _hits);
        HitCounter.Add(1, _tag);
        TagActivity(hit: true);
    }

    public void RecordMiss()
    {
        Interlocked.Increment(ref _misses);
        MissCounter.Add(1, _tag);
        TagActivity(hit: false);
    }

    public void RecordEviction()
    {
        Interlocked.Increment(ref _evictions);
        EvictionCounter.Add(1, _tag);
    }

    private void TagActivity(bool hit)
    {
        var activity = Activity.Current;
        activity?.SetTag("cache.name", _name);
        activity?.SetTag("cache.hit", hit);
    }
}
