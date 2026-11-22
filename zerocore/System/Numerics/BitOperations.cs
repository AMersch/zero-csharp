using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

// System.Numerics.BitOperations, with .NET's members and results.
//
// Each method uses the processor's instruction where the target has one: clz,
// rbit and cnt on arm64; bsr and bsf on x64, and lzcnt, tzcnt and popcnt with
// -m x86-64-v3. Elsewhere (WebAssembly, DOS) the portable code below runs.
// [Intrinsic] lets the compiler expand a call itself where it knows how.
namespace System.Numerics
{
    public static unsafe class BitOperations
    {
        [Intrinsic]
        public static bool IsPow2(int value) => (value & (value - 1)) == 0 && value > 0;

        [Intrinsic]
        public static bool IsPow2(uint value) => (value & (value - 1)) == 0 && value != 0;

        [Intrinsic]
        public static bool IsPow2(long value) => (value & (value - 1)) == 0 && value > 0;

        [Intrinsic]
        public static bool IsPow2(ulong value) => (value & (value - 1)) == 0 && value != 0;

        [Intrinsic]
        public static bool IsPow2(nint value) => (value & (value - 1)) == 0 && value > 0;

        [Intrinsic]
        public static bool IsPow2(nuint value) => (value & (value - 1)) == 0 && value != 0;

        // The smallest power of 2 not below value; 0 for 0 and for values
        // above the largest power of 2 the type holds.
        public static uint RoundUpToPowerOf2(uint value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }

        public static ulong RoundUpToPowerOf2(ulong value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value |= value >> 32;
            return value + 1;
        }

        public static nuint RoundUpToPowerOf2(nuint value) => sizeof(nuint) == 8
            ? (nuint)RoundUpToPowerOf2((ulong)value)
            : (nuint)RoundUpToPowerOf2((uint)value);

        // Leading zeros: 32 (or 64) for 0.
        [Intrinsic]
        public static int LeadingZeroCount(uint value)
        {
            if (Lzcnt.IsSupported)
                return (int)Lzcnt.LeadingZeroCount(value);
            if (ArmBase.IsSupported)
                return ArmBase.LeadingZeroCount(value);
            if (value == 0)
                return 32;
            if (X86Base.IsSupported)
                return (int)(31 ^ X86Base.BitScanReverse(value));
            return 31 ^ Log2SoftwareFallback(value);
        }

        [Intrinsic]
        public static int LeadingZeroCount(ulong value)
        {
            if (Lzcnt.X64.IsSupported)
                return (int)Lzcnt.X64.LeadingZeroCount(value);
            if (ArmBase.Arm64.IsSupported)
                return ArmBase.Arm64.LeadingZeroCount(value);
            if (value == 0)
                return 64;
            if (X86Base.X64.IsSupported)
                return 63 ^ (int)X86Base.X64.BitScanReverse(value);
            uint high = (uint)(value >> 32);
            return high == 0 ? 32 + LeadingZeroCount((uint)value) : LeadingZeroCount(high);
        }

        public static int LeadingZeroCount(nuint value) => sizeof(nuint) == 8
            ? LeadingZeroCount((ulong)value)
            : LeadingZeroCount((uint)value);

        // The position of the highest set bit; 0 for 0, as in .NET.
        [Intrinsic]
        public static int Log2(uint value) => 31 ^ LeadingZeroCount(value | 1);

        [Intrinsic]
        public static int Log2(ulong value) => 63 ^ LeadingZeroCount(value | 1);

        public static int Log2(nuint value) => sizeof(nuint) == 8 ? Log2((ulong)value) : Log2((uint)value);

        [Intrinsic]
        public static int PopCount(uint value)
        {
            if (Popcnt.IsSupported)
                return (int)Popcnt.PopCount(value);
            if (AdvSimd.Arm64.IsSupported)
            {
                Vector64<ulong> input = Vector64.CreateScalar((ulong)value);
                return AdvSimd.Arm64.AddAcross(AdvSimd.PopCount(input.AsByte())).ToScalar();
            }
            value -= (value >> 1) & 0x55555555u;
            value = (value & 0x33333333u) + ((value >> 2) & 0x33333333u);
            return (int)((((value + (value >> 4)) & 0x0F0F0F0Fu) * 0x01010101u) >> 24);
        }

        [Intrinsic]
        public static int PopCount(ulong value)
        {
            if (Popcnt.X64.IsSupported)
                return (int)Popcnt.X64.PopCount(value);
            if (AdvSimd.Arm64.IsSupported)
            {
                Vector64<ulong> input = Vector64.CreateScalar(value);
                return AdvSimd.Arm64.AddAcross(AdvSimd.PopCount(input.AsByte())).ToScalar();
            }
            if (sizeof(nuint) == 4)
                return PopCount((uint)value) + PopCount((uint)(value >> 32));
            value -= (value >> 1) & 0x5555555555555555ul;
            value = (value & 0x3333333333333333ul) + ((value >> 2) & 0x3333333333333333ul);
            return (int)((((value + (value >> 4)) & 0x0F0F0F0F0F0F0F0Ful) * 0x0101010101010101ul) >> 56);
        }

        public static int PopCount(nuint value) => sizeof(nuint) == 8 ? PopCount((ulong)value) : PopCount((uint)value);

        // Trailing zeros: 32 (or 64) for 0.
        [Intrinsic]
        public static int TrailingZeroCount(int value) => TrailingZeroCount((uint)value);

        [Intrinsic]
        public static int TrailingZeroCount(uint value)
        {
            if (Bmi1.IsSupported)
                return (int)Bmi1.TrailingZeroCount(value);
            if (ArmBase.IsSupported)
                return ArmBase.LeadingZeroCount(ArmBase.ReverseElementBits(value));
            if (value == 0)
                return 32;
            if (X86Base.IsSupported)
                return (int)X86Base.BitScanForward(value);
            // The bits below the lowest set bit, counted.
            return PopCount((value & (0 - value)) - 1);
        }

        [Intrinsic]
        public static int TrailingZeroCount(long value) => TrailingZeroCount((ulong)value);

        [Intrinsic]
        public static int TrailingZeroCount(ulong value)
        {
            if (Bmi1.X64.IsSupported)
                return (int)Bmi1.X64.TrailingZeroCount(value);
            if (ArmBase.Arm64.IsSupported)
                return ArmBase.Arm64.LeadingZeroCount(ArmBase.Arm64.ReverseElementBits(value));
            if (value == 0)
                return 64;
            if (X86Base.X64.IsSupported)
                return (int)X86Base.X64.BitScanForward(value);
            uint low = (uint)value;
            return low == 0 ? 32 + TrailingZeroCount((uint)(value >> 32)) : TrailingZeroCount(low);
        }

        public static int TrailingZeroCount(nint value) => TrailingZeroCount((nuint)value);

        public static int TrailingZeroCount(nuint value) => sizeof(nuint) == 8
            ? TrailingZeroCount((ulong)value)
            : TrailingZeroCount((uint)value);

        // Shift counts are taken modulo the width, as the instructions do; the
        // compiler turns these into rol and ror.
        [Intrinsic]
        public static uint RotateLeft(uint value, int offset) => (value << offset) | (value >> (32 - offset));

        [Intrinsic]
        public static ulong RotateLeft(ulong value, int offset) => (value << offset) | (value >> (64 - offset));

        public static nuint RotateLeft(nuint value, int offset) => sizeof(nuint) == 8
            ? (nuint)RotateLeft((ulong)value, offset)
            : (nuint)RotateLeft((uint)value, offset);

        [Intrinsic]
        public static uint RotateRight(uint value, int offset) => (value >> offset) | (value << (32 - offset));

        [Intrinsic]
        public static ulong RotateRight(ulong value, int offset) => (value >> offset) | (value << (64 - offset));

        public static nuint RotateRight(nuint value, int offset) => sizeof(nuint) == 8
            ? (nuint)RotateRight((ulong)value, offset)
            : (nuint)RotateRight((uint)value, offset);

        // Binary search for the highest set bit of a nonzero value.
        private static int Log2SoftwareFallback(uint value)
        {
            int result = 0;
            if (value >= 1u << 16) { value >>= 16; result += 16; }
            if (value >= 1u << 8) { value >>= 8; result += 8; }
            if (value >= 1u << 4) { value >>= 4; result += 4; }
            if (value >= 1u << 2) { value >>= 2; result += 2; }
            if (value >= 1u << 1) result += 1;
            return result;
        }
    }
}
