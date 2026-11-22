// Static constructors reached from several threads at once (lib/Threads.cs).
// Prints:
//   values 7 7 7 7 7 7 7 7    every thread saw SlowInit set up
//   runs 1                    its static constructor ran once
//   self ok                   a constructor that re-enters its own type
//   cycle ok                  two constructors that need each other, started
//                             on two threads at once, don't deadlock
using System.Runtime.InteropServices;

public static class Native
{
    // A call the compiler can't evaluate at build time, so the static
    // constructors below really run at run time.
    [DllImport("libc")]
    public static safe extern int abs(int value);
}

public static class SlowInit
{
    public static readonly int Value;
    public static int Runs;

    static SlowInit()
    {
        int seed = Native.abs(-1);
        long spin = 0;
        for (int i = 0; i < 30_000_000 * seed; i++)
            spin += i ^ seed;
        System.Threading.Interlocked.Increment(ref Runs);
        Value = spin == -1 ? 0 : 7;
    }
}

public static class Self
{
    public static int A;
    public static int B;

    static Self()
    {
        A = Native.abs(-3);
        B = ReadsSelf.A() + 1;   // back into Self while its constructor runs
    }
}

public static class ReadsSelf
{
    public static int A() => Self.A;
}

public static class CycleA
{
    public static int X;
    static CycleA()
    {
        int seed = Native.abs(-1);
        long spin = 0;
        for (int i = 0; i < 5_000_000 * seed; i++) spin += i;
        X = CycleB.Y + (spin == -1 ? 0 : 1);
    }
}

public static class CycleB
{
    public static int Y;
    static CycleB()
    {
        int seed = Native.abs(-1);
        long spin = 0;
        for (int i = 0; i < 5_000_000 * seed; i++) spin += i;
        Y = CycleA.X + (spin == -1 ? 0 : 1);
    }
}

public sealed class Box
{
    public int[] Values;
    public int Cycle;
}

public static class Program
{
    public static int Main()
    {
        var box = new Box { Values = new int[8] };
        Parallel.For(0, 8, box, static (b, i) => b.Values[i] = SlowInit.Value);
        Out.Write("values");
        for (int i = 0; i < 8; i++)
        {
            Out.Write(" ");
            Out.Write(box.Values[i]);
        }
        Out.Line();
        Out.Write("runs ");
        Out.Write(SlowInit.Runs);
        Out.Line();

        Out.Line(Self.B == 4 ? "self ok" : "self wrong");

        Parallel.For(0, 2, box, static (b, i) =>
        {
            int seen = i == 0 ? CycleA.X : CycleB.Y;
            System.Threading.Interlocked.Add(ref b.Cycle, seen >= 1 ? 1 : 0);
        });
        Out.Line(box.Cycle == 2 ? "cycle ok" : "cycle wrong");
        return 0;
    }
}
