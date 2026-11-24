// Arena.Scratch: temporary memory whose lifetime the compiler checks.
//
//   int sum = Arena.Scratch(static (scoped ScratchScope s) =>
//   {
//       Span<int> buffer = s.AllocSpan<int>(1000);
//       ...
//       return total;
//   });                    // buffer's memory is reused from here on
//
// Everything allocated through the scope is freed when the callback returns,
// and the compiler makes sure nothing from it is still reachable then: the
// scope is a ref struct passed as a scoped parameter, so a span or ref it
// gives out can't be returned, stored in a field or a static, put in the
// state, or kept in a variable that outlives the callback. Each of those is a
// compile error, not a dangling pointer. No unsafe block is needed anywhere.
//
// The memory comes from a scratch arena of the thread's own, not from one of
// yours, so a List or StrBuilder of yours that grows during the callback grows
// in its own arena and is unaffected (with Arena.OpenScope it would grow into
// memory the scope then frees). Scratch calls nest. Each thread keeps its
// scratch arena for its whole life, sized to the most it ever needed at once.
//
// To pass data in and out without a closure (which would allocate), use the
// overloads with a state passed by ref; it may be a ref struct holding spans
// of the caller's.

using System;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>Allocates memory that lives until the Arena.Scratch callback it was given to returns.</summary>
public ref struct ScratchScope
{
    readonly Arena _arena;

    internal ScratchScope(Arena arena) => _arena = arena;

    /// <summary>A zeroed span of <paramref name="count"/> items, valid until the callback returns.</summary>
    public Span<T> AllocSpan<T>(int count) where T : unmanaged
    {
        // SAFETY: the span points into the scratch arena, which Arena.Scratch
        // restores only after the callback returns; the compiler keeps the
        // span from outliving the callback (this scope is a scoped parameter).
        unsafe { return _arena.AllocSpan<T>(count); }
    }

    /// <summary>A zeroed T, valid until the callback returns.</summary>
    public ref T Alloc<T>() where T : unmanaged
    {
        unsafe
        {
            // SAFETY: as in AllocSpan. The scratch arena stops the program
            // rather than return null.
            T* item = _arena.Alloc<T>();
            return ref *item;
        }
    }
}

public delegate void ScratchAction(scoped ScratchScope scratch);

public delegate void ScratchAction<TState>(scoped ScratchScope scratch, scoped ref TState state)
    where TState : allows ref struct;

public delegate TResult ScratchFunc<TResult>(scoped ScratchScope scratch);

public delegate TResult ScratchFunc<TState, TResult>(scoped ScratchScope scratch, scoped ref TState state)
    where TState : allows ref struct;

public partial struct Arena
{
    // Each thread's scratch arena, behind a pthread key (zero mode has no
    // [ThreadStatic]). The key's destructor frees it when the thread ends.
    static nuint s_scratchKey;
    static int s_scratchKeyState; // 0 none, 1 being created, 2 ready

    /// <summary>
    /// Runs <paramref name="action"/> with a scope to allocate temporary memory
    /// from, and frees it all when the action returns. The compiler stops
    /// anything allocated from the scope from escaping the action.
    /// </summary>
    public static void Scratch(ScratchAction action)
    {
        Mark mark = EnterScratch(out Arena arena);
        action(new ScratchScope(arena));
        LeaveScratch(arena, mark);
    }

    /// <summary>The same, with a state the action can read and change, passed by ref.</summary>
    public static void Scratch<TState>(ref TState state, ScratchAction<TState> action)
        where TState : allows ref struct
    {
        Mark mark = EnterScratch(out Arena arena);
        action(new ScratchScope(arena), ref state);
        LeaveScratch(arena, mark);
    }

    /// <summary>The same, returning the action's result, which can't come from the scope's memory.</summary>
    public static TResult Scratch<TResult>(ScratchFunc<TResult> func)
    {
        Mark mark = EnterScratch(out Arena arena);
        TResult result = func(new ScratchScope(arena));
        LeaveScratch(arena, mark);
        return result;
    }

    /// <summary>The same, with a state and a result.</summary>
    public static TResult Scratch<TState, TResult>(ref TState state, ScratchFunc<TState, TResult> func)
        where TState : allows ref struct
    {
        Mark mark = EnterScratch(out Arena arena);
        TResult result = func(new ScratchScope(arena), ref state);
        LeaveScratch(arena, mark);
        return result;
    }

    static Mark EnterScratch(out Arena arena)
    {
        if (Volatile.Read(ref s_scratchKeyState) != 2)
            CreateScratchKey();
        unsafe
        {
            // SAFETY: the key holds null or the state of an arena this thread
            // created below and hasn't destroyed.
            State* state = (State*)Platform.pthread_getspecific(s_scratchKey);
            if (state == null)
            {
                arena = Create(16 * 1024, "scratch", stopOnOutOfMemory: true);
                if (Platform.pthread_setspecific(s_scratchKey, arena._state) != 0)
                    Environment.FailFast("Arena.Scratch: can't keep the thread's scratch arena");
            }
            else
            {
                arena = new Arena { _state = state };
            }
        }
        return arena.Save();
    }

    static void CreateScratchKey()
    {
        if (Interlocked.CompareExchange(ref s_scratchKeyState, 1, 0) == 0)
        {
            nuint key = 0;
            unsafe
            {
                // SAFETY: DestroyScratch has the signature a pthread key destructor needs.
                if (Platform.pthread_key_create(&key, (void*)(delegate* unmanaged<void*, void>)&DestroyScratch) != 0)
                    Environment.FailFast("Arena.Scratch: no thread-local key");
            }
            s_scratchKey = key;
            Volatile.Write(ref s_scratchKeyState, 2);
        }
        while (Volatile.Read(ref s_scratchKeyState) != 2)
        {
            // another thread is creating it
        }
    }

    // Called by the system when a thread with a scratch arena ends.
    [UnmanagedCallersOnly]
    static void DestroyScratch(void* state)
    {
        var arena = new Arena { _state = (State*)state };
        unsafe { arena.Destroy(); } // SAFETY: the thread has ended; nothing of it uses its scratch memory.
    }

    static void LeaveScratch(Arena arena, Mark mark)
    {
        // SAFETY: only ScratchScope allocates from the scratch arena, and the
        // compiler kept everything it gave out inside the callback, which has
        // returned. Nested Scratch calls restore in the reverse order.
        unsafe { arena.Restore(mark); }
    }
}
