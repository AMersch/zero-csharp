// Memory<T> and ReadOnlyMemory<T>: a piece of an array that, unlike a span,
// can be kept on the heap, so an async method can take one and use it after
// it has waited (Stream.ReadAsync, Socket.ReceiveAsync). Only arrays (and,
// for ReadOnlyMemory<char>, strings) back them here: no MemoryManager, no
// Pin.

using System.Runtime.CompilerServices;

namespace System
{
    /// <summary>A piece of an array, kept on the heap; Span gives its elements.</summary>
    public readonly struct Memory<T> : IEquatable<Memory<T>>
    {
        internal readonly T[]? _array;
        internal readonly int _index;
        internal readonly int _length;

        /// <summary>All of <paramref name="array"/> (null: empty).</summary>
        public Memory(T[]? array)
        {
            _array = array;
            _index = 0;
            _length = array == null ? 0 : array.Length;
        }

        /// <summary><paramref name="length"/> elements of <paramref name="array"/> from <paramref name="start"/>.</summary>
        public Memory(T[]? array, int start, int length)
        {
            if (array == null)
            {
                if (start != 0 || length != 0)
                    throw new ArgumentOutOfRangeException(nameof(start));
                this = default;
                return;
            }
            if ((uint)start > (uint)array.Length || (uint)length > (uint)(array.Length - start))
                throw new ArgumentOutOfRangeException(nameof(start));
            _array = array;
            _index = start;
            _length = length;
        }

        /// <summary>No elements.</summary>
        public static Memory<T> Empty => default;

        public int Length => _length;
        public bool IsEmpty => _length == 0;

        /// <summary>The elements, as a span.</summary>
        public Span<T> Span => _array == null ? default : new Span<T>(_array, _index, _length);

        public Memory<T> Slice(int start)
        {
            if ((uint)start > (uint)_length)
                throw new ArgumentOutOfRangeException(nameof(start));
            return new Memory<T>(_array, _index + start, _length - start, true);
        }

        public Memory<T> Slice(int start, int length)
        {
            if ((uint)start > (uint)_length || (uint)length > (uint)(_length - start))
                throw new ArgumentOutOfRangeException(nameof(start));
            return new Memory<T>(_array, _index + start, length, true);
        }

        Memory(T[]? array, int start, int length, bool _)
        {
            _array = array;
            _index = start;
            _length = length;
        }

        public void CopyTo(Memory<T> destination) => Span.CopyTo(destination.Span);
        public bool TryCopyTo(Memory<T> destination)
        {
            if (_length > destination.Length)
                return false;
            CopyTo(destination);
            return true;
        }

        /// <summary>A new array with the elements.</summary>
        public T[] ToArray()
        {
            var copy = new T[_length];
            Span.CopyTo(copy);
            return copy;
        }

        public static implicit operator Memory<T>(T[]? array) => new Memory<T>(array);
        public static implicit operator ReadOnlyMemory<T>(Memory<T> memory) => new ReadOnlyMemory<T>(memory._array, memory._index, memory._length, null);

        public bool Equals(Memory<T> other) => _array == other._array && _index == other._index && _length == other._length;
        public override bool Equals(object? obj) => obj is Memory<T> other && Equals(other);
        public override int GetHashCode() => (_array == null ? 0 : _array.GetHashCode()) ^ _index ^ (_length << 16);
    }

    /// <summary>A read-only piece of an array (or a string, for char), kept on the heap.</summary>
    public readonly struct ReadOnlyMemory<T> : IEquatable<ReadOnlyMemory<T>>
    {
        internal readonly T[]? _array;
        internal readonly string? _text;    // for ReadOnlyMemory<char> over a string
        internal readonly int _index;
        internal readonly int _length;

        public ReadOnlyMemory(T[]? array)
        {
            _array = array;
            _text = null;
            _index = 0;
            _length = array == null ? 0 : array.Length;
        }

        public ReadOnlyMemory(T[]? array, int start, int length)
        {
            this = new Memory<T>(array, start, length);
        }

        internal ReadOnlyMemory(T[]? array, int start, int length, string? text)
        {
            _array = array;
            _text = text;
            _index = start;
            _length = length;
        }

        public static ReadOnlyMemory<T> Empty => default;

        public int Length => _length;
        public bool IsEmpty => _length == 0;

        public ReadOnlySpan<T> Span
        {
            get
            {
                if (_array != null)
                    return new ReadOnlySpan<T>(_array, _index, _length);
                if (_text != null)
                    return System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                        ref Unsafe.As<char, T>(ref Unsafe.Add(ref _text.FirstChar, _index)), _length);
                return default;
            }
        }

        public ReadOnlyMemory<T> Slice(int start)
        {
            if ((uint)start > (uint)_length)
                throw new ArgumentOutOfRangeException(nameof(start));
            return new ReadOnlyMemory<T>(_array, _index + start, _length - start, _text);
        }

        public ReadOnlyMemory<T> Slice(int start, int length)
        {
            if ((uint)start > (uint)_length || (uint)length > (uint)(_length - start))
                throw new ArgumentOutOfRangeException(nameof(start));
            return new ReadOnlyMemory<T>(_array, _index + start, length, _text);
        }

        public void CopyTo(Memory<T> destination) => Span.CopyTo(destination.Span);
        public bool TryCopyTo(Memory<T> destination)
        {
            if (_length > destination.Length)
                return false;
            CopyTo(destination);
            return true;
        }

        public T[] ToArray()
        {
            var copy = new T[_length];
            Span.CopyTo(copy);
            return copy;
        }

        public static implicit operator ReadOnlyMemory<T>(T[]? array) => new ReadOnlyMemory<T>(array);

        public bool Equals(ReadOnlyMemory<T> other) => _array == other._array && (object?)_text == other._text && _index == other._index && _length == other._length;
        public override bool Equals(object? obj) => obj is ReadOnlyMemory<T> other && Equals(other);
        public override int GetHashCode() => (_array == null ? 0 : _array.GetHashCode()) ^ _index ^ (_length << 16);
    }
}
