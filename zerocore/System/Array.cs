using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System
{
    // T[] derives from this. The length is the first field after the method table.
    public abstract class Array
    {
        private int _length;

        public int Length => _length;

        /// <summary>An empty T[]: the same one every time, so asking for it allocates nothing.</summary>
        public static T[] Empty<T>() => EmptyArray<T>.Value;
    }

    internal static class EmptyArray<T>
    {
        internal static readonly T[] Value = new T[0];
    }

    // The compiler makes every T[] derive from this, with its interfaces: so an
    // array is an IEnumerable<T>, as in .NET (F#'s seq functions take arrays
    // this way). A program that never enumerates an array through the
    // interface pays for no enumerator code.
    public class Array<T> : Array, IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator() => new ArrayEnumerator<T>(Unsafe.As<T[]>(this));

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal sealed class ArrayEnumerator<T> : IEnumerator<T>
    {
        private readonly T[] _array;
        private int _index = -1;

        public ArrayEnumerator(T[] array) => _array = array;

        public T Current => _array[_index];

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_index < _array.Length)
                _index++;
            return _index < _array.Length;
        }

        public void Reset() => _index = -1;

        public void Dispose() { }
    }
}
