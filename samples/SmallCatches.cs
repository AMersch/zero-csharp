// Test: Catch.Run (lib/Catch.cs) sized for a small machine: a throw comes
// back, nested ones, state by ref, and one per fiber. Catches.cs checks
// more, with files and scratch memory, and doesn't fit in a Commodore 64
// (./build.sh --c64). It runs on every target with fibers: not WebAssembly,
// where nothing can jump back up the stack. Exit code = number of failed
// checks.
// Build: ./build.sh SmallCatches

using System;

namespace SmallCatchesSample;

public struct Count
{
    public int Before, After;
    public Exception Again; // one to throw many times: nothing frees exceptions
}

// What a fiber is given: a class, as in SmallFibers, so the program has one
// copy of Fiber.Spawn's code.
public sealed class Turn
{
    public Exception Thrown;    // what its fiber throws
    public Exception Caught;    // what its Catch.Run gave back
}

public static class Program
{
    static int s_failures;

    static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok)
            s_failures++;
    }

    static int Deep(ref Count c, int level)
    {
        if (level == 0)
            throw new InvalidOperationException("deep");
        c.Before++;
        int result = Deep(ref c, level - 1);
        c.After++;
        return result;
    }

    public static int Main()
    {
        var count = new Count();
        Exception e = Catch.Run(ref count, static (ref Count c) => Deep(ref c, 3));
        Check("a throw comes back", e != null && e.Message == "deep");
        Check("and nothing after it ran", count.Before == 3 && count.After == 0);

        e = Catch.Run(ref count, static (ref Count c) =>
        {
            Exception inner = Catch.Run(ref c, static (ref Count d) => throw new FormatException("inner"));
            c.Before = inner is FormatException ? 100 : 0;
        });
        Check("an inner one keeps its exception", e == null && count.Before == 100);

        e = Catch.Run(ref count, static (ref Count c) =>
        {
            throw Catch.Run(ref c, static (ref Count d) => throw new ArgumentException("again"));
        });
        Check("thrown again, it goes out", e is ArgumentException);

        e = Catch.Run(ref count, static (ref Count c) => throw null);
        Check("throw null", e is NullReferenceException);

        // The same exception each time: 200 new ones, never freed, are more
        // than a C64 has memory for.
        count.Before = 0;
        count.Again = new InvalidOperationException("loop");
        for (int i = 0; i < 200; i++)
            if (Catch.Run(ref count, static (ref Count c) => { c.Before++; throw c.Again; }) != count.Again)
                count.Before = -1000;
        Check("200 in a row", count.Before == 200);

        // Each fiber's throw comes back to its own Catch.Run, after it has
        // waited with the other's in between. Everything is made before
        // the spawns: objects made between them would split the little
        // memory a small machine has left for the fibers' stacks.
        Turn[] turns = [new Turn(), new Turn()];
        turns[0].Thrown = new InvalidOperationException("one");
        turns[1].Thrown = new InvalidOperationException("two");
        for (int i = 0; i < turns.Length; i++)
            Fiber.Spawn(turns[i], static t =>
                t.Caught = Catch.Run(ref t, static (ref Turn turn) =>
                {
                    Fiber.Yield();
                    throw turn.Thrown;
                }), stackSize: Fiber.MinStackSize);
        Fiber.Run();
        Check("each fiber's own", turns[0].Caught == turns[0].Thrown && turns[1].Caught == turns[1].Thrown);
        return s_failures;
    }
}
