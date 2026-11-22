using System.Runtime.CompilerServices;

// System.Collections.BitArray: a fixed number of bits, packed 32 to an int,
// with .NET's members and behaviour. Count is the length, as in .NET; the bits
// beyond it in the last int are kept clear, so whole-int operations stay right.
// It implements IEnumerable, not ICollection and ICloneable, which zerocore
// doesn't have; foreach over it boxes each bool.
namespace System.Collections
{
    public sealed class BitArray : IEnumerable
    {
        private int[] m_array;
        private int m_length;

        public BitArray(int length) : this(length, false) { }

        public BitArray(int length, bool defaultValue)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            m_array = new int[Int32Count(length)];
            m_length = length;
            if (defaultValue)
                SetAll(true);
        }

        // Byte i holds bits 8i to 8i + 7, the lowest bit first.
        public BitArray(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length > int.MaxValue / 8)
                throw new ArgumentException("bytes is too long");
            m_array = new int[(bytes.Length + 3) / 4];
            m_length = bytes.Length * 8;
            for (int i = 0; i < bytes.Length; i++)
                m_array[i / 4] |= bytes[i] << (i % 4 * 8);
        }

        public BitArray(bool[] values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            m_array = new int[Int32Count(values.Length)];
            m_length = values.Length;
            for (int i = 0; i < values.Length; i++)
                if (values[i])
                    m_array[i >> 5] |= 1 << i;
        }

        // Int i holds bits 32i to 32i + 31, the lowest bit first.
        public BitArray(int[] values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (values.Length > int.MaxValue / 32)
                throw new ArgumentException("values is too long");
            m_array = new int[values.Length];
            m_length = values.Length * 32;
            for (int i = 0; i < values.Length; i++)
                m_array[i] = values[i];
        }

        public BitArray(BitArray bits)
        {
            if (bits == null)
                throw new ArgumentNullException(nameof(bits));
            int count = Int32Count(bits.m_length);
            m_array = new int[count];
            m_length = bits.m_length;
            for (int i = 0; i < count; i++)
                m_array[i] = bits.m_array[i];
        }

        public bool this[int index]
        {
            get => Get(index);
            set => Set(index, value);
        }

        public bool Get(int index)
        {
            if ((uint)index >= (uint)m_length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return (m_array[index >> 5] & (1 << index)) != 0;
        }

        public void Set(int index, bool value)
        {
            if ((uint)index >= (uint)m_length)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (value)
                m_array[index >> 5] |= 1 << index;
            else
                m_array[index >> 5] &= ~(1 << index);
        }

        public void SetAll(bool value)
        {
            int fill = value ? -1 : 0;
            int count = Int32Count(m_length);
            for (int i = 0; i < count; i++)
                m_array[i] = fill;
            ClearBitsPastLength();
        }

        // And, Or and Xor change this array and return it. Both arrays must
        // have the same length.
        public BitArray And(BitArray value)
        {
            int count = CheckSameLength(value);
            for (int i = 0; i < count; i++)
                m_array[i] &= value.m_array[i];
            return this;
        }

        public BitArray Or(BitArray value)
        {
            int count = CheckSameLength(value);
            for (int i = 0; i < count; i++)
                m_array[i] |= value.m_array[i];
            return this;
        }

        public BitArray Xor(BitArray value)
        {
            int count = CheckSameLength(value);
            for (int i = 0; i < count; i++)
                m_array[i] ^= value.m_array[i];
            return this;
        }

        public BitArray Not()
        {
            int count = Int32Count(m_length);
            for (int i = 0; i < count; i++)
                m_array[i] = ~m_array[i];
            ClearBitsPastLength();
            return this;
        }

        // Bit i takes the value of bit i + count; the top count bits become 0.
        public BitArray RightShift(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0)
                return this;
            int ints = Int32Count(m_length);
            if (count >= m_length)
            {
                for (int i = 0; i < ints; i++)
                    m_array[i] = 0;
                return this;
            }
            int skip = count >> 5;
            int shift = count & 31;
            for (int i = 0; i < ints; i++)
            {
                uint low = i + skip < ints ? (uint)m_array[i + skip] : 0;
                uint high = i + skip + 1 < ints ? (uint)m_array[i + skip + 1] : 0;
                m_array[i] = shift == 0 ? (int)low : (int)((low >> shift) | (high << (32 - shift)));
            }
            return this;
        }

        // Bit i takes the value of bit i - count; the bottom count bits become 0.
        public BitArray LeftShift(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0)
                return this;
            int ints = Int32Count(m_length);
            if (count >= m_length)
            {
                for (int i = 0; i < ints; i++)
                    m_array[i] = 0;
                return this;
            }
            int skip = count >> 5;
            int shift = count & 31;
            for (int i = ints - 1; i >= 0; i--)
            {
                uint high = i - skip >= 0 ? (uint)m_array[i - skip] : 0;
                uint low = i - skip - 1 >= 0 ? (uint)m_array[i - skip - 1] : 0;
                m_array[i] = shift == 0 ? (int)high : (int)((high << shift) | (low >> (32 - shift)));
            }
            ClearBitsPastLength();
            return this;
        }

        public bool HasAllSet()
        {
            int full = m_length >> 5;
            for (int i = 0; i < full; i++)
                if (m_array[i] != -1)
                    return false;
            int extra = m_length & 31;
            return extra == 0 || m_array[full] == (1 << extra) - 1;
        }

        public bool HasAnySet()
        {
            int count = Int32Count(m_length);
            for (int i = 0; i < count; i++)
                if (m_array[i] != 0)
                    return true;
            return false;
        }

        // Setting it keeps the bits below the new length; new bits are 0.
        public int Length
        {
            get => m_length;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                int count = Int32Count(value);
                if (count != m_array.Length)
                {
                    int[] array = new int[count];
                    int keep = count < m_array.Length ? count : m_array.Length;
                    for (int i = 0; i < keep; i++)
                        array[i] = m_array[i];
                    m_array = array;
                }
                m_length = value;
                ClearBitsPastLength();
            }
        }

        public int Count => m_length;

        public bool IsReadOnly => false;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public object Clone() => new BitArray(this);

        // To an int[] (32 bits per element), byte[] (8) or bool[] (1),
        // starting at element index. The type is compared exactly: zerocore's
        // casts don't go between array types.
        public void CopyTo(Array array, int index)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            Type type = array.GetType();
            if (type == typeof(int[]))
            {
                int[] ints = Unsafe.As<int[]>(array);
                int count = Int32Count(m_length);
                if (ints.Length - index < count)
                    throw new ArgumentException("array is too short");
                for (int i = 0; i < count; i++)
                    ints[index + i] = m_array[i];
            }
            else if (type == typeof(byte[]))
            {
                byte[] bytes = Unsafe.As<byte[]>(array);
                int count = (m_length + 7) / 8;
                if (bytes.Length - index < count)
                    throw new ArgumentException("array is too short");
                for (int i = 0; i < count; i++)
                    bytes[index + i] = (byte)(m_array[i / 4] >> (i % 4 * 8));
            }
            else if (type == typeof(bool[]))
            {
                bool[] bools = Unsafe.As<bool[]>(array);
                if (bools.Length - index < m_length)
                    throw new ArgumentException("array is too short");
                for (int i = 0; i < m_length; i++)
                    bools[index + i] = (m_array[i >> 5] & (1 << i)) != 0;
            }
            else
                throw new ArgumentException("array must be int[], byte[] or bool[]");
        }

        public IEnumerator GetEnumerator() => new Enumerator(this);

        private int CheckSameLength(BitArray value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (value.m_length != m_length)
                throw new ArgumentException("Array lengths must be the same.");
            return Int32Count(m_length);
        }

        private void ClearBitsPastLength()
        {
            int extra = m_length & 31;
            if (extra != 0)
                m_array[m_length >> 5] &= (1 << extra) - 1;
        }

        private static int Int32Count(int bits) => (int)((uint)(bits + 31) >> 5);

        private sealed class Enumerator : IEnumerator
        {
            private readonly BitArray _bits;
            private int _index = -1;

            internal Enumerator(BitArray bits) => _bits = bits;

            public object Current => _bits.Get(_index);

            public bool MoveNext() => ++_index < _bits.m_length;

            public void Reset() => _index = -1;
        }
    }
}
