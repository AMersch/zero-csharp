// BankedList<T>: a growable list in the Commander X16's banked RAM, for
// lists too big for the 38 KB a program runs in. Only for programs built
// with ./build.sh --cx16.
//
//   BankedList<int> scores = BankedList<int>.Create();
//   scores.Add(120);
//   scores.Add(95);
//   int best = scores[0];
//   scores.RemoveAt(0);
//
// Like List<T>, a BankedList is a handle: copies share the same contents,
// like passing a pointer in C. Its count and capacity are kept with its
// elements in the banks (lib/Banks.cs), so they cost a bank access to read.
// The elements go in and out by copying: see BankedArray for what that costs
// and why Read and AddRange beat many single calls. T has to be an
// unmanaged type: no references.
//
// - A default(BankedList<T>) is "not created": Count is 0 and Add fails.
// - Growing doubles the capacity, in place while the list is the last thing
//   the banks handed out, and otherwise into a new region (the old one stays
//   reserved until Banks.Reset or Banks.Release).
// - Add and AddRange return false when the banks are full; nothing changes.
// - An out-of-range index stops the program, as for List.
// - After Banks.Reset or Banks.Release takes the list's room back, it must
//   not be used.

using System;

public readonly struct BankedList<T> where T : unmanaged
{
    // In the banks, at the list's address: the count, the capacity, and
    // where the elements are.
    const int HeaderSize = 12;

    readonly int _header;
    readonly bool _created;

    BankedList(int header)
    {
        _header = header;
        _created = true;
    }

    /// <summary>Returns a new list with room for <paramref name="capacity"/> elements, or a not-created list if the banks are full.</summary>
    public static BankedList<T> Create(int capacity = 64)
    {
        if (capacity < 4)
            capacity = 4;
        int mark = Banks.Mark();
        int header = Banks.Alloc(HeaderSize);
        int data = capacity > int.MaxValue / SizeOfT() ? -1 : Banks.Alloc(capacity * SizeOfT());
        if (header < 0 || data < 0)
        {
            Banks.Release(mark);
            return default;
        }
        BankedList<T> list = new BankedList<T>(header);
        list.WriteHeader(0, capacity, data);
        return list;
    }

    public bool IsCreated => _created;

    public int Count
    {
        get
        {
            ReadHeader(out int count, out _, out _);
            return count;
        }
    }

    public int Capacity
    {
        get
        {
            ReadHeader(out _, out int capacity, out _);
            return capacity;
        }
    }

    /// <summary>Appends an item. Returns false if the list isn't created or the banks are full.</summary>
    public bool Add(T item)
    {
        if (!_created)
            return false;
        ReadHeader(out int count, out int capacity, out int data);
        if (count == capacity && !Grow(count, ref capacity, ref data, count + 1))
            return false;
        unsafe
        {
            // SAFETY: the span is the parameter's own bytes, sizeof(T) of
            // them; the element is inside the list's capacity.
            Banks.Write(data + count * sizeof(T), new ReadOnlySpan<byte>(&item, sizeof(T)));
        }
        WriteHeader(count + 1, capacity, data);
        return true;
    }

    /// <summary>Appends a span's elements. Returns false, and appends nothing, if the list isn't created or the banks haven't the room.</summary>
    public bool AddRange(ReadOnlySpan<T> items)
    {
        if (!_created)
            return false;
        ReadHeader(out int count, out int capacity, out int data);
        if (items.Length > int.MaxValue - count)
            return false;
        int needed = count + items.Length;
        if (needed > capacity && !Grow(count, ref capacity, ref data, needed))
            return false;
        unsafe
        {
            // SAFETY: the byte span covers the span's own elements, and the
            // room for them is inside the capacity.
            fixed (T* p = items)
                Banks.Write(data + count * sizeof(T), new ReadOnlySpan<byte>(p, items.Length * sizeof(T)));
        }
        WriteHeader(needed, capacity, data);
        return true;
    }

    /// <summary>The element at an index. Stops the program if the index is out of range.</summary>
    public T this[int index]
    {
        get
        {
            ReadHeader(out int count, out _, out int data);
            CheckIndex(index, count);
            T value = default;
            unsafe
            {
                // SAFETY: the span is the local's own bytes, and CheckIndex
                // put the element inside the list.
                Banks.Read(data + index * sizeof(T), new Span<byte>(&value, sizeof(T)));
            }
            return value;
        }
        set
        {
            ReadHeader(out int count, out _, out int data);
            CheckIndex(index, count);
            unsafe
            {
                // SAFETY: as above, for the parameter's bytes.
                Banks.Write(data + index * sizeof(T), new ReadOnlySpan<byte>(&value, sizeof(T)));
            }
        }
    }

    /// <summary>Copies elements from an index on to a span. Returns false, and copies nothing, if they aren't all in the list.</summary>
    public bool Read(int index, Span<T> destination)
    {
        ReadHeader(out int count, out _, out int data);
        if (!_created || index < 0 || destination.Length > count - index)
            return false;
        unsafe
        {
            // SAFETY: the byte span covers the destination's own elements,
            // and the source is inside the list.
            fixed (T* p = destination)
                return Banks.Read(data + index * sizeof(T), new Span<byte>(p, destination.Length * sizeof(T)));
        }
    }

    /// <summary>Removes the element at an index, moving the ones after it up. Returns false if there is none.</summary>
    public bool RemoveAt(int index)
    {
        ReadHeader(out int count, out int capacity, out int data);
        if (!_created || (uint)index >= (uint)count)
            return false;
        int size = SizeOfT();
        Banks.Copy(data + (index + 1) * size, data + index * size, (count - index - 1) * size);
        WriteHeader(count - 1, capacity, data);
        return true;
    }

    /// <summary>Removes all elements, keeping the capacity.</summary>
    public void Clear()
    {
        if (!_created)
            return;
        ReadHeader(out _, out int capacity, out int data);
        WriteHeader(0, capacity, data);
    }

    // Makes room for `needed` elements: in place if the elements are the last
    // thing the banks handed out, else in a new region the elements are copied to.
    static bool Grow(int count, ref int capacity, ref int data, int needed)
    {
        int size = SizeOfT();
        int newCapacity = capacity;
        while (newCapacity < needed)
        {
            if (newCapacity > int.MaxValue / 2)
                return false;
            newCapacity *= 2;
        }
        if (newCapacity > int.MaxValue / size)
            return false;

        if (!Banks.TryExtend(data, capacity * size, newCapacity * size))
        {
            int moved = Banks.Alloc(newCapacity * size);
            if (moved < 0)
                return false;
            Banks.Copy(data, moved, count * size);
            data = moved;
        }
        capacity = newCapacity;
        return true;
    }

    void ReadHeader(out int count, out int capacity, out int data)
    {
        count = capacity = data = 0;
        if (!_created)
            return;
        Span<int> h = stackalloc int[3];
        unsafe
        {
            // SAFETY: the byte span covers h's own 12 bytes.
            fixed (int* p = h)
                Banks.Read(_header, new Span<byte>(p, HeaderSize));
        }
        count = h[0];
        capacity = h[1];
        data = h[2];
    }

    void WriteHeader(int count, int capacity, int data)
    {
        Span<int> h = stackalloc int[3];
        h[0] = count;
        h[1] = capacity;
        h[2] = data;
        unsafe
        {
            // SAFETY: as in ReadHeader.
            fixed (int* p = h)
                Banks.Write(_header, new ReadOnlySpan<byte>(p, HeaderSize));
        }
    }

    static int SizeOfT()
    {
        unsafe { return sizeof(T); }
    }

    static void CheckIndex(int index, int count)
    {
        if ((uint)index >= (uint)count)
            Environment.FailFast("BankedList: index out of range");
    }
}
