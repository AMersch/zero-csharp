// Example: arrays in the Commander X16's banked RAM, from C#. A 240 KB array
// of 12-byte structs (some lie across the border between two banks), a sieve
// of Eratosthenes over 60,000 flags, and the allocator's bookkeeping. None of
// it fits in the 38 KB a program runs in. Exit code = number of failed checks.
// Build: ./build.sh --cx16 BankedDemo    Run: ./run.sh --cx16 BankedDemo
//
using System;

public struct Vec
{
    public int X, Y, Z;
}

public static class Program
{
    static int s_failures;
    static int s_checks;
    static string s_firstFailure;

    static void Check(string name, bool ok)
    {
        s_checks++;
        if (!ok)
        {
            s_failures++;
            s_firstFailure ??= name;
        }
    }

    // docs:sieve
    // Primes below n, with the flags in banked RAM: flag[i] is 1 for a composite.
    static int CountPrimes(int n)
    {
        BankedArray<byte> composite = BankedArray<byte>.Create(n);
        int count = 0;
        for (int i = 2; i < n; i++)
        {
            if (composite[i] != 0)
                continue;
            count++;
            for (int j = i * 2; j < n; j += i)
                composite[j] = 1;
        }
        return count;
    }
    // docs:end

    public static int Main()
    {
        int mark = Banks.Mark();

        // 20,000 * 12 bytes = 240,000: 29 banks, and 12 doesn't divide 8192, so
        // some elements are split between two banks.
        BankedArray<Vec> points = BankedArray<Vec>.Create(20000);
        Check("created", points.IsCreated && points.Length == 20000);
        Check("room used", Banks.Used == 240000);
        Check("zeroed", points[0].X == 0 && points[19999].Z == 0);

        for (int i = 0; i < points.Length; i++)
            points[i] = new Vec { X = i, Y = i * 3, Z = -i };
        bool all = true;
        for (int i = 0; i < points.Length && all; i++)
        {
            Vec v = points[i];
            all = v.X == i && v.Y == i * 3 && v.Z == -i;
        }
        Check("20,000 structs", all);
        // element 682 is the first to reach into the second bank (682 * 12 = 8184)
        Check("across a border", points[682].Y == 682 * 3 && points[683].Z == -683);

        // Ranges at once, and refused ones.
        Span<Vec> five = stackalloc Vec[5];
        Check("read a range", points.Read(680, five) && five[0].X == 680 && five[4].X == 684);
        five[2] = new Vec { X = -1 };
        Check("write a range", points.Write(680, five) && points[682].X == -1 && points[683].X == 683);
        Check("past the end", !points.Read(19998, five) && !points.Write(-1, five));

        // Copy, fill, clear.
        BankedArray<Vec> copy = BankedArray<Vec>.Create(20000, zero: false);
        Check("copied", points.CopyTo(copy) && copy[682].X == -1 && copy[19999].Y == 19999 * 3);
        copy.Fill(new Vec { X = 7, Y = 8, Z = 9 });
        Check("filled", copy[0].Z == 9 && copy[12345].Y == 8 && copy[19999].X == 7);
        Check("the original is separate", points[5].X == 5);
        copy.Clear();
        Check("cleared", copy[682].X == 0 && copy[19999].Z == 0);
        Check("too short a target", !points.CopyTo(BankedArray<Vec>.Create(10)));

        // The rest of the banks: asking for more than is left gives a not-created array.
        BankedArray<byte> huge = BankedArray<byte>.Create(Banks.Available + 1);
        Check("a refused array", !huge.IsCreated && huge.Length == 0);

        // Take back everything since the mark, and use the room again.
        Check("release", Banks.Release(mark) && Banks.Used == mark);
        Check("the sieve", CountPrimes(60000) == 6057);
        Check("it gave the room back", Banks.Release(mark));

        if (s_failures == 0)
            Out.Line($"All {s_checks} checks passed.");
        else
            Out.Line($"{s_failures} of {s_checks} checks FAILED, the first: {s_firstFailure}");
        return s_failures;
    }
}
