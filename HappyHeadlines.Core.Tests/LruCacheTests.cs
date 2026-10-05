using HappyHeadlines.Core.Caching;

namespace HappyHeadlines.Core.Tests;

public class LruCacheTests
{
    [Fact]
    public void Holds_at_most_30_articles_and_evicts_the_least_recently_used_when_the_31st_arrives()
    {
        var evicted = new List<int>();
        var cache = new LruCache<int, string>(30, evicted.Add);

        for (var articleId = 1; articleId <= 30; articleId++)
            cache.Set(articleId, $"comments-{articleId}");
        Assert.Equal(30, cache.Count);
        Assert.Empty(evicted);

        cache.Set(31, "comments-31");

        Assert.Equal(30, cache.Count);
        Assert.Equal([1], evicted);                         // article 1 was used longest ago
        Assert.False(cache.TryGet(1, out _));
        Assert.True(cache.TryGet(2, out _));
        Assert.True(cache.TryGet(31, out _));
    }

    [Fact]
    public void Reading_an_article_protects_it_so_the_next_least_recently_used_one_is_evicted()
    {
        var evicted = new List<int>();
        var cache = new LruCache<int, string>(30, evicted.Add);
        for (var articleId = 1; articleId <= 30; articleId++)
            cache.Set(articleId, "x");

        Assert.True(cache.TryGet(1, out _));                // 1 is now the MOST recently used
        cache.Set(31, "x");

        Assert.Equal([2], evicted);                         // 2 is now the least recently used
        Assert.True(cache.TryGet(1, out _));
        Assert.False(cache.TryGet(2, out _));
    }

    [Fact]
    public void Keys_are_ordered_most_recently_used_first()
    {
        var cache = new LruCache<int, string>(3);
        cache.Set(1, "a");
        cache.Set(2, "b");
        cache.Set(3, "c");
        cache.TryGet(1, out _);

        Assert.Equal([1, 3, 2], cache.Keys);
    }

    [Fact]
    public void Overwriting_an_existing_key_does_not_evict_anything()
    {
        var evicted = new List<int>();
        var cache = new LruCache<int, string>(2, evicted.Add);
        cache.Set(1, "a");
        cache.Set(2, "b");

        cache.Set(1, "a2");

        Assert.Empty(evicted);
        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGet(1, out var value));
        Assert.Equal("a2", value);
    }

    [Fact]
    public void TryUpdate_only_changes_existing_entries()
    {
        var cache = new LruCache<int, string>(2);
        cache.Set(1, "a");

        Assert.True(cache.TryUpdate(1, v => v + "!"));
        Assert.False(cache.TryUpdate(99, v => v + "!"));
        Assert.Equal(1, cache.Count);
        cache.TryGet(1, out var value);
        Assert.Equal("a!", value);
    }

    [Fact]
    public void Capacity_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<int, string>(0));
    }
}
