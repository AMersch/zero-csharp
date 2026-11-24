// Example: Arena.Scratch (lib/Scratch.cs), temporary memory whose lifetime the
// compiler checks. Exit code = number of failed checks.
// Build: ./build.sh Scratch
//
// Everything a ScratchScope gives out is freed when the callback returns, and
// the compiler refuses any way to keep it longer: tests/must-not-compile has
// one program per way, which test.sh checks the compiler rejects. So unlike
// Arena.OpenScope, nothing here is unsafe.

using System;
using System.Runtime.CompilerServices;

namespace ScratchSample;

// docs:state
// What Arena.Scratch passes to its callback by ref: the data going in.
public ref struct Job
{
    public ReadOnlySpan<int> Input;
}
// docs:end

public struct Growing
{
    public List<int> Items;
}

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

    // Where a span's memory is, as a number: enough to compare two of them.
    static nint Address(Span<int> span)
    {
        unsafe { return (nint)Unsafe.AsPointer(ref span[0]); } // SAFETY: the address is only compared.
    }

    static int ScratchLength()
        => Arena.Scratch(static (scoped ScratchScope scratch) => scratch.AllocSpan<int>(10).Length);

    public static int Main()
    {
        // docs:scratch
        int[] scores = { 5, 3, 8, 1 };
        var job = new Job { Input = scores };
        int median = Arena.Scratch(ref job,
            static (scoped ScratchScope scratch, scoped ref Job state) =>
            {
                // A copy to sort, so the caller's array keeps its order.
                Span<int> copy = scratch.AllocSpan<int>(state.Input.Length);
                state.Input.CopyTo(copy);
                copy.Sort();
                return copy[copy.Length / 2];
            });
        // docs:end
        Check("a sorted scratch copy", median == 5 && scores[0] == 5);
        int written = Arena.Scratch(static (scoped ScratchScope scratch) =>
        {
            ref int value = ref scratch.Alloc<int>();
            bool zeroed = value == 0;
            value = 42;
            return zeroed ? value : -1;
        });
        Check("Alloc gives a zeroed T to write through", written == 42);
        Section("using it");

        nint firstAddress = Arena.Scratch(static (scoped ScratchScope scratch) => Address(scratch.AllocSpan<int>(64)));
        nint secondAddress = Arena.Scratch(static (scoped ScratchScope scratch) => Address(scratch.AllocSpan<int>(64)));
        Check("the memory is reused", firstAddress == secondAddress);
        int outer = Arena.Scratch(static (scoped ScratchScope scratch) =>
        {
            Span<int> mine = scratch.AllocSpan<int>(4);
            mine[0] = 7;
            Arena.Scratch(static (scoped ScratchScope inner) => { inner.AllocSpan<int>(1000).Fill(9); });
            return mine[0];
        });
        Check("calls nest", outer == 7);
        // The lambda's delegate is made on its first call and kept; after
        // that, a call allocates nothing.
        int sum = ScratchLength();
        long before = GC.GetTotalAllocatedBytes();
        for (int i = 0; i < 100; i++)
            sum += ScratchLength();
        Check("nothing allocated", sum == 1010 && GC.GetTotalAllocatedBytes() == before);
        Section("its memory");

        // docs:growing
        // A list of yours that grows during the callback grows in its own
        // arena, so the scratch memory going away doesn't touch it.
        Arena arena = Arena.Create(4096);
        Arena.DebugChecks = true;
        var growing = new Growing { Items = List<int>.Create(arena, 4) };
        Arena.Scratch(ref growing,
            static (scoped ScratchScope scratch, scoped ref Growing state) =>
            {
                Span<int> temp = scratch.AllocSpan<int>(100);
                for (int i = 0; i < temp.Length; i++)
                    state.Items.Add(i);
            });
        Check("your list is intact",
            growing.Items.Count == 100 && growing.Items.Get(99) == 99);
        // docs:end
        Arena.DebugChecks = false;
        Section("your arenas");

        Out.Line();
        if (s_failures == 0)
            Out.Line("All checks passed.");
        else
            Out.Line($"{s_failures} checks FAILED.");
        return s_failures;
    }
}
