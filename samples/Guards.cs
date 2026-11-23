// Example: the mistakes lib/ ends the program for, and `throw`, which is a
// panic in zero mode: one per run.
// Build: ./build.sh Guards        Run: ./run.sh Guards <case>
//
// Zero mode has no exceptions, so a guard can only stop the program: a message
// on stderr and a non-zero exit code. Each case below makes one such mistake
// on purpose. test.sh runs them all and checks the message of each against
// tests/expected/Guards.fail, so a guard that stops working is noticed.
//
// Debug.Assert and Debug.Fail only exist where DEBUG is defined (the SDK
// defines it in Debug builds); build.sh doesn't, so this file does.
#define DEBUG

namespace GuardsSample;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Out.Line("Usage: Guards <case>, where the case is one of:");
            Out.Line("  restore-order        an older mark restored before a newer one");
            Out.Line("  restore-other-arena  a mark restored in another arena");
            Out.Line("  restore-after-reset  a mark from before a Reset()");
            Out.Line("  list-after-reset     a list used after its arena was reset (DebugChecks)");
            Out.Line("  list-grew-in-scope   an older list that grew inside a scope, used after it (DebugChecks)");
            Out.Line("  list-grew-in-place   an older list that grew in place inside a scope, used after it (DebugChecks)");
            Out.Line("  builder-grew-in-place  the same for a StrBuilder (DebugChecks)");
            Out.Line("  list-index           a list index out of range");
            Out.Line("  out-of-memory        an allocation the system can't satisfy (stopOnOutOfMemory)");
            Out.Line("  lambda-in-scope      a lambda first run inside AllocateObjectsHere, cached in the arena (DebugChecks)");
            Out.Line("  throw                a throw, inside a try with a matching catch");
            Out.Line("  throw-null           throw null");
            Out.Line("  union-no-case        a switch on a union that holds none of its cases");
            Out.Line("  debug-assert         a Debug.Assert whose condition is false");
            Out.Line("  debug-fail           a Debug.Fail");
            Out.Line("  range-too-long       an array range that ends past the array");
            return 0;
        }

        switch (args[0])
        {
            case "restore-order": RestoreOrder(); break;
            case "restore-other-arena": RestoreOtherArena(); break;
            case "restore-after-reset": RestoreAfterReset(); break;
            case "list-after-reset": ListAfterReset(); break;
            case "list-grew-in-scope": ListGrewInScope(); break;
            case "list-grew-in-place": ListGrewInPlace(); break;
            case "builder-grew-in-place": BuilderGrewInPlace(); break;
            case "list-index": ListIndex(); break;
            case "out-of-memory": OutOfMemory(); break;
            case "lambda-in-scope": LambdaInScope(); break;
            case "throw": Throw(); break;
            case "throw-null": ThrowNull(); break;
            case "union-no-case": UnionNoCase(); break;
            case "debug-assert": DebugAssert(Opaque(0)); break;
            case "debug-fail": DebugFail(); break;
            case "range-too-long": RangeTooLong(Opaque(10)); break;
            default:
                Out.Line("Unknown case");
                return 0;
        }

        // Every case above should have ended the program.
        Out.Line("NOT STOPPED");
        return 0;
    }

    // The unsafe blocks below break their promises on purpose: that is the test.

    static void RestoreOrder()
    {
        // docs:restore-order
        Arena arena = Arena.Create(256, "order");
        Arena.Mark outer = arena.Save();
        arena.AllocArray<byte>(64);
        Arena.Mark inner = arena.Save();
        arena.AllocArray<byte>(64);
        unsafe
        {
            arena.Restore(outer); // also ends `inner`
            arena.Restore(inner);
        }
        // docs:end
    }

    static void RestoreOtherArena()
    {
        Arena first = Arena.Create(256, "first");
        Arena second = Arena.Create(256, "second");
        Arena.Mark mark = first.Save();
        unsafe { second.Restore(mark); }
    }

    static void RestoreAfterReset()
    {
        Arena arena = Arena.Create(256, "reset");
        Arena.Mark mark = arena.Save();
        unsafe
        {
            arena.Reset();
            arena.Restore(mark);
        }
    }

    static void ListAfterReset()
    {
        // docs:list-after-reset
        Arena.DebugChecks = true;
        Arena arena = Arena.Create(256);
        List<int> list = List<int>.Create(arena);
        list.Add(1);
        unsafe { arena.Reset(); }
        list.Add(2);
        // docs:end
    }

    static void ListGrewInScope()
    {
        Arena.DebugChecks = true;
        Arena arena = Arena.Create(256, "lists");
        List<int> list = List<int>.Create(arena, 4);

        Arena.Scope temp;
        unsafe { temp = arena.OpenScope(); }
        using (temp)
        {
            arena.AllocArray<byte>(8); // the list is no longer last, so it can't grow in place
            for (int i = 0; i < 10; i++)
                list.Add(i);           // its new buffer is allocated inside the scope
        }
        list.Get(0);
    }

    static void ListGrewInPlace()
    {
        // docs:grew-in-place
        Arena.DebugChecks = true;
        Arena arena = Arena.Create(256, "in place");
        List<int> list = List<int>.Create(arena, 4);

        Arena.Scope temp;
        unsafe { temp = arena.OpenScope(); }
        using (temp)
        {
            // The list is still the arena's newest allocation, so its buffer
            // is made longer where it is: it starts before the scope's mark
            // and now ends after it.
            for (int i = 0; i < 10; i++)
                list.Add(i);
        }
        list.Get(0);
        // docs:end
    }

    static void BuilderGrewInPlace()
    {
        Arena.DebugChecks = true;
        Arena arena = Arena.Create(256, "builder");
        StrBuilder text = StrBuilder.Create(arena, 4);

        Arena.Scope temp;
        unsafe { temp = arena.OpenScope(); }
        using (temp)
        {
            text.Append("longer than four characters");
        }
        text.Append("x");
    }

    static void ListIndex()
    {
        Arena arena = Arena.Create(256);
        List<int> list = List<int>.Create(arena);
        list.Add(1);
        list.Get(1);
    }

    static void OutOfMemory()
    {
        // docs:out-of-memory
        Arena arena = Arena.Create(256, "small", stopOnOutOfMemory: true);
        // Half the address space: no system has that much to give.
        arena.Alloc(~(nuint)0 / 2);
        // docs:end
    }

    static void LambdaInScope()
    {
        Arena.DebugChecks = true;
        Arena frame = Arena.Create(1024, "frame");
        Arena.ObjectScope objects;
        unsafe { objects = frame.AllocateObjectsHere(); }
        using (objects)
        {
            // The first run of a lambda that captures nothing creates it and
            // caches it in a static field: here, inside the arena.
            System.Func<int, int> twice = static x => x * 2;
            twice(21);
        }
    }

    static void Throw()
    {
        // docs:throw-panic
        try
        {
            throw new System.InvalidOperationException("the queue is empty");
        }
        catch (System.InvalidOperationException)
        {
            // Never runs: the throw has already ended the program.
            Out.Line("caught");
        }
        // docs:end
    }

    static void ThrowNull()
    {
        throw null;
    }

    static int UnionNoCase()
    {
        // docs:union-no-case
        Choice choice = default;  // holds neither a Left nor a Right
        return choice switch
        {
            Left l => l.Value,
            Right r => r.Value,
        };
        // docs:end
    }

    // docs:debug-assert
    static int DebugAssert(int count)
    {
        System.Diagnostics.Debug.Assert(count > 0, "the queue is empty");
        return count - 1;
    }
    // docs:end

    static void DebugFail()
    {
        System.Diagnostics.Debug.Fail("unreachable state");
    }

    static int RangeTooLong(int end)
    {
        int[] numbers = { 1, 2, 3 };
        return numbers[1..end].Length;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Opaque(int value) => value;
}

public struct Left { public int Value; }
public struct Right { public int Value; }
public union Choice(Left, Right);
