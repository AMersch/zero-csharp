// RefList<T>: a List<T> for references (string, classes, arrays), in an Arena.
//
//   RefList<string> names = RefList<string>.Create(keep);
//   names.Add(Str.ToArenaString(keep, line));   // the string itself must live on
//   foreach (string name in names) Out.Line(name);
//
// List<T> takes only unmanaged types, because it stores items in arena memory
// through a T*. A reference is just an address in zero mode (there is no GC to
// move objects or to find them), so this stores each one as an nint in a
// List<nint> and converts it back when read. It has List's behavior: a handle
// whose copies share contents, safe to use when not created, growth in place.
//
// The list holds references, not the objects: the objects stay wherever they
// were allocated. Each one must outlive the list's use of it. A string built in
// a scratch arena dangles once that arena is reset, even if the list lives in
// another one, so copy it into a long-lived arena before adding it.

using System;
using System.Runtime.CompilerServices;

public struct RefList<T> where T : class
{
    List<nint> _items;

    /// <summary>Returns a new list in <paramref name="arena"/>, or a not-created list if it is out of memory.</summary>
    public static RefList<T> Create(Arena arena, int initialCapacity = 16)
        => new RefList<T> { _items = List<nint>.Create(arena, initialCapacity) };

    public bool IsCreated => _items.IsCreated;

    public int Count => _items.Count;

    public int Capacity => _items.Capacity;

    /// <summary>Appends an item (null is allowed). Returns false if the list isn't created or the arena is full.</summary>
    public bool Add(T item) => _items.Add(ToAddress(item));

    public T Get(int index) => FromAddress(_items.Get(index));

    public void Set(int index, T value) => _items.Set(index, ToAddress(value));

    public bool RemoveAt(int index) => _items.RemoveAt(index);

    public void Clear() => _items.Clear();

    /// <summary>
    /// Sorts the items in place, like List.Sort: <paramref name="compare"/> returns
    /// a negative number if its first argument goes before its second, a positive
    /// one if after, and zero if either order will do. Heapsort, not stable.
    /// <code>
    /// static int ByName(string a, string b) => Str.Compare(a, b);
    /// names.Sort(&amp;ByName);
    /// </code>
    /// </summary>
    public void Sort(delegate*<T, T, int> compare)
    {
        if (compare == null)
            return;

        Span<nint> items;
        unsafe { items = _items.AsSpan(); } // SAFETY: nothing changes the list during the sort.
        int count = items.Length;
        for (int start = count / 2 - 1; start >= 0; start--)
            SiftDown(items, start, count, compare);
        for (int end = count - 1; end > 0; end--)
        {
            nint largest = items[0];
            items[0] = items[end];
            items[end] = largest;
            SiftDown(items, 0, end, compare);
        }
    }

    // Moves items[root] down until neither child is larger, within [0, count).
    static void SiftDown(Span<nint> items, int root, int count, delegate*<T, T, int> compare)
    {
        while (true)
        {
            int child = 2 * root + 1;
            if (child >= count)
                return;
            unsafe
            {
                // SAFETY: compare is the caller's function, called with two items of the list.
                if (child + 1 < count && compare(FromAddress(items[child]), FromAddress(items[child + 1])) < 0)
                    child++;
                if (compare(FromAddress(items[root]), FromAddress(items[child])) >= 0)
                    return;
            }
            nint item = items[root];
            items[root] = items[child];
            items[child] = item;
            root = child;
        }
    }

    /// <summary>
    /// Returns an independent copy of the list in <paramref name="target"/>, or a
    /// not-created list if this one isn't created or the target is out of memory.
    /// </summary>
    /// <remarks>
    /// Only the references are copied: both lists point at the same objects, so
    /// the copy outlives the original's arena but not the objects' arenas.
    /// </remarks>
    public RefList<T> Clone(Arena target) => new RefList<T> { _items = _items.Clone(target) };

    /// <summary>For foreach: every item, in order.</summary>
    /// <remarks>Don't Add or RemoveAt during the loop.</remarks>
    public Enumerator GetEnumerator() => new Enumerator(this);

    public struct Enumerator
    {
        readonly RefList<T> _list;
        int _index;

        internal Enumerator(RefList<T> list)
        {
            _list = list;
            _index = -1;
        }

        public bool MoveNext() => ++_index < _list.Count;

        public T Current => _list.Get(_index);
    }

    static nint ToAddress(T item) => Unsafe.As<T, nint>(ref item);

    static T FromAddress(nint address) => Unsafe.As<nint, T>(ref address);
}
