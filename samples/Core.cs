// Example: the smaller parts of the core library (zerocore): Math and MathF,
// Interlocked, Volatile, interface calls from one call site to many classes,
// extension members, and C# 15 unions (zerocore/System/Exceptions.cs and
// Unions.cs explain what those two need from the library).
// Exit code = number of failed checks.
// Build: ./build.sh Core

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CoreSample;

public interface IShape
{
    int Sides { get; }
}

// docs:extension-members
public struct Vec
{
    public int X, Y;
}

public static class VecExtensions
{
    // Classic extension method: `this` on the first parameter.
    public static int Dot(this Vec a, Vec b) => a.X * b.X + a.Y * b.Y;

    // C# 14 extension block: properties and methods on a Vec value.
    extension(Vec v)
    {
        public int LengthSquared => v.Dot(v);
        public bool IsZero => v.X == 0 && v.Y == 0;
        public Vec Scale(int k) => new Vec { X = v.X * k, Y = v.Y * k };
    }

    // Static members, called as Vec.Zero and Vec.Of(1, 2).
    extension(Vec)
    {
        public static Vec Zero => default;
        public static Vec Of(int x, int y) => new Vec { X = x, Y = y };
    }

    // By reference: changes the caller's variable.
    extension(ref Vec v)
    {
        public void Translate(int dx, int dy)
        {
            v.X += dx;
            v.Y += dy;
        }
    }
}
// docs:end

// docs:union-declaration
public struct Parsed { public int Value; }
public struct BadChar { public int Position; }

// A ParseResult holds either a Parsed or a BadChar, and a switch on it must
// handle both.
public union ParseResult(Parsed, BadChar);
// docs:end

public class Triangle : IShape { public int Sides => 3; }
public class Square : IShape { public int Sides => 4; }
public class Pentagon : IShape { public int Sides => 5; }
public class Hexagon : IShape { public int Sides => 6; }
public class Heptagon : IShape { public int Sides => 7; }
public class Octagon : IShape { public int Sides => 8; }

public static class Program
{
    static int s_failures;
    static int s_sectionChecks;
    static int s_sectionFailures;

    static void Section(string section)
    {
        Out.Line($"{(s_sectionFailures == 0 ? "PASS" : "FAIL")} {section} ({s_sectionChecks} checks)");
        s_sectionChecks = 0;
        s_sectionFailures = 0;
    }

    static void Check(string name, bool ok)
    {
        s_sectionChecks++;
        if (!ok)
        {
            s_failures++;
            s_sectionFailures++;
            Out.Line($"  FAIL: {name}");
        }
    }

    // Hides a value from the optimizer, which would otherwise do the math at
    // build time and never run the code being tested.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static double Opaque(double value) => value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static float Opaque(float value) => value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Opaque(int value) => value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static object Opaque(object value) => value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string Opaque(string value) => value;

    static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 1e-12;

    static bool Near(float actual, float expected) => MathF.Abs(actual - expected) < 1e-5f;

    // One call site, whichever class each element has.
    static int TotalSides(IShape[] shapes)
    {
        int total = 0;
        for (int i = 0; i < shapes.Length; i++)
            total += shapes[i].Sides;
        return total;
    }

    // docs:union-function
    static ParseResult ParseNumber(string text)
    {
        int value = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c < '0' || c > '9')
                return new BadChar { Position = i };
            value = value * 10 + (c - '0');
        }
        return new Parsed { Value = value };
    }

    static int ValueOrMinusOne(string text) => ParseNumber(text) switch
    {
        Parsed p => p.Value,
        BadChar => -1,
    };
    // docs:end

    public static int Main()
    {
        Check("Sqrt", Math.Sqrt(Opaque(2.0)) * Math.Sqrt(Opaque(2.0)) > 1.9999999999 && Math.Sqrt(Opaque(144.0)) == 12);
        Check("Abs", Math.Abs(Opaque(-3)) == 3 && Math.Abs(Opaque(-2.5)) == 2.5 && Math.Abs(-7L) == 7);
        Check("Min, Max", Math.Min(Opaque(3), 5) == 3 && Math.Max(Opaque(3), 5) == 5 && Math.Max(Opaque(-1.5), -2.5) == -1.5);
        double nan = Opaque(0.0) / Opaque(0.0);
        Check("Min and Max of NaN are NaN", Math.Min(nan, 1) != Math.Min(nan, 1) && Math.Max(1, nan) != Math.Max(1, nan));
        Check("Clamp", Math.Clamp(Opaque(15), 0, 10) == 10 && Math.Clamp(Opaque(-5), 0, 10) == 0 && Math.Clamp(Opaque(0.5), 0, 1) == 0.5);
        Check("Floor, Ceiling", Math.Floor(Opaque(-1.5)) == -2 && Math.Ceiling(Opaque(-1.5)) == -1 && Math.Floor(Opaque(2.9)) == 2);
        Check("Truncate", Math.Truncate(Opaque(-1.9)) == -1 && Math.Truncate(Opaque(1.9)) == 1);
        Check("Round: halves go to even", Math.Round(Opaque(2.5)) == 2 && Math.Round(Opaque(3.5)) == 4 && Math.Round(Opaque(-0.4)) == 0);
        Check("Pow, Exp, Log", Math.Pow(Opaque(2.0), 10) == 1024 && Near(Math.Log(Math.Exp(Opaque(3.0))), 3) && Near(Math.Log2(Opaque(8.0)), 3) && Near(Math.Log10(Opaque(1000.0)), 3));
        Check("Sin, Cos, Tan, Atan2", Near(Math.Sin(Opaque(Math.PI / 2)), 1) && Near(Math.Cos(Opaque(Math.PI)), -1) && Near(Math.Tan(Opaque(Math.PI / 4)), 1) && Near(Math.Atan2(Opaque(1.0), 1), Math.PI / 4));
        Section("Math");

        Check("Sqrt, Abs", MathF.Sqrt(Opaque(81f)) == 9 && MathF.Abs(Opaque(-1.5f)) == 1.5f);
        Check("Floor, Ceiling, Truncate, Round", MathF.Floor(Opaque(1.5f)) == 1 && MathF.Ceiling(Opaque(1.5f)) == 2 && MathF.Truncate(Opaque(-1.5f)) == -1 && MathF.Round(Opaque(2.5f)) == 2);
        Check("Min, Max", MathF.Min(Opaque(1f), 2f) == 1 && MathF.Max(Opaque(1f), 2f) == 2);
        Check("Pow, Exp, Log", MathF.Pow(Opaque(3f), 2) == 9 && Near(MathF.Log(MathF.Exp(Opaque(2f))), 2));
        Check("Sin, Cos, Tanh", Near(MathF.Sin(Opaque(0f)), 0) && Near(MathF.Cos(Opaque(0f)), 1) && Near(MathF.Tanh(Opaque(0f)), 0));
        Section("MathF");

        double zero = Opaque(0.0);
        float zeroF = Opaque(0f);
        Check("NaN", double.IsNaN(zero / zero) && double.IsNaN(double.NaN) && !double.IsNaN(Opaque(1.0)) && float.IsNaN(zeroF / zeroF) && float.IsNaN(float.NaN));
        Check("infinities", double.IsPositiveInfinity(1 / zero) && double.IsNegativeInfinity(-1 / zero) && double.IsInfinity(Opaque(double.NegativeInfinity)) && float.IsPositiveInfinity(1 / zeroF) && float.IsInfinity(Opaque(float.NegativeInfinity)));
        Check("IsFinite", double.IsFinite(Opaque(double.MaxValue)) && !double.IsFinite(Opaque(double.NaN)) && !double.IsFinite(1 / zero) && float.IsFinite(Opaque(float.MinValue)) && !float.IsFinite(1 / zeroF));
        // The outer Opaque stores each result as a double or float: on DOS the
        // 387 computes with a wider exponent, so MaxValue * 2 only overflows,
        // and Epsilon / 2 only rounds to 0, once stored.
        Check("MaxValue overflows to infinity", double.IsPositiveInfinity(Opaque(Opaque(double.MaxValue) * 2)) && float.IsPositiveInfinity(Opaque(Opaque(float.MaxValue) * 2)));
        Check("Epsilon is the smallest subnormal", Opaque(Opaque(double.Epsilon) / 2) == 0 && double.IsSubnormal(Opaque(double.Epsilon)) && Opaque(Opaque(float.Epsilon) / 2) == 0 && float.IsSubnormal(Opaque(float.Epsilon)));
        Check("IsNormal, IsSubnormal", double.IsNormal(Opaque(1.0)) && !double.IsNormal(zero) && !double.IsSubnormal(zero) && float.IsNormal(Opaque(1f)) && !float.IsNormal(Opaque(float.Epsilon)));
        Check("IsNegative sees -0", double.IsNegative(Opaque(double.NegativeZero)) && !double.IsNegative(zero) && float.IsNegative(Opaque(float.NegativeZero)) && float.IsNegative(Opaque(-1f)));
        Check("Pi, E, Tau", double.Pi == Math.PI && double.E == Math.E && double.Tau == 2 * Math.PI && float.Pi == MathF.PI && float.Tau == 2 * float.Pi);
        Section("float and double");

        int counter = Opaque(10);
        Check("Increment returns the new value", Interlocked.Increment(ref counter) == 11 && counter == 11);
        Check("Decrement", Interlocked.Decrement(ref counter) == 10 && counter == 10);
        Check("Add", Interlocked.Add(ref counter, 32) == 42 && counter == 42);
        Check("Exchange returns the old value", Interlocked.Exchange(ref counter, 7) == 42 && counter == 7);
        Check("CompareExchange stores on a match", Interlocked.CompareExchange(ref counter, 8, 7) == 7 && counter == 8);
        Check("CompareExchange leaves a mismatch alone", Interlocked.CompareExchange(ref counter, 99, 7) == 8 && counter == 8);
        long big = 1L << 40;
        Check("64-bit Add and Increment", Interlocked.Add(ref big, 1L << 40) == 1L << 41 && Interlocked.Increment(ref big) == (1L << 41) + 1);
        Check("64-bit Exchange and CompareExchange", Interlocked.Exchange(ref big, -1) == (1L << 41) + 1 && Interlocked.CompareExchange(ref big, 5, -1) == -1 && big == 5);
        nint pointer = 100;
        Check("pointer-sized", Interlocked.CompareExchange(ref pointer, 200, 100) == 100 && Interlocked.Exchange(ref pointer, 300) == 200 && pointer == 300);
        Interlocked.MemoryBarrier();
        Section("Interlocked");

        int flag = 0;
        long wide = 0;
        bool done = false;
        nint address = 0;
        Volatile.Write(ref flag, 5);
        Volatile.Write(ref wide, 1L << 50);
        Volatile.Write(ref done, true);
        Volatile.Write(ref address, 77);
        Check("int", Volatile.Read(ref flag) == 5);
        Check("long", Volatile.Read(ref wide) == 1L << 50);
        Check("bool", Volatile.Read(ref done));
        Check("pointer-sized", Volatile.Read(ref address) == 77);
        Section("Volatile");

        // The runtime remembers a few classes per call site. Six classes here
        // go past that, so some of these calls take the cached path and some
        // the full search; the answers have to agree either way, however the
        // classes are ordered and repeated.
        IShape[] shapes =
        {
            (IShape)Opaque(new Triangle()), (IShape)Opaque(new Square()), (IShape)Opaque(new Pentagon()),
            (IShape)Opaque(new Hexagon()), (IShape)Opaque(new Heptagon()), (IShape)Opaque(new Octagon()),
        };
        Check("six classes through one call site", TotalSides(shapes) == 33);
        Check("again, now that the site has a cache", TotalSides(shapes) == 33);
        IShape[] reversed = new IShape[12];
        for (int i = 0; i < 6; i++)
        {
            reversed[i] = shapes[5 - i];
            reversed[6 + i] = shapes[5 - i];
        }
        Check("reversed and repeated", TotalSides(reversed) == 66);
        long many = 0;
        for (int round = 0; round < 10000; round++)
            many += TotalSides(shapes);
        Check("ten thousand rounds", many == 330000);
        Section("interface calls to many classes");

        // docs:extension-calls
        Vec v = Vec.Of(Opaque(3), Opaque(4));
        Check("extension method", v.Dot(Vec.Of(1, 1)) == 7);
        Check("extension property", v.LengthSquared == 25);
        Check("extension method in a block", v.Scale(2).LengthSquared == 100);
        Check("static extension members", Vec.Zero.IsZero && !v.IsZero);
        v.Translate(-3, -4);
        Check("by-reference extension", v.IsZero);
        // docs:end
        Section("extension members");

        // docs:union-calls
        Check("union holding the first case", ParseNumber(Opaque("123")) is Parsed { Value: 123 });
        Check("union holding the second case", ParseNumber(Opaque("12x")) is BadChar { Position: 2 });
        Check("switch on a union", ValueOrMinusOne(Opaque("42")) == 42 && ValueOrMinusOne(Opaque("4.2")) == -1);
        // docs:end
        Section("unions");

        Out.Line();
        if (s_failures == 0)
            Out.Line("All checks passed.");
        else
            Out.Line($"{s_failures} checks FAILED.");
        return s_failures;
    }
}
