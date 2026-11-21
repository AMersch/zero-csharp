// The primitive types. The compiler knows their sizes; they need no fields.
// Each gets Equals, GetHashCode and ToString, which records, anonymous types,
// EqualityComparer<T> and `"n=" + 5` call.

namespace System
{
    public struct Void { }

    public struct Boolean : IEquatable<bool>
    {
        public bool Equals(bool other) => (bool)this == other;
        public override bool Equals(object obj) => obj is bool other && (bool)this == other;
        public override int GetHashCode() => (bool)this ? 1 : 0;
        public override string ToString() => (bool)this ? "True" : "False";
    }

    public unsafe struct Char : IEquatable<char>
    {
        public const char MaxValue = (char)0xFFFF;
        public const char MinValue = (char)0;

        public bool Equals(char other) => (char)this == other;
        public override bool Equals(object obj) => obj is char other && (char)this == other;
        public override int GetHashCode() => (char)this;
        public override string ToString()
        {
            char c = (char)this;
            return Number.FromChars(&c, 1);
        }
    }

    public struct SByte : IEquatable<sbyte>
    {
        public const sbyte MaxValue = 0x7F;
        public const sbyte MinValue = -0x80;

        public bool Equals(sbyte other) => (sbyte)this == other;
        public override bool Equals(object obj) => obj is sbyte other && (sbyte)this == other;
        public override int GetHashCode() => unchecked((int)this);
        public override string ToString() => Number.FormatInt64((long)this);
    }

    public struct Byte : IEquatable<byte>
    {
        public const byte MaxValue = 0xFF;
        public const byte MinValue = 0;

        public bool Equals(byte other) => (byte)this == other;
        public override bool Equals(object obj) => obj is byte other && (byte)this == other;
        public override int GetHashCode() => unchecked((int)this);
        public override string ToString() => Number.FormatUInt64((ulong)this);
    }

    public struct Int16 : IEquatable<short>
    {
        public const short MaxValue = 0x7FFF;
        public const short MinValue = -0x8000;

        public bool Equals(short other) => (short)this == other;
        public override bool Equals(object obj) => obj is short other && (short)this == other;
        public override int GetHashCode() => unchecked((int)this);
        public override string ToString() => Number.FormatInt64((long)this);
    }

    public struct UInt16 : IEquatable<ushort>
    {
        public const ushort MaxValue = 0xFFFF;
        public const ushort MinValue = 0;

        public bool Equals(ushort other) => (ushort)this == other;
        public override bool Equals(object obj) => obj is ushort other && (ushort)this == other;
        public override int GetHashCode() => unchecked((int)this);
        public override string ToString() => Number.FormatUInt64((ulong)this);
    }

    public struct Int32 : IEquatable<int>
    {
        public const int MaxValue = 0x7FFFFFFF;
        public const int MinValue = -0x80000000;

        public bool Equals(int other) => (int)this == other;
        public override bool Equals(object obj) => obj is int other && (int)this == other;
        public override int GetHashCode() => unchecked((int)this);
        public override string ToString() => Number.FormatInt64((long)this);
    }

    public struct UInt32 : IEquatable<uint>
    {
        public const uint MaxValue = 0xFFFFFFFF;
        public const uint MinValue = 0;

        public bool Equals(uint other) => (uint)this == other;
        public override bool Equals(object obj) => obj is uint other && (uint)this == other;
        public override int GetHashCode() => unchecked((int)this);
        public override string ToString() => Number.FormatUInt64((ulong)this);
    }

    public struct Int64 : IEquatable<long>
    {
        public const long MaxValue = 0x7FFFFFFFFFFFFFFF;
        public const long MinValue = -0x8000000000000000;

        public bool Equals(long other) => (long)this == other;
        public override bool Equals(object obj) => obj is long other && (long)this == other;
        public override int GetHashCode() => unchecked((int)this ^ (int)((long)this >> 32));
        public override string ToString() => Number.FormatInt64((long)this);
    }

    public struct UInt64 : IEquatable<ulong>
    {
        public const ulong MaxValue = 0xFFFFFFFFFFFFFFFF;
        public const ulong MinValue = 0;

        public bool Equals(ulong other) => (ulong)this == other;
        public override bool Equals(object obj) => obj is ulong other && (ulong)this == other;
        public override int GetHashCode() => unchecked((int)this ^ (int)((ulong)this >> 32));
        public override string ToString() => Number.FormatUInt64((ulong)this);
    }

    public unsafe struct IntPtr
    {
        public static readonly nint Zero;

        public static int Size
        {
            get => sizeof(nint);
        }

        public override bool Equals(object obj) => obj is nint other && (nint)this == other;
        public override int GetHashCode() => unchecked((int)(long)(nint)this ^ (int)((long)(nint)this >> 32));
        public override string ToString() => Number.FormatInt64((nint)this);
    }

    public unsafe struct UIntPtr
    {
        public static readonly nuint Zero;

        public static int Size
        {
            get => sizeof(nuint);
        }

        public override bool Equals(object obj) => obj is nuint other && (nuint)this == other;
        public override int GetHashCode() => unchecked((int)(ulong)(nuint)this ^ (int)((ulong)(nuint)this >> 32));
        public override string ToString() => Number.FormatUInt64((nuint)this);
    }

    // Equals here is bit equality, so NaN equals NaN, as .NET's Equals has
    // it (unlike ==); 0.0 and -0.0 are equal, as in .NET.
    public unsafe struct Single : IEquatable<float>
    {
        public bool Equals(float other) => (float)this == other || ((float)this != (float)this && other != other);
        public override bool Equals(object obj) => obj is float other && Equals(other);
        public override int GetHashCode()
        {
            float value = (float)this == 0 ? 0 : (float)this;
            return *(int*)&value;
        }
        public override string ToString() => Number.FormatDouble((float)this, 7);
    }

    public unsafe struct Double : IEquatable<double>
    {
        public bool Equals(double other) => (double)this == other || ((double)this != (double)this && other != other);
        public override bool Equals(object obj) => obj is double other && Equals(other);
        public override int GetHashCode()
        {
            double value = (double)this == 0 ? 0 : (double)this;
            long bits = *(long*)&value;
            return unchecked((int)bits ^ (int)(bits >> 32));
        }
        public override string ToString() => Number.FormatDouble((double)this);
    }
}
