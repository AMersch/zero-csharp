// StrDict<TValue>: hash map with string keys compared by their text, in an Arena.
// Values can be of any type: numbers, structs, strings or other references.
//
//   StrDict<int> counts = StrDict<int>.Create(keep);
//   int length = In.ReadLine(buffer);
//   ReadOnlySpan<char> word = buffer.Slice(0, length);
//   counts.TryGet(word, out int n);              // looks up a span: no allocation
//   counts.Set(word, n + 1);                     // a new key is copied into keep
//
// Keys are taken as ReadOnlySpan<char> (a string converts implicitly) and a new
// key is copied into the map's arena, so the map never depends on where its
// keys came from: a buffer that is overwritten, or a scratch arena that is
// reset. A removed key's copy stays allocated until the arena is reset.
//
// Values are stored as they are. A reference value (a string, say) points
// wherever it was allocated and must outlive the map's use of it, as in RefList.
//
// Like Dict, StrDict is a handle to a header in the arena, so copies share
// contents, and default(StrDict) is "not created" (lookups miss, Set fails).
// Open addressing with linear probing, kept at most 3/4 full; keys are hashed
// with FNV-1a over their chars.

using System;
using System.Runtime.CompilerServices;

public struct StrDict<TValue>
{
    const int MinCapacity = 8;

    struct Entry
    {
        public string Key; // the map's own copy
        public TValue Value;
        public uint Hash;
        public bool Used;
    }

    struct Header
    {
        public Arena Arena;   // where the map, its keys and its tables live
        public byte* Entries; // Capacity entries; reached through At()
        public int Capacity;  // always a power of two
        public int Count;
    }

    Header* _header;

    /// <summary>Returns a new map in <paramref name="arena"/>, or a not-created map if it is out of memory.</summary>
    public static StrDict<TValue> Create(Arena arena, int capacity = 32)
    {
        int rounded = MinCapacity;
        while (rounded < capacity && rounded <= int.MaxValue / 2)
            rounded *= 2;

        Header* header = arena.Alloc<Header>();
        byte* entries = AllocEntries(arena, rounded);
        if (header == null || entries == null)
            return default;

        unsafe
        {
            // SAFETY: header was just allocated and zeroed.
            header->Arena = arena;
            header->Entries = entries;
            header->Capacity = rounded;
        }
        return new StrDict<TValue> { _header = header };
    }

    public bool IsCreated => _header != null;

    public int Count
    {
        get
        {
            if (_header == null)
                return 0;
            unsafe { return _header->Count; }
        }
    }

    /// <summary>
    /// Inserts or overwrites. A new key is copied into the map's arena. Returns
    /// false if the map isn't created or the arena is full.
    /// </summary>
    public bool Set(ReadOnlySpan<char> key, TValue value)
    {
        if (_header == null)
            return false;
        DebugCheck();

        uint hash = Hash(key);
        int index = Find(key, hash);
        unsafe
        {
            // SAFETY: _header points at a live header (see List<T>.Add); Find
            // returns an index below Capacity.
            if (index >= 0)
            {
                At(_header, index).Value = value;
                return true;
            }

            // Keep the load factor at or below 3/4.
            if ((_header->Count + 1) * 4 > _header->Capacity * 3 && !Grow())
                return false;

            string copy = Str.ToArenaString(_header->Arena, key);
            if (copy is null)
                return false;
            Insert(_header, copy, value, hash);
        }
        return true;
    }

    public bool TryGet(ReadOnlySpan<char> key, out TValue value)
    {
        if (_header != null)
        {
            DebugCheck();
            int index = Find(key, Hash(key));
            if (index >= 0)
            {
                unsafe { value = At(_header, index).Value; } // SAFETY: a live header; index < Capacity.
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool ContainsKey(ReadOnlySpan<char> key) => TryGet(key, out _);

    /// <summary>Removes a key. Returns false if it wasn't there.</summary>
    public bool Remove(ReadOnlySpan<char> key)
    {
        if (_header == null)
            return false;
        DebugCheck();

        int hole = Find(key, Hash(key));
        if (hole < 0)
            return false;

        unsafe
        {
            // SAFETY: a live header; every index is masked into [0, Capacity).
            // Close the gap as Dict.Remove does (see the comment there): move back
            // each following entry whose home slot is at or before the hole.
            int mask = _header->Capacity - 1;
            int next = hole;
            while (true)
            {
                next = (next + 1) & mask;
                if (!At(_header, next).Used)
                    break;
                int home = (int)At(_header, next).Hash & mask;
                bool homeAfterHole = hole <= next ? (hole < home && home <= next) : (hole < home || home <= next);
                if (homeAfterHole)
                    continue;
                At(_header, hole) = At(_header, next);
                hole = next;
            }

            At(_header, hole) = default;
            _header->Count--;
        }
        return true;
    }

    /// <summary>
    /// Returns an independent copy in <paramref name="target"/>, keys included, so
    /// it outlives this map's arena. Returns a not-created map if this one isn't
    /// created or the target is out of memory.
    /// </summary>
    /// <remarks>
    /// Values are copied as they are: a reference value still points at the same
    /// object, wherever that lives.
    /// </remarks>
    public StrDict<TValue> Clone(Arena target)
    {
        if (_header == null)
            return default;
        DebugCheck();

        unsafe
        {
            // SAFETY: both headers are live; the copy has the same capacity, so
            // every entry keeps its slot.
            int capacity = _header->Capacity;
            Header* header = target.Alloc<Header>();
            byte* entries = AllocEntries(target, capacity);
            if (header == null || entries == null)
                return default;

            header->Arena = target;
            header->Entries = entries;
            header->Capacity = capacity;
            header->Count = _header->Count;

            for (int i = 0; i < capacity; i++)
            {
                ref Entry source = ref At(_header, i);
                if (!source.Used)
                    continue;
                string key = Str.ToArenaString(target, source.Key);
                if (key is null)
                    return default;
                ref Entry copy = ref At(header, i);
                copy = source;
                copy.Key = key;
            }
            return new StrDict<TValue> { _header = header };
        }
    }

    /// <summary>A key with its value, as foreach yields them.</summary>
    public struct Pair
    {
        public string Key; // the map's own copy: valid until the map's arena is reset
        public TValue Value;
    }

    /// <summary>For foreach: every key and value, in no particular order.</summary>
    /// <remarks>Don't Set or Remove during the loop: entries move, so some may be skipped or seen twice.</remarks>
    public Enumerator GetEnumerator()
    {
        if (_header != null)
            DebugCheck();
        return new Enumerator(this);
    }

    public struct Enumerator
    {
        readonly StrDict<TValue> _dict;
        int _index;

        internal Enumerator(StrDict<TValue> dict)
        {
            _dict = dict;
            _index = -1;
        }

        public bool MoveNext()
        {
            Header* header = _dict._header;
            if (header == null)
                return false;
            unsafe
            {
                // SAFETY: a live header; _index stays below Capacity.
                while (++_index < header->Capacity)
                {
                    if (At(header, _index).Used)
                        return true;
                }
            }
            return false;
        }

        public Pair Current
        {
            get
            {
                unsafe
                {
                    // SAFETY: MoveNext returned true, so _index names a used entry.
                    ref Entry entry = ref At(_dict._header, _index);
                    return new Pair { Key = entry.Key, Value = entry.Value };
                }
            }
        }
    }

    // The slot holding key, or -1.
    int Find(ReadOnlySpan<char> key, uint hash)
    {
        unsafe
        {
            // SAFETY: callers checked _header != null; the load factor guarantees
            // an empty slot, so the probe ends.
            int mask = _header->Capacity - 1;
            int index = (int)hash & mask;
            while (At(_header, index).Used)
            {
                ref Entry entry = ref At(_header, index);
                if (entry.Hash == hash && entry.Key.AsSpan().SequenceEqual(key))
                    return index;
                index = (index + 1) & mask;
            }
        }
        return -1;
    }

    // Puts a key known to be absent into the first free slot of its probe.
    static unsafe void Insert(Header* header, string key, TValue value, uint hash)
    {
        unsafe
        {
            int mask = header->Capacity - 1;
            int index = (int)hash & mask;
            while (At(header, index).Used)
                index = (index + 1) & mask;

            ref Entry slot = ref At(header, index);
            slot.Key = key;
            slot.Value = value;
            slot.Hash = hash;
            slot.Used = true;
            header->Count++;
        }
    }

    unsafe bool Grow()
    {
        unsafe
        {
            int oldCapacity = _header->Capacity;
            if (oldCapacity > int.MaxValue / 2)
                return false;

            int newCapacity = oldCapacity * 2;
            byte* newEntries = AllocEntries(_header->Arena, newCapacity);
            if (newEntries == null)
                return false;

            Header old = *_header;
            _header->Entries = newEntries;
            _header->Capacity = newCapacity;
            _header->Count = 0;

            // Keys are already the map's own copies; only the table moves.
            for (int i = 0; i < oldCapacity; i++)
            {
                ref Entry entry = ref At(&old, i);
                if (entry.Used)
                    Insert(_header, entry.Key, entry.Value, entry.Hash);
            }
        }
        return true;
    }

    // A zeroed table: every slot unused.
    static byte* AllocEntries(Arena arena, int capacity)
    {
        nuint size = (nuint)Unsafe.SizeOf<Entry>();
        nuint bytes = size * (nuint)capacity;
        if (bytes / size != (nuint)capacity)
            return null; // overflowed: possible with 32-bit pointers
        return (byte*)arena.Alloc(bytes);
    }

    // Entry i of a table. Entry holds references, so it can't be reached through
    // an Entry*; without a GC, a ref into arena memory is just as good.
    static unsafe ref Entry At(Header* header, int index)
    {
        unsafe
        {
            // SAFETY: the caller keeps index below header->Capacity.
            return ref Unsafe.AsRef<Entry>(header->Entries + (nuint)index * (nuint)Unsafe.SizeOf<Entry>());
        }
    }

    static uint Hash(ReadOnlySpan<char> key)
    {
        uint hash = 2166136261;
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            hash = (hash ^ (byte)c) * 16777619;
            hash = (hash ^ (byte)(c >> 8)) * 16777619;
        }
        return hash;
    }

    // Under Arena.DebugChecks, stops the program if the map's memory was freed.
    void DebugCheck()
    {
        if (Arena.DebugChecks)
            unsafe { Arena.CheckLive(_header->Arena, _header->Entries, 1); } // SAFETY: callers checked _header != null.
    }
}
