// Calls the F# library's exports through the bindings made from its IL.
using System;
using NativeLib;

public static unsafe class Program
{
    public static void Main()
    {
        Console.WriteLine($"add {fsnativelib.add(2, 3)}");
        int* values = stackalloc int[] { 1, 2, 3, 4 };
        Console.WriteLine($"sum {fsnativelib.sum(values, 4)}");
        Point offset = fsnativelib.offset(new Point { X = 1, Y = 12 }, 10);
        Console.WriteLine($"offset {offset.X},{offset.Y}");
        fsnativelib.negate(&offset);
        Console.WriteLine($"negate {offset.X},{offset.Y}");
    }
}
