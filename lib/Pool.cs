// Pool<T>: items in an Arena that can be removed one by one, found again by a
// Handle<T>, and that notice when a handle is out of date.
//
//   var nodes = Pool<TreeNode>.Create(arena);
//   Handle<TreeNode> root = nodes.Add(new TreeNode { Key = 5 });
//   nodes[root].Left = nodes.Add(new TreeNode { Key = 3 });
//   nodes.Remove(root);
//   nodes[root].Key = 1;       // stops the program: root was removed
//
// A handle is its pool's id, a slot number and the slot's generation (12
// bytes, and a plain unmanaged struct, so items can hold handles to each
// other: trees, graphs, lists with cycles). Removing an item bumps its slot's generation, so every
// copy of the old handle is then refused, even after the slot holds a new
// item: a handle that is out of date stops the program instead of reaching
// the wrong item. Contains and TryGet ask without stopping.
//
// Move hands an item to a new owner: the item stays, but it gets a new handle
// and every copy of the old one is refused from then on. MoveTo does the same
// into another pool, for example from a ScratchPool into one you keep.
//
// The items live in chunks that never move, so a ref from pool[handle] stays
// good while the pool grows. After Remove, a later Add can put a new item in
// the same slot: take the ref again rather than keep it across a Remove. Like
// every collection, the pool goes when its arena is reset, restored to before
// it or destroyed; default(Pool<T>) is "not created": Add returns a null
// handle and lookups find nothing.
//
// A handle used with another pool than its own is always refused: each pool
// gets an id of its own when it is created, and its handles carry it.

using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>Finds an item in a <see cref="Pool{T}"/>; refused once the item is removed or moved.</summary>
public readonly struct Handle<T> : IEquatable<Handle<T>> where T : unmanaged
{
    internal readonly int Index;
    internal readonly uint Generation; // odd for a live item; 0 for the null handle
    internal readonly int Pool;        // the id of the pool it came from

    internal Handle(int pool, int index, uint generation)
    {
        Pool = pool;
        Index = index;
        Generation = generation;
    }

    /// <summary>True for the default handle, which finds nothing.</summary>
    public bool IsNull => Generation == 0;

    /// <summary>Whether both are the same handle (the same pool, slot and generation).</summary>
    public bool Equals(Handle<T> other) => Pool == other.Pool && Index == other.Index && Generation == other.Generation;

    /// <summary>Whether <paramref name="obj"/> is the same handle.</summary>
    public override bool Equals(object obj) => obj is Handle<T> other && Equals(other);

    /// <summary>A hash of the pool, slot and generation, matching Equals.</summary>
    public override int GetHashCode() => HashCode.Combine(Pool, Index, Generation);

    /// <summary>Whether both are the same handle.</summary>
    public static bool operator ==(Handle<T> left, Handle<T> right) => left.Equals(right);

    /// <summary>Whether they are different handles.</summary>
    public static bool operator !=(Handle<T> left, Handle<T> right) => !left.Equals(right);
}

/// <summary>Items in an arena that can be removed one by one, each found by a <see cref="Handle{T}"/>.</summary>
public struct Pool<T> where T : unmanaged
{
    struct Slot
    {
        public T Item;
        public uint Generation; // odd while the slot holds an item, even while free
        public int NextFree;    // the next free slot, while this one is free
    }

    struct Header
    {
        public Arena Arena;
        public int Id;            // carried by the pool's handles
        public List<nint> Chunks; // Slot* each, ChunkSize slots; they never move
        public int Shift;         // ChunkSize == 1 << Shift
        public int Count;         // items in the pool
        public int Used;          // slots ever handed out; the rest are untouched
        public int Capacity;      // slots in all chunks
        public int FreeHead;      // a removed item's slot, or -1
    }

    Header* _header;

    // The last id given to a Pool<T> of this T (a Handle<T> only fits those);
    // 0 is no pool's.
    static int s_lastId;

    /// <summary>
    /// Returns a new pool in <paramref name="arena"/> that grows by chunks of
    /// <paramref name="chunkSize"/> items (rounded up to a power of two), or a
    /// not-created pool if the arena is out of memory.
    /// </summary>
    public static Pool<T> Create(Arena arena, int chunkSize = 64)
    {
        int shift = 3;
        while ((1 << shift) < chunkSize && shift < 20)
            shift++;

        Header* header = arena.Alloc<Header>();
        if (header == null)
            return default;
        List<nint> chunks = List<nint>.Create(arena, 4);
        if (!chunks.IsCreated)
            return default;
        unsafe
        {
            // SAFETY: header was just allocated and zeroed.
            header->Arena = arena;
            header->Id = Interlocked.Increment(ref s_lastId);
            header->Chunks = chunks;
            header->Shift = shift;
            header->FreeHead = -1;
        }
        return new Pool<T> { _header = header };
    }

    /// <summary>False for default(...): a pool that was never created.</summary>
    public bool IsCreated => _header != null;

    /// <summary>How many items the pool holds.</summary>
    public int Count
    {
        get
        {
            if (_header == null)
                return 0;
            unsafe { return _header->Count; } // SAFETY: a live header (see List<T>).
        }
    }

    /// <summary>
    /// Adds an item and returns its handle, or a null handle if the pool isn't
    /// created or the arena is out of memory. Reuses a removed item's slot.
    /// </summary>
    public Handle<T> Add(T item)
    {
        if (_header == null)
            return default;
        unsafe
        {
            // SAFETY: a live header; SlotAt gets indexes below Used <= Capacity.
            int index = _header->FreeHead;
            Slot* slot;
            if (index >= 0)
            {
                slot = SlotAt(index);
                _header->FreeHead = slot->NextFree;
            }
            else
            {
                if (_header->Used == _header->Capacity && !AddChunk())
                    return default;
                index = _header->Used++;
                slot = SlotAt(index);
            }
            slot->Generation++; // even (free) to odd (live)
            slot->Item = item;
            _header->Count++;
            return new Handle<T>(_header->Id, index, slot->Generation);
        }
    }

    /// <summary>
    /// The item <paramref name="handle"/> finds, to read or change in place. A
    /// null handle, or one whose item was removed or moved, stops the program.
    /// </summary>
    public ref T this[Handle<T> handle]
    {
        get
        {
            unsafe
            {
                // SAFETY: Find returns a live slot in a chunk, which never
                // moves and is freed only with the arena (by unsafe calls).
                Slot* slot = Find(handle);
                if (slot == null)
                    Refuse(handle);
                return ref slot->Item;
            }
        }
    }

    /// <summary>Whether <paramref name="handle"/> finds an item: not null, not removed, not moved.</summary>
    public bool Contains(Handle<T> handle) => Find(handle) != null;

    /// <summary>The item <paramref name="handle"/> finds, if it finds one.</summary>
    public bool TryGet(Handle<T> handle, out T item)
    {
        Slot* slot = Find(handle);
        if (slot == null)
        {
            item = default;
            return false;
        }
        unsafe { item = slot->Item; } // SAFETY: a live slot.
        return true;
    }

    /// <summary>
    /// Removes the item <paramref name="handle"/> finds; every copy of the
    /// handle is refused from then on. False if it finds none.
    /// </summary>
    public bool Remove(Handle<T> handle)
    {
        Slot* slot = Find(handle);
        if (slot == null)
            return false;
        unsafe
        {
            // SAFETY: a live slot of a live header.
            slot->Generation++; // odd to even: free
            slot->Item = default;
            slot->NextFree = _header->FreeHead;
            _header->FreeHead = handle.Index;
            _header->Count--;
        }
        return true;
    }

    /// <summary>
    /// Gives the item a new handle and returns it, and sets
    /// <paramref name="handle"/> to null: it and every other copy of it are
    /// refused from then on, so only the new owner can reach the item. A
    /// handle that finds nothing stops the program.
    /// </summary>
    public Handle<T> Move(ref Handle<T> handle)
    {
        Slot* slot = Find(handle);
        if (slot == null)
            Refuse(handle);
        unsafe
        {
            slot->Generation += 2; // SAFETY: a live slot. Still odd: still live.
            var moved = new Handle<T>(handle.Pool, handle.Index, slot->Generation);
            handle = default;
            return moved;
        }
    }

    /// <summary>
    /// Moves the item into <paramref name="target"/> and returns its handle
    /// there; it is removed from this pool and <paramref name="handle"/> is set
    /// to null. If the target is full, returns a null handle and changes
    /// nothing. A handle that finds nothing stops the program.
    /// </summary>
    public Handle<T> MoveTo(Pool<T> target, ref Handle<T> handle)
    {
        Handle<T> moved = target.Add(this[handle]);
        if (moved.IsNull)
            return default;
        Remove(handle);
        handle = default;
        return moved;
    }

    /// <summary>Removes every item; every handle is refused from then on.</summary>
    public void Clear()
    {
        if (_header == null)
            return;
        unsafe
        {
            // SAFETY: a live header; indexes below Used.
            _header->FreeHead = -1;
            for (int i = _header->Used - 1; i >= 0; i--)
            {
                Slot* slot = SlotAt(i);
                if ((slot->Generation & 1) != 0)
                {
                    slot->Generation++;
                    slot->Item = default;
                }
                slot->NextFree = _header->FreeHead;
                _header->FreeHead = i;
            }
            _header->Count = 0;
        }
    }

    /// <summary>For foreach: the handle of each item, in slot order.</summary>
    public Enumerator GetEnumerator() => new Enumerator(this);

    /// <summary>Walks the handles of the items in a pool.</summary>
    public struct Enumerator
    {
        readonly Pool<T> _pool;
        int _index;
        Handle<T> _current;

        internal Enumerator(Pool<T> pool)
        {
            _pool = pool;
            _index = -1;
            _current = default;
        }

        public bool MoveNext()
        {
            if (_pool._header == null)
                return false;
            unsafe
            {
                // SAFETY: a live header; indexes below Used.
                while (++_index < _pool._header->Used)
                {
                    Slot* slot = _pool.SlotAt(_index);
                    if ((slot->Generation & 1) != 0)
                    {
                        _current = new Handle<T>(_pool._header->Id, _index, slot->Generation);
                        return true;
                    }
                }
            }
            return false;
        }

        public Handle<T> Current => _current;
    }

    // The live slot the handle finds, or null.
    Slot* Find(Handle<T> handle)
    {
        if (_header == null || handle.IsNull)
            return null;
        unsafe
        {
            // SAFETY: a live header; the index is checked against Used.
            if (handle.Pool != _header->Id || (uint)handle.Index >= (uint)_header->Used)
                return null;
            Slot* slot = SlotAt(handle.Index);
            if (Arena.DebugChecks)
                Arena.CheckLive(_header->Arena, slot, (nuint)sizeof(Slot));
            return slot->Generation == handle.Generation ? slot : null;
        }
    }

    void Refuse(Handle<T> handle)
    {
        if (handle.IsNull)
            Environment.FailFast("Pool: null handle");
        if (_header == null)
            Environment.FailFast("Pool: the pool isn't created");
        unsafe
        {
            // SAFETY: a live header.
            if (handle.Pool != _header->Id)
                Environment.FailFast("Pool: the handle is from another pool");
        }
        Environment.FailFast("Pool: the handle's item was removed or moved");
    }

    Slot* SlotAt(int index)
    {
        unsafe
        {
            // SAFETY: callers pass an index below Capacity, whose chunk exists.
            Slot* chunk = (Slot*)_header->Chunks.Get(index >> _header->Shift);
            return chunk + (index & ((1 << _header->Shift) - 1));
        }
    }

    bool AddChunk()
    {
        unsafe
        {
            // SAFETY: a live header. Zeroed slots have generation 0: free.
            int size = 1 << _header->Shift;
            Slot* chunk = _header->Arena.AllocArray<Slot>(size);
            if (chunk == null || !_header->Chunks.Add((nint)chunk))
                return false;
            _header->Capacity += size;
            return true;
        }
    }
}
