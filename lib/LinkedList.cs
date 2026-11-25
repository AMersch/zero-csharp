// LinkedList<T>: a doubly linked list of unmanaged items in an Arena, built
// on Pool<T>, with .NET's names.
//
//   var list = LinkedList<int>.Create(arena);
//   var two = list.AddLast(2);
//   list.AddFirst(1);
//   list.AddAfter(two, 3);
//   list.Remove(two);
//   foreach (int item in list) ...        // 1, 3
//
// Each Add returns the node's handle, which finds it in O(1) for AddBefore,
// AddAfter, Remove and list[node] (the node: its Value, Previous and Next).
// A handle whose node was removed stops the program when used, and Remove
// returns false for it, instead of unlinking whatever is there now. A removed
// node's memory is reused by the next Add.
//
// Like every collection, it is a handle to a header in its arena: copies share
// the same list, and default(LinkedList<T>) is "not created".

using System;
using System.Collections.Generic;

/// <summary>A node of a <see cref="LinkedList{T}"/>: its value and its neighbours.</summary>
public struct LinkedListNode<T> where T : unmanaged
{
    /// <summary>The item, to read or change in place.</summary>
    public T Value;

    internal Handle<LinkedListNode<T>> _previous;
    internal Handle<LinkedListNode<T>> _next;

    /// <summary>The node before this one, or a null handle at the start.</summary>
    public readonly Handle<LinkedListNode<T>> Previous => _previous;

    /// <summary>The node after this one, or a null handle at the end.</summary>
    public readonly Handle<LinkedListNode<T>> Next => _next;
}

/// <summary>A doubly linked list in an arena, with a handle for each node.</summary>
public struct LinkedList<T> where T : unmanaged
{
    struct Header
    {
        public Pool<LinkedListNode<T>> Nodes;
        public Handle<LinkedListNode<T>> First;
        public Handle<LinkedListNode<T>> Last;
    }

    Header* _header;

    /// <summary>Returns a new list in <paramref name="arena"/>, or a not-created one if it is out of memory.</summary>
    public static LinkedList<T> Create(Arena arena, int chunkSize = 64)
    {
        Header* header = arena.Alloc<Header>();
        if (header == null)
            return default;
        var nodes = Pool<LinkedListNode<T>>.Create(arena, chunkSize);
        if (!nodes.IsCreated)
            return default;
        unsafe { header->Nodes = nodes; } // SAFETY: header was just allocated and zeroed.
        return new LinkedList<T> { _header = header };
    }

    /// <summary>False for default(...): a list that was never created.</summary>
    public bool IsCreated => _header != null;

    /// <summary>How many nodes the list holds.</summary>
    public int Count => Nodes.Count;

    /// <summary>The first node, or a null handle if the list is empty.</summary>
    public Handle<LinkedListNode<T>> First
    {
        get
        {
            if (_header == null)
                return default;
            unsafe { return _header->First; } // SAFETY: a live header (see List<T>).
        }
    }

    /// <summary>The last node, or a null handle if the list is empty.</summary>
    public Handle<LinkedListNode<T>> Last
    {
        get
        {
            if (_header == null)
                return default;
            unsafe { return _header->Last; } // SAFETY: a live header.
        }
    }

    /// <summary>The node, to read or change its Value. A removed node stops the program.</summary>
    public ref LinkedListNode<T> this[Handle<LinkedListNode<T>> node] => ref Nodes[node];

    /// <summary>Adds a node at the start. A null handle if the list isn't created or the arena is full.</summary>
    public Handle<LinkedListNode<T>> AddFirst(T value) => Link(value, default, First);

    /// <summary>Adds a node at the end. A null handle if the list isn't created or the arena is full.</summary>
    public Handle<LinkedListNode<T>> AddLast(T value) => Link(value, Last, default);

    /// <summary>Adds a node right after <paramref name="node"/>, which must be in the list.</summary>
    public Handle<LinkedListNode<T>> AddAfter(Handle<LinkedListNode<T>> node, T value)
        => Link(value, node, Nodes[node]._next);

    /// <summary>Adds a node right before <paramref name="node"/>, which must be in the list.</summary>
    public Handle<LinkedListNode<T>> AddBefore(Handle<LinkedListNode<T>> node, T value)
        => Link(value, Nodes[node]._previous, node);

    /// <summary>Takes the node out of the list. False if it was already removed.</summary>
    public bool Remove(Handle<LinkedListNode<T>> node)
    {
        if (_header == null || !Nodes.Contains(node))
            return false;
        Pool<LinkedListNode<T>> nodes = Nodes;
        LinkedListNode<T> removed = nodes[node];
        unsafe
        {
            // SAFETY: a live header; the neighbours are live nodes of the list.
            if (removed._previous.IsNull)
                _header->First = removed._next;
            else
                nodes[removed._previous]._next = removed._next;
            if (removed._next.IsNull)
                _header->Last = removed._previous;
            else
                nodes[removed._next]._previous = removed._previous;
        }
        return nodes.Remove(node);
    }

    /// <summary>Removes the first node. False if the list is empty.</summary>
    public bool RemoveFirst() => Remove(First);

    /// <summary>Removes the last node. False if the list is empty.</summary>
    public bool RemoveLast() => Remove(Last);

    /// <summary>
    /// The first node whose value equals <paramref name="value"/>
    /// (EqualityComparer&lt;T&gt;.Default), or a null handle.
    /// </summary>
    public Handle<LinkedListNode<T>> Find(T value)
    {
        Pool<LinkedListNode<T>> nodes = Nodes;
        for (var node = First; !node.IsNull; node = nodes[node]._next)
        {
            if (EqualityComparer<T>.Default.Equals(nodes[node].Value, value))
                return node;
        }
        return default;
    }

    /// <summary>Whether a node's value equals <paramref name="value"/>.</summary>
    public bool Contains(T value) => !Find(value).IsNull;

    /// <summary>Whether <paramref name="node"/> is a node of the list that hasn't been removed.</summary>
    public bool Contains(Handle<LinkedListNode<T>> node) => Nodes.Contains(node);

    /// <summary>Removes every node; their handles are refused from then on.</summary>
    public void Clear()
    {
        if (_header == null)
            return;
        Nodes.Clear();
        unsafe
        {
            // SAFETY: a live header.
            _header->First = default;
            _header->Last = default;
        }
    }

    /// <summary>For foreach: the values, first to last, by ref.</summary>
    public Enumerator GetEnumerator() => new Enumerator(this);

    /// <summary>Walks the values of a list from first to last.</summary>
    public struct Enumerator
    {
        readonly Pool<LinkedListNode<T>> _nodes;
        Handle<LinkedListNode<T>> _next;
        Handle<LinkedListNode<T>> _current;

        internal Enumerator(LinkedList<T> list)
        {
            _nodes = list.Nodes;
            _next = list.First;
            _current = default;
        }

        public bool MoveNext()
        {
            if (_next.IsNull)
                return false;
            _current = _next;
            _next = _nodes[_current]._next;
            return true;
        }

        public ref T Current => ref _nodes[_current].Value;
    }

    // A not-created list has a not-created pool: lookups in it are refused.
    Pool<LinkedListNode<T>> Nodes
    {
        get
        {
            if (_header == null)
                return default;
            unsafe { return _header->Nodes; } // SAFETY: a live header.
        }
    }

    Handle<LinkedListNode<T>> Link(T value, Handle<LinkedListNode<T>> previous, Handle<LinkedListNode<T>> next)
    {
        if (_header == null)
            return default;
        Pool<LinkedListNode<T>> nodes = Nodes;
        var node = nodes.Add(new LinkedListNode<T> { Value = value, _previous = previous, _next = next });
        if (node.IsNull)
            return default;
        unsafe
        {
            // SAFETY: a live header; previous and next are live nodes or null.
            if (previous.IsNull)
                _header->First = node;
            else
                nodes[previous]._next = node;
            if (next.IsNull)
                _header->Last = node;
            else
                nodes[next]._previous = node;
        }
        return node;
    }
}
