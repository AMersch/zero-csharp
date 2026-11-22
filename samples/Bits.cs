// Example: Half, BitOperations and BitArray from the core library
// (zerocore/System/Half.cs, Numerics/BitOperations.cs, Collections/BitArray.cs),
// under .NET's names and with .NET's results.
// Exit code = number of failed checks.
// Build: ./build.sh Bits
//
// BitOperations uses the processor's instruction where the target has one, so
// each target checks a different path against the slow loops below: clz, rbit
// and cnt on arm64, bsr and bsf on x64, portable code on WebAssembly and DOS.

using System;
using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace BitsSample;

public static class Program
{
    static int s_failures;
    static int s_sectionChecks;
    static int s_sectionFailures;

    static void Section(string section)
    {
        Out.Line($"{(s_sectionFailures == 0 ? "PASS" : "FAIL")} {section} ({s_sectionChecks} checks)");
        s_sectionChecks = 0;
        s_sectionFailures = 0;
    }

    static void Check(string name, bool ok)
    {
        s_sectionChecks++;
        if (!ok)
        {
            s_failures++;
            s_sectionFailures++;
            Out.Line($"  FAIL: {name}");
        }
    }

    // Hides a value from the optimizer, which would otherwise do the work at
    // build time and never run the code being tested.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static float Opaque(float value) => value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static double Opaque(double value) => value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static uint Opaque(uint value) => value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ulong Opaque(ulong value) => value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Opaque(int value) => value;

    static ushort Bits(Half value) => BitConverter.HalfToUInt16Bits(value);

    // One bit at a time, to check BitOperations against.
    static int SlowPopCount(ulong value)
    {
        int count = 0;
        for (int i = 0; i < 64; i++)
            count += (int)((value >> i) & 1);
        return count;
    }

    static int SlowLeadingZeros(ulong value, int width)
    {
        int count = 0;
        for (int i = width - 1; i >= 0 && ((value >> i) & 1) == 0; i--)
            count++;
        return count;
    }

    static int SlowTrailingZeros(ulong value, int width)
    {
        int count = 0;
        for (int i = 0; i < width && ((value >> i) & 1) == 0; i++)
            count++;
        return count;
    }

    static ulong s_random = 0x9E3779B97F4A7C15;

    // xorshift64, with values of every bit width.
    static ulong NextRandom()
    {
        s_random ^= s_random << 13;
        s_random ^= s_random >> 7;
        s_random ^= s_random << 17;
        return s_random >> (int)(s_random & 63);
    }

    public static int Main()
    {
        // docs:half
        Half pi = (Half)Opaque(3.14159f);
        Check("float to Half rounds to nearest", (float)pi == 3.140625f);
        Check("shortest text that reads back", pi.ToString() == "3.14");
        Half product = (Half)Opaque(1.5f) * (Half)2.5f;
        Check("arithmetic, rounded to Half", (float)product == 3.75f);
        // docs:end
        Check("the bits", Bits(pi) == 0x4248);
        Check("compare", pi > Half.One && Half.NegativeOne < Half.Zero && pi == Half.Pi);
        Check("ToString of the limits", Half.MaxValue.ToString() == "65500" && Half.Epsilon.ToString() == "6E-08" && Half.MinValue.ToString() == "-65500");
        Check("ToString of special values", Half.NaN.ToString() == "NaN" && Half.NegativeZero.ToString() == "-0" && Half.PositiveInfinity.ToString() == "∞");
        Check("ToString of 0.1 and 1E-05", ((Half)Opaque(0.1f)).ToString() == "0.1" && ((Half)Opaque(0.00001f)).ToString() == "1E-05");
        Check("ToString of a tie: the even digit, if it reads back", ((Half)Opaque(0.46875f)).ToString() == "0.4688" && ((Half)Opaque(0.015625f)).ToString() == "0.01563");
        Check("overflow to infinity", Half.IsPositiveInfinity((Half)Opaque(65520f)) && (Half)Opaque(65519f) == Half.MaxValue && Half.IsNegativeInfinity((Half)Opaque(-1e10)));
        Check("ties go to even", (Half)Opaque(2049f) == (Half)2048 && (Half)Opaque(2051f) == (Half)2052 && (Half)Opaque(2049.0) == (Half)2048);
        Check("subnormals", Half.IsSubnormal(Half.Epsilon) && (float)Half.Epsilon == 5.9604645E-08f && !Half.IsNormal(Half.Epsilon));
        Check("half of Epsilon rounds to 0, a bit more to Epsilon", Bits((Half)Opaque(2.9802322E-08f)) == 0 && (Half)Opaque(2.9802326E-08f) == Half.Epsilon && (Half)Opaque(2.98023224e-8 * 1.0000001) == Half.Epsilon);
        Half nan = (Half)(Opaque(0f) / Opaque(0f));
        Check("NaN", Half.IsNaN(nan) && nan != nan && !(nan < Half.One) && nan.Equals(Half.NaN) && Half.IsNaN((Half)Opaque(double.NaN)));
        Check("NaN sorts first", nan.CompareTo(Half.MinValue) < 0 && Half.One.CompareTo(nan) > 0 && pi.CompareTo(Half.Pi) == 0);
        Check("zeros", Half.Zero == Half.NegativeZero && Half.Zero.Equals(Half.NegativeZero) && Half.IsNegative(Half.NegativeZero) && Half.Zero.GetHashCode() == Half.NegativeZero.GetHashCode());
        Check("integers", (int)(Half)Opaque(-300) == -300 && (Half)(byte)200 == (Half)200 && (long)(Half)Opaque(1000.7f) == 1000);
        Check("+, -, /, %, ++", (float)(pi + Half.One) == 4.140625f && (float)(-pi) == -3.140625f && (float)((Half)7 / (Half)2) == 3.5f && (float)((Half)7 % (Half)2) == 1 && (float)(++pi) == 4.140625f);
        Check("Min, Max, Abs", Half.Min(Half.One, Half.NegativeOne) == Half.NegativeOne && Half.Max(Half.One, Half.NegativeOne) == Half.One && Half.Abs(Half.MinValue) == Half.MaxValue);
        Check("Equals through object", ((object)Half.One).Equals(Half.One) && !((object)Half.One).Equals(1f));
        int failures = 0;
        for (int i = 0; i < 65536; i++)
        {
            Half x = BitConverter.UInt16BitsToHalf((ushort)i);
            if (!Half.IsNaN(x) && (Bits((Half)(float)x) != i || Bits((Half)(double)x) != i))
                failures++;
        }
        Check("all 65536 values read back through float and double", failures == 0);
        Section("Half");

        Check("PopCount", BitOperations.PopCount(Opaque(0xF0F0u)) == 8 && BitOperations.PopCount(Opaque(ulong.MaxValue)) == 64);
        Check("LeadingZeroCount", BitOperations.LeadingZeroCount(Opaque(1u)) == 31 && BitOperations.LeadingZeroCount(Opaque(0ul)) == 64);
        Check("TrailingZeroCount", BitOperations.TrailingZeroCount(Opaque(8u)) == 3 && BitOperations.TrailingZeroCount(Opaque(0u)) == 32);
        Check("Log2", BitOperations.Log2(Opaque(1000u)) == 9 && BitOperations.Log2(Opaque(0u)) == 0);
        Check("RotateLeft, RotateRight", BitOperations.RotateLeft(Opaque(0x80000001u), 1) == 3 && BitOperations.RotateRight(Opaque(1ul), 1) == 1ul << 63);
        Check("IsPow2, RoundUpToPowerOf2", BitOperations.IsPow2(Opaque(64)) && !BitOperations.IsPow2(Opaque(0)) && BitOperations.RoundUpToPowerOf2(Opaque(100u)) == 128);
        failures = 0;
        for (int i = 0; i < 20000; i++)
        {
            ulong value = i < 3 ? (i == 0 ? 0 : i == 1 ? ulong.MaxValue : 1ul << 63) : NextRandom();
            uint low = (uint)value;
            int offset = (int)(low % 70);
            if (BitOperations.PopCount(value) != SlowPopCount(value) || BitOperations.PopCount(low) != SlowPopCount(low))
                failures++;
            if (BitOperations.LeadingZeroCount(value) != SlowLeadingZeros(value, 64) || BitOperations.LeadingZeroCount(low) != SlowLeadingZeros(low, 32))
                failures++;
            if (BitOperations.TrailingZeroCount(value) != SlowTrailingZeros(value, 64) || BitOperations.TrailingZeroCount(low) != SlowTrailingZeros(low, 32))
                failures++;
            if (BitOperations.TrailingZeroCount((long)value) != SlowTrailingZeros(value, 64) || BitOperations.TrailingZeroCount((int)low) != SlowTrailingZeros(low, 32))
                failures++;
            if (value != 0 && (BitOperations.Log2(value) != 63 - SlowLeadingZeros(value, 64) || (low != 0 && BitOperations.Log2(low) != 31 - SlowLeadingZeros(low, 32))))
                failures++;
            if (BitOperations.RotateRight(BitOperations.RotateLeft(value, offset), offset) != value || BitOperations.RotateLeft(low, offset) != ((low << offset) | (low >> (32 - offset))))
                failures++;
            if (BitOperations.LeadingZeroCount((nuint)value) != SlowLeadingZeros((nuint)value, IntPtr.Size * 8) || BitOperations.PopCount((nuint)value) != SlowPopCount((nuint)value))
                failures++;
        }
        Check("20000 values against bit-by-bit loops", failures == 0);
        Section("BitOperations");

        // docs:bitarray
        // A sieve of Eratosthenes: bit i says whether i is prime.
        var primes = new BitArray(100, true);
        primes[0] = primes[1] = false;
        for (int i = 2; i * i < 100; i++)
            if (primes[i])
                for (int j = i * i; j < 100; j += i)
                    primes[j] = false;
        int count = 0;
        foreach (bool isPrime in primes)
            if (isPrime)
                count++;
        Check("25 primes below 100", count == 25 && primes[97] && !primes[91]);

        var odd = new BitArray(100);
        for (int i = 1; i < 100; i += 2)
            odd[i] = true;
        var oddPrimes = new BitArray(primes).And(odd);
        Check("And", !oddPrimes[2] && oddPrimes[3] && !oddPrimes[9]);
        // docs:end
        Check("Or, Xor, Not", new BitArray(primes).Or(odd)[2] && !new BitArray(primes).Xor(odd)[3] && new BitArray(odd).Not()[0] && !new BitArray(odd).Not()[1]);
        Check("Count is the length, as in .NET", primes.Count == 100 && primes.Length == 100);
        Check("SetAll, HasAllSet, HasAnySet", new BitArray(70, true).HasAllSet() && !new BitArray(70).HasAnySet() && !primes.HasAllSet() && primes.HasAnySet());
        var shifted = new BitArray(70);
        shifted[0] = true;
        shifted[69] = true;
        shifted.LeftShift(Opaque(33));
        Check("LeftShift: bits past the end are gone", shifted[33] && !shifted[0] && !shifted.RightShift(Opaque(40)).HasAnySet());
        shifted = new BitArray(new int[] { -1, -1, -1 }).RightShift(Opaque(65));
        Check("RightShift: zeros come in at the top", shifted[30] && !shifted[31] && !shifted[95]);
        // .NET 10's BitArray brings back bits shifted past the end on a later
        // RightShift; this one doesn't.
        var noGhosts = new BitArray(68);
        noGhosts[10] = true;
        noGhosts.LeftShift(Opaque(62)).RightShift(Opaque(22));
        Check("no bits come back from past the end", !noGhosts.HasAnySet());
        var bytes = new BitArray(new byte[] { 0x01, 0x80 });
        Check("from bytes: lowest bit first", bytes.Length == 16 && bytes[0] && bytes[15] && !bytes[7]);
        byte[] copied = new byte[3];
        bytes.CopyTo(copied, 1);
        int[] ints = new int[1];
        bytes.CopyTo(ints, 0);
        bool[] bools = new bool[16];
        bytes.CopyTo(bools, 0);
        Check("CopyTo byte[], int[], bool[]", copied[1] == 1 && copied[2] == 0x80 && ints[0] == 0x8001 && bools[15] && !bools[14]);
        bytes.Length = 9;
        bytes.Length = 40;
        Check("Length: shrinking drops bits, growing adds zeros", bytes[0] && !bytes[15] && !bytes[39]);
        Check("Clone", ((BitArray)primes.Clone())[97]);
        Section("BitArray");

        Check("float", BitConverter.SingleToUInt32Bits(Opaque(1f)) == 0x3F800000 && BitConverter.Int32BitsToSingle(Opaque(0x40490FDB)) == 3.1415927f);
        Check("double", BitConverter.DoubleToInt64Bits(Opaque(1.0)) == 0x3FF0000000000000 && BitConverter.UInt64BitsToDouble(0x4000000000000000) == 2.0);
        Check("Half", BitConverter.HalfToInt16Bits(Half.NegativeOne) == unchecked((short)0xBC00) && BitConverter.Int16BitsToHalf(0x3C00) == Half.One);
        Section("BitConverter");

        Out.Line();
        if (s_failures == 0)
            Out.Line("All checks passed.");
        else
            Out.Line($"{s_failures} checks FAILED.");
        return s_failures;
    }
}
