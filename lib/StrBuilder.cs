// StrBuilder: growable string buffer backed by an Arena, with $"..." support.
//
//   StrBuilder sb = StrBuilder.Create(arena);
//   sb.Append("total: ");
//   sb.Append($"{count} items, {price:F2} each");
//   Out.Line(sb.AsSpan());
//
// Like List<T>, a StrBuilder is a handle to a header in the arena, so copies
// share the same contents, and default(StrBuilder) is "not created": it reads
// as empty and Append returns false. Append also returns false when the arena
// is full. Growing extends the buffer in place while it is still the arena's
// most recent allocation; otherwise it copies into a fresh buffer and the old
// one stays allocated until the arena is reset.
//
// The buffer is laid out like a string object, [MethodTable*][length][chars],
// so Str.Format(arena, $"...") turns a finished buffer into a string without copying.

using System;
using System.Runtime.CompilerServices;

public struct StrBuilder
{
    const int MinCapacity = 16;

    struct Header
    {
        public Arena Arena;    // where the builder lives and grows
        public byte* Object;   // string-object-shaped buffer (see Str.MakeString)
        public int Length;
        public int Capacity;   // in chars, terminator not included
    }

    Header* _header;

    /// <summary>Returns a new builder in <paramref name="arena"/>, or a not-created one if it is out of memory.</summary>
    public static StrBuilder Create(Arena arena, int initialCapacity = 64)
    {
        if (initialCapacity < MinCapacity)
            initialCapacity = MinCapacity;

        Header* header = arena.Alloc<Header>();
        byte* obj = Str.AllocStringObject(arena, initialCapacity);
        if (header == null || obj == null)
            return default;

        unsafe
        {
            // SAFETY: header was just allocated and zeroed.
            header->Arena = arena;
            header->Object = obj;
            header->Capacity = initialCapacity;
        }
        return new StrBuilder { _header = header };
    }

    public bool IsCreated => _header != null;

    public int Length
    {
        get
        {
            if (_header == null)
                return 0;
            unsafe { return _header->Length; } // SAFETY: a live header (see List<T>).
        }
    }

    /// <summary>The text so far. Valid until the next Append or until the arena is reset.</summary>
    public ReadOnlySpan<char> AsSpan()
    {
        if (_header == null)
            return default;
        DebugCheck();
        unsafe { return new ReadOnlySpan<char>(Chars(), _header->Length); } // SAFETY: Length chars are initialized.
    }

    public bool Append(ReadOnlySpan<char> text)
    {
        if (_header == null)
            return false;
        DebugCheck();

        unsafe
        {
            // SAFETY: a live header; after Grow there is room for text at Chars() + Length.
            int length = _header->Length;
            if (text.Length > _header->Capacity - length && !Grow(text.Length))
                return false;
            text.CopyTo(new Span<char>(Chars() + length, text.Length));
            _header->Length = length + text.Length;
        }
        return true;
    }

    public bool Append(char value)
    {
        Span<char> one = stackalloc char[1];
        one[0] = value;
        return Append(one);
    }

    public bool Append(long value)
    {
        Span<char> buffer = stackalloc char[Fmt.MaxChars];
        return Append(buffer.Slice(0, Fmt.Int(value, buffer)));
    }

    /// <summary>Appends $"..." directly, without an intermediate string.</summary>
    public bool Append([InterpolatedStringHandlerArgument("")] ref InterpolatedHandler handler) => handler.Ok;

    public void Clear()
    {
        if (_header != null)
            unsafe { _header->Length = 0; } // SAFETY: a live header.
    }

    /// <summary>Copies the text into a new string in the builder's own arena. Returns null if it is out of memory.</summary>
    public string ToArenaString()
    {
        if (_header == null)
            return null;
        unsafe { return Str.ToArenaString(_header->Arena, AsSpan()); } // SAFETY: a live header.
    }

    /// <summary>
    /// Copies the text into a new string in <paramref name="target"/>: build in a
    /// scratch arena, keep the result in a longer-lived one.
    /// </summary>
    public string ToArenaString(Arena target) => Str.ToArenaString(target, AsSpan());

    /// <summary>Turns the buffer itself into a string. The builder must not be used afterwards.</summary>
    internal string TakeString()
    {
        if (_header == null)
            return null;
        unsafe { return Str.MakeString(_header->Object, _header->Length); } // SAFETY: Object has room for Capacity >= Length chars.
    }

    // Under Arena.DebugChecks, stops the program if the builder's memory was freed.
    void DebugCheck()
    {
        if (Arena.DebugChecks)
            unsafe { Arena.CheckLive(_header->Arena, _header->Object, Str.StringObjectSize(_header->Capacity)); } // SAFETY: callers checked _header != null.
    }

    char* Chars()
    {
        unsafe { return (char*)(_header->Object + RuntimeHelpers.OffsetToStringData); } // SAFETY: callers checked _header != null.
    }

    bool Grow(int extra)
    {
        int length;
        int capacity;
        Arena arena;
        byte* oldObj;
        unsafe
        {
            length = _header->Length; // SAFETY: callers checked _header != null.
            capacity = _header->Capacity;
            arena = _header->Arena;
            oldObj = _header->Object;
        }

        long needed = (long)length + extra;
        long newCapacity = (long)capacity * 2;
        if (newCapacity < needed)
            newCapacity = needed;
        if (newCapacity > int.MaxValue / 2)
            newCapacity = needed;
        if (newCapacity > int.MaxValue / 2)
            return false;

        // Nothing was allocated after the buffer: just make it longer.
        if (arena.TryExtend(oldObj, Str.StringObjectSize(capacity), Str.StringObjectSize((int)newCapacity)))
        {
            unsafe { _header->Capacity = (int)newCapacity; } // SAFETY: a live header.
            return true;
        }

        byte* obj = Str.AllocStringObject(arena, (int)newCapacity);
        if (obj == null)
            return false;

        unsafe
        {
            // SAFETY: both buffers hold at least length chars.
            AsSpan().CopyTo(new Span<char>(obj + RuntimeHelpers.OffsetToStringData, length));
            _header->Object = obj;
            _header->Capacity = (int)newCapacity;
        }
        return true;
    }

    /// <summary>
    /// Lets StrBuilder.Append and Str.Format take $"..." and format each hole
    /// straight into the arena. Supports {x,alignment} and {x:format}; see Fmt for
    /// the format strings.
    /// </summary>
    [InterpolatedStringHandler]
    public ref struct InterpolatedHandler
    {
        StrBuilder _builder;
        bool _ok;

        /// <summary>Used by StrBuilder.Append: appends to that builder.</summary>
        public InterpolatedHandler(int literalLength, int formattedCount, StrBuilder builder)
        {
            _builder = builder;
            _ok = builder.IsCreated;
        }

        /// <summary>Used by Str.Format: starts a new builder in that arena.</summary>
        public InterpolatedHandler(int literalLength, int formattedCount, Arena arena)
        {
            _builder = Create(arena, literalLength + formattedCount * 8);
            _ok = _builder.IsCreated;
        }

        internal bool Ok => _ok;

        internal string TakeString() => _ok ? _builder.TakeString() : null;

        public void AppendLiteral(string value) => Put(value, 0);

        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string format = null) => Put(value, alignment);

        public void AppendFormatted(char value, int alignment = 0)
        {
            Span<char> one = stackalloc char[1];
            one[0] = value;
            Put(one, alignment);
        }

        public void AppendFormatted(long value, int alignment = 0, string format = null)
        {
            Span<char> buffer = stackalloc char[Fmt.MaxChars];
            Put(buffer.Slice(0, Fmt.Long(value, format, buffer)), alignment);
        }

        public void AppendFormatted(ulong value, int alignment = 0, string format = null)
        {
            Span<char> buffer = stackalloc char[Fmt.MaxChars];
            Put(buffer.Slice(0, Fmt.ULong(value, format, buffer)), alignment);
        }

        public void AppendFormatted(double value, int alignment = 0, string format = null)
        {
            Span<char> buffer = stackalloc char[Fmt.MaxChars];
            Put(buffer.Slice(0, Fmt.Double(value, format, buffer)), alignment);
        }

        public void AppendFormatted(bool value, int alignment = 0) => Put(Fmt.Bool(value), alignment);

        // alignment > 0 pads on the left (right-aligns), < 0 pads on the right.
        void Put(scoped ReadOnlySpan<char> text, int alignment)
        {
            int padding = (alignment < 0 ? -alignment : alignment) - text.Length;
            if (alignment > 0)
                Pad(padding);
            if (_ok)
                _ok = _builder.Append(text);
            if (alignment < 0)
                Pad(padding);
        }

        void Pad(int count)
        {
            for (; count > 0 && _ok; count--)
                _ok = _builder.Append(' ');
        }
    }
}
