// Test: async/await and Task on fibers (lib/Tasks.cs). An async method runs
// on its caller's stack until it awaits something not done, then carries on
// on a fiber of its own while the caller continues; awaiting in Main runs
// the fibers until the task is done. Not on WebAssembly, which has no fibers.
// Exit code = number of failed checks.
//
// `Tasks canceled` awaits a canceled task plainly, `Tasks throw` calls
// ThrowIfCancellationRequested on a canceled token, `Tasks closed` awaits
// ReceiveAsync on a closed channel, and `Tasks twice` awaits a timer's
// ValueTask after the timer has reused it: each must end the program with
// a message. test.sh checks them.
// Build: ./build.sh Tasks

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace TasksSample;

public static class Program
{
    static int s_failures;
    static int s_sectionChecks;
    static int s_sectionFailures;
    static string s_log = "";

    static void End(string section)
    {
        Out.Write(s_sectionFailures == 0 ? "PASS " : "FAIL ");
        Out.Write(section);
        Out.Write(" (");
        Out.Write(s_sectionChecks);
        Out.Line(" checks)");
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
            Out.Line($"  failed: {name}");
        }
    }

    static long Milliseconds(long since) => (Clock.Monotonic - since) / 1_000_000;

    // docs:order
    static async Task<int> Twice(string name, int value, int delay)
    {
        s_log += name + "1 ";
        await Task.Delay(delay);
        s_log += name + "2 ";
        return value * 2;
    }
    // docs:end

    static async Task<int> NoWait(int value)
    {

        return value + 1;
    }

    static async Task Steps(string name, int count)
    {
        for (int i = 0; i < count; i++)
        {
            s_log += name;
            await Task.Yield();
        }
    }

    static async Task<int> Sum(int n) => n == 0 ? 0 : n + await Sum(n - 1);

    static async Task<int> SumWaiting(int n)
    {
        if (n == 0)
            return 0;
        await Task.Yield();
        return n + await SumWaiting(n - 1);
    }

    static async Task<string> Echo(TaskCompletionSource<string> source) => "got " + await source.Task;

    // Blocks on a channel after its first await: the dispatcher running it
    // hands its other continuations to a new dispatcher.
    static async Task<int> Receiver(Channel<int> channel)
    {
        await Task.Yield();
        int total = 0;
        while (channel.Receive(out int value))
            total += value;
        return total;
    }

    // docs:own-fiber
    // A fiber of its own from the first await to the end, for a method
    // that mostly blocks.
    [AsyncMethodBuilder(typeof(OwnFiberMethodBuilder<>))]
    static async Task<int> Sleeper(int times)
    {
        await Task.Yield();
        for (int i = 0; i < times; i++)
            Fiber.Sleep(1);
        return times;
    }
    // docs:end

    [AsyncMethodBuilder(typeof(OwnFiberMethodBuilder))]
    static async Task OwnFiberSteps(string name, int count)
    {
        for (int i = 0; i < count; i++)
        {
            s_log += name;
            await Task.Yield();
        }
    }

    static async Task<int> One(int i)
    {
        await Task.Delay(20);
        return i;
    }

    static int s_voidCount;

    static async void Fire(int delay)
    {
        await Task.Delay(delay);
        s_voidCount++;
    }

    // docs:cancel-loop
    // Works until canceled, checking the token between steps; returns how
    // many steps it did.
    static async Task<int> Work(CancellationToken token)
    {
        int steps = 0;
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(5);
            steps++;
        }
        return steps;
    }
    // docs:end

    static async ValueTask<int> Quick(int i) => i + 1;

    static async ValueTask<int> Slow(int i)
    {
        await Task.Delay(1);
        return i + 1;
    }

    static async ValueTask Pause() => await Task.Delay(1);

    // docs:channel-async
    // Sends 1..10 and closes; the reader's await foreach ends there.
    static async Task Produce(Channel<int> channel)
    {
        for (int i = 1; i <= 10; i++)
            await channel.SendAsync(i);
        channel.Close();
    }

    static async Task<int> Consume(Channel<int> channel)
    {
        int sum = 0;
        await foreach (int value in channel.ReceiveAllAsync())
            sum += value;
        return sum;
    }
    // docs:end

    static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "canceled")
        {
            await Task.FromCanceled(new CancellationToken(true));
            return 0;
        }
        if (args.Length > 0 && args[0] == "closed")
        {
            var closed = new Channel<int>();
            closed.Close();
            await closed.ReceiveAsync();
            return 0;
        }
        if (args.Length > 0 && args[0] == "twice")
        {
            // Long enough that the second wait never finds a tick already
            // come (slow emulators): it has to wait, so it reuses the task.
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            ValueTask<bool> once = timer.WaitForNextTickAsync();
            await once;
            await timer.WaitForNextTickAsync();
            await once; // its task has been reused
            return 0;
        }
        if (args.Length > 0 && args[0] == "throw")
        {
            new CancellationToken(true).ThrowIfCancellationRequested();
            return 0;
        }

        // Main runs on the program's own stack until its first await that
        // waits (then on the dispatcher, like any async method): Fiber.Run
        // can be called here. A canceled delay leaves the timer, so Run
        // doesn't wait the 5 s for it. Checked in Cancellation.
        var early = new CancellationTokenSource();
        Task longDelay = Task.Delay(5_000, early.Token);
        early.Cancel();
        long runStart = Clock.Monotonic;
        Fiber.Run();
        bool runDidntWait = Milliseconds(runStart) < 1000 && longDelay.IsCanceled;

        s_log = "";
        // docs:await
        // Each call runs until its first await, then returns, and the
        // caller goes on: a and b wait at the same time, on fibers.
        Task<int> a = Twice("a", 1, 30), b = Twice("b", 2, 10);
        s_log += "main ";
        // docs:end
        Check("caller goes on at the first await", s_log == "a1 b1 main ");
        Check("not done yet", !a.IsCompleted && !b.IsCompleted);
        int sum = await a + await b;
        Check("results", sum == 6);
        Check("b finishes first", s_log == "a1 b1 main b2 a2 ");
        End("Order");

        // A method that doesn't wait completes before it returns, with no fiber.
        s_log = "";
        int fibers = Fiber.Count;
        Task<int> n = NoWait(41);
        Check("completed at once", n.IsCompleted && n.Result == 42);
        Check("no fiber", Fiber.Count == fibers);
        Check("await a completed task", await n == 42);
        Check("CompletedTask", Task.CompletedTask.IsCompleted && Task.FromResult(7).Result == 7);
        Check("Delay(0) is done", Task.Delay(0).IsCompleted);
        End("Synchronous");

        long start = Clock.Monotonic;
        // docs:when-all
        // Three delays of 50 ms side by side take about 50 ms, not 150.
        int[] all = await Task.WhenAll(
            Twice("x", 1, 50), Twice("y", 2, 50), Twice("z", 3, 50));
        // docs:end
        long ms = Milliseconds(start);
        Check("WhenAll results in order", all.Length == 3 && all[0] == 2 && all[1] == 4 && all[2] == 6);
        Check($"WhenAll took {ms} ms, under 140", ms >= 49 && ms < 140);
        Task first = await Task.WhenAny(Task.Delay(200), Task.Delay(20));
        Check("WhenAny", first.IsCompleted);
        Task<int> slow = Twice("s", 1, 200), fast = Twice("f", 5, 10);
        Check("WhenAny<T> gives the first", await await Task.WhenAny(slow, fast) == 10);
        await Task.WhenAll(Task.Delay(1), Task.CompletedTask);
        Check("WhenAll()", (await Task.WhenAll(new Task<int>[0])).Length == 0);
        End("WhenAll and WhenAny");

        // Task.Yield lets the others run: two loops interleave.
        s_log = "";
        await Task.WhenAll(Steps("a", 3), Steps("b", 3));
        Check($"interleaved: {s_log}", s_log == "abababa" || s_log == "ababab");
        End("Yield");

        // Task.Run starts a fiber.
        Task<int> run = Task.Run(() => 6 * 7);
        Check("Task.Run<T>", await run == 42);
        int counter = 0;
        Task<int> runAsync = Task.Run(async () =>
        {
            await Task.Delay(5);
            return 9;
        });
        Check("Task.Run async", await runAsync == 9);
        await Task.Run(() => { counter++; });
        Check("Task.Run Action", counter == 1);
        End("Task.Run");

        // A task completed by hand wakes its awaiters.
        var source = new TaskCompletionSource<string>();
        Task<string> echo = Echo(source);
        Check("waiting", !echo.IsCompleted);
        _ = Task.Run(async () =>
        {
            await Task.Delay(5);
            source.SetResult("it");
        });
        Check("TaskCompletionSource", await echo == "got it");
        Check("TrySetResult after", !source.TrySetResult("again"));
        End("TaskCompletionSource");

        // Recursion: each level that waits has a box of its own.
        // Without waiting, the calls nest on this stack: fewer on a
        // microcontroller, whose stack is a few KB.
        bool small = Fiber.DefaultStackSize < 64 * 1024;
        Check("Sum without waiting", await Sum(small ? 10 : 100) == (small ? 55 : 5050));
        fibers = Fiber.Count;
        Task<int> deep = SumWaiting(1000);
        Check($"waiting at 1000 levels: {Fiber.Count - fibers} more fibers", Fiber.Count <= fibers + 1);
        Check("Sum waiting at every level", await deep == 500500);
        End("Recursion");

        // Many at once: a box and two tasks each, and two fibers in all
        // (the timer's and the dispatcher's). 100 on a small machine.
        int many = small ? 100 : 10_000;
        long before = GC.GetTotalAllocatedBytes();
        var waiting = new Task<int>[many];
        for (int i = 0; i < many; i++)
            waiting[i] = One(i);
        long perCall = (GC.GetTotalAllocatedBytes() - before) / many;
        Check($"{many} waiting: {Fiber.Count} fibers", Fiber.Count <= fibers + 2);
        Check($"{perCall} bytes each, under 400", perCall < 400);
        long manySum = 0;
        foreach (int r in await Task.WhenAll(waiting))
            manySum += r;
        Check("all done", manySum == (long)many * (many - 1) / 2);
        End("Many");

        // Wait and Result block the caller: in a fiber, only that fiber.
        Task<int> later = Twice("w", 4, 10);
        Check("Result waits", later.Result == 8);
        Task delay = Task.Delay(10);
        delay.Wait();
        Check("Wait", delay.IsCompleted);
        Task<int> inFiber = Task.Run(() => Twice("v", 3, 10).Result);
        Check("Result in a fiber", await inFiber == 6);
        End("Wait");

        // Async code and channels mix: an async method on its fiber receives.
        var channel = new Channel<int>();
        Task<int> received = Receiver(channel);
        Fiber.Spawn(channel, static ch =>
        {
            for (int i = 1; i <= 10; i++)
                ch.Send(i);
            ch.Close();
        });
        Check("channel total", await received == 55);
        Task<int> sleeper = Sleeper(3);
        Check("own fiber", await sleeper == 3);
        s_log = "";
        await Task.WhenAll(OwnFiberSteps("a", 3), Steps("b", 3));
        Check($"own fiber and dispatcher interleave: {s_log}", s_log.Length == 6 && s_log.StartsWith("ab"));
        End("Blocking");

        // ConfigureAwait changes nothing; async void runs to its end.
        Check("ConfigureAwait", await Twice("c", 2, 1).ConfigureAwait(false) == 4);
        Fire(5);
        Fire(1);
        await Task.Delay(30);
        Check("async void", s_voidCount == 2);
        End("Odds and ends");

        // docs:cancel
        // A delay of 10 s, canceled after 20 ms: SuppressThrowing makes the
        // await return (a plain await would end the program).
        using (var cts = new CancellationTokenSource(20))
        {
            Task pause = Task.Delay(10_000, cts.Token);
            await pause.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            Console.WriteLine($"canceled: {pause.IsCanceled}");
        }
        // docs:end
        start = Clock.Monotonic;
        using (var cts = new CancellationTokenSource(20))
        {
            Task pause = Task.Delay(10_000, cts.Token);
            await pause.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            Check("delay canceled", pause.IsCanceled && pause.Status == TaskStatus.Canceled && !pause.IsCompletedSuccessfully);
        }
        Check($"canceled in {Milliseconds(start)} ms, under 1000", Milliseconds(start) < 1000);

        Check("Run doesn't wait for a canceled delay", runDidntWait);
        var tokens = new CancellationTokenSource();
        Check("Delay(-1) waits for the token", Task.Delay(-1, new CancellationToken(true)).IsCanceled);

        // Callbacks: the last registered first; a removed one doesn't run.
        tokens = new CancellationTokenSource();
        string calls = "";
        tokens.Token.Register(() => calls += "a");
        var removed = tokens.Token.Register(() => calls += "x");
        tokens.Token.Register(s => calls += (string)s!, "b");
        removed.Dispose();
        Check("not yet", calls == "" && !tokens.IsCancellationRequested);
        tokens.Cancel();
        tokens.Cancel();
        Check($"callbacks: {calls}", calls == "ba" && tokens.Token.IsCancellationRequested);
        tokens.Token.Register(() => calls += "c");
        Check("registered after: runs at once", calls == "bac");
        Check("tokens", CancellationToken.None == default && !CancellationToken.None.CanBeCanceled && new CancellationToken(true).IsCancellationRequested);

        // A linked tokens is canceled with any of its parents.
        var parent1 = new CancellationTokenSource();
        var parent2 = new CancellationTokenSource();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(parent1.Token, parent2.Token);
        parent2.Cancel();
        Check("linked", linked.IsCancellationRequested && !parent1.IsCancellationRequested);

        // A loop that checks its token.
        var stop = new CancellationTokenSource(30);
        int steps = await Work(stop.Token);
        Check($"worked {steps} steps", steps >= 1);

        // Canceled tasks: by hand, through WhenAll, and before Task.Run starts.
        var manual = new TaskCompletionSource<int>();
        manual.SetCanceled();
        Check("TrySetResult after cancel", !manual.TrySetResult(1) && manual.Task.IsCanceled);
        Task allOf = Task.WhenAll(Task.Delay(5), manual.Task);
        await allOf.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Check("WhenAll canceled", allOf.IsCanceled);
        bool ran = false;
        Task notRun = Task.Run(() => { ran = true; }, new CancellationToken(true));
        await notRun.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Check("Task.Run canceled first", notRun.IsCanceled && !ran);

        // WaitAsync gives up gaveUp; the task it waited for goes on.
        var never = new TaskCompletionSource<int>();
        tokens = new CancellationTokenSource(10);
        Task<int> gaveUp = never.Task.WaitAsync(tokens.Token);
        await ((Task)gaveUp).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Check("WaitAsync canceled", gaveUp.IsCanceled && !never.Task.IsCompleted);
        never.SetResult(5);
        Check("WaitAsync with time to spare", await Twice("q", 3, 1).WaitAsync(new CancellationTokenSource().Token) == 6);
        Check("ForceYielding a done task", await Task.FromResult(3).ConfigureAwait(ConfigureAwaitOptions.ForceYielding) == 3);
        End("Cancellation");

        // Fibers' own waits take a token too.
        tokens = new CancellationTokenSource(10);
        Task<bool> slept = Task.Run(() => Fiber.Sleep(5_000, tokens.Token));
        start = Clock.Monotonic;
        Check("Sleep canceled", !await slept && Milliseconds(start) < 1000);
        Check("Sleep not canceled", await Task.Run(() => Fiber.Sleep(1, new CancellationTokenSource().Token)));
        var ints = new Channel<int>();
        tokens = new CancellationTokenSource(10);
        Task<bool> gotOne = Task.Run(() => ints.Receive(out _, tokens.Token));
        Check("Receive canceled", !await gotOne);
        Task<int> receiver = Task.Run(() => ints.Receive(out int v, new CancellationTokenSource().Token) ? v : -1);
        Fiber.Spawn(ints, static ch => ch.Send(7));
        Check("the channel still works", await receiver == 7);
        End("Fibers and tokens");

        // ValueTask: a method that doesn't wait allocates nothing.
        long allocated = GC.GetTotalAllocatedBytes();
        int quick = 0;
        for (int i = 0; i < 100; i++)
            quick += await Quick(i);
        long valueTaskBytes = GC.GetTotalAllocatedBytes() - allocated;
        Check($"ValueTask without waiting: {valueTaskBytes} bytes", valueTaskBytes == 0 && quick == 5050);
        Check("ValueTask<T> waiting", await Slow(41) == 42);
        ValueTask paused = Pause();
        await paused;
        Check("ValueTask waiting", paused.IsCompletedSuccessfully);
        Check("AsTask", await Slow(1).AsTask() == 2 && Quick(1).AsTask().Result == 2);
        Check("FromResult", await ValueTask.FromResult(3) == 3 && ValueTask.CompletedTask.IsCompleted);
        End("ValueTask");

        // docs:periodic-async
        // Ticks every 10 ms without a fiber of its own; Dispose ends the loop.
        using (var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10)))
        {
            int ticks = 0;
            while (await timer.WaitForNextTickAsync())
                if (++ticks == 3)
                    timer.Dispose();
            Console.WriteLine($"ticks: {ticks}");
        }
        // docs:end
        start = Clock.Monotonic;
        int ticked = 0;
        using (var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10)))
        {
            while (await timer.WaitForNextTickAsync())
                if (++ticked == 3)
                    timer.Dispose();
        }
        long tickMs = Milliseconds(start);
        Check($"3 ticks in {tickMs} ms", ticked == 3 && tickMs >= 29 && tickMs < 500);
        var slowTimer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            slowTimer.Dispose();
        });
        start = Clock.Monotonic;
        Check("Dispose ends a wait with false", !await slowTimer.WaitForNextTickAsync() && Milliseconds(start) < 1000);
        var canceledTimer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        Task<bool> tick = canceledTimer.WaitForNextTickAsync(new CancellationTokenSource(10).Token).AsTask();
        await ((Task)tick).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Check("a canceled wait", tick.IsCanceled);
        canceledTimer.Period = TimeSpan.FromMilliseconds(5);
        Check("waits again after a cancel", await canceledTimer.WaitForNextTickAsync());
        canceledTimer.Dispose();
        End("PeriodicTimer");

        // Channels, async on both sides, and mixed with fibers.
        var numbers = new Channel<int>(2);
        Task<int> consumed = Consume(numbers);
        await Produce(numbers);
        Check("SendAsync and await foreach", await consumed == 55);
        var rendezvous = new Channel<int>();
        Task<int> pendingReceive = rendezvous.ReceiveAsync().AsTask();
        Check("ReceiveAsync waits", !pendingReceive.IsCompleted);
        await rendezvous.SendAsync(7);
        Check("ReceiveAsync gets it", await pendingReceive == 7);
        ValueTask sending = rendezvous.SendAsync(8);
        Check("SendAsync waits for a receiver", !sending.IsCompleted);
        Check("a fiber's Receive takes it", rendezvous.TryReceive(out int eight) && eight == 8);
        await sending;
        var fromFiber = new Channel<int>();
        Fiber.Spawn(fromFiber, static ch =>
        {
            for (int i = 1; i <= 5; i++)
                ch.Send(i);
            ch.Close();
        });
        int total2 = 0;
        while (await fromFiber.WaitToReceiveAsync())
            while (fromFiber.TryReceive(out int value))
                total2 += value;
        Check("a fiber sends, WaitToReceiveAsync reads", total2 == 15);
        var quiet = new Channel<int>();
        Task<int> gaveUp2 = quiet.ReceiveAsync(new CancellationTokenSource(10).Token).AsTask();
        await ((Task)gaveUp2).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Check("ReceiveAsync canceled", gaveUp2.IsCanceled);
        Fiber.Spawn(quiet, static ch => ch.Send(9));
        Check("the channel still works", await quiet.ReceiveAsync() == 9);
        End("Channels, async");

        // Waits that loop forever allocate nothing per round: the timer and
        // the channel reuse their tasks (a round first, which makes them).
        int rounds2 = small ? 20 : 200;
        var cancel = new CancellationTokenSource();
        using (var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1)))
        {
            await timer.WaitForNextTickAsync(cancel.Token);
            allocated = GC.GetTotalAllocatedBytes();
            for (int i = 0; i < rounds2 / 10; i++)
                await timer.WaitForNextTickAsync(cancel.Token);
            long timerBytes = GC.GetTotalAllocatedBytes() - allocated;
            Check($"timer loop: {timerBytes} bytes", timerBytes == 0);
        }
        var pipe = new Channel<int>();
        Fiber.Spawn((pipe, rounds2 + 1), static s =>
        {
            for (int i = 0; i <= s.Item2; i++)
            {
                Fiber.Yield();
                s.pipe.Send(i);
            }
            s.pipe.Close();
        });
        await pipe.ReceiveAsync(cancel.Token);
        allocated = GC.GetTotalAllocatedBytes();
        for (int i = 0; i < rounds2 / 2; i++)
            await pipe.ReceiveAsync(cancel.Token);
        long receiveBytes = GC.GetTotalAllocatedBytes() - allocated;
        Check($"ReceiveAsync loop: {receiveBytes} bytes", receiveBytes == 0);
        IAsyncEnumerator<int> each = pipe.ReceiveAllAsync(cancel.Token).GetAsyncEnumerator();
        await each.MoveNextAsync();
        allocated = GC.GetTotalAllocatedBytes();
        int left = 0;
        while (await each.MoveNextAsync())
            left++;
        long eachBytes = GC.GetTotalAllocatedBytes() - allocated;
        Check($"await foreach loop: {eachBytes} bytes, {left} values", eachBytes == 0 && left == rounds2 / 2);
        var sink = new Channel<int>();
        Fiber.Spawn((sink, rounds2), static s =>
        {
            for (int i = 0; i < s.Item2; i++)
            {
                Fiber.Yield();
                s.sink.Receive(out _);
            }
        });
        await sink.SendAsync(0, cancel.Token);
        allocated = GC.GetTotalAllocatedBytes();
        for (int i = 1; i < rounds2; i++)
            await sink.SendAsync(i, cancel.Token);
        long sendBytes = GC.GetTotalAllocatedBytes() - allocated;
        Check($"SendAsync loop: {sendBytes} bytes", sendBytes == 0);

        // A reused task's ValueTask is awaited once; AsTask and Preserve keep it.
        using (var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1)))
        {
            Task<bool> kept = timer.WaitForNextTickAsync().AsTask();
            Check("AsTask while waiting", await kept);
            ValueTask<bool> preserved = timer.WaitForNextTickAsync().Preserve();
            Check("Preserve: awaited twice", await preserved && await preserved);
            Check("AsTask of a reused task is a copy", await kept && await timer.WaitForNextTickAsync());
        }
        End("No allocation per wait");

        // Cost: awaits that wait, and ones that don't.
        start = Clock.Monotonic;
        // Each call that doesn't wait allocates its Task<int>.
        int rounds = small ? 500 : 10_000;
        await Steps("", rounds);
        long perYield = (Clock.Monotonic - start) / rounds;
        start = Clock.Monotonic;
        int total = 0;
        for (int i = 0; i < rounds; i++)
            total += await NoWait(i);
        long perSyncCall = (Clock.Monotonic - start) / rounds;
        Out.Line($"  {perYield} ns per await Task.Yield, {perSyncCall} ns per call of an async method that doesn't wait");
        Check("total", total == rounds * (rounds + 1) / 2);
        End("Cost");

        return s_failures;
    }
}
