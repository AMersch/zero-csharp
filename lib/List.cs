// List<T>: growable array backed by the Arena.
//
// List<T> is a handle (one pointer) to a header that lives in the arena, so
// copies of a List share the same contents, like passing List* in C.
// A default(List<T>) is "not created": reads see an empty list and Add fails.
//
// Growing copies into a fresh arena buffer; the old buffer stays allocated
// until Arena.Reset(). Out-of-range Get/Set fail fast.

using System;

public struct List<T> where T : unmanaged
{
    const int MinCapacity = 4;

    struct Header
    {
        public T* Data;
        public int Count;
        public int Capacity;
    }

    Header* _header;

    /// <summary>Returns a new list, or a not-created list if the arena is out of memory.</summary>
    public static List<T> Create(int initialCapacity = 16)
    {
        if (initialCapacity < MinCapacity)
            initialCapacity = MinCapacity;

        Header* header = Arena.Alloc<Header>();
        T* data = Arena.AllocArray<T>(initialCapacity, Arena.DefaultAlign, zero: false);
        if (header == null || data == null)
            return default;

        unsafe
        {
            // SAFETY: header was just allocated and zeroed.
            header->Data = data;
            header->Capacity = initialCapacity;
        }
        return new List<T> { _header = header };
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

    public int Capacity
    {
        get
        {
            if (_header == null)
                return 0;
            unsafe { return _header->Capacity; }
        }
    }

    /// <summary>Appends an item. Returns false if the list isn't created or the arena is full.</summary>
    public bool Add(T item)
    {
        if (_header == null)
            return false;

        unsafe
        {
            // SAFETY: _header points at a live header (the arena hasn't been reset;
            // Arena.Reset is unsafe and its caller promises that).
            if (_header->Count == _header->Capacity && !Grow())
                return false;
            _header->Data[_header->Count++] = item;
        }
        return true;
    }

    public T Get(int index)
    {
        unsafe
        {
            CheckIndex(index);
            return _header->Data[index]; // SAFETY: CheckIndex validated header and index.
        }
    }

    public void Set(int index, T value)
    {
        unsafe
        {
            CheckIndex(index);
            _header->Data[index] = value; // SAFETY: CheckIndex validated header and index.
        }
    }

    /// <summary>Pointer to an element. Invalidated by Add (growth) and RemoveAt.</summary>
    public T* GetPtr(int index)
    {
        unsafe
        {
            CheckIndex(index);
            return &_header->Data[index];
        }
    }

    public bool RemoveAt(int index)
    {
        if (_header == null)
            return false;

        unsafe
        {
            int count = _header->Count;
            if ((uint)index >= (uint)count)
                return false;

            T* data = _header->Data;
            for (int i = index; i < count - 1; i++)
                data[i] = data[i + 1];
            _header->Count = count - 1;
        }
        return true;
    }

    public void Clear()
    {
        if (_header == null)
            return;
        unsafe { _header->Count = 0; }
    }

    /// <summary>View of the current items.</summary>
    /// <safety>The span is invalidated by Add (growth), RemoveAt, Clear and Arena.Reset.</safety>
    public unsafe Span<T> AsSpan()
    {
        if (_header == null)
            return default;
        unsafe { return new Span<T>(_header->Data, _header->Count); }
    }

    unsafe bool Grow()
    {
        unsafe
        {
            int oldCapacity = _header->Capacity;
            if (oldCapacity > int.MaxValue / 2)
                return false;

            int newCapacity = oldCapacity * 2;
            T* newData = Arena.AllocArray<T>(newCapacity, Arena.DefaultAlign, zero: false);
            if (newData == null)
                return false;

            T* oldData = _header->Data;
            for (int i = 0; i < _header->Count; i++)
                newData[i] = oldData[i];

            _header->Data = newData;
            _header->Capacity = newCapacity;
        }
        return true;
    }

    unsafe void CheckIndex(int index)
    {
        unsafe
        {
            if (_header == null || (uint)index >= (uint)_header->Count)
                Environment.FailFast(null);
        }
    }
}
