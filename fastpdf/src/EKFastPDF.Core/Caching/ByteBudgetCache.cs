namespace EKFastPDF.Core.Caching;

/// <summary>
/// Least-recently-used cache bounded by a byte budget. Entries marked as in use during the
/// current frame (<see cref="Touch"/> with a frame stamp) are never evicted by that frame, so a
/// burst of new tiles cannot evict what is on screen. Not thread-safe: the viewer uses it from
/// the UI thread only.
/// </summary>
public sealed class ByteBudgetCache<TKey, TValue> where TKey : notnull
{
    private sealed class Node
    {
        public required TKey Key;
        public required TValue Value;
        public long Bytes;
        public long Frame;
        public Node? Prev, Next;
    }

    private readonly Dictionary<TKey, Node> _map = new();
    private Node? _head; // most recent
    private Node? _tail; // least recent
    private readonly Action<TKey, TValue>? _evicted;

    public ByteBudgetCache(long budgetBytes, Action<TKey, TValue>? evicted = null)
    {
        BudgetBytes = budgetBytes;
        _evicted = evicted;
    }

    public long BudgetBytes { get; set; }
    public long Bytes { get; private set; }
    public int Count => _map.Count;
    public long Hits { get; private set; }
    public long Misses { get; private set; }
    public long Evictions { get; private set; }
    public long CurrentFrame { get; set; }

    public bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            MoveToFront(node);
            node.Frame = CurrentFrame;
            Hits++;
            value = node.Value;
            return true;
        }
        Misses++;
        value = default!;
        return false;
    }

    /// <summary>Lookup without affecting statistics or recency.</summary>
    public bool Contains(TKey key) => _map.ContainsKey(key);

    public bool Peek(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node)) { value = node.Value; return true; }
        value = default!;
        return false;
    }

    public void Touch(TKey key)
    {
        if (!_map.TryGetValue(key, out var node)) return;
        MoveToFront(node);
        node.Frame = CurrentFrame;
    }

    public void Add(TKey key, TValue value, long bytes)
    {
        if (_map.TryGetValue(key, out var existing)) Remove(existing, notify: true);
        var node = new Node { Key = key, Value = value, Bytes = bytes, Frame = CurrentFrame };
        _map[key] = node;
        Bytes += bytes;
        LinkFront(node);
        Trim();
    }

    public bool Remove(TKey key)
    {
        if (!_map.TryGetValue(key, out var node)) return false;
        Remove(node, notify: true);
        return true;
    }

    public void RemoveWhere(Func<TKey, bool> predicate)
    {
        foreach (var node in _map.Values.Where(n => predicate(n.Key)).ToList()) Remove(node, notify: true);
    }

    public void Trim()
    {
        var node = _tail;
        while (Bytes > BudgetBytes && node != null)
        {
            var prev = node.Prev;
            if (node.Frame != CurrentFrame)
            {
                Remove(node, notify: true);
                Evictions++;
            }
            node = prev;
        }
    }

    private void Remove(Node node, bool notify)
    {
        _map.Remove(node.Key);
        Unlink(node);
        Bytes -= node.Bytes;
        if (notify) _evicted?.Invoke(node.Key, node.Value);
    }

    private void MoveToFront(Node node)
    {
        if (_head == node) return;
        Unlink(node);
        LinkFront(node);
    }

    private void LinkFront(Node node)
    {
        node.Prev = null;
        node.Next = _head;
        if (_head != null) _head.Prev = node;
        _head = node;
        _tail ??= node;
    }

    private void Unlink(Node node)
    {
        if (node.Prev != null) node.Prev.Next = node.Next; else if (_head == node) _head = node.Next;
        if (node.Next != null) node.Next.Prev = node.Prev; else if (_tail == node) _tail = node.Prev;
        node.Prev = node.Next = null;
    }
}
