using System.Runtime.CompilerServices;

// Arm64 instructions, under the names .NET gives them.
//
// IsSupported is decided at compile time: when compiling for arm64 the compiler
// replaces it with true or false according to the -m flags passed to bflat, and
// replaces each method call with its instruction. For any other target nothing
// is replaced, so IsSupported has to say false itself. The methods' bodies
// never run: check IsSupported before calling them.
namespace System.Runtime.Intrinsics.Arm
{
    [Intrinsic]
    public abstract unsafe class AdvSimd
    {
        public static bool IsSupported => false;

        public static Vector128<float> Add(Vector128<float> left, Vector128<float> right) => Add(left, right);
        public static Vector128<int> Add(Vector128<int> left, Vector128<int> right) => Add(left, right);
        public static Vector128<float> Subtract(Vector128<float> left, Vector128<float> right) => Subtract(left, right);
        public static Vector128<float> Multiply(Vector128<float> left, Vector128<float> right) => Multiply(left, right);
        public static Vector128<float> Max(Vector128<float> left, Vector128<float> right) => Max(left, right);
        public static Vector128<float> Min(Vector128<float> left, Vector128<float> right) => Min(left, right);
        public static Vector128<float> FusedMultiplyAdd(Vector128<float> addend, Vector128<float> left, Vector128<float> right) => FusedMultiplyAdd(addend, left, right);
        public static Vector128<float> RoundToNearest(Vector128<float> value) => RoundToNearest(value);
        public static Vector128<int> ConvertToInt32RoundToEven(Vector128<float> value) => ConvertToInt32RoundToEven(value);
        public static Vector128<uint> ShiftLeftLogical(Vector128<uint> value, byte count) => ShiftLeftLogical(value, count);
        public static Vector128<float> DuplicateToVector128(float value) => DuplicateToVector128(value);
        public static Vector128<int> DuplicateToVector128(int value) => DuplicateToVector128(value);
        public static Vector128<float> LoadVector128(float* address) => LoadVector128(address);
        public static Vector128<int> LoadVector128(int* address) => LoadVector128(address);
        public static Vector128<sbyte> LoadVector128(sbyte* address) => LoadVector128(address);
        public static void Store(float* address, Vector128<float> source) => Store(address, source);
        public static void Store(int* address, Vector128<int> source) => Store(address, source);
        public static Vector64<byte> PopCount(Vector64<byte> value) => PopCount(value);

        [Intrinsic]
        public abstract class Arm64
        {
            public static bool IsSupported => false;

            public static Vector128<float> Divide(Vector128<float> left, Vector128<float> right) => Divide(left, right);
            public static Vector128<float> AddPairwise(Vector128<float> left, Vector128<float> right) => AddPairwise(left, right);
            public static Vector64<int> AddAcross(Vector128<int> value) => AddAcross(value);
            public static Vector64<float> MaxAcross(Vector128<float> value) => MaxAcross(value);
            public static Vector64<ushort> AddAcross(Vector64<byte> value) => AddAcross(value);
        }
    }

    // SDOT/UDOT, ARMv8.2 and later (-m dotprod).
    [Intrinsic]
    public abstract class Dp
    {
        public static bool IsSupported => false;

        public static Vector128<int> DotProduct(Vector128<int> addend, Vector128<sbyte> left, Vector128<sbyte> right) => DotProduct(addend, left, right);
    }

    // clz and rbit, in every arm64 processor; BitOperations uses them.
    [Intrinsic]
    public abstract class ArmBase
    {
        public static bool IsSupported => false;

        public static int LeadingZeroCount(uint value) => LeadingZeroCount(value);
        public static uint ReverseElementBits(uint value) => ReverseElementBits(value);

        [Intrinsic]
        public abstract class Arm64
        {
            public static bool IsSupported => false;

            public static int LeadingZeroCount(ulong value) => LeadingZeroCount(value);
            public static ulong ReverseElementBits(ulong value) => ReverseElementBits(value);
        }
    }
}
