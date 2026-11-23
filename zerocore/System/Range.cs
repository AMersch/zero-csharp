using System.Runtime.CompilerServices;

namespace System
{
    // ^1 and 1..^1. The compiler needs these two types to exist; for a type
    // with Length (or Count) and an int indexer, span[^1] becomes
    // span[span.Length - 1], and for a type with Slice(int, int), span[a..b]
    // becomes a Slice call: Span<T> and ReadOnlySpan<T> need nothing more.
    // string[a..b] calls Substring and array[a..b] calls
    // RuntimeHelpers.GetSubArray, and both allocate a copy; text.AsSpan()[a..b]
    // doesn't.
    public readonly struct Index : IEquatable<Index>
    {
        // The value, or its bitwise complement when counted from the end, as in .NET.
        private readonly int _value;

        public Index(int value, bool fromEnd = false)
        {
            if (value < 0)
                Environment.FailFast("Index: value must not be negative");
            _value = fromEnd ? ~value : value;
        }

        private Index(int value, int unused) => _value = value;

        public static Index Start => new Index(0, 0);

        public static Index End => new Index(~0, 0);

        public static Index FromStart(int value) => new Index(value);

        public static Index FromEnd(int value) => new Index(value, true);

        public int Value => _value < 0 ? ~_value : _value;

        public bool IsFromEnd => _value < 0;

        // The position in a sequence of `length` elements. Like .NET, it doesn't
        // check the result against length: the indexer or Slice that uses it does.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetOffset(int length)
        {
            int offset = _value;
            if (IsFromEnd)
                offset += length + 1;
            return offset;
        }

        public static implicit operator Index(int value) => FromStart(value);

        public bool Equals(Index other) => _value == other._value;

        public override bool Equals(object value) => value is Index other && _value == other._value;

        public override int GetHashCode() => _value;

        public override string ToString() => IsFromEnd ? "^" + Value.ToString() : Value.ToString();
    }

    public readonly struct Range : IEquatable<Range>
    {
        public Index Start { get; }

        public Index End { get; }

        public Range(Index start, Index end)
        {
            Start = start;
            End = end;
        }

        public static Range StartAt(Index start) => new Range(start, Index.End);

        public static Range EndAt(Index end) => new Range(Index.Start, end);

        public static Range All => new Range(Index.Start, Index.End);

        // Where the range starts in a sequence of `length` elements, and how many
        // it covers. Ends the program if it doesn't fit, as .NET would throw.
        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            int start = Start.GetOffset(length);
            int end = End.GetOffset(length);
            if ((uint)end > (uint)length || (uint)start > (uint)end)
                Environment.FailFast("Range: out of range for the length");
            return (start, end - start);
        }

        public bool Equals(Range other) => Start.Equals(other.Start) && End.Equals(other.End);

        public override bool Equals(object value) => value is Range other && Equals(other);

        public override int GetHashCode() => Start.GetHashCode() * 31 + End.GetHashCode();

        public override string ToString() => Start.ToString() + ".." + End.ToString();
    }
}

namespace System.Runtime.CompilerServices
{
    public static partial class RuntimeHelpers
    {
        // array[a..b]: a new array holding a copy of those elements.
        public static T[] GetSubArray<T>(T[] array, Range range)
        {
            if (array == null)
                Environment.FailFast("RuntimeHelpers.GetSubArray: array is null");
            (int offset, int length) = range.GetOffsetAndLength(array.Length);
            T[] result = new T[length];
            for (int i = 0; i < length; i++)
                result[i] = array[offset + i];
            return result;
        }
    }
}
