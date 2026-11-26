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
        public static Vector256<float> And(Vector256<float> left, Vector256<float> right) => And(left, right);
        public static Vector256<float> Or(Vector256<float> left, Vector256<float> right) => Or(left, right);
        public static Vector256<float> AndNot(Vector256<float> left, Vector256<float> right) => AndNot(left, right);
        public static Vector256<float> CompareLessThan(Vector256<float> left, Vector256<float> right) => CompareLessThan(left, right);
        public static Vector256<float> BlendVariable(Vector256<float> left, Vector256<float> right, Vector256<float> mask) => BlendVariable(left, right, mask);

        public static Vector256<float> ConvertToVector256Single(Vector256<int> value) => ConvertToVector256Single(value);
        public static Vector256<int> ConvertToVector256Int32WithTruncation(Vector256<float> value) => ConvertToVector256Int32WithTruncation(value);
        public static Vector256<float> Permute2x128(Vector256<float> left, Vector256<float> right, byte control) => Permute2x128(left, right, control);
        public static Vector256<byte> LoadVector256(byte* address) => LoadVector256(address);
        public static Vector256<short> LoadVector256(short* address) => LoadVector256(address);
        public static Vector256<ushort> LoadVector256(ushort* address) => LoadVector256(address);
        public static Vector256<uint> LoadVector256(uint* address) => LoadVector256(address);
        public static void Store(byte* address, Vector256<byte> source) => Store(address, source);
        public static void Store(short* address, Vector256<short> source) => Store(address, source);
        public static void Store(ushort* address, Vector256<ushort> source) => Store(address, source);
        public static void Store(uint* address, Vector256<uint> source) => Store(address, source);
    }

    [Intrinsic]
    public abstract unsafe class Avx2
    {
        public static bool IsSupported => false;

        public static Vector256<int> Add(Vector256<int> left, Vector256<int> right) => Add(left, right);
        public static Vector256<byte> Abs(Vector256<sbyte> value) => Abs(value);
        public static Vector256<sbyte> Sign(Vector256<sbyte> left, Vector256<sbyte> right) => Sign(left, right);
        public static Vector256<short> MultiplyAddAdjacent(Vector256<byte> left, Vector256<sbyte> right) => MultiplyAddAdjacent(left, right);
        public static Vector256<int> MultiplyAddAdjacent(Vector256<short> left, Vector256<short> right) => MultiplyAddAdjacent(left, right);
        public static Vector256<int> ShiftLeftLogical(Vector256<int> value, byte count) => ShiftLeftLogical(value, count);

        public static Vector256<short> Add(Vector256<short> left, Vector256<short> right) => Add(left, right);
        public static Vector256<ushort> Add(Vector256<ushort> left, Vector256<ushort> right) => Add(left, right);
        public static Vector256<short> Subtract(Vector256<short> left, Vector256<short> right) => Subtract(left, right);
        public static Vector256<ushort> Subtract(Vector256<ushort> left, Vector256<ushort> right) => Subtract(left, right);
        public static Vector256<int> Subtract(Vector256<int> left, Vector256<int> right) => Subtract(left, right);
        public static Vector256<short> MultiplyLow(Vector256<short> left, Vector256<short> right) => MultiplyLow(left, right);
        public static Vector256<ushort> MultiplyLow(Vector256<ushort> left, Vector256<ushort> right) => MultiplyLow(left, right);
        public static Vector256<int> MultiplyLow(Vector256<int> left, Vector256<int> right) => MultiplyLow(left, right);
        public static Vector256<int> CompareGreaterThan(Vector256<int> left, Vector256<int> right) => CompareGreaterThan(left, right);
        public static Vector256<int> And(Vector256<int> left, Vector256<int> right) => And(left, right);
        public static Vector256<ushort> And(Vector256<ushort> left, Vector256<ushort> right) => And(left, right);
        public static Vector256<int> Xor(Vector256<int> left, Vector256<int> right) => Xor(left, right);
        public static Vector256<ushort> Xor(Vector256<ushort> left, Vector256<ushort> right) => Xor(left, right);

        public static Vector256<short> ConvertToVector256Int16(byte* address) => ConvertToVector256Int16(address);
        public static Vector256<int> ConvertToVector256Int32(byte* address) => ConvertToVector256Int32(address);
        public static Vector256<int> ConvertToVector256Int32(ushort* address) => ConvertToVector256Int32(address);

        public static Vector256<byte> PackUnsignedSaturate(Vector256<short> left, Vector256<short> right) => PackUnsignedSaturate(left, right);
        public static Vector256<ushort> PackUnsignedSaturate(Vector256<int> left, Vector256<int> right) => PackUnsignedSaturate(left, right);
        public static Vector256<short> UnpackLow(Vector256<short> left, Vector256<short> right) => UnpackLow(left, right);
        public static Vector256<short> UnpackHigh(Vector256<short> left, Vector256<short> right) => UnpackHigh(left, right);
        public static Vector256<ushort> BroadcastScalarToVector256(ushort* source) => BroadcastScalarToVector256(source);
        public static Vector256<int> PermuteVar8x32(Vector256<int> left, Vector256<int> control) => PermuteVar8x32(left, control);
        public static Vector256<long> Permute4x64(Vector256<long> value, byte control) => Permute4x64(value, control);
        public static Vector256<int> Permute2x128(Vector256<int> left, Vector256<int> right, byte control) => Permute2x128(left, right, control);
    }

    [Intrinsic]
    public abstract class Fma
    {
        public static bool IsSupported => false;

        public static Vector256<float> MultiplyAdd(Vector256<float> a, Vector256<float> b, Vector256<float> c) => MultiplyAdd(a, b, c);
    }

    // SSE and SSE2: in every x64 processor, so IsSupported is true on x64
    // without -m. TensorPrimitives uses them where AVX isn't enabled.
    [Intrinsic]
    public abstract unsafe class Sse
    {
        public static bool IsSupported => false;

        public static Vector128<float> Add(Vector128<float> left, Vector128<float> right) => Add(left, right);
        public static Vector128<float> Subtract(Vector128<float> left, Vector128<float> right) => Subtract(left, right);
        public static Vector128<float> Multiply(Vector128<float> left, Vector128<float> right) => Multiply(left, right);
        public static Vector128<float> Divide(Vector128<float> left, Vector128<float> right) => Divide(left, right);
        public static Vector128<float> Max(Vector128<float> left, Vector128<float> right) => Max(left, right);
        public static Vector128<float> Min(Vector128<float> left, Vector128<float> right) => Min(left, right);
        public static Vector128<float> And(Vector128<float> left, Vector128<float> right) => And(left, right);
        public static Vector128<float> Or(Vector128<float> left, Vector128<float> right) => Or(left, right);
        public static Vector128<float> AndNot(Vector128<float> left, Vector128<float> right) => AndNot(left, right);
        public static Vector128<float> CompareLessThan(Vector128<float> left, Vector128<float> right) => CompareLessThan(left, right);
        public static Vector128<float> LoadVector128(float* address) => LoadVector128(address);
        public static void Store(float* address, Vector128<float> source) => Store(address, source);
    }

    [Intrinsic]
    public abstract unsafe class Sse2
    {
        public static bool IsSupported => false;

        public static Vector128<int> Add(Vector128<int> left, Vector128<int> right) => Add(left, right);
        public static Vector128<int> ShiftLeftLogical(Vector128<int> value, byte count) => ShiftLeftLogical(value, count);
        public static Vector128<int> ConvertToVector128Int32(Vector128<float> value) => ConvertToVector128Int32(value);
        public static Vector128<float> ConvertToVector128Single(Vector128<int> value) => ConvertToVector128Single(value);
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
