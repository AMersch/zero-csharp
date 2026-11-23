// Queue<T>, Stack<T> and PriorityQueue<TElement, TPriority>: first in first
// out, last in first out, and smallest priority first, for unmanaged items,
// backed by an Arena. Each is a handle whose copies share contents, and
// default(...) is "not created": it is empty, and adding fails.
//
//   var work = Queue<int>.Create(arena);
//   work.Enqueue(start);
//   while (work.TryDequeue(out int next)) { ... work.Enqueue(neighbor); ... }
//
// The names are .NET's, with Try in front of the ones that take an item out
// (Dequeue, Pop, Peek): there are no exceptions to say a collection is empty.

using System;
using System.Collections.Generic;

public struct Queue<T> where T : unmanaged
{
    // The items are a List; the ones before Head have been dequeued. When
    // they're half the list, the rest move to the front, so each item moves
    // about once, and the list's memory is used again.
    struct Header
    {
        public List<T> Items;
        public int Head;
    }

    Header* _header;

    /// <summary>Returns a new queue in <paramref name="arena"/>, or a not-created queue if it is out of memory.</summary>
    public static Queue<T> Create(Arena arena, int initialCapacity = 16)
    {
        Header* header = arena.Alloc<Header>();
        if (header == null)
            return default;
        List<T> items = List<T>.Create(arena, initialCapacity);
        if (!items.IsCreated)
            return default;
        unsafe { header->Items = items; } // SAFETY: header was just allocated and zeroed.
        return new Queue<T> { _header = header };
    }

    public bool IsCreated => _header != null;

    public int Count
    {
        get
        {
            if (_header == null)
                return 0;
            unsafe { return _header->Items.Count - _header->Head; } // SAFETY: a live header.
        }
    }

    /// <summary>Adds an item at the back. Returns false if the queue isn't created or the arena is full.</summary>
    public bool Enqueue(T item)
    {
        if (_header == null)
            return false;
        unsafe { return _header->Items.Add(item); } // SAFETY: a live header.
    }

    /// <summary>Takes the item at the front. Returns false if the queue is empty.</summary>
    public bool TryDequeue(out T item)
    {
        if (Count == 0)
        {
            item = default;
            return false;
        }
        unsafe
        {
            // SAFETY: a live header (Count > 0); Head is below the list's Count.
            item = _header->Items.Get(_header->Head++);
            int count = _header->Items.Count;
            if (_header->Head == count)
            {
                _header->Items.Clear();
                _header->Head = 0;
            }
            else if (_header->Head >= 16 && _header->Head * 2 >= count)
            {
                _header->Items.RemoveFirst(_header->Head);
                _header->Head = 0;
            }
        }
        return true;
    }

    /// <summary>The item at the front, without taking it. Returns false if the queue is empty.</summary>
    public bool TryPeek(out T item)
    {
        if (Count == 0)
        {
            item = default;
            return false;
        }
        unsafe { item = _header->Items.Get(_header->Head); } // SAFETY: as in TryDequeue.
        return true;
    }

    public void Clear()
    {
        if (_header == null)
            return;
        unsafe
        {
            // SAFETY: a live header.
            _header->Items.Clear();
            _header->Head = 0;
        }
    }

    /// <summary>For foreach: the items from front to back. Don't change the queue during the loop.</summary>
    public Enumerator GetEnumerator() => new Enumerator(this);

    public struct Enumerator
    {
        readonly Queue<T> _queue;
        int _index;

        internal Enumerator(Queue<T> queue)
        {
            _queue = queue;
            _index = -1;
        }

        public bool MoveNext() => ++_index < _queue.Count;

        public T Current
        {
            get { unsafe { return _queue._header->Items.Get(_queue._header->Head + _index); } } // SAFETY: MoveNext returned true.
        }
    }

    /// <summary>
    /// Returns an independent copy in <paramref name="target"/>, or a not-created
    /// queue if this one isn't created or the target is out of memory.
    /// </summary>
    public Queue<T> Clone(Arena target)
    {
        int count = Count;
        if (_header == null)
            return default;
        Queue<T> copy = Create(target, count);
        if (!copy.IsCreated)
            return default;
        foreach (T item in this)
            copy.Enqueue(item); // copy has room for count items
        return copy;
    }
}

public struct Stack<T> where T : unmanaged
{
    // The top is the end of the list.
    List<T> _items;

    /// <summary>Returns a new stack in <paramref name="arena"/>, or a not-created stack if it is out of memory.</summary>
    public static Stack<T> Create(Arena arena, int initialCapacity = 16)
        => new Stack<T> { _items = List<T>.Create(arena, initialCapacity) };

    public bool IsCreated => _items.IsCreated;

    public int Count => _items.Count;

    /// <summary>Puts an item on top. Returns false if the stack isn't created or the arena is full.</summary>
    public bool Push(T item) => _items.Add(item);

    /// <summary>Takes the item on top. Returns false if the stack is empty.</summary>
    public bool TryPop(out T item)
    {
        int count = _items.Count;
        if (count == 0)
        {
            item = default;
            return false;
        }
        item = _items.Get(count - 1);
        _items.RemoveAt(count - 1);
        return true;
    }

    /// <summary>The item on top, without taking it. Returns false if the stack is empty.</summary>
    public bool TryPeek(out T item)
    {
        int count = _items.Count;
        if (count == 0)
        {
            item = default;
            return false;
        }
        item = _items.Get(count - 1);
        return true;
    }

    public void Clear() => _items.Clear();

    /// <summary>For foreach: the items from the top down, as .NET's Stack. Don't change the stack during the loop.</summary>
    public Enumerator GetEnumerator() => new Enumerator(_items);

    public struct Enumerator
    {
        readonly List<T> _items;
        int _index;

        internal Enumerator(List<T> items)
        {
            _items = items;
            _index = items.Count;
        }

        public bool MoveNext() => --_index >= 0;

        public T Current => _items.Get(_index);
    }

    /// <summary>
    /// Returns an independent copy in <paramref name="target"/>, or a not-created
    /// stack if this one isn't created or the target is out of memory.
    /// </summary>
    public Stack<T> Clone(Arena target) => new Stack<T> { _items = _items.Clone(target) };
}

// Smallest priority first, by TComparer, an unmanaged struct that implements
// IComparer<TPriority>; PriorityQueue<TElement, TPriority> uses TPriority's own
// order (IComparable<TPriority>). A binary heap, as .NET's: Enqueue and
// TryDequeue take log n steps. Elements with equal priorities come out in no
// particular order.
public struct PriorityQueue<TElement, TPriority, TComparer>
    where TElement : unmanaged
    where TPriority : unmanaged
    where TComparer : unmanaged, IComparer<TPriority>
{
    struct Node
    {
        public TElement Element;
        public TPriority Priority;
    }

    // The heap: each node's priority is at most its children's (at 2i+1 and
    // 2i+2). The comparer is a value, so the handle's copies carry the same one.
    List<Node> _nodes;
    TComparer _comparer;

    /// <summary>Returns a new queue in <paramref name="arena"/>, or a not-created queue if it is out of memory.</summary>
    public static PriorityQueue<TElement, TPriority, TComparer> Create(Arena arena, int initialCapacity = 16, TComparer comparer = default)
        => new PriorityQueue<TElement, TPriority, TComparer> { _nodes = List<Node>.Create(arena, initialCapacity), _comparer = comparer };

    public bool IsCreated => _nodes.IsCreated;

    public int Count => _nodes.Count;

    /// <summary>Adds an element. Returns false if the queue isn't created or the arena is full.</summary>
    public bool Enqueue(TElement element, TPriority priority)
    {
        if (!_nodes.Add(new Node { Element = element, Priority = priority }))
            return false;
        Span<Node> nodes;
        unsafe { nodes = _nodes.AsSpan(); } // SAFETY: nothing changes the list while the span is used.
        int child = nodes.Length - 1;
        Node node = nodes[child];
        while (child > 0)
        {
            int parent = (child - 1) / 2;
            if (_comparer.Compare(node.Priority, nodes[parent].Priority) >= 0)
                break;
            nodes[child] = nodes[parent];
            child = parent;
        }
        nodes[child] = node;
        return true;
    }

    /// <summary>Takes the element with the smallest priority. Returns false if the queue is empty.</summary>
    public bool TryDequeue(out TElement element, out TPriority priority)
    {
        int count = _nodes.Count;
        if (count == 0)
        {
            element = default;
            priority = default;
            return false;
        }
        Span<Node> nodes;
        unsafe { nodes = _nodes.AsSpan(); } // SAFETY: as in Enqueue; RemoveAt comes after the last use.
        element = nodes[0].Element;
        priority = nodes[0].Priority;

        // Move the last node to the root, then down until neither child is smaller.
        Node last = nodes[count - 1];
        count--;
        int parent = 0;
        while (true)
        {
            int child = 2 * parent + 1;
            if (child >= count)
                break;
            if (child + 1 < count && _comparer.Compare(nodes[child + 1].Priority, nodes[child].Priority) < 0)
                child++;
            if (_comparer.Compare(last.Priority, nodes[child].Priority) <= 0)
                break;
            nodes[parent] = nodes[child];
            parent = child;
        }
        nodes[parent] = last;
        _nodes.RemoveAt(count);
        return true;
    }

    /// <summary>The element with the smallest priority, without taking it. Returns false if the queue is empty.</summary>
    public bool TryPeek(out TElement element, out TPriority priority)
    {
        if (_nodes.Count == 0)
        {
            element = default;
            priority = default;
            return false;
        }
        Node root = _nodes.Get(0);
        element = root.Element;
        priority = root.Priority;
        return true;
    }

    public void Clear() => _nodes.Clear();

    /// <summary>
    /// Returns an independent copy in <paramref name="target"/>, or a not-created
    /// queue if this one isn't created or the target is out of memory.
    /// </summary>
    public PriorityQueue<TElement, TPriority, TComparer> Clone(Arena target)
        => new PriorityQueue<TElement, TPriority, TComparer> { _nodes = _nodes.Clone(target), _comparer = _comparer };
}

// PriorityQueue<TElement, TPriority>: smallest priority first, by TPriority's
// own order (IComparable<TPriority>), as .NET's PriorityQueue.
public struct PriorityQueue<TElement, TPriority>
    where TElement : unmanaged
    where TPriority : unmanaged
{
    PriorityQueue<TElement, TPriority, DefaultComparer<TPriority>> _queue;

    /// <summary>Returns a new queue in <paramref name="arena"/>, or a not-created queue if it is out of memory.</summary>
    public static PriorityQueue<TElement, TPriority> Create(Arena arena, int initialCapacity = 16)
        => new PriorityQueue<TElement, TPriority> { _queue = PriorityQueue<TElement, TPriority, DefaultComparer<TPriority>>.Create(arena, initialCapacity) };

    public bool IsCreated => _queue.IsCreated;

    public int Count => _queue.Count;

    /// <summary>Adds an element. Returns false if the queue isn't created or the arena is full.</summary>
    public bool Enqueue(TElement element, TPriority priority) => _queue.Enqueue(element, priority);

    /// <summary>Takes the element with the smallest priority. Returns false if the queue is empty.</summary>
    public bool TryDequeue(out TElement element, out TPriority priority) => _queue.TryDequeue(out element, out priority);

    /// <summary>The element with the smallest priority, without taking it. Returns false if the queue is empty.</summary>
    public bool TryPeek(out TElement element, out TPriority priority) => _queue.TryPeek(out element, out priority);

    public void Clear() => _queue.Clear();

    /// <summary>
    /// Returns an independent copy in <paramref name="target"/>, or a not-created
    /// queue if this one isn't created or the target is out of memory.
    /// </summary>
    public PriorityQueue<TElement, TPriority> Clone(Arena target)
        => new PriorityQueue<TElement, TPriority> { _queue = _queue.Clone(target) };
}
