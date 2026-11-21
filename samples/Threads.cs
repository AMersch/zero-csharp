// Example: Parallel.For (lib/Threads.cs), loops whose iterations run on all
// processors. Exit code = number of failed checks.
// Build: ./build.sh Threads
//
// Where there are no threads (WebAssembly, DOS) the same loops run on one
// thread and give the same answers, which is what the checks look at.

using System;
using System.Threading;

namespace ThreadsSample;

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
            Out.Line($"  failed: {name}");
        }
    }

    // What the iterations share. Each writes only its own element.
    struct Squares
    {
        public long[] Results;
    }

    struct Counter
    {
        public int[] Hits; // one int, counted with Interlocked
    }

    struct Parts
    {
        public int Count;
        public int PerPart;
        public long[] Sums;
    }

    // Some work worth spreading: the sum of i*i, computed the slow way.
    static long SlowSquare(int i)
    {
        long sum = 0;
        for (int k = 0; k < i; k++)
            sum += 2 * k + 1;
        return sum;
    }

    public static int Main()
    {
        Check("at least one processor", Parallel.ProcessorCount >= 1);

        // docs:parallel-for
        // Each iteration writes its own element, so no two threads touch the
        // same memory. The lambda is static: what it needs comes as state.
        var squares = new Squares { Results = new long[2000] };
        Parallel.For(0, 2000, squares,
            static (s, i) => s.Results[i] = SlowSquare(i));
        // docs:end
        bool allRight = true;
        for (int i = 0; i < 2000; i++)
            allRight &= squares.Results[i] == (long)i * i;
        Check("every iteration ran once, with its own index", allRight);

        // docs:interlocked
        // A total that every iteration adds to needs Interlocked.
        var counter = new Counter { Hits = new int[1] };
        Parallel.For(0, 100000, counter,
            static (c, i) => Interlocked.Increment(ref c.Hits[0]));
        // docs:end
        Check("Interlocked counts every iteration", counter.Hits[0] == 100000);

        Parallel.For(5, 5, counter, static (c, i) => c.Hits[0] = -1);
        Check("an empty range runs nothing", counter.Hits[0] == 100000);
        Section("Parallel.For");

        // docs:arena-per-part
        // Arenas aren't thread-safe, so split the work into one part per
        // processor and give each part an arena of its own. Inside it,
        // AllocateObjectsHere sends that thread's ordinary allocations to the
        // part's arena too: the setting belongs to the thread that makes it.
        var parts = new Parts { Count = Parallel.ProcessorCount, PerPart = 500 };
        parts.Sums = new long[parts.Count];
        Action<Parts, int> sumPart = static (p, part) =>
        {
            Arena arena = Arena.Create(4096);
            Arena.ObjectScope objects;
            // SAFETY: nothing allocated in the part outlives it.
            unsafe { objects = arena.AllocateObjectsHere(); }
            using (objects)
            {
                long sum = 0;
                for (int i = 0; i < p.PerPart; i++)
                {
                    int[] scratch = new int[8]; // goes into this part's arena
                    scratch[i % 8] = i;
                    sum += scratch[i % 8];
                }
                p.Sums[part] = sum;
            }
            unsafe { arena.Destroy(); } // SAFETY: see above.
        };
        long heapBefore = GC.GetTotalAllocatedBytes(); // after creating the lambda
        Parallel.For(0, parts.Count, parts, sumPart);
        long heapGrowth = GC.GetTotalAllocatedBytes() - heapBefore;
        // docs:end
        bool partsRight = true;
        for (int part = 0; part < parts.Count; part++)
            partsRight &= parts.Sums[part] == 499 * 500 / 2;
        Check("every part summed its own range", partsRight);
        Check("workers' allocations went to their arenas", heapGrowth == 0);
        Section("an arena per part");

        Out.Line();
        if (s_failures == 0)
            Out.Line("All checks passed.");
        else
            Out.Line($"{s_failures} checks FAILED.");
        return s_failures;
    }
}
