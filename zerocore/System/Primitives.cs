// The primitive types. The compiler knows their sizes; they need no fields.

namespace System
{
    public struct Void { }

    public struct Boolean { }

    public struct Char
    {
        public const char MaxValue = (char)0xFFFF;
        public const char MinValue = (char)0;
    }

    public struct SByte
    {
        public const sbyte MaxValue = 0x7F;
        public const sbyte MinValue = -0x80;
    }

    public struct Byte
    {
        public const byte MaxValue = 0xFF;
        public const byte MinValue = 0;
    }

    public struct Int16
    {
        public const short MaxValue = 0x7FFF;
        public const short MinValue = -0x8000;
    }

    public struct UInt16
    {
        public const ushort MaxValue = 0xFFFF;
        public const ushort MinValue = 0;
    }

    public struct Int32
    {
        public const int MaxValue = 0x7FFFFFFF;
        public const int MinValue = -0x80000000;
    }

    public struct UInt32
    {
        public const uint MaxValue = 0xFFFFFFFF;
        public const uint MinValue = 0;
    }

    public struct Int64
    {
        public const long MaxValue = 0x7FFFFFFFFFFFFFFF;
        public const long MinValue = -0x8000000000000000;
    }

    public struct UInt64
    {
        public const ulong MaxValue = 0xFFFFFFFFFFFFFFFF;
        public const ulong MinValue = 0;
    }

    public unsafe struct IntPtr
    {
        public static readonly nint Zero;

        public static int Size
        {
            get => sizeof(nint);
        }
    }

    public unsafe struct UIntPtr
    {
        public static readonly nuint Zero;

        public static int Size
        {
            get => sizeof(nuint);
        }
    }

    public struct Single { }

    public struct Double { }
}
