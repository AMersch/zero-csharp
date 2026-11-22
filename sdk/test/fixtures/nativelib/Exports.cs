// A native library (NativeLib=Shared) for the tests: sdk/test/call-native-lib.sh
// calls each export from .NET, and fixtures/nativelib-c from C.
using System.Runtime.InteropServices;

public struct Point
{
    public int X;
    public int Y;
}

public static class Native
{
    // Calls the compiler can't evaluate at build time, so the static
    // constructors below run when the library is used.
    [DllImport("libc")]
    public static safe extern int abs(int value);
}

public static class History
{
    // Assigned at run time: lives in the static base the library's startup
    // sets up.
    public static int[] Values;
}

public static class Answer
{
    public static readonly int Value;

    static Answer()
    {
        Value = Native.abs(-41) + 1;
    }
}

// Slow to set up, for the caller's eight threads that all call at once.
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

public static class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "nl_add")]
    public static int Add(int a, int b) => a + b;

    [UnmanagedCallersOnly(EntryPoint = "nl_mul_long")]
    public static long MulLong(long a, long b) => a * b;

    [UnmanagedCallersOnly(EntryPoint = "nl_scale")]
    public static double Scale(double x, double f) => x * f;

    [UnmanagedCallersOnly(EntryPoint = "nl_sum")]
    public static unsafe long Sum(int* values, int count)
    {
        long sum = 0;
        for (int i = 0; i < count; i++)
        {
            // SAFETY: the caller passes count readable ints.
            unsafe { sum += values[i]; }
        }
        return sum;
    }

    [UnmanagedCallersOnly(EntryPoint = "nl_offset")]
    public static Point Offset(Point p, int d) => new Point { X = p.X + d, Y = p.Y + d };

    [UnmanagedCallersOnly(EntryPoint = "nl_negate")]
    public static unsafe void Negate(Point* p)
    {
        // SAFETY: the caller passes one writable Point.
        unsafe
        {
            p->X = -p->X;
            p->Y = -p->Y;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "nl_remember")]
    public static int Remember(int value)
    {
        int[] old = History.Values ?? new int[0];
        int[] next = new int[old.Length + 1];
        for (int i = 0; i < old.Length; i++)
            next[i] = old[i];
        next[old.Length] = value;
        History.Values = next;
        return next.Length;
    }

    [UnmanagedCallersOnly(EntryPoint = "nl_cctor_value")]
    public static int CctorValue() => Answer.Value;

    [UnmanagedCallersOnly(EntryPoint = "nl_slow_value")]
    public static int SlowValue() => SlowInit.Value;

    [UnmanagedCallersOnly(EntryPoint = "nl_slow_runs")]
    public static int SlowRuns() => SlowInit.Runs;
}
