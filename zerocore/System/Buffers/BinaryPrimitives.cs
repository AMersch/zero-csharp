using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace System.Buffers.Binary
{
    // Numbers in a byte span in a given byte order, as file formats and
    // network protocols store them, with .NET's names:
    //
    //   uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
    //   BinaryPrimitives.WriteInt16BigEndian(packet[2..], port);
    //
    // Read and Write end the program if the span is too short (.NET throws);
    // TryRead and TryWrite return false instead. The span needn't be aligned.
    // Every target is little-endian (BitConverter.IsLittleEndian), so the
    // LittleEndian methods are a plain load or store and the BigEndian ones
    // add a byte swap.
    public static unsafe class BinaryPrimitives
    {
#if WASM
        private static int s_zero; // never written: always 0
#endif

        // The compiler turns these into the processor's byte-swap instruction
        // (bswap, rev). Not in the WASM build, which DOS shares: LLVM's bswap
        // is a 486 instruction, and DOS programs must run on a 386.
#if !WASM
        [Intrinsic]
#endif
        public static byte ReverseEndianness(byte value) => value;

#if !WASM
        [Intrinsic]
#endif
        public static sbyte ReverseEndianness(sbyte value) => value;

#if !WASM
        [Intrinsic]
#endif
        public static ushort ReverseEndianness(ushort value) => (ushort)((value >> 8) + (value << 8));

#if !WASM
        [Intrinsic]
#endif
        public static short ReverseEndianness(short value) => (short)ReverseEndianness((ushort)value);

#if !WASM
        [Intrinsic]
#endif
        public static uint ReverseEndianness(uint value)
        {
#if WASM
            // LLVM recognizes a byte swap written any usual way and emits
            // bswap. A shift count it can't see as a constant stops that: a
            // volatile read of a static field (one of a local is optimized
            // into a plain value before LLVM sees it).
            // Every shift needs it: from (value << 24) | (value >> 24) alone,
            // LLVM still makes a bswap of the outer bytes.
            int eight = 8 + Volatile.Read(ref s_zero);
            int twentyFour = 3 * eight;
            return (value << twentyFour) | ((value & 0xFF00u) << eight) | ((value >> eight) & 0xFF00u) | (value >> twentyFour);
#else
            return BitOperations.RotateRight(value & 0x00FF00FFu, 8) + BitOperations.RotateLeft(value & 0xFF00FF00u, 8);
#endif
        }

#if !WASM
        [Intrinsic]
#endif
        public static int ReverseEndianness(int value) => (int)ReverseEndianness((uint)value);

#if !WASM
        [Intrinsic]
#endif
        public static ulong ReverseEndianness(ulong value)
            => ((ulong)ReverseEndianness((uint)value) << 32) + ReverseEndianness((uint)(value >> 32));

#if !WASM
        [Intrinsic]
#endif
        public static long ReverseEndianness(long value) => (long)ReverseEndianness((ulong)value);

        /// <summary>Reverses the byte order of each item, in place.</summary>
        public static void ReverseEndianness(Span<ushort> values)
        {
            for (int i = 0; i < values.Length; i++)
                values[i] = ReverseEndianness(values[i]);
        }

        public static void ReverseEndianness(Span<uint> values)
        {
            for (int i = 0; i < values.Length; i++)
                values[i] = ReverseEndianness(values[i]);
        }

        public static void ReverseEndianness(Span<ulong> values)
        {
            for (int i = 0; i < values.Length; i++)
                values[i] = ReverseEndianness(values[i]);
        }

        public static short ReadInt16LittleEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(short))
                TooShort();
            short value = Unsafe.ReadUnaligned<short>(ref MemoryMarshal.GetReference(source));
            return value;
        }

        public static bool TryReadInt16LittleEndian(ReadOnlySpan<byte> source, out short value)
        {
            if (source.Length < sizeof(short))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<short>(ref MemoryMarshal.GetReference(source));
            return true;
        }

        public static void WriteInt16LittleEndian(Span<byte> destination, short value)
        {
            if (destination.Length < sizeof(short))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
        }

        public static bool TryWriteInt16LittleEndian(Span<byte> destination, short value)
        {
            if (destination.Length < sizeof(short))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static short ReadInt16BigEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(short))
                TooShort();
            short value = Unsafe.ReadUnaligned<short>(ref MemoryMarshal.GetReference(source));
            return ReverseEndianness(value);
        }

        public static bool TryReadInt16BigEndian(ReadOnlySpan<byte> source, out short value)
        {
            if (source.Length < sizeof(short))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<short>(ref MemoryMarshal.GetReference(source));
            value = ReverseEndianness(value);
            return true;
        }

        public static void WriteInt16BigEndian(Span<byte> destination, short value)
        {
            if (destination.Length < sizeof(short))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), ReverseEndianness(value));
        }

        public static bool TryWriteInt16BigEndian(Span<byte> destination, short value)
        {
            if (destination.Length < sizeof(short))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), ReverseEndianness(value));
            return true;
        }

        public static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(ushort))
                TooShort();
            ushort value = Unsafe.ReadUnaligned<ushort>(ref MemoryMarshal.GetReference(source));
            return value;
        }

        public static bool TryReadUInt16LittleEndian(ReadOnlySpan<byte> source, out ushort value)
        {
            if (source.Length < sizeof(ushort))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<ushort>(ref MemoryMarshal.GetReference(source));
            return true;
        }

        public static void WriteUInt16LittleEndian(Span<byte> destination, ushort value)
        {
            if (destination.Length < sizeof(ushort))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
        }

        public static bool TryWriteUInt16LittleEndian(Span<byte> destination, ushort value)
        {
            if (destination.Length < sizeof(ushort))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(ushort))
                TooShort();
            ushort value = Unsafe.ReadUnaligned<ushort>(ref MemoryMarshal.GetReference(source));
            return ReverseEndianness(value);
        }

        public static bool TryReadUInt16BigEndian(ReadOnlySpan<byte> source, out ushort value)
        {
            if (source.Length < sizeof(ushort))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<ushort>(ref MemoryMarshal.GetReference(source));
            value = ReverseEndianness(value);
            return true;
        }

        public static void WriteUInt16BigEndian(Span<byte> destination, ushort value)
        {
            if (destination.Length < sizeof(ushort))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), ReverseEndianness(value));
        }

        public static bool TryWriteUInt16BigEndian(Span<byte> destination, ushort value)
        {
            if (destination.Length < sizeof(ushort))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), ReverseEndianness(value));
            return true;
        }

        public static int ReadInt32LittleEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(int))
                TooShort();
            int value = Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(source));
            return value;
        }

        public static bool TryReadInt32LittleEndian(ReadOnlySpan<byte> source, out int value)
        {
            if (source.Length < sizeof(int))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(source));
            return true;
        }

        public static void WriteInt32LittleEndian(Span<byte> destination, int value)
        {
            if (destination.Length < sizeof(int))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
        }

        public static bool TryWriteInt32LittleEndian(Span<byte> destination, int value)
        {
            if (destination.Length < sizeof(int))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static int ReadInt32BigEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(int))
                TooShort();
            int value = Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(source));
            return ReverseEndianness(value);
        }

        public static bool TryReadInt32BigEndian(ReadOnlySpan<byte> source, out int value)
        {
            if (source.Length < sizeof(int))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(source));
            value = ReverseEndianness(value);
            return true;
        }

        public static void WriteInt32BigEndian(Span<byte> destination, int value)
        {
            if (destination.Length < sizeof(int))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), ReverseEndianness(value));
        }

        public static bool TryWriteInt32BigEndian(Span<byte> destination, int value)
        {
            if (destination.Length < sizeof(int))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), ReverseEndianness(value));
            return true;
        }

        public static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(uint))
                TooShort();
            uint value = Unsafe.ReadUnaligned<uint>(ref MemoryMarshal.GetReference(source));
            return value;
        }

        public static bool TryReadUInt32LittleEndian(ReadOnlySpan<byte> source, out uint value)
        {
            if (source.Length < sizeof(uint))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<uint>(ref MemoryMarshal.GetReference(source));
            return true;
        }

        public static void WriteUInt32LittleEndian(Span<byte> destination, uint value)
        {
            if (destination.Length < sizeof(uint))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
        }

        public static bool TryWriteUInt32LittleEndian(Span<byte> destination, uint value)
        {
            if (destination.Length < sizeof(uint))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static uint ReadUInt32BigEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(uint))
                TooShort();
            uint value = Unsafe.ReadUnaligned<uint>(ref MemoryMarshal.GetReference(source));
            return ReverseEndianness(value);
        }

        public static bool TryReadUInt32BigEndian(ReadOnlySpan<byte> source, out uint value)
        {
            if (source.Length < sizeof(uint))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<uint>(ref MemoryMarshal.GetReference(source));
            value = ReverseEndianness(value);
            return true;
        }

        public static void WriteUInt32BigEndian(Span<byte> destination, uint value)
        {
            if (destination.Length < sizeof(uint))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), ReverseEndianness(value));
        }

        public static bool TryWriteUInt32BigEndian(Span<byte> destination, uint value)
        {
            if (destination.Length < sizeof(uint))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), ReverseEndianness(value));
            return true;
        }

        public static long ReadInt64LittleEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(long))
                TooShort();
            long value = Unsafe.ReadUnaligned<long>(ref MemoryMarshal.GetReference(source));
            return value;
        }

        public static bool TryReadInt64LittleEndian(ReadOnlySpan<byte> source, out long value)
        {
            if (source.Length < sizeof(long))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<long>(ref MemoryMarshal.GetReference(source));
            return true;
        }

        public static void WriteInt64LittleEndian(Span<byte> destination, long value)
        {
            if (destination.Length < sizeof(long))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
        }

        public static bool TryWriteInt64LittleEndian(Span<byte> destination, long value)
        {
            if (destination.Length < sizeof(long))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static long ReadInt64BigEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(long))
                TooShort();
            long value = Unsafe.ReadUnaligned<long>(ref MemoryMarshal.GetReference(source));
            return ReverseEndianness(value);
        }

        public static bool TryReadInt64BigEndian(ReadOnlySpan<byte> source, out long value)
        {
            if (source.Length < sizeof(long))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<long>(ref MemoryMarshal.GetReference(source));
            value = ReverseEndianness(value);
            return true;
        }

        public static void WriteInt64BigEndian(Span<byte> destination, long value)
        {
            if (destination.Length < sizeof(long))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), ReverseEndianness(value));
        }

        public static bool TryWriteInt64BigEndian(Span<byte> destination, long value)
        {
            if (destination.Length < sizeof(long))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), ReverseEndianness(value));
            return true;
        }

        public static ulong ReadUInt64LittleEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(ulong))
                TooShort();
            ulong value = Unsafe.ReadUnaligned<ulong>(ref MemoryMarshal.GetReference(source));
            return value;
        }

        public static bool TryReadUInt64LittleEndian(ReadOnlySpan<byte> source, out ulong value)
        {
            if (source.Length < sizeof(ulong))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<ulong>(ref MemoryMarshal.GetReference(source));
            return true;
        }

        public static void WriteUInt64LittleEndian(Span<byte> destination, ulong value)
        {
            if (destination.Length < sizeof(ulong))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
        }

        public static bool TryWriteUInt64LittleEndian(Span<byte> destination, ulong value)
        {
            if (destination.Length < sizeof(ulong))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static ulong ReadUInt64BigEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(ulong))
                TooShort();
            ulong value = Unsafe.ReadUnaligned<ulong>(ref MemoryMarshal.GetReference(source));
            return ReverseEndianness(value);
        }

        public static bool TryReadUInt64BigEndian(ReadOnlySpan<byte> source, out ulong value)
        {
            if (source.Length < sizeof(ulong))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<ulong>(ref MemoryMarshal.GetReference(source));
            value = ReverseEndianness(value);
            return true;
        }

        public static void WriteUInt64BigEndian(Span<byte> destination, ulong value)
        {
            if (destination.Length < sizeof(ulong))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), ReverseEndianness(value));
        }

        public static bool TryWriteUInt64BigEndian(Span<byte> destination, ulong value)
        {
            if (destination.Length < sizeof(ulong))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), ReverseEndianness(value));
            return true;
        }

        public static float ReadSingleLittleEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(float))
                TooShort();
            float value = Unsafe.ReadUnaligned<float>(ref MemoryMarshal.GetReference(source));
            return value;
        }

        public static bool TryReadSingleLittleEndian(ReadOnlySpan<byte> source, out float value)
        {
            if (source.Length < sizeof(float))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<float>(ref MemoryMarshal.GetReference(source));
            return true;
        }

        public static void WriteSingleLittleEndian(Span<byte> destination, float value)
        {
            if (destination.Length < sizeof(float))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
        }

        public static bool TryWriteSingleLittleEndian(Span<byte> destination, float value)
        {
            if (destination.Length < sizeof(float))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static float ReadSingleBigEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(float))
                TooShort();
            float value = Unsafe.ReadUnaligned<float>(ref MemoryMarshal.GetReference(source));
            return BitConverter.Int32BitsToSingle(ReverseEndianness(BitConverter.SingleToInt32Bits(value)));
        }

        public static bool TryReadSingleBigEndian(ReadOnlySpan<byte> source, out float value)
        {
            if (source.Length < sizeof(float))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<float>(ref MemoryMarshal.GetReference(source));
            value = BitConverter.Int32BitsToSingle(ReverseEndianness(BitConverter.SingleToInt32Bits(value)));
            return true;
        }

        public static void WriteSingleBigEndian(Span<byte> destination, float value)
        {
            if (destination.Length < sizeof(float))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), BitConverter.Int32BitsToSingle(ReverseEndianness(BitConverter.SingleToInt32Bits(value))));
        }

        public static bool TryWriteSingleBigEndian(Span<byte> destination, float value)
        {
            if (destination.Length < sizeof(float))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), BitConverter.Int32BitsToSingle(ReverseEndianness(BitConverter.SingleToInt32Bits(value))));
            return true;
        }

        public static double ReadDoubleLittleEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(double))
                TooShort();
            double value = Unsafe.ReadUnaligned<double>(ref MemoryMarshal.GetReference(source));
            return value;
        }

        public static bool TryReadDoubleLittleEndian(ReadOnlySpan<byte> source, out double value)
        {
            if (source.Length < sizeof(double))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<double>(ref MemoryMarshal.GetReference(source));
            return true;
        }

        public static void WriteDoubleLittleEndian(Span<byte> destination, double value)
        {
            if (destination.Length < sizeof(double))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
        }

        public static bool TryWriteDoubleLittleEndian(Span<byte> destination, double value)
        {
            if (destination.Length < sizeof(double))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static double ReadDoubleBigEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(double))
                TooShort();
            double value = Unsafe.ReadUnaligned<double>(ref MemoryMarshal.GetReference(source));
            return BitConverter.Int64BitsToDouble(ReverseEndianness(BitConverter.DoubleToInt64Bits(value)));
        }

        public static bool TryReadDoubleBigEndian(ReadOnlySpan<byte> source, out double value)
        {
            if (source.Length < sizeof(double))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<double>(ref MemoryMarshal.GetReference(source));
            value = BitConverter.Int64BitsToDouble(ReverseEndianness(BitConverter.DoubleToInt64Bits(value)));
            return true;
        }

        public static void WriteDoubleBigEndian(Span<byte> destination, double value)
        {
            if (destination.Length < sizeof(double))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), BitConverter.Int64BitsToDouble(ReverseEndianness(BitConverter.DoubleToInt64Bits(value))));
        }

        public static bool TryWriteDoubleBigEndian(Span<byte> destination, double value)
        {
            if (destination.Length < sizeof(double))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), BitConverter.Int64BitsToDouble(ReverseEndianness(BitConverter.DoubleToInt64Bits(value))));
            return true;
        }

        public static Half ReadHalfLittleEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(Half))
                TooShort();
            Half value = Unsafe.ReadUnaligned<Half>(ref MemoryMarshal.GetReference(source));
            return value;
        }

        public static bool TryReadHalfLittleEndian(ReadOnlySpan<byte> source, out Half value)
        {
            if (source.Length < sizeof(Half))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<Half>(ref MemoryMarshal.GetReference(source));
            return true;
        }

        public static void WriteHalfLittleEndian(Span<byte> destination, Half value)
        {
            if (destination.Length < sizeof(Half))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
        }

        public static bool TryWriteHalfLittleEndian(Span<byte> destination, Half value)
        {
            if (destination.Length < sizeof(Half))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static Half ReadHalfBigEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < sizeof(Half))
                TooShort();
            Half value = Unsafe.ReadUnaligned<Half>(ref MemoryMarshal.GetReference(source));
            return BitConverter.Int16BitsToHalf(ReverseEndianness(BitConverter.HalfToInt16Bits(value)));
        }

        public static bool TryReadHalfBigEndian(ReadOnlySpan<byte> source, out Half value)
        {
            if (source.Length < sizeof(Half))
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<Half>(ref MemoryMarshal.GetReference(source));
            value = BitConverter.Int16BitsToHalf(ReverseEndianness(BitConverter.HalfToInt16Bits(value)));
            return true;
        }

        public static void WriteHalfBigEndian(Span<byte> destination, Half value)
        {
            if (destination.Length < sizeof(Half))
                TooShort();
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), BitConverter.Int16BitsToHalf(ReverseEndianness(BitConverter.HalfToInt16Bits(value))));
        }

        public static bool TryWriteHalfBigEndian(Span<byte> destination, Half value)
        {
            if (destination.Length < sizeof(Half))
                return false;
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), BitConverter.Int16BitsToHalf(ReverseEndianness(BitConverter.HalfToInt16Bits(value))));
            return true;
        }

        private static void TooShort() => Environment.FailFast("BinaryPrimitives: the span is too short for the value");
    }
}
