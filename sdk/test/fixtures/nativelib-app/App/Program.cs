// Calls MathLib through the bindings MathLib's build writes; .NET finds the
// library next to the program by its name.
using System;

public static class Program
{
    // docs:call
    public static void Main()
    {
        Console.WriteLine($"add {MathLib.Add(2, 3)}");
        Point moved = MathLib.Offset(new Point { X = 1, Y = 12 }, 10);
        Console.WriteLine($"offset {moved.X},{moved.Y}");
    }
    // docs:end
}
