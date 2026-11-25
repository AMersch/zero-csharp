// BankedArray<T>: an array in the Commander X16's banked RAM, for programs
// whose data is bigger than the 38 KB they run in. Only for programs built
// with ./build.sh --cx16.
//
//   BankedArray<int> counts = BankedArray<int>.Create(100000);   // 400 KB
//   counts[5] = 42;
//   counts[5]++;
//   BankedArray<byte> map = BankedArray<byte>.Create(200000, zero: false);
//
// The array's bytes are in the banks (lib/Banks.cs, whose Alloc reserves
// them), and each element goes in and out by copying: a read or write costs a
// bank switch and a copy, a few hundred processor cycles, so loop over
// ranges (Read, Write) when there are many. An element can lie across the
// border between two banks; that works. T has to be an unmanaged type: no
// references.
//
// - BankedArray is a value: copies refer to the same elements, like passing
//   a pointer in C. A default(BankedArray<T>) is "not created", with Length 0.
// - Create returns a not-created array if the banks haven't that much left.
// - An out-of-range index stops the program, as for List.
// - The bytes are taken back with Banks.Reset or Banks.Release, after which
//   the array must not be used.

using System;

public readonly struct BankedArray<T> where T : unmanaged
{
    readonly int _address;
    readonly int _length;
    readonly bool _created;

    BankedArray(int address, int length)
    {
        _address = address;
        _length = length;
        _created = true;
    }

    /// <summary>Returns a new array of <paramref name="length"/> elements, zeroed unless <paramref name="zero"/> is false, or a not-created array if the banks are full.</summary>
    public static BankedArray<T> Create(int length, bool zero = true)
    {
        int size;
        unsafe { size = sizeof(T); }
        if (length < 0 || length > int.MaxValue / size)
            return default;
        int address = Banks.Alloc(length * size);
        if (address < 0)
            return default;
        if (zero)
            Banks.Fill(address, length * size, 0);
        return new BankedArray<T>(address, length);
    }

    public bool IsCreated => _created;

    public int Length => _length;

    /// <summary>The element at an index. Stops the program if the index is out of range.</summary>
    public T this[int index]
    {
        get
        {
            CheckIndex(index);
            T value = default;
            unsafe
            {
                // SAFETY: the span is the local's own bytes, sizeof(T) of
                // them, and CheckIndex put the element inside the array.
                Banks.Read(_address + index * sizeof(T), new Span<byte>(&value, sizeof(T)));
            }
            return value;
        }
        set
        {
            CheckIndex(index);
            unsafe
            {
                // SAFETY: as above, for the parameter's bytes.
                Banks.Write(_address + index * sizeof(T), new ReadOnlySpan<byte>(&value, sizeof(T)));
            }
        }
    }

    /// <summary>Copies elements from an index on to a span. Returns false, and copies nothing, if they aren't all in the array.</summary>
    public bool Read(int index, Span<T> destination)
    {
        if (!Contains(index, destination.Length))
            return false;
        unsafe
        {
            // SAFETY: the byte span covers exactly the destination's own
            // elements, and Contains put the source inside the array.
            fixed (T* p = destination)
                return Banks.Read(_address + index * sizeof(T), new Span<byte>(p, destination.Length * sizeof(T)));
        }
    }

    /// <summary>Copies a span's elements into the array from an index on. Returns false, and copies nothing, if they won't all fit.</summary>
    public bool Write(int index, ReadOnlySpan<T> source)
    {
        if (!Contains(index, source.Length))
            return false;
        unsafe
        {
            // SAFETY: as in Read.
            fixed (T* p = source)
                return Banks.Write(_address + index * sizeof(T), new ReadOnlySpan<byte>(p, source.Length * sizeof(T)));
        }
    }

    /// <summary>Sets every element to its zero.</summary>
    public void Clear()
    {
        if (_created)
            Banks.Fill(_address, _length * SizeOfT(), 0);
    }

    /// <summary>Sets every element to a value.</summary>
    public void Fill(T value)
    {
        // Written a few elements at a time, from a small buffer.
        Span<T> chunk = stackalloc T[Math.Max(1, 64 / SizeOfT())];
        chunk.Fill(value);
        for (int at = 0; at < _length; at += chunk.Length)
            Write(at, chunk.Slice(0, Math.Min(chunk.Length, _length - at)));
    }

    /// <summary>Copies all elements to another array, which must be at least as long. Returns false, and copies nothing, if it isn't.</summary>
    public bool CopyTo(BankedArray<T> destination)
    {
        if (destination._length < _length)
            return false;
        Span<T> chunk = stackalloc T[Math.Max(1, 64 / SizeOfT())];
        for (int at = 0; at < _length; at += chunk.Length)
        {
            Span<T> part = chunk.Slice(0, Math.Min(chunk.Length, _length - at));
            Read(at, part);
            destination.Write(at, part);
        }
        return true;
    }

    static int SizeOfT()
    {
        unsafe { return sizeof(T); }
    }

    bool Contains(int index, int count)
        => _created && index >= 0 && count >= 0 && count <= _length - index;

    void CheckIndex(int index)
    {
        if (!_created || (uint)index >= (uint)_length)
            Environment.FailFast("BankedArray: index out of range");
    }
}
