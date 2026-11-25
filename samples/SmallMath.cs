// Test: the Math functions that a small machine's C library may lack (sin,
// cos, tan, exp, log, log2, log10, pow, atan2, and what is built on them),
// sized for a small machine. The Commander X16 (./build.sh --cx16) has them
// in cx16/math.c; every other target has its own math library, so the
// checks allow for a few units in the last place. Numerics.cs tests the rest
// of System.Numerics. It runs on every target that has these functions.
// Exit code = number of failed checks.
// Build: ./build.sh SmallMath

using System;
using System.Runtime.CompilerServices;

namespace SmallMathSample;

public static class Program
{
    static int s_failures;
    static int s_sectionChecks;
    static int s_sectionFailures;

    static void End(string section)
    {
        Console.Write(s_sectionFailures == 0 ? "PASS " : "FAIL ");
        Console.Write(section);
        Console.Write(" (");
        Console.Write(s_sectionChecks);
        Console.WriteLine(" checks)");
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
            Console.Write("  failed: ");
            Console.WriteLine(name);
        }
    }

    // Hide a value from the optimizer, which would otherwise do the
    // arithmetic at build time. (Always false: see SmallIntegers.cs.)
    static bool s_never;
    [MethodImpl(MethodImplOptions.NoInlining)] static double D(double value) => s_never ? 0 : value;
    [MethodImpl(MethodImplOptions.NoInlining)] static float F(float value) => s_never ? 0 : value;

    // Within a relative error, or an absolute one for values near 0.
    static bool Near(double actual, double expected, double tolerance = 1e-14) =>
        Math.Abs(actual - expected) <= tolerance * Math.Max(1, Math.Abs(expected));

    static bool Near(float actual, double expected) => Near((double)actual, expected, 1e-6);

    // One method for each group: a method this size is more than a RAM bank of a
    // Commander X16 holds, and the build moves whole methods into banks.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Trigonometry()
    {
        Check("Sin, Cos, Tan", Near(Math.Sin(D(1)), 0.8414709848078965) && Near(Math.Cos(D(1)), 0.5403023058681398)
            && Near(Math.Tan(D(1)), 1.557407724654902) && Near(Math.Cos(D(-3)), -0.9899924966004454));
        Check("Sin and Cos of 0, and of Pi", Math.Sin(D(0)) == 0 && Math.Cos(D(0)) == 1
            && Near(Math.Sin(D(Math.PI)), 0) && Near(Math.Cos(D(Math.PI)), -1));
        Check("larger arguments", Near(Math.Sin(D(100)), -0.5063656411097588) && Near(Math.Cos(D(100)), 0.8623188722876839)
            && Near(Math.Sin(D(1e6)), -0.34999350217129294));
        Check("Sin of infinity is NaN", double.IsNaN(Math.Sin(D(double.PositiveInfinity))) && double.IsNaN(Math.Cos(D(double.NaN))));
        End("trigonometry");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Exponentials()
    {
        Check("Exp", Near(Math.Exp(D(1)), 2.718281828459045) && Near(Math.Exp(D(-1)), 0.36787944117144233)
            && Near(Math.Exp(D(10)), 22026.465794806718) && Near(Math.Exp(D(0.5)), 1.6487212707001282)
            && Math.Exp(D(0)) == 1);
        Check("Exp at the edges", Near(Math.Exp(D(700)), 1.0142320547350045e+304) && double.IsPositiveInfinity(Math.Exp(D(710)))
            && Math.Exp(D(-750)) == 0);
        Check("Log", Near(Math.Log(D(2)), 0.6931471805599453) && Near(Math.Log(D(10)), 2.302585092994046)
            && Near(Math.Log(D(0.5)), -0.6931471805599453) && Near(Math.Log(D(1e300)), 690.7755278982137)
            && Math.Log(D(1)) == 0);
        Check("Log of 0 and below", double.IsNegativeInfinity(Math.Log(D(0))) && double.IsNaN(Math.Log(D(-1))));
        Check("Log2, Log10", Math.Log2(D(8)) == 3 && Math.Log2(D(0.5)) == -1 && Near(Math.Log2(D(10)), 3.321928094887362)
            && Math.Log10(D(1000)) == 3 && Near(Math.Log10(D(2)), 0.3010299956639812));
        End("exponentials and logarithms");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Powers()
    {
        Check("Pow with whole exponents", Math.Pow(D(2), D(10)) == 1024 && Math.Pow(D(10), D(2)) == 100
            && Math.Pow(D(-8), D(3)) == -512 && Math.Pow(D(5), D(0)) == 1 && Near(Math.Pow(D(10), D(-3)), 0.001));
        Check("Pow with fractions", Math.Pow(D(2), D(0.5)) == Math.Sqrt(D(2)) && Near(Math.Pow(D(1.5), D(20.5)), 4072.591126225418)
            && Near(Math.Pow(D(0.3), D(7.7)), 9.415289134866057e-05));
        Check("Pow at the edges", double.IsNaN(Math.Pow(D(-8), D(1.0 / 3))) && double.IsPositiveInfinity(Math.Pow(D(10), D(400)))
            && Math.Pow(D(10), D(-400)) == 0);
        End("powers");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Inverse()
    {
        Check("Atan2", Near(Math.Atan2(D(1), D(1)), Math.PI / 4) && Near(Math.Atan2(D(3), D(-2)), 2.158798930342464)
            && Near(Math.Atan2(D(-1), D(-1)), -3 * Math.PI / 4) && Math.Atan2(D(0), D(1)) == 0
            && Near(Math.Atan2(D(0), D(-1)), Math.PI));
        Check("Atan, Asin, Acos", Near(Math.Atan(D(0.5)), 0.46364760900080615) && Near(Math.Asin(D(0.5)), 0.5235987755982988)
            && Near(Math.Acos(D(0.25)), 1.318116071652818));
        Check("Sinh, Cosh, Tanh", Near(Math.Sinh(D(1)), 1.1752011936438014) && Near(Math.Cosh(D(1)), 1.5430806348152437)
            && Near(Math.Tanh(D(0.5)), 0.46211715726000974));
        End("inverse and hyperbolic functions");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Single()
    {
        Check("MathF", Near(MathF.Sin(F(1)), 0.8414709848078965) && Near(MathF.Cos(F(1)), 0.5403023058681398)
            && Near(MathF.Exp(F(1)), 2.718281828459045) && Near(MathF.Log(F(2)), 0.6931471805599453)
            && Near(MathF.Pow(F(2), F(8)), 256) && Near(MathF.Tanh(F(0.5f)), 0.46211715726000974));
        End("single precision");
    }

    public static int Main()
    {
        s_never = false;
        Trigonometry();
        Exponentials();
        Powers();
        Inverse();
        Single();

        Console.WriteLine();
        if (s_failures == 0)
            Console.WriteLine("All checks passed.");
        else
        {
            Console.Write(s_failures);
            Console.WriteLine(" checks FAILED.");
        }
        return s_failures;
    }
}
