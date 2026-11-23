// Test: floating-point arithmetic, sized for a small machine. A Commodore 64
// (./build.sh --c64) has none: llvm-mos computes float and double in
// software, and c64/runtime.c adds what its library lacks (rounding,
// remainder, square root, and C#'s conversions to integers). This is the
// test of those. SmallFloats.cs prints floating point; SmallIntegers.cs has
// the whole numbers. It runs on every target.
// Exit code = number of failed checks.
// Build: ./build.sh SmallDoubles

using System;
using System.Runtime.CompilerServices;

namespace SmallDoublesSample;

public static class Program
{
    static int s_failures;
    static int s_sectionChecks;
    static int s_sectionFailures;

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
            Out.Write("  failed: ");
            Out.Line(name);
        }
    }

    // Hide a value from the optimizer, which would otherwise do the
    // arithmetic at build time. (Always false: see SmallIntegers.cs.)
    static bool s_never;
    [MethodImpl(MethodImplOptions.NoInlining)] static int I(int value) => s_never ? 0 : value;
    [MethodImpl(MethodImplOptions.NoInlining)] static long L(long value) => s_never ? 0 : value;
    [MethodImpl(MethodImplOptions.NoInlining)] static ulong UL(ulong value) => s_never ? 0 : value;
    [MethodImpl(MethodImplOptions.NoInlining)] static double D(double value) => s_never ? 0 : value;
    [MethodImpl(MethodImplOptions.NoInlining)] static float F(float value) => s_never ? 0 : value;

    public static int Main()
    {
        s_never = false;
        double x = D(1.5), y = D(-2.25);
        Check("double + - * /", x + y == -0.75 && x - y == 3.75 && x * y == -3.375 && x / y == -0.6666666666666666);
        Check("double compares", x > y && y < 0 && D(0.1) + D(0.2) != 0.3 && D(0.1) + D(0.2) == 0.30000000000000004);
        Check("double %", D(7.5) % D(2) == 1.5 && D(-7.5) % D(2) == -1.5);
        Check("int to double and back", (double)I(123456789) == 123456789.0 && (int)D(-3.99) == -3
            && (long)D(1e15) == 1000000000000000 && (double)UL(ulong.MaxValue) == 18446744073709551616.0);
        Check("out of range converts to the limit", (int)D(1e20) == int.MaxValue && (int)D(-1e20) == int.MinValue
            && (long)D(1e20) == long.MaxValue && (uint)D(-1) == 0);
        Check("NaN converts to 0", (int)D(double.NaN) == 0 && (long)D(double.NaN) == 0);
        Check("float", F(1.5f) * F(2.5f) == 3.75f && (double)F(0.1f) != 0.1 && (float)D(16777217) == 16777216f
            && (int)F(-2.5f) == -2);
        Check("Floor, Ceiling, Truncate", Math.Floor(D(-2.5)) == -3 && Math.Ceiling(D(-2.5)) == -2
            && Math.Truncate(D(-2.5)) == -2 && Math.Floor(D(2.5)) == 2 && Math.Floor(D(1e300)) == 1e300);
        Check("Sqrt", Math.Sqrt(D(144)) == 12 && Math.Sqrt(D(2)) == 1.4142135623730951 && Math.Sqrt(D(0.25)) == 0.5
            && Math.Sqrt(D(1e-300)) == 1e-150 && Math.Sqrt(D(0)) == 0);
        Check("Abs, Min, Max", Math.Abs(D(-0.5)) == 0.5 && Math.Min(D(1), D(2)) == 1 && Math.Max(D(1), D(2)) == 2);
        End("floating point");

        Out.Line();
        if (s_failures == 0)
            Out.Line("All checks passed.");
        else
        {
            Out.Write(s_failures);
            Out.Line(" checks FAILED.");
        }
        return s_failures;
    }
}
