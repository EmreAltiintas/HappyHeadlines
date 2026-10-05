namespace HappyHeadlines.Core.Caching;

/// <summary>
/// Small thread-safe LRU cache with a hard entry limit. A <see cref="Dictionary{TKey,TValue}"/>
/// gives O(1) lookup and a <see cref="LinkedList{T}"/> keeps usage order (first = most recently
/// used, last = least recently used). When <see cref="Set"/> would exceed the capacity, the
/// LAST node - the entry used longest ago - is evicted. <see cref="TryGet"/>, <see cref="Set"/>
/// and <see cref="TryUpdate"/> all count as "use". Eviction is per entry (here: per article),
/// which memory limits in an off-the-shelf cache cannot give us.
/// </summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Action<TKey>? _onEvicted;
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map = new();
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _usage = new();
    private readonly object _gate = new();

    public LruCache(int capacity, Action<TKey>? onEvicted = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _onEvicted = onEvicted;
    }

    public int Capacity => _capacity;

    public int Count
    {
        get { lock (_gate) return _map.Count; }
    }

    /// <summary>Cache lookup. A hit makes the entry the most recently used.</summary>
    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _usage.Remove(node);
                _usage.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = default!;
        return false;
    }

    /// <summary>Inserts or replaces an entry (as most recently used); evicts the LRU entry when full.</summary>
    public void Set(TKey key, TValue value)
    {
        TKey? evicted = default;
        var didEvict = false;

        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _usage.Remove(existing);
                _map.Remove(key);
            }
            else if (_map.Count >= _capacity)
            {
                var lru = _usage.Last!;
                _usage.RemoveLast();
                _map.Remove(lru.Value.Key);
                evicted = lru.Value.Key;
                didEvict = true;
            }

            var node = new LinkedListNode<KeyValuePair<TKey, TValue>>(new(key, value));
            _usage.AddFirst(node);
            _map[key] = node;
        }

        // Callback outside the lock so a slow logger can never block other callers.
        if (didEvict)
            _onEvicted?.Invoke(evicted!);
    }

    /// <summary>
    /// Replaces the value of an existing entry (counts as a use). Returns false and does
    /// nothing when the key is not cached.
    /// </summary>
    public bool TryUpdate(TKey key, Func<TValue, TValue> update)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out var node))
                return false;

            var updated = new LinkedListNode<KeyValuePair<TKey, TValue>>(new(key, update(node.Value.Value)));
            _usage.Remove(node);
            _usage.AddFirst(updated);
            _map[key] = updated;
            return true;
        }
    }

    public bool Remove(TKey key)
    {
        lock (_gate)
        {
            if (!_map.Remove(key, out var node))
                return false;
            _usage.Remove(node);
            return true;
        }
    }

    /// <summary>Snapshot of the keys, most recently used first.</summary>
    public IReadOnlyList<TKey> Keys
    {
        get { lock (_gate) return _usage.Select(n => n.Key).ToList(); }
    }
}
