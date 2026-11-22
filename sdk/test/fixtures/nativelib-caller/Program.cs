// Calls every export of a library built from fixtures/nativelib and prints
// what it got (expected.txt holds what it should print).
//   ZERO_NATIVE_LIB=<library> nativelib-caller
//   nativelib-caller two <copy a> <copy b>     two copies keep separate state
//   nativelib-caller area <library>            nl_rect_area(2, 3), an export that
//                                              calls a referenced zero library
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

[StructLayout(LayoutKind.Sequential)]
public struct Point
{
    public int X;
    public int Y;
}

public static unsafe class Native
{
    private const string Lib = "nativelib";

    [DllImport(Lib)] public static extern int nl_add(int a, int b);
    [DllImport(Lib)] public static extern long nl_mul_long(long a, long b);
    [DllImport(Lib)] public static extern double nl_scale(double x, double f);
    [DllImport(Lib)] public static extern long nl_sum(int* values, int count);
    [DllImport(Lib)] public static extern Point nl_offset(Point p, int d);
    [DllImport(Lib)] public static extern void nl_negate(Point* p);
    [DllImport(Lib)] public static extern int nl_remember(int value);
    [DllImport(Lib)] public static extern int nl_cctor_value();
    [DllImport(Lib)] public static extern int nl_slow_value();
    [DllImport(Lib)] public static extern int nl_slow_runs();
}

public static unsafe class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "two")
            return Two(args[1], args[2]);
        if (args.Length == 2 && args[0] == "area")
        {
            var area = (delegate* unmanaged<double, double, double>)NativeLibrary.GetExport(NativeLibrary.Load(args[1]), "nl_rect_area");
            Console.WriteLine($"area {area(2, 3).ToString(CultureInfo.InvariantCulture)}");
            return 0;
        }

        string path = Environment.GetEnvironmentVariable("ZERO_NATIVE_LIB");
        if (string.IsNullOrEmpty(path))
        {
            Console.Error.WriteLine("Set ZERO_NATIVE_LIB to the library's path.");
            return 2;
        }
        IntPtr library = NativeLibrary.Load(path);
        NativeLibrary.SetDllImportResolver(typeof(Program).Assembly,
            (name, assembly, search) => name == "nativelib" ? library : IntPtr.Zero);

        Console.WriteLine($"add {Native.nl_add(2, 3)}");
        Console.WriteLine($"mul {Native.nl_mul_long(60000, 100000)}");
        Console.WriteLine($"scale {Native.nl_scale(2.5, 3.0).ToString(CultureInfo.InvariantCulture)}");
        int* values = stackalloc int[] { 1, 2, 3, 4 };
        Console.WriteLine($"sum {Native.nl_sum(values, 4)}");
        Point offset = Native.nl_offset(new Point { X = 1, Y = 12 }, 10);
        Console.WriteLine($"offset {offset.X},{offset.Y}");
        Point negated = new Point { X = 3, Y = 4 };
        Native.nl_negate(&negated);
        Console.WriteLine($"negate {negated.X},{negated.Y}");
        Native.nl_remember(5);
        Native.nl_remember(6);
        Console.WriteLine($"remember {Native.nl_remember(7)}");
        Console.WriteLine($"cctor {Native.nl_cctor_value()}");

        // Eight threads make their first call into SlowInit at once.
        const int count = 8;
        int[] seen = new int[count];
        using (var barrier = new Barrier(count))
        {
            var threads = new Thread[count];
            for (int i = 0; i < count; i++)
            {
                int index = i;
                threads[i] = new Thread(() => { barrier.SignalAndWait(); seen[index] = Native.nl_slow_value(); });
                threads[i].Start();
            }
            foreach (Thread thread in threads)
                thread.Join();
        }
        bool same = Array.TrueForAll(seen, v => v == seen[0]);
        Console.WriteLine(same
            ? $"threads {count} {seen[0]} {Native.nl_slow_runs()}"
            : $"threads {count} {string.Join(",", seen)} {Native.nl_slow_runs()}");

        // zerocore's own symbols must stay inside the library.
        var found = new List<string>();
        foreach (string name in new[] { "RhpNewFast", "RhpReversePInvoke", "ZeroInitializeLibrary", "__start___modules", "___start___modules", "__stop___modules" })
        {
            if (NativeLibrary.TryGetExport(library, name, out _))
                found.Add(name);
        }
        Console.WriteLine(found.Count == 0 ? "hidden ok" : $"hidden {string.Join(" ", found)}");
        return 0;
    }

    private static int Two(string a, string b)
    {
        var rememberA = (delegate* unmanaged<int, int>)NativeLibrary.GetExport(NativeLibrary.Load(a), "nl_remember");
        var rememberB = (delegate* unmanaged<int, int>)NativeLibrary.GetExport(NativeLibrary.Load(b), "nl_remember");
        rememberA(1);
        rememberA(2);
        int inA = rememberA(3);
        int inB = rememberB(9);
        Console.WriteLine($"two {inA} {inB}");
        return 0;
    }
}
