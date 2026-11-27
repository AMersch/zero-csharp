// Parallel.For: a loop whose iterations run on several threads at once, on
// the system's threads (pthreads; win/runtime.c maps them to Windows threads).
//
//   Parallel.For(0, rows, matrix, static (m, row) => m.ScaleRow(row));
//
// The calling thread works too, and For returns when every iteration is done.
// A throw in an iteration ends the program, whichever thread it's on and
// even inside a Catch.Run; a Catch.Run inside the iteration catches as usual.
// Iterations are handed out in chunks from a shared counter, so a thread that
// finishes early takes more. Starting the workers allocates nothing: the job
// lives on the caller's stack, which outlives them because For waits.
//
// Where there are no threads (WebAssembly, DOS), only one processor (UEFI), or
// a thread can't be started, the loop simply runs on the calling thread.
//
// What runs in parallel must be safe to: iterations may only write what no
// other iteration touches, or use Interlocked. Arenas and lib's collections
// aren't thread-safe: to give each thread its own, split the work into
// ProcessorCount parts, For(0, Parallel.ProcessorCount, ...), and let each
// part create its arena. Objects allocated with new are fine:
// zerocore's allocator is thread-safe, and an arena's AllocateObjectsHere
// applies only to the thread that opened it. Static constructors are not
// thread-safe: touch a type's statics once before the loop.

using System;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>Loops whose iterations run on several threads.</summary>
public static class Parallel
{
    [DllImport("libc")]
    static unsafe extern int pthread_create(nint* thread, void* attributes, delegate* unmanaged<void*, void*> start, void* argument);

    [DllImport("libc")]
    static unsafe extern int pthread_join(nint thread, void** result);

    [DllImport("libc")]
    static safe extern int get_nprocs();

    // The most threads one For uses, the calling thread included.
    const int MaxThreads = 64;

    /// <summary>The number of processors, and so of threads For uses: at least 1.</summary>
    public static int ProcessorCount
    {
        get
        {
            int count = get_nprocs();
            return count < 1 ? 1 : count > MaxThreads ? MaxThreads : count;
        }
    }

    // Shared by the threads of one For. Lives on the caller's stack.
    struct Job
    {
        public int Next;      // the first iteration not yet handed out
        public int End;
        public int Chunk;
        public delegate*<Job*, int, int, void> RunRange; // For<TState>'s instantiation
        public void* State;   // the caller's TState, by address
        public object Body;   // the caller's Action<TState, int>
    }

    /// <summary>
    /// Runs <paramref name="body"/>(state, i) for every i from
    /// <paramref name="from"/> up to but not including <paramref name="to"/>,
    /// on up to ProcessorCount threads, and returns when all have run. Pass
    /// what the iterations need as <paramref name="state"/> and use a static
    /// lambda, so nothing is captured or allocated. A throw in an iteration
    /// ends the program, also inside a Catch.Run; a Catch.Run inside the
    /// iteration works.
    /// </summary>
    public static void For<TState>(int from, int to, TState state, Action<TState, int> body)
    {
        if (to <= from)
            return;
        int count = to - from;
        int threads = ProcessorCount;
        if (threads > count)
            threads = count;

        Job job = default;
        job.Next = from;
        job.End = to;
        // Several chunks per thread, so uneven iterations even out.
        job.Chunk = Math.Max(1, count / (threads * 8));
        job.Body = body;
        unsafe
        {
            // SAFETY: job and state live until every worker has been joined
            // below, and For's callers can't see the pointers.
            job.RunRange = &RunRange<TState>;
            job.State = &state;
            Job* shared = &job;

            nint* workers = stackalloc nint[MaxThreads];
            int started = 0;
            for (int i = 1; i < threads; i++)
            {
                if (pthread_create(&workers[started], null, &Worker, shared) != 0)
                    break; // no threads here, or no more: the rest runs below
                started++;
            }

            // A throw in an iteration ends the program, also on this thread
            // and also inside a Catch.Run (Catch.cs), whose marks are put
            // away while the loop runs: a jump out of here would leave the
            // workers with a job and a state on a stack that has moved on.
            // (The pointer is null in a program that never calls Catch.Run.)
            delegate*<void*, void*> swapMarks = System.Runtime.CatchFrames.s_swap;
            void* marks = swapMarks != null ? swapMarks(null) : null;

            Work(shared);

            for (int i = 0; i < started; i++)
                pthread_join(workers[i], null);
            if (swapMarks != null)
                swapMarks(marks);
        }
    }

    // Takes chunks until none are left.
    static unsafe void Work(Job* job)
    {
        // SAFETY (this method and the two below): job points at For's Job,
        // which For keeps alive until every worker has been joined.
        unsafe
        {
            int end = job->End;
            int chunk = job->Chunk;
            while (true)
            {
                int start = Interlocked.Add(ref job->Next, chunk) - chunk;
                if (start >= end)
                    return;
                job->RunRange(job, start, Math.Min(start + chunk, end));
            }
        }
    }

    [UnmanagedCallersOnly]
    static unsafe void* Worker(void* job)
    {
        unsafe { Work((Job*)job); }
        return null;
    }

    static unsafe void RunRange<TState>(Job* job, int start, int end)
    {
        Action<TState, int> body;
        TState state;
        unsafe
        {
            body = System.Runtime.CompilerServices.Unsafe.As<Action<TState, int>>(job->Body);
            state = *(TState*)job->State;
        }
        for (int i = start; i < end; i++)
            body(state, i);
    }
}
