using System.Collections.Generic;

namespace NoOvertime.VirtualTexture
{
    public class LRUCache<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, (LinkedListNode<TKey> node, TValue value)> _cache;
        private readonly LinkedList<TKey> _list;
        private readonly LinkedListNodeCache<TKey> _nodeCache;

        public LRUCache(int capacity)
        {
            _capacity = capacity;
            _cache = new Dictionary<TKey, (LinkedListNode<TKey> node, TValue value)>(capacity);
            _list = new LinkedList<TKey>();
            _nodeCache = new LinkedListNodeCache<TKey>();
        }

        public bool Touch(TKey key, out TValue value)
        {
            if (!_cache.TryGetValue(key, out var node))
            {
                value = default;
                return false;
            }

            value = node.value;
            _list.Remove(node.node);
            _list.AddFirst(node.node);
            return true;
        }

        public bool RemoveLast(out TKey key, out TValue value)
        {
            if (_list.Count > 0)
            {
                var last = _list.Last;
                TKey removeKey = last.Value;
                key = removeKey;
                value = _cache[removeKey].value;
                _cache.Remove(removeKey);
                _list.RemoveLast();
                _nodeCache.Release(last);
                return true;
            }

            key = default;
            value = default;
            return false;
        }

        public bool Insert(TKey key, TValue value)
        {
            if (_cache.ContainsKey(key)) return false;
            if (_cache.Count >= _capacity) return false;
            var node = _nodeCache.Acquire(key);
            _list.AddFirst(node);
            _cache.Add(key, (node, value));
            return true;
        }
    }
}