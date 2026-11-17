// Dict<TKey, TValue>: open-addressing hash map backed by an Arena.
//
// Like List<T>, Dict is a handle to an arena header, so copies share contents,
// and default(Dict) is "not created" (lookups miss, Set fails).
//
// Keys are hashed (FNV-1a) and compared by their raw bytes. That is exact for
// integers and for structs without padding. It is NOT value equality for
// floats (0.0 vs -0.0, NaN payloads) or for structs whose padding bytes may
// differ, so avoid such keys or normalize them first.

using System;

public struct Dict<TKey, TValue>
    where TKey : unmanaged
    where TValue : unmanaged
{
    const int MinCapacity = 8;

    struct Entry
    {
        public TKey Key;
        public TValue Value;
        public uint Hash;
        public bool Used;
    }

    struct Header
    {
        public Arena Arena; // where the map lives and grows
        public Entry* Entries;
        public int Capacity; // always a power of two
        public int Count;
    }

    Header* _header;

    /// <summary>Returns a new map in <paramref name="arena"/>, or a not-created map if it is out of memory.</summary>
    public static Dict<TKey, TValue> Create(Arena arena, int capacity = 32)
    {
        int rounded = MinCapacity;
        while (rounded < capacity && rounded <= int.MaxValue / 2)
            rounded *= 2;

        Header* header = arena.Alloc<Header>();
        Entry* entries = arena.AllocArray<Entry>(rounded);
        if (header == null || entries == null)
            return default;

        unsafe
        {
            // SAFETY: header was just allocated and zeroed.
            header->Arena = arena;
            header->Entries = entries;
            header->Capacity = rounded;
        }
        return new Dict<TKey, TValue> { _header = header };
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

    /// <summary>Inserts or overwrites. Returns false if the map isn't created or the arena is full.</summary>
    public bool Set(TKey key, TValue value)
    {
        if (_header == null)
            return false;

        unsafe
        {
            // SAFETY: _header points at a live header (see List<T>.Add).
            // Keep the load factor at or below 3/4.
            if ((_header->Count + 1) * 4 > _header->Capacity * 3 && !Grow())
                return false;

            Insert(_header, key, value, Hash(key));
        }
        return true;
    }

    public bool TryGet(TKey key, out TValue value)
    {
        if (_header != null)
        {
            uint hash = Hash(key);
            unsafe
            {
                int mask = _header->Capacity - 1;
                int index = (int)hash & mask;
                // The load factor guarantees at least one empty slot, so this terminates.
                while (_header->Entries[index].Used)
                {
                    Entry* entry = &_header->Entries[index];
                    if (entry->Hash == hash && BytesEqual(entry->Key, key))
                    {
                        value = entry->Value;
                        return true;
                    }
                    index = (index + 1) & mask;
                }
            }
        }

        value = default;
        return false;
    }

    public bool ContainsKey(TKey key) => TryGet(key, out _);

    /// <summary>
    /// Returns an independent copy in <paramref name="target"/> (this map's own
    /// arena or another one). Returns a not-created map if this one isn't created
    /// or the target is out of memory.
    /// </summary>
    /// <remarks>
    /// Keys and values are copied as they are. One that is, or contains, a pointer
    /// still points wherever it did: into the original's arena, if that's where
    /// it led.
    /// </remarks>
    public Dict<TKey, TValue> Clone(Arena target)
    {
        if (_header == null)
            return default;

        unsafe
        {
            // SAFETY: _header is a live header with Capacity entries; the copy's
            // table has the same capacity, so every entry keeps its slot.
            int capacity = _header->Capacity;
            Header* header = target.Alloc<Header>();
            Entry* entries = target.AllocArray<Entry>(capacity, Arena.DefaultAlign, zero: false);
            if (header == null || entries == null)
                return default;

            Entry* source = _header->Entries;
            for (int i = 0; i < capacity; i++)
                entries[i] = source[i];

            header->Arena = target;
            header->Entries = entries;
            header->Capacity = capacity;
            header->Count = _header->Count;
            return new Dict<TKey, TValue> { _header = header };
        }
    }

    static unsafe void Insert(Header* header, TKey key, TValue value, uint hash)
    {
        unsafe
        {
            int mask = header->Capacity - 1;
            int index = (int)hash & mask;
            while (header->Entries[index].Used)
            {
                Entry* entry = &header->Entries[index];
                if (entry->Hash == hash && BytesEqual(entry->Key, key))
                {
                    entry->Value = value;
                    return;
                }
                index = (index + 1) & mask;
            }

            Entry* slot = &header->Entries[index];
            slot->Key = key;
            slot->Value = value;
            slot->Hash = hash;
            slot->Used = true;
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
            Entry* newEntries = _header->Arena.AllocArray<Entry>(newCapacity);
            if (newEntries == null)
                return false;

            Entry* oldEntries = _header->Entries;
            _header->Entries = newEntries;
            _header->Capacity = newCapacity;
            _header->Count = 0;

            for (int i = 0; i < oldCapacity; i++)
            {
                if (oldEntries[i].Used)
                    Insert(_header, oldEntries[i].Key, oldEntries[i].Value, oldEntries[i].Hash);
            }
        }
        return true;
    }

    static uint Hash(TKey key)
    {
        byte* p = (byte*)&key;
        uint hash = 2166136261;
        unsafe
        {
            // SAFETY: p points at the local copy of key, sizeof(TKey) bytes long.
            for (int i = 0; i < sizeof(TKey); i++)
                hash = (hash ^ p[i]) * 16777619;
        }
        return hash;
    }

    static bool BytesEqual(TKey a, TKey b)
    {
        byte* pa = (byte*)&a;
        byte* pb = (byte*)&b;
        unsafe
        {
            // SAFETY: both point at local copies, sizeof(TKey) bytes long.
            for (int i = 0; i < sizeof(TKey); i++)
            {
                if (pa[i] != pb[i])
                    return false;
            }
        }
        return true;
    }
}
