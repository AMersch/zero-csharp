// Calls MathLib, found next to the program by its name alone.
using System;
using System.Runtime.InteropServices;

public static class Program
{
    [DllImport("MathLib")]
    private static extern int nl_add(int a, int b);

    public static void Main() => Console.WriteLine($"add {nl_add(2, 3)}");
}
