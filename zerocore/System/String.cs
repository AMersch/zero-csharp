using System.Runtime;
using System.Runtime.CompilerServices;
using Internal.Runtime;

namespace System
{
    // A string object is [method table][length][characters][0]. Strings made
    // here (by + and Concat) are allocated like any other object and never freed.
    // Object has no Equals or GetHashCode to override here.
#pragma warning disable CS0660, CS0661
    public sealed unsafe class String
#pragma warning restore CS0660, CS0661
    {
        // The compiler finds these two fields by name, to lay out string literals.
        private readonly int _stringLength;
        private char _firstChar;

        public int Length => _stringLength;

        [IndexerName("Chars")]
        public char this[int index]
        {
            [Intrinsic]
            get
            {
                if ((uint)index >= (uint)_stringLength)
                    Environment.FailFast("String: index out of range");
                return Unsafe.Add(ref _firstChar, index);
            }
        }

        // For `fixed (char* p = text)`.
        public ref readonly char GetPinnableReference() => ref _firstChar;

        public static bool IsNullOrEmpty(string value) => value == null || value._stringLength == 0;

        public static bool Equals(string a, string b)
        {
            if ((object)a == (object)b)
                return true;
            if ((object)a == null || (object)b == null || a._stringLength != b._stringLength)
                return false;
            ref char x = ref a._firstChar;
            ref char y = ref b._firstChar;
            for (int i = 0; i < a._stringLength; i++)
            {
                if (Unsafe.Add(ref x, i) != Unsafe.Add(ref y, i))
                    return false;
            }
            return true;
        }

        public bool Equals(string other) => Equals(this, other);

        public static bool operator ==(string a, string b) => Equals(a, b);

        public static bool operator !=(string a, string b) => !Equals(a, b);

        // Null counts as the empty string, as in .NET.
        public static string Concat(string a, string b)
        {
            string result = Allocate(LengthOf(a) + LengthOf(b));
            int at = result.Put(a, 0);
            result.Put(b, at);
            return result;
        }

        public static string Concat(string a, string b, string c)
        {
            string result = Allocate(LengthOf(a) + LengthOf(b) + LengthOf(c));
            int at = result.Put(a, 0);
            at = result.Put(b, at);
            result.Put(c, at);
            return result;
        }

        public static string Concat(string a, string b, string c, string d)
        {
            string result = Allocate(LengthOf(a) + LengthOf(b) + LengthOf(c) + LengthOf(d));
            int at = result.Put(a, 0);
            at = result.Put(b, at);
            at = result.Put(c, at);
            result.Put(d, at);
            return result;
        }

        public static string Concat(params string[] values)
        {
            int length = 0;
            for (int i = 0; i < values.Length; i++)
                length += LengthOf(values[i]);
            string result = Allocate(length);
            int at = 0;
            for (int i = 0; i < values.Length; i++)
                at = result.Put(values[i], at);
            return result;
        }

        private static int LengthOf(string value) => value == null ? 0 : value._stringLength;

        // Copies `value` in at `index` and returns the index after it.
        private int Put(string value, int index)
        {
            int length = LengthOf(value);
            for (int i = 0; i < length; i++)
                Unsafe.Add(ref _firstChar, index + i) = Unsafe.Add(ref value._firstChar, i);
            return index + length;
        }

        // A string of `length` characters, all zero, for the caller to fill in.
        internal static string Allocate(int length)
            => Unsafe.As<string>(RuntimeExports.RhpNewArray(MethodTable.Of<string>(), length));

        internal ref char FirstChar => ref _firstChar;
    }
}

namespace System.Runtime.CompilerServices
{
    public sealed class IndexerNameAttribute : Attribute
    {
        public IndexerNameAttribute(string indexerName) { }
    }
}
