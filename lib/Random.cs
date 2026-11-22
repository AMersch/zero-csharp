// Random numbers: xoshiro256** (Blackman and Vigna), seeded through SplitMix64.
//
//   var random = new Random(42);           // the same numbers on every target
//   int die = random.Next(1, 7);           // 1 to 6
//   double x = random.NextDouble();        // 0 <= x < 1
//   random.Shuffle(cards);
//
//   var varied = Random.FromClock();       // different numbers each run
//
// The members have .NET's names, but the generator is a struct holding its
// state: a copy goes on from the same point and repeats the original's numbers.
// Pass one by ref. default(Random) is new Random(0). The numbers are fast and
// statistically good, but predictable: not for keys, tokens or passwords.

using System;
using System.Numerics;

/// <summary>A random number generator (xoshiro256**). A struct: pass it by ref.</summary>
public struct Random
{
    ulong _s0, _s1, _s2, _s3;

    /// <summary>A generator whose numbers are fixed by <paramref name="seed"/>, on every target.</summary>
    public Random(ulong seed)
    {
        // SplitMix64 spreads the seed over the 256 bits of state; it never makes
        // all four words zero, which xoshiro can't start from.
        _s0 = SplitMix64(ref seed);
        _s1 = SplitMix64(ref seed);
        _s2 = SplitMix64(ref seed);
        _s3 = SplitMix64(ref seed);
    }

    /// <summary>A generator seeded from the clocks, so each run gets different numbers.</summary>
    public static Random FromClock()
        => new Random((ulong)Clock.UnixMilliseconds * 0x9E3779B97F4A7C15UL ^ (ulong)Clock.Monotonic);

    static ulong SplitMix64(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>64 random bits.</summary>
    public ulong NextUInt64()
    {
        if ((_s0 | _s1 | _s2 | _s3) == 0)
            this = new Random(0);
        ulong result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>A number from 0 up to but not including int.MaxValue.</summary>
    public int Next()
    {
        while (true)
        {
            int value = (int)(NextUInt64() >> 33);
            if (value != int.MaxValue)
                return value;
        }
    }

    /// <summary>A number from 0 up to but not including <paramref name="maxValue"/>; 0 if it is 0.</summary>
    public int Next(int maxValue) => Next(0, maxValue);

    /// <summary>
    /// A number from <paramref name="minValue"/> up to but not including
    /// <paramref name="maxValue"/>; minValue if they are equal. Every number in the range
    /// is equally likely. Ends the program if maxValue is less than minValue.
    /// </summary>
    public int Next(int minValue, int maxValue)
    {
        if (maxValue < minValue)
            Environment.FailFast("Random.Next: maxValue is less than minValue");
        return (int)((uint)minValue + Below((uint)(maxValue - minValue)));
    }

    /// <summary>
    /// A number from <paramref name="minValue"/> up to but not including
    /// <paramref name="maxValue"/>; minValue if they are equal. Ends the program if
    /// maxValue is less than minValue.
    /// </summary>
    public long NextInt64(long minValue, long maxValue)
    {
        if (maxValue < minValue)
            Environment.FailFast("Random.NextInt64: maxValue is less than minValue");
        ulong range = (ulong)(maxValue - minValue);
        if (range <= 1)
            return minValue;
        // Keep only as many bits as the range needs, and draw again when the
        // number is past its end: at most half the draws are thrown away.
        ulong mask = ulong.MaxValue >> BitOperations.LeadingZeroCount(range - 1);
        while (true)
        {
            ulong value = NextUInt64() & mask;
            if (value < range)
                return (long)((ulong)minValue + value);
        }
    }

    /// <summary>A number from 0 up to but not including 1, in steps of 2^-53.</summary>
    public double NextDouble() => (long)(NextUInt64() >> 11) * (1.0 / (1L << 53));

    /// <summary>A number from 0 up to but not including 1, in steps of 2^-24.</summary>
    public float NextSingle() => (int)(NextUInt64() >> 40) * (1.0f / (1 << 24));

    /// <summary>Fills <paramref name="buffer"/> with random bytes.</summary>
    public void NextBytes(Span<byte> buffer)
    {
        int i = 0;
        while (i < buffer.Length)
        {
            ulong bits = NextUInt64();
            for (int b = 0; b < 8 && i < buffer.Length; b++, bits >>= 8)
                buffer[i++] = (byte)bits;
        }
    }

    /// <summary>Puts <paramref name="values"/> in a random order, each order equally likely.</summary>
    public void Shuffle<T>(Span<T> values)
    {
        // Fisher-Yates: swap each place, from the back, with a random one at or before it.
        for (int i = values.Length - 1; i > 0; i--)
        {
            int j = (int)Below((uint)i + 1);
            T swap = values[i];
            values[i] = values[j];
            values[j] = swap;
        }
    }

    // A number from 0 up to but not including range (0 if range is 0), every
    // one equally likely: Lemire's multiply-and-reject method.
    uint Below(uint range)
    {
        ulong product = (ulong)(uint)(NextUInt64() >> 32) * range;
        uint low = (uint)product;
        if (low < range)
        {
            uint threshold = (0u - range) % range;
            while (low < threshold)
            {
                product = (ulong)(uint)(NextUInt64() >> 32) * range;
                low = (uint)product;
            }
        }
        return (uint)(product >> 32);
    }
}
