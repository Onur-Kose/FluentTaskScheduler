using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace FluentTaskScheduler.Core;

/// <summary>A snapshot-enumerated collection whose mutations notify its owning job.</summary>
public sealed class JobCollection<T> : IList<T>, IReadOnlyList<T>
{
    private readonly List<T> _items;
    private object _gate = new();
    private Action<Action>? _change;
    public JobCollection() => _items = [];
    public JobCollection(IEnumerable<T> items) => _items = items.ToList();
    internal void Attach(object gate, Action<Action> change) { _gate = gate; _change = change; }
    internal void Detach() => _change = null;
    internal void ReplaceSilently(IEnumerable<T> items) { lock (_gate) { _items.Clear(); _items.AddRange(items); } }
    private void Mutate(Action mutation)
    {
        lock (_gate)
        {
            var before = _items.ToArray();
            try { if (_change is null) mutation(); else _change(mutation); }
            catch { _items.Clear(); _items.AddRange(before); throw; }
        }
    }
    public T this[int index] { get { lock (_gate) return _items[index]; } set => Mutate(() => _items[index] = value); }
    public int Count { get { lock (_gate) return _items.Count; } }
    public int Length => Count;
    public bool IsReadOnly => false;
    public void Add(T item) => Mutate(() => _items.Add(item));
    public void AddRange(IEnumerable<T> items) { var copy = items.ToArray(); Mutate(() => _items.AddRange(copy)); }
    public void Clear() => Mutate(_items.Clear);
    public bool Contains(T item) { lock (_gate) return _items.Contains(item); }
    public int IndexOf(T item) { lock (_gate) return _items.IndexOf(item); }
    public void CopyTo(T[] array, int arrayIndex) { lock (_gate) _items.CopyTo(array, arrayIndex); }
    public void Insert(int index, T item) => Mutate(() => _items.Insert(index, item));
    public bool Remove(T item) { var removed = false; Mutate(() => removed = _items.Remove(item)); return removed; }
    public void RemoveAt(int index) => Mutate(() => _items.RemoveAt(index));
    public void RemoveRange(int index, int count) => Mutate(() => _items.RemoveRange(index, count));
    public void InsertRange(int index, IEnumerable<T> items) { var copy = items.ToArray(); Mutate(() => _items.InsertRange(index, copy)); }
    public int RemoveAll(Predicate<T> match) { var removed = 0; Mutate(() => removed = _items.RemoveAll(match)); return removed; }
    public void Reverse() => Mutate(_items.Reverse);
    public void Sort() => Mutate(_items.Sort);
    public void Sort(IComparer<T>? comparer) => Mutate(() => _items.Sort(comparer));
    public void Sort(Comparison<T> comparison) => Mutate(() => _items.Sort(comparison));
    public T[] ToArray() { lock (_gate) return _items.ToArray(); }
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)ToArray()).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    [return: NotNullIfNotNull(nameof(items))]
    public static implicit operator JobCollection<T>?(T[]? items) => items is null ? null : new(items);
    [return: NotNullIfNotNull(nameof(items))]
    public static implicit operator JobCollection<T>?(List<T>? items) => items is null ? null : new(items);
}
