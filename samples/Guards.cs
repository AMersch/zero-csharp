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
            Out.Line("  bad-base64           Convert.FromBase64String on text that isn't Base64");
            Out.Line("  bad-pattern          Regex.Create with a pattern that isn't valid");
            Out.Line("  scratch-outer-scope  an Arena.Scratch scope allocating inside a nested one");
            Out.Line("  scratch-outer-list   a ScratchList added to inside a nested Arena.Scratch");
            Out.Line("  scratch-outer-text   a ScratchText appended to inside a nested Arena.Scratch");
            Out.Line("  ptr-null             the Value of a null Ptr");
            Out.Line("  ptr-after-reset      a Ptr used after its arena was reset (DebugChecks)");
            Out.Line("  pool-removed         a Pool handle used after its item was removed");
            Out.Line("  pool-moved           a copy of a Pool handle used after Move");
            Out.Line("  scratch-pool-outer   a ScratchPool added to inside a nested Arena.Scratch");
            Out.Line("  pool-other           a Pool handle used with another pool");
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
            case "bad-base64": System.Convert.FromBase64String("not base64!"); break;
            case "bad-pattern": Regex.Create(Arena.Create(4096), "(abc"); break;
            case "scratch-outer-scope": ScratchOuterScope(); break;
            case "scratch-outer-list": ScratchOuterList(); break;
            case "scratch-outer-text": ScratchOuterText(); break;
            case "ptr-null": PtrNull(); break;
            case "ptr-after-reset": PtrAfterReset(); break;
            case "pool-removed": PoolRemoved(); break;
            case "pool-moved": PoolMoved(); break;
            case "scratch-pool-outer": ScratchPoolOuter(); break;
            case "pool-other": PoolOther(); break;
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

    // The compiler accepts these, as the state passed by ref may hold the outer
    // scope, list or text; what they allocate would be freed with the inner
    // call's memory, so it stops the program instead.
    static void ScratchOuterScope()
    {
        Arena.Scratch(static (scoped ScratchScope scratch) =>
        {
            var outer = new OuterScratch { Scope = scratch };
            Arena.Scratch(ref outer, static (scoped ScratchScope inner, scoped ref OuterScratch state) =>
            {
                state.Scope.AllocSpan<int>(4);
            });
        });
    }

    static void ScratchOuterList()
    {
        Arena.Scratch(static (scoped ScratchScope scratch) =>
        {
            var outer = new OuterScratch { Items = scratch.List<int>() };
            Arena.Scratch(ref outer, static (scoped ScratchScope inner, scoped ref OuterScratch state) =>
            {
                state.Items.Add(1);
            });
        });
    }

    static void ScratchOuterText()
    {
        Arena.Scratch(static (scoped ScratchScope scratch) =>
        {
            var outer = new OuterScratch { Text = scratch.Text() };
            Arena.Scratch(ref outer, static (scoped ScratchScope inner, scoped ref OuterScratch state) =>
            {
                state.Text.Append($"{42}");
            });
        });
    }

    static void PtrNull()
    {
        Ptr<int> nothing = default;
        nothing.Value = 1;
    }

    static void PtrAfterReset()
    {
        Arena.DebugChecks = true;
        Arena arena = Arena.Create(256, "links");
        Ptr<int> item = arena.New(7);
        unsafe { arena.Reset(); } // SAFETY: none, on purpose: item is used below.
        item.Value = 8;
    }

    static void PoolRemoved()
    {
        var pool = Pool<int>.Create(Arena.Create(4096));
        Handle<int> item = pool.Add(1);
        pool.Remove(item);
        pool.Add(2); // the same slot, a new generation
        pool[item] = 3;
    }

    static void PoolMoved()
    {
        var pool = Pool<int>.Create(Arena.Create(4096));
        Handle<int> item = pool.Add(1);
        Handle<int> copy = item;
        pool.Move(ref item);
        pool[copy] = 2;
    }

    static void PoolOther()
    {
        Arena arena = Arena.Create(4096);
        var first = Pool<int>.Create(arena);
        var second = Pool<int>.Create(arena);
        second.Add(0);
        Handle<int> item = first.Add(1); // slot 0, generation 1, as second's item
        second[item] = 2;
    }

    static void ScratchPoolOuter()
    {
        Arena.Scratch(static (scoped ScratchScope scratch) =>
        {
            var outer = new OuterScratch { Pool = scratch.Pool<int>() };
            Arena.Scratch(ref outer, static (scoped ScratchScope inner, scoped ref OuterScratch state) =>
            {
                state.Pool.Add(1);
            });
        });
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Opaque(int value) => value;
}

public ref struct OuterScratch
{
    public ScratchScope Scope;
    public ScratchList<int> Items;
    public ScratchText Text;
    public ScratchPool<int> Pool;
}

public struct Left { public int Value; }
public struct Right { public int Value; }
public union Choice(Left, Right);
