// Test: an arena and spans, sized for a small machine. Arena.cs checks more
// and doesn't fit in a Commodore 64 (./build.sh --c64). The arena here is
// 1 KB, because the machine this is for has about 10 KB of heap once the
// program is loaded. It runs on every target.
// Exit code = number of failed checks.
// Build: ./build.sh SmallArena

using System;

namespace SmallArenaSample;

public static class Program
{
    static int s_failures;
    static int s_sectionChecks;
    static int s_sectionFailures;

    static void End(string section)
    {
        Console.Write(s_sectionFailures == 0 ? "PASS " : "FAIL ");
        Console.Write(section);
        Console.Write(" (");
        Console.Write(s_sectionChecks);
        Console.WriteLine(" checks)");
        s_failures += s_sectionFailures;
        s_sectionChecks = 0;
        s_sectionFailures = 0;
    }

    static void Check(string name, bool ok)
    {
        s_sectionChecks++;
        if (!ok)
        {
            s_sectionFailures++;
            Console.Write("  failed: ");
            Console.WriteLine(name);
        }
    }

    static int Sum(ReadOnlySpan<int> values)
    {
        int sum = 0;
        foreach (int value in values)
            sum += value;
        return sum;
    }

    public static int Main()
    {
        Arena arena = Arena.Create(1024);
        Check("created, empty", arena.IsCreated && arena.Used == 0 && arena.BlockCount == 1);
        Span<int> numbers;
        unsafe { numbers = arena.AllocSpan<int>(10); } // SAFETY: used before the arena is reset.
        Check("AllocSpan is zeroed", numbers.Length == 10 && Sum(numbers) == 0);
        for (int i = 0; i < numbers.Length; i++)
            numbers[i] = i * i;
        Check("span of ints", numbers[9] == 81 && Sum(numbers) == 285 && Sum(numbers.Slice(2, 3)) == 29);
        nuint used = arena.Used;
        Check("Used grows", used >= 40);
        Arena.Mark mark = arena.Save();
        unsafe { arena.AllocSpan<long>(20); } // SAFETY: not used.
        Check("more is used", arena.Used > used);
        unsafe { arena.Restore(mark); } // SAFETY: nothing allocated after the mark is used again.
        Check("Restore gives it back", arena.Used == used && numbers[9] == 81);
        unsafe { arena.AllocSpan<byte>(3000); } // SAFETY: not used.
        Check("a big request gets a block of its own", arena.BlockCount == 2);
        unsafe { arena.Reset(); } // SAFETY: `numbers` isn't used after this.
        Check("Reset", arena.Used == 0);
        Span<byte> stack = stackalloc byte[16];
        stack.Fill(7);
        stack.Slice(4, 4).Clear();
        int stackSum = 0;
        foreach (byte value in stack)
            stackSum += value;
        Check("stackalloc, Fill, Clear", stackSum == 84);
        End("arena and spans");

        Console.WriteLine();
        if (s_failures == 0)
            Console.WriteLine("All checks passed.");
        else
        {
            Console.Write(s_failures);
            Console.WriteLine(" checks FAILED.");
        }
        return s_failures;
    }
}
