// Runs zerocore's TensorPrimitives (namespace ZeroTest) and .NET's on the
// same random spans and reports, per operation, the largest difference and
// the input that gave it: in units in the last place for element-wise
// results, relative to the sum of the magnitudes added for sums and products
// (which add in a different order). Argument errors must agree: both throw,
// or neither. Exit code = number of operations over their limit.

using System;
using System.Collections.Generic;
using N = System.Numerics.Tensors.TensorPrimitives;
using Z = ZeroTest.TensorPrimitives;

delegate void Unary(ReadOnlySpan<float> x, Span<float> d);
delegate void Binary(ReadOnlySpan<float> x, ReadOnlySpan<float> y, Span<float> d);
delegate void BinaryScalar(ReadOnlySpan<float> x, float y, Span<float> d);
delegate void ScalarBinary(float x, ReadOnlySpan<float> y, Span<float> d);
delegate void Ternary(ReadOnlySpan<float> x, ReadOnlySpan<float> y, ReadOnlySpan<float> z, Span<float> d);
delegate void TernaryLastScalar(ReadOnlySpan<float> x, ReadOnlySpan<float> y, float z, Span<float> d);
delegate void TernaryMiddleScalar(ReadOnlySpan<float> x, float y, ReadOnlySpan<float> z, Span<float> d);
delegate float Reduce(ReadOnlySpan<float> x);
delegate float Reduce2(ReadOnlySpan<float> x, ReadOnlySpan<float> y);
delegate int Index(ReadOnlySpan<float> x);

static class Program
{
    static Random s_random;
    static readonly List<(string Name, double Worst, string Input, double Limit)> s_results = new();
    static readonly Dictionary<string, int> s_index = new();

    static void Record(string name, double error, string input, double limit)
    {
        if (!s_index.TryGetValue(name, out int i))
        {
            i = s_results.Count;
            s_index[name] = i;
            s_results.Add((name, -1, "", limit));
        }
        if (error > s_results[i].Worst)
            s_results[i] = (name, error, input, limit);
    }

    static readonly float[] s_edges =
    {
        0, -0f, 1, -1, 0.5f, -0.5f, 2, float.NaN, float.PositiveInfinity, float.NegativeInfinity,
        1e-30f, -1e-30f, 1e30f, 1.4e-45f, 88.5f, -88.5f, 100, MathF.PI,
    };

    static float AnyFloat()
    {
        if (s_random.Next(20) == 0)
            return s_edges[s_random.Next(s_edges.Length)];
        switch (s_random.Next(3))
        {
            case 0: return (float)(s_random.NextDouble() * 2 - 1);
            case 1: return (float)((s_random.NextDouble() * 2 - 1) * 10);
            default: return (float)((s_random.NextDouble() * 2 - 1) * 100);
        }
    }

    static float[] AnySpan(int n)
    {
        var a = new float[n];
        for (int i = 0; i < n; i++)
            a[i] = AnyFloat();
        return a;
    }

    static double Ulps(float actual, float expected)
    {
        if (float.IsNaN(actual) || float.IsNaN(expected))
            return float.IsNaN(actual) && float.IsNaN(expected) ? 0 : double.PositiveInfinity;
        if (actual == expected)
            return BitConverter.SingleToInt32Bits(actual) == BitConverter.SingleToInt32Bits(expected) ? 0 : 1; // ±0
        long a = BitConverter.SingleToInt32Bits(actual), e = BitConverter.SingleToInt32Bits(expected);
        a = a < 0 ? int.MinValue - a : a;
        e = e < 0 ? int.MinValue - e : e;
        return Math.Abs(a - e);
    }

    static string Show(float[] a) => "[" + string.Join(", ", Array.ConvertAll(a, v => v.ToString("R"))) + "]";

    // Both throw an ArgumentException, or neither does.
    static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    static void Errors(string name, Action zero, Action net) =>
        Record(name + " (argument errors)", Throws(zero) == Throws(net) ? 0 : 1, "", 0);

    static float[] s_x, s_y;

    // Reported with the element that differs most: x[i] (and y[i]) and the
    // two results.
    static void Elementwise(string name, float[] zero, float[] net, string input, double limit)
    {
        double worst = 0;
        int at = -1;
        for (int i = 0; i < zero.Length; i++)
        {
            double u = Ulps(zero[i], net[i]);
            if (u > worst)
            {
                worst = u;
                at = i;
            }
        }
        if (at >= 0 && s_x != null && at < s_x.Length)
            input = $"[{at}] x={s_x[at]:R} y={s_y[at]:R}: ours {zero[at]:R}, .NET's {net[at]:R}; " + input;
        Record(name, worst, input, limit);
    }

    // For sums: the error relative to the sum of the magnitudes added.
    static void Sum(string name, float zero, float net, double scale, string input, double limit)
    {
        double error;
        if (float.IsNaN(zero) || float.IsNaN(net) || float.IsInfinity(zero) || float.IsInfinity(net) || double.IsInfinity(scale))
            error = zero.Equals(net) || (float.IsNaN(zero) && float.IsNaN(net)) ? 0 : double.PositiveInfinity;
        else
            error = Math.Abs((double)zero - net) / Math.Max(scale, 1e-30);
        Record(name, error, input, limit);
    }

    static double Magnitudes(float[] terms)
    {
        double s = 0;
        foreach (float t in terms)
            s += Math.Abs((double)t);
        return s;
    }

    static int Main(string[] args)
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        int iterations = args.Length > 0 ? int.Parse(args[0]) : 20000;
        s_random = new Random(args.Length > 1 ? int.Parse(args[1]) : 12345);

        var unary = new (string, Unary, Unary, double)[]
        {
            ("Abs", Z.Abs, N.Abs, 0), ("Negate", Z.Negate, N.Negate, 0),
            ("Ceiling", Z.Ceiling, N.Ceiling, 0), ("Floor", Z.Floor, N.Floor, 0),
            ("Truncate", Z.Truncate, N.Truncate, 0), ("Round", Z.Round, N.Round, 0),
            ("Reciprocal", Z.Reciprocal, N.Reciprocal, 0), ("Sqrt", Z.Sqrt, N.Sqrt, 0),
            ("Sigmoid", Z.Sigmoid, N.Sigmoid, double.PositiveInfinity), ("SoftMax", Z.SoftMax, N.SoftMax, double.PositiveInfinity),
        };
        // The functions: ours and .NET's against the function in double,
        // rounded to float (correctly rounded nearly always). Ours must be
        // within the limit; .NET's error is reported for comparison.
        var functions = new (string, Unary, Unary, Func<double, double>)[]
        {
            ("Exp", Z.Exp, N.Exp, Math.Exp), ("Log", Z.Log, N.Log, Math.Log), ("Log2", Z.Log2, N.Log2, Math.Log2),
            ("Log10", Z.Log10, N.Log10, Math.Log10), ("Cbrt", Z.Cbrt, N.Cbrt, Math.Cbrt),
            ("Sin", Z.Sin, N.Sin, Math.Sin), ("Cos", Z.Cos, N.Cos, Math.Cos), ("Tan", Z.Tan, N.Tan, Math.Tan),
            ("Asin", Z.Asin, N.Asin, Math.Asin), ("Acos", Z.Acos, N.Acos, Math.Acos), ("Atan", Z.Atan, N.Atan, Math.Atan),
            ("Sinh", Z.Sinh, N.Sinh, Math.Sinh), ("Cosh", Z.Cosh, N.Cosh, Math.Cosh), ("Tanh", Z.Tanh, N.Tanh, Math.Tanh),
            ("Asinh", Z.Asinh, N.Asinh, Math.Asinh), ("Acosh", Z.Acosh, N.Acosh, Math.Acosh), ("Atanh", Z.Atanh, N.Atanh, Math.Atanh),
            ("Sigmoid", Z.Sigmoid, N.Sigmoid, v => 1 / (1 + Math.Exp(-v))),
        };
        var binary = new (string, Binary, Binary, double)[]
        {
            ("Add", Z.Add, N.Add, 0), ("Subtract", Z.Subtract, N.Subtract, 0),
            ("Multiply", Z.Multiply, N.Multiply, 0), ("Divide", Z.Divide, N.Divide, 0),
            ("Max", Z.Max, N.Max, 0), ("Min", Z.Min, N.Min, 0),
            ("MaxMagnitude", Z.MaxMagnitude, N.MaxMagnitude, 0), ("MinMagnitude", Z.MinMagnitude, N.MinMagnitude, 0),
            ("CopySign", Z.CopySign, N.CopySign, 0), ("Hypot", Z.Hypot, N.Hypot, 1),
            ("Pow", Z.Pow, N.Pow, 2), ("Atan2", Z.Atan2, N.Atan2, 2),
        };
        var binaryScalar = new (string, BinaryScalar, BinaryScalar, double)[]
        {
            ("Add (x, scalar)", Z.Add, N.Add, 0), ("Subtract (x, scalar)", Z.Subtract, N.Subtract, 0),
            ("Multiply (x, scalar)", Z.Multiply, N.Multiply, 0), ("Divide (x, scalar)", Z.Divide, N.Divide, 0),
            ("Pow (x, scalar)", Z.Pow, N.Pow, 2),
        };
        var scalarBinary = new (string, ScalarBinary, ScalarBinary, double)[]
        {
            ("Subtract (scalar, y)", Z.Subtract, N.Subtract, 0), ("Divide (scalar, y)", Z.Divide, N.Divide, 0),
        };
        var ternary = new (string, Ternary, Ternary)[]
        {
            ("MultiplyAdd", Z.MultiplyAdd, N.MultiplyAdd), ("AddMultiply", Z.AddMultiply, N.AddMultiply),
        };
        var ternaryLast = new (string, TernaryLastScalar, TernaryLastScalar)[]
        {
            ("MultiplyAdd (x, y, scalar)", Z.MultiplyAdd, N.MultiplyAdd), ("AddMultiply (x, y, scalar)", Z.AddMultiply, N.AddMultiply),
        };
        var ternaryMiddle = new (string, TernaryMiddleScalar, TernaryMiddleScalar)[]
        {
            ("MultiplyAdd (x, scalar, z)", Z.MultiplyAdd, N.MultiplyAdd), ("AddMultiply (x, scalar, z)", Z.AddMultiply, N.AddMultiply),
        };
        var folds = new (string, Reduce, Reduce)[]
        {
            ("Max (span)", Z.Max, N.Max), ("Min (span)", Z.Min, N.Min),
            ("MaxMagnitude (span)", Z.MaxMagnitude, N.MaxMagnitude), ("MinMagnitude (span)", Z.MinMagnitude, N.MinMagnitude),
        };
        var indexes = new (string, Index, Index)[]
        {
            ("IndexOfMax", Z.IndexOfMax, N.IndexOfMax), ("IndexOfMin", Z.IndexOfMin, N.IndexOfMin),
            ("IndexOfMaxMagnitude", Z.IndexOfMaxMagnitude, N.IndexOfMaxMagnitude),
            ("IndexOfMinMagnitude", Z.IndexOfMinMagnitude, N.IndexOfMinMagnitude),
        };

        for (int it = 0; it < iterations; it++)
        {
            int n = s_random.Next(71);
            float[] x = AnySpan(n), y = AnySpan(n), z = AnySpan(n);
            float s = AnyFloat();
            string input = $"x={Show(x)} y={Show(y)} z={Show(z)} s={s:R}";
            s_x = x;
            s_y = y;
            var dz = new float[n];
            var dn = new float[n];

            foreach (var (name, zero, net, limit) in unary)
            {
                bool zt = Throws(() => zero(x, dz)), nt = Throws(() => net(x, dn));
                Record(name + " (argument errors)", zt == nt ? 0 : 1, input, 0);
                if (!zt && !nt)
                    Elementwise(name, dz, dn, input, limit);
            }
            foreach (var (name, zero, net, f) in functions)
            {
                if (n == 0)
                    continue;
                var reference = Array.ConvertAll(x, v => (float)f(v));
                zero(x, dz);
                net(x, dn);
                // The vector forms (see TensorPrimitives.cs) are a few ulps off.
                bool fast = name is "Exp" or "Tanh" or "Sigmoid";
                Elementwise(name + " (ours vs exact)", dz, reference, input, fast ? 4 : 1);
                Elementwise(name + " (.NET's vs exact, for comparison)", dn, reference, input, double.PositiveInfinity);
            }
            if (n > 0 && Array.TrueForAll(x, v => float.IsFinite(v)))
            {
                // SoftMax in double, as the reference.
                double max = double.NegativeInfinity, total = 0;
                foreach (float v in x)
                    max = Math.Max(max, v);
                foreach (float v in x)
                    total += Math.Exp(v - max);
                var reference = Array.ConvertAll(x, v => (float)(Math.Exp(v - max) / total));
                Z.SoftMax(x, dz);
                Elementwise("SoftMax (ours vs exact)", dz, reference, input, 6);
                if (Array.TrueForAll(x, v => Math.Abs(v) < 80))
                {
                    N.SoftMax(x, dn);
                    Elementwise("SoftMax (.NET's vs exact, for comparison)", dn, reference, input, double.PositiveInfinity);
                }
            }
            foreach (var (name, zero, net, limit) in binary)
            {
                zero(x, y, dz);
                net(x, y, dn);
                Elementwise(name, dz, dn, input, limit);
            }
            foreach (var (name, zero, net, limit) in binaryScalar)
            {
                zero(x, s, dz);
                net(x, s, dn);
                Elementwise(name, dz, dn, input, limit);
            }
            foreach (var (name, zero, net, limit) in scalarBinary)
            {
                zero(s, y, dz);
                net(s, y, dn);
                Elementwise(name, dz, dn, input, limit);
            }
            foreach (var (name, zero, net) in ternary)
            {
                zero(x, y, z, dz);
                net(x, y, z, dn);
                Elementwise(name, dz, dn, input, 0);
            }
            foreach (var (name, zero, net) in ternaryLast)
            {
                zero(x, y, s, dz);
                net(x, y, s, dn);
                Elementwise(name, dz, dn, input, 0);
            }
            foreach (var (name, zero, net) in ternaryMiddle)
            {
                zero(x, s, z, dz);
                net(x, s, z, dn);
                Elementwise(name, dz, dn, input, 0);
            }

            // In place: the destination is the input.
            float[] inPlaceZ = (float[])x.Clone(), inPlaceN = (float[])x.Clone();
            Z.Multiply(inPlaceZ, y, inPlaceZ);
            N.Multiply(inPlaceN, y, inPlaceN);
            Elementwise("Multiply (in place)", inPlaceZ, inPlaceN, input, 0);

            foreach (var (name, zero, net) in folds)
            {
                bool zt = Throws(() => zero(x)), nt = Throws(() => net(x));
                Record(name + " (argument errors)", zt == nt ? 0 : 1, input, 0);
                if (!zt && !nt)
                    Elementwise(name, new[] { zero(x) }, new[] { net(x) }, input, 0);
            }
            foreach (var (name, zero, net) in indexes)
                Record(name, zero(x) == net(x) ? 0 : 1, input, 0);

            // Sums, relative to the magnitudes added.
            const double SumLimit = 1e-6;
            Sum("Sum", Z.Sum(x), N.Sum(x), Magnitudes(x), input, SumLimit);
            Sum("SumOfMagnitudes", Z.SumOfMagnitudes(x), N.SumOfMagnitudes(x), Magnitudes(x), input, SumLimit);
            float[] products = new float[n], squares = new float[n], differences = new float[n];
            for (int i = 0; i < n; i++)
            {
                products[i] = x[i] * y[i];
                squares[i] = x[i] * x[i];
                differences[i] = (x[i] - y[i]) * (x[i] - y[i]);
            }
            Sum("Dot", Z.Dot(x, y), N.Dot(x, y), Magnitudes(products), input, SumLimit);
            Sum("SumOfSquares", Z.SumOfSquares(x), N.SumOfSquares(x), Magnitudes(squares), input, SumLimit);
            Sum("Norm", Z.Norm(x), N.Norm(x), Math.Sqrt(Magnitudes(squares)), input, SumLimit);
            if (n > 0)
            {
                Sum("Distance", Z.Distance(x, y), N.Distance(x, y), Math.Sqrt(Magnitudes(differences)), input, SumLimit);
                Sum("CosineSimilarity", Z.CosineSimilarity(x, y), N.CosineSimilarity(x, y),
                    Magnitudes(products) / Math.Sqrt(Magnitudes(squares) * Magnitudes(Array.ConvertAll(y, v => v * v))), input, SumLimit);
                // Products of a few finite values, where neither overflows or
                // underflows (otherwise the order decides between 0, ∞ and NaN).
                int m = Math.Min(n, 8);
                bool tame = Array.TrueForAll(x, v => float.IsFinite(v) && Math.Abs(v) > 1e-20 && Math.Abs(v) < 1e20)
                         && Array.TrueForAll(y, v => float.IsFinite(v) && Math.Abs(v) > 1e-20 && Math.Abs(v) < 1e20);
                if (tame)
                {
                Sum("Product", Z.Product(x.AsSpan(0, m)), N.Product(x.AsSpan(0, m)),
                    Math.Abs((double)N.Product(x.AsSpan(0, m))), input, SumLimit);
                Sum("ProductOfSums", Z.ProductOfSums(x.AsSpan(0, m), y.AsSpan(0, m)), N.ProductOfSums(x.AsSpan(0, m), y.AsSpan(0, m)),
                    Math.Abs((double)N.ProductOfSums(x.AsSpan(0, m), y.AsSpan(0, m))), input, SumLimit);
                Sum("ProductOfDifferences", Z.ProductOfDifferences(x.AsSpan(0, m), y.AsSpan(0, m)), N.ProductOfDifferences(x.AsSpan(0, m), y.AsSpan(0, m)),
                    Math.Abs((double)N.ProductOfDifferences(x.AsSpan(0, m), y.AsSpan(0, m))), input, SumLimit);
                }
            }

            var hz = new Half[n];
            var hn = new Half[n];
            Z.ConvertToHalf(x, hz);
            N.ConvertToHalf(x, hn);
            Record("ConvertToHalf", hz.AsSpan().SequenceEqual(hn) ? 0 : 1, input, 0);
            Z.ConvertToSingle(hn, dz);
            N.ConvertToSingle(hn, dn);
            Elementwise("ConvertToSingle", dz, dn, input, 0);
        }

        // Argument errors: lengths, short destinations, partial overlap, empty spans.
        float[] a = { 1, 2, 3, 4 }, b = { 1, 2, 3 }, buffer = new float[8];
        Errors("Add (different lengths)", () => Z.Add(a, b, new float[4]), () => N.Add(a, b, new float[4]));
        Errors("Add (short destination)", () => Z.Add(a, a, new float[3]), () => N.Add(a, a, new float[3]));
        Errors("Add (partial overlap)", () => Z.Add(buffer.AsSpan(0, 4), a, buffer.AsSpan(1, 4)),
                                        () => N.Add(buffer.AsSpan(0, 4), a, buffer.AsSpan(1, 4)));
        Errors("Exp (short destination)", () => Z.Exp(a, new float[2]), () => N.Exp(a, new float[2]));
        Errors("Dot (different lengths)", () => Z.Dot(a, b), () => N.Dot(a, b));
        Errors("Max (empty)", () => Z.Max(ReadOnlySpan<float>.Empty), () => N.Max(ReadOnlySpan<float>.Empty));
        Errors("Product (empty)", () => Z.Product(ReadOnlySpan<float>.Empty), () => N.Product(ReadOnlySpan<float>.Empty));
        Errors("Distance (empty)", () => Z.Distance(ReadOnlySpan<float>.Empty, ReadOnlySpan<float>.Empty),
                                   () => N.Distance(ReadOnlySpan<float>.Empty, ReadOnlySpan<float>.Empty));
        Errors("CosineSimilarity (empty)", () => Z.CosineSimilarity(ReadOnlySpan<float>.Empty, ReadOnlySpan<float>.Empty),
                                           () => N.CosineSimilarity(ReadOnlySpan<float>.Empty, ReadOnlySpan<float>.Empty));
        Errors("Sum (empty)", () => Z.Sum(ReadOnlySpan<float>.Empty), () => N.Sum(ReadOnlySpan<float>.Empty));
        Errors("SoftMax (empty)", () => Z.SoftMax(ReadOnlySpan<float>.Empty, Span<float>.Empty),
                                  () => N.SoftMax(ReadOnlySpan<float>.Empty, Span<float>.Empty));
        Errors("Sigmoid (empty)", () => Z.Sigmoid(ReadOnlySpan<float>.Empty, Span<float>.Empty),
                                  () => N.Sigmoid(ReadOnlySpan<float>.Empty, Span<float>.Empty));
        Errors("ConvertToHalf (short destination)", () => Z.ConvertToHalf(a, new Half[2]), () => N.ConvertToHalf(a, new Half[2]));

        int failures = 0;
        foreach (var (name, worst, inputText, limit) in s_results)
        {
            bool over = worst > limit;
            if (over)
                failures++;
            Console.Write($"{(over ? "OVER" : "ok  ")} {name,-42} {worst,-12:G4} (limit {limit:G3})");
            Console.WriteLine(over ? $"  at {(inputText.Length > 400 ? inputText.Substring(0, 400) + "..." : inputText)}" : "");
        }
        Console.WriteLine(failures == 0 ? "All within limits." : $"{failures} over their limit.");
        return failures;
    }
}
