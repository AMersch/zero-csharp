using System.Runtime;
using System.Runtime.CompilerServices;
using Internal.Runtime;

namespace System
{
    // A string object is [method table][length][characters][0]. Strings made
    // here (by + and Concat) are allocated like any other object and never freed.
    // Object has no Equals or GetHashCode to override here.
#pragma warning disable CS0660, CS0661
    public sealed unsafe class String : IEquatable<string>, IComparable<string>
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

        // By UTF-16 code unit, as string.CompareOrdinal does. .NET's CompareTo
        // uses the current culture's rules; zero mode has no cultures. So
        // "B" goes before "a" here ('B' is 66, 'a' is 97), not after it.
        public int CompareTo(string other) => CompareOrdinal(this, other);

        // Null goes before every string, as in .NET.
        public static int CompareOrdinal(string a, string b)
        {
            if ((object)a == b)
                return 0;
            if (a is null)
                return -1;
            if (b is null)
                return 1;
            int length = a._stringLength < b._stringLength ? a._stringLength : b._stringLength;
            for (int i = 0; i < length; i++)
            {
                int difference = Unsafe.Add(ref a._firstChar, i) - Unsafe.Add(ref b._firstChar, i);
                if (difference != 0)
                    return difference;
            }
            return a._stringLength - b._stringLength;
        }

        public override bool Equals(object obj) => obj is string other && Equals(this, other);

        // FNV-1a over the characters: equal strings hash alike.
        public override int GetHashCode()
        {
            uint hash = 2166136261;
            ref char c = ref _firstChar;
            for (int i = 0; i < _stringLength; i++)
                hash = (hash ^ Unsafe.Add(ref c, i)) * 16777619;
            return (int)hash;
        }

        public override string ToString() => this;

        // string.Format with positional holes only: "{0}", "{1}", ..., and
        // "{{" and "}}" for braces. Alignment and format strings ("{0,5}",
        // "{0:F2}") aren't supported and end the program; lib's Out and Fmt
        // have them. Each argument's ToString fills its hole; null is empty.
        public static string Format(string format, params object[] args) => Format(null, format, args);

        public static string Format(IFormatProvider provider, string format, params object[] args)
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < format._stringLength; i++)
            {
                char c = format[i];
                if (c == '{' && i + 1 < format._stringLength && format[i + 1] == '{')
                {
                    builder.Append('{');
                    i++;
                }
                else if (c == '}' && i + 1 < format._stringLength && format[i + 1] == '}')
                {
                    builder.Append('}');
                    i++;
                }
                else if (c == '{')
                {
                    int index = 0;
                    i++;
                    while (i < format._stringLength && format[i] >= '0' && format[i] <= '9')
                        index = index * 10 + (format[i++] - '0');
                    if (i >= format._stringLength || format[i] != '}' || args == null || index >= args.Length)
                        Environment.FailFast("string.Format: only {0}, {1}, ... holes are supported");
                    object arg = args[index];
                    if (arg != null)
                        builder.Append(arg.ToString());
                }
                else
                {
                    builder.Append(c);
                }
            }
            return builder.ToString();
        }

        public static bool operator ==(string a, string b) => Equals(a, b);

        public static bool operator !=(string a, string b) => !Equals(a, b);

        // A new string holding a copy of these characters; text[a..b] calls it.
        // text.AsSpan(start, length) gives the same characters without a copy.
        public string Substring(int startIndex) => Substring(startIndex, _stringLength - startIndex);

        public string Substring(int startIndex, int length)
        {
            if ((uint)startIndex > (uint)_stringLength || (uint)length > (uint)(_stringLength - startIndex))
                Environment.FailFast("string.Substring: out of range");
            if (length == _stringLength)
                return this;
            string result = Allocate(length);
            for (int i = 0; i < length; i++)
                Unsafe.Add(ref result._firstChar, i) = Unsafe.Add(ref _firstChar, startIndex + i);
            return result;
        }

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
