using System.Runtime.CompilerServices;

// x64 instructions, under the names .NET gives them.
//
// IsSupported is decided at compile time: when compiling for x64 the compiler
// replaces it with true or false according to the -m flags passed to bflat
// (-m x86-64-v3 for these), and replaces each method call with its instruction.
// For any other target nothing is replaced, so IsSupported has to say false
// itself. The methods' bodies never run: check IsSupported before calling them.
namespace System.Runtime.Intrinsics.X86
{
    [Intrinsic]
    public abstract unsafe class Avx
    {
        public static bool IsSupported => false;

        public static Vector256<float> Add(Vector256<float> left, Vector256<float> right) => Add(left, right);
        public static Vector256<float> Subtract(Vector256<float> left, Vector256<float> right) => Subtract(left, right);
        public static Vector256<float> Multiply(Vector256<float> left, Vector256<float> right) => Multiply(left, right);
        public static Vector256<float> Divide(Vector256<float> left, Vector256<float> right) => Divide(left, right);
        public static Vector256<float> Max(Vector256<float> left, Vector256<float> right) => Max(left, right);
        public static Vector256<float> Min(Vector256<float> left, Vector256<float> right) => Min(left, right);
        public static Vector256<float> RoundToNearestInteger(Vector256<float> value) => RoundToNearestInteger(value);
        public static Vector256<int> ConvertToVector256Int32(Vector256<float> value) => ConvertToVector256Int32(value);
        public static Vector256<float> LoadVector256(float* address) => LoadVector256(address);
        public static Vector256<int> LoadVector256(int* address) => LoadVector256(address);
        public static Vector256<sbyte> LoadVector256(sbyte* address) => LoadVector256(address);
        public static void Store(float* address, Vector256<float> source) => Store(address, source);
        public static void Store(int* address, Vector256<int> source) => Store(address, source);
    }

    [Intrinsic]
    public abstract class Avx2
    {
        public static bool IsSupported => false;

        public static Vector256<int> Add(Vector256<int> left, Vector256<int> right) => Add(left, right);
        public static Vector256<byte> Abs(Vector256<sbyte> value) => Abs(value);
        public static Vector256<sbyte> Sign(Vector256<sbyte> left, Vector256<sbyte> right) => Sign(left, right);
        public static Vector256<short> MultiplyAddAdjacent(Vector256<byte> left, Vector256<sbyte> right) => MultiplyAddAdjacent(left, right);
        public static Vector256<int> MultiplyAddAdjacent(Vector256<short> left, Vector256<short> right) => MultiplyAddAdjacent(left, right);
        public static Vector256<int> ShiftLeftLogical(Vector256<int> value, byte count) => ShiftLeftLogical(value, count);
    }

    [Intrinsic]
    public abstract class Fma
    {
        public static bool IsSupported => false;

        public static Vector256<float> MultiplyAdd(Vector256<float> a, Vector256<float> b, Vector256<float> c) => MultiplyAdd(a, b, c);
    }

    // The bit instructions BitOperations uses. X86Base (bsf, bsr) is in every
    // x64 processor; Lzcnt, Popcnt and Bmi1 (tzcnt) come with -m x86-64-v3.
    // The bit scans are internal in .NET too.
    [Intrinsic]
    public abstract class X86Base
    {
        public static bool IsSupported => false;

        internal static uint BitScanForward(uint value) => BitScanForward(value);
        internal static uint BitScanReverse(uint value) => BitScanReverse(value);

        [Intrinsic]
        public abstract class X64
        {
            public static bool IsSupported => false;

            internal static ulong BitScanForward(ulong value) => BitScanForward(value);
            internal static ulong BitScanReverse(ulong value) => BitScanReverse(value);
        }
    }

    [Intrinsic]
    public abstract class Lzcnt
    {
        public static bool IsSupported => false;

        public static uint LeadingZeroCount(uint value) => LeadingZeroCount(value);

        [Intrinsic]
        public abstract class X64
        {
            public static bool IsSupported => false;

            public static ulong LeadingZeroCount(ulong value) => LeadingZeroCount(value);
        }
    }

    [Intrinsic]
    public abstract class Popcnt
    {
        public static bool IsSupported => false;

        public static uint PopCount(uint value) => PopCount(value);

        [Intrinsic]
        public abstract class X64
        {
            public static bool IsSupported => false;

            public static ulong PopCount(ulong value) => PopCount(value);
        }
    }

    [Intrinsic]
    public abstract class Bmi1
    {
        public static bool IsSupported => false;

        public static uint TrailingZeroCount(uint value) => TrailingZeroCount(value);

        [Intrinsic]
        public abstract class X64
        {
            public static bool IsSupported => false;

            public static ulong TrailingZeroCount(ulong value) => TrailingZeroCount(value);
        }
    }
}
