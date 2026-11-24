// Ptr<T>: a typed pointer to one item in an Arena, for linking items together.
//
//   public struct Node
//   {
//       public int Number;
//       public Ptr<Node> Prev;
//       public Ptr<Node> Next;
//   }
//
//   Ptr<Node> a = arena.New(new Node { Number = 1 });
//   Ptr<Node> b = arena.New(new Node { Number = 2 });
//   a.Value.Next = b;
//   b.Value.Prev = a;              // a cycle, no unsafe needed
//
// A struct can't contain itself, so linked lists, trees and graphs need a
// pointer between items. Ptr<T> is one that needs no unsafe in your code:
// Value gives a ref to the item, and a null Ptr (the default) stops the
// program when used instead of crashing it.
//
// Like a List<T>, a Ptr is valid until its arena is reset, destroyed or
// restored to before the item was made: the unsafe calls whose caller
// promises that nothing still uses that memory. Under Arena.DebugChecks, a
// Ptr used after that stops the program. An item lives as long as its arena;
// there is no freeing one item on its own.
//
// A Ptr is a pointer and its arena (16 bytes on 64-bit machines), so it can
// make that check, and copies of it point at the same item.
//
// Value hands out a ref into the arena without unsafe, unlike AllocSpan and
// List.AsSpan. An item never moves (List.AsSpan is unsafe because Add can
// move the items) and only those unsafe calls free it, so a ref from Value
// is good for as long as the Ptr is. DebugChecks only sees uses of Value,
// though: a ref kept in a local and used after a Reset goes unnoticed.

using System;

/// <summary>A pointer to an item in an arena, from <see cref="Arena.New{T}(T)"/>.</summary>
public readonly struct Ptr<T> : IEquatable<Ptr<T>> where T : unmanaged
{
    readonly T* _item;
    readonly Arena _arena;

    internal Ptr(T* item, Arena arena)
    {
        _item = item;
        _arena = arena;
    }

    /// <summary>True for the default Ptr, which points at nothing.</summary>
    public bool IsNull => _item == null;

    /// <summary>The item, to read or change in place. A null Ptr stops the program.</summary>
    public ref T Value
    {
        get
        {
            if (_item == null)
                Environment.FailFast("Ptr: Value of a null Ptr");
            unsafe
            {
                // SAFETY: the item is live: its arena hasn't been reset,
                // destroyed or restored to before it (those are unsafe and
                // their callers promise that), which DebugChecks verifies.
                Arena.CheckItemLive(_arena, _item, (nuint)sizeof(T));
                return ref *_item;
            }
        }
    }

    /// <summary>Whether both point at the same item (not whether the items are equal).</summary>
    public bool Equals(Ptr<T> other) => _item == other._item;

    /// <summary>Whether <paramref name="obj"/> is a Ptr to the same item.</summary>
    public override bool Equals(object obj) => obj is Ptr<T> other && Equals(other);

    /// <summary>A hash of the address, matching Equals.</summary>
    public override int GetHashCode() => ((nint)_item).GetHashCode();

    /// <summary>Whether both point at the same item.</summary>
    public static bool operator ==(Ptr<T> left, Ptr<T> right) => left._item == right._item;

    /// <summary>Whether they point at different items.</summary>
    public static bool operator !=(Ptr<T> left, Ptr<T> right) => left._item != right._item;
}

public partial struct Arena
{
    /// <summary>
    /// Copies <paramref name="value"/> into the arena and returns a pointer to
    /// it, or a null Ptr if the arena is out of memory.
    /// </summary>
    public Ptr<T> New<T>(T value) where T : unmanaged
    {
        T* item = Alloc<T>();
        if (item == null)
            return default;
        unsafe { *item = value; } // SAFETY: just allocated, room for one T.
        return new Ptr<T>(item, this);
    }

    /// <summary>A zeroed item in the arena, or a null Ptr if it is out of memory.</summary>
    public Ptr<T> New<T>() where T : unmanaged
    {
        T* item = Alloc<T>();
        return item == null ? default : new Ptr<T>(item, this);
    }

    // For Ptr<T>: under DebugChecks, stops the program if the item was freed.
    internal static void CheckItemLive(Arena arena, void* item, nuint size)
    {
        if (!DebugChecks)
            return;
        if (arena._state != null && !arena.IsLive(item, size))
        {
            // SAFETY: the state is live; see the note above BlockCount.
            unsafe { Fail(arena._state, "Arena.DebugChecks: Ptr used after its item was freed (its arena was reset, or restored to before the item was made)"); }
        }
    }
}
