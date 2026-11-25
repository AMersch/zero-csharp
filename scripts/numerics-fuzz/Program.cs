// Runs zerocore's numerics (namespace ZeroTest) and .NET's on the same random
// and edge values and reports, per operation, the largest difference and the
// input that gave it. Scalar functions are compared in units in the last
// place; Complex, vectors and matrices by the error relative to the size of
// .NET's result. Exit code = number of operations over their limit.

using System;
using System.Collections.Generic;
using N = System.Numerics;
using Z = ZeroTest;

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

    // ---- inputs ----------------------------------------------------------

    static readonly double[] s_edges =
    {
        0, -0.0, 1, -1, 0.5, -0.5, 2, -2, 1e-300, -1e-300, 1e300, -1e300, 4.9e-324,
        double.PositiveInfinity, double.NegativeInfinity, double.NaN, Math.PI, -Math.PI,
        Math.PI / 2, 710, -710, 22, 1e-8, 0.9999999999, 1.0000000001,
    };

    static double AnyDouble(int iteration)
    {
        if (iteration % 50 == 0)
            return s_edges[s_random.Next(s_edges.Length)];
        switch (s_random.Next(4))
        {
            case 0: return s_random.NextDouble() * 2 - 1;
            case 1: return (s_random.NextDouble() * 2 - 1) * 20;
            case 2: return (s_random.NextDouble() * 2 - 1) * 1e-6;
            default:
                double magnitude = Math.Pow(10, s_random.NextDouble() * 600 - 300);
                return s_random.Next(2) == 0 ? magnitude : -magnitude;
        }
    }

    static float AnyFloat() => (float)((s_random.NextDouble() * 2 - 1) * 10);

    // ---- distances -------------------------------------------------------

    static long Ordered(double d)
    {
        long bits = BitConverter.DoubleToInt64Bits(d);
        return bits < 0 ? long.MinValue - bits : bits;
    }

    static double Ulps(double actual, double expected)
    {
        if (double.IsNaN(actual) || double.IsNaN(expected))
            return double.IsNaN(actual) && double.IsNaN(expected) ? 0 : double.PositiveInfinity;
        if (actual == expected)
            return 0;
        return (double)Math.Abs((decimal)Ordered(actual) - Ordered(expected));
    }

    static double UlpsF(float actual, float expected)
    {
        if (float.IsNaN(actual) || float.IsNaN(expected))
            return float.IsNaN(actual) && float.IsNaN(expected) ? 0 : double.PositiveInfinity;
        if (actual == expected)
            return 0;
        int a = BitConverter.SingleToInt32Bits(actual), e = BitConverter.SingleToInt32Bits(expected);
        long oa = a < 0 ? int.MinValue - (long)a : a, oe = e < 0 ? int.MinValue - (long)e : e;
        return Math.Abs(oa - oe);
    }

    // The error relative to the result's size; values that aren't finite
    // must match in kind (both NaN, or the same infinity).
    static double Relative(double[] actual, double[] expected, double floor = 0)
    {
        double scale = floor;
        foreach (double e in expected)
            if (double.IsFinite(e))
                scale = Math.Max(scale, Math.Abs(e));
        double worst = 0;
        for (int i = 0; i < actual.Length; i++)
        {
            double a = actual[i], e = expected[i];
            if (!double.IsFinite(e) || !double.IsFinite(a))
            {
                if (!(a.Equals(e)))
                    return double.PositiveInfinity;
                continue;
            }
            worst = Math.Max(worst, Math.Abs(a - e) / Math.Max(scale, double.Epsilon));
        }
        return worst;
    }

    static double[] D(Z.Complex c) => new[] { c.Real, c.Imaginary };
    static double[] D(N.Complex c) => new[] { c.Real, c.Imaginary };
    static double[] D(params float[] values) => Array.ConvertAll(values, v => (double)v);

    static double[] D(Z.Matrix4x4 m) => D(m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
                                          m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);
    static double[] D(N.Matrix4x4 m) => D(m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
                                          m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);
    static double[] D(Z.Matrix3x2 m) => D(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32);
    static double[] D(N.Matrix3x2 m) => D(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32);
    static double[] D(Z.Quaternion q) => D(q.X, q.Y, q.Z, q.W);
    static double[] D(N.Quaternion q) => D(q.X, q.Y, q.Z, q.W);
    static double[] D(Z.Vector2 v) => D(v.X, v.Y);
    static double[] D(N.Vector2 v) => D(v.X, v.Y);
    static double[] D(Z.Vector3 v) => D(v.X, v.Y, v.Z);
    static double[] D(N.Vector3 v) => D(v.X, v.Y, v.Z);
    static double[] D(Z.Vector4 v) => D(v.X, v.Y, v.Z, v.W);
    static double[] D(N.Vector4 v) => D(v.X, v.Y, v.Z, v.W);
    static double[] D(Z.Plane p) => D(p.Normal.X, p.Normal.Y, p.Normal.Z, p.D);
    static double[] D(N.Plane p) => D(p.Normal.X, p.Normal.Y, p.Normal.Z, p.D);

    static string Show(double d) => d.ToString("R");
    static string Show(Z.Complex c) => $"({Show(c.Real)}, {Show(c.Imaginary)})";

    // ---- the checks ------------------------------------------------------

    static void Scalar(string name, Func<double, double> zero, Func<double, double> net, double x, double limit) =>
        Record(name, Ulps(zero(x), net(x)), Show(x), limit);

    static void ScalarF(string name, Func<float, float> zero, Func<float, float> net, float x, double limit) =>
        Record(name, UlpsF(zero(x), net(x)), x.ToString("R"), limit);

    static void Complex1(string name, Func<Z.Complex, Z.Complex> zero, Func<N.Complex, N.Complex> net,
                         double re, double im, double limit)
    {
        var z = zero(new Z.Complex(re, im));
        var n = net(new N.Complex(re, im));
        Record(name, Relative(D(z), D(n)), $"({Show(re)}, {Show(im)}): {Show(z)} vs {Show(n.Real)}, {Show(n.Imaginary)}", limit);
    }

    static void Complex2(string name, Func<Z.Complex, Z.Complex, Z.Complex> zero, Func<N.Complex, N.Complex, N.Complex> net,
                         double a, double b, double c, double d, double limit) =>
        Record(name, Relative(D(zero(new Z.Complex(a, b), new Z.Complex(c, d))), D(net(new N.Complex(a, b), new N.Complex(c, d)))),
               $"({Show(a)}, {Show(b)}) ({Show(c)}, {Show(d)})", limit);

    // Floats: relative to the result's size, or absolute below 1, since
    // the inputs are about 1 to 10 and a result near 0 has lost digits to
    // cancellation in .NET's as much as in ours.
    static void Values(string name, double[] zero, double[] net, string input, double limit) =>
        Record(name, Relative(zero, net, 1), input, limit);

    static Z.Vector3 ZV3(float[] f, int i) => new Z.Vector3(f[i], f[i + 1], f[i + 2]);
    static N.Vector3 NV3(float[] f, int i) => new N.Vector3(f[i], f[i + 1], f[i + 2]);

    static Z.Matrix4x4 ZM(float[] f, int i) => new Z.Matrix4x4(
        f[i], f[i + 1], f[i + 2], f[i + 3], f[i + 4], f[i + 5], f[i + 6], f[i + 7],
        f[i + 8], f[i + 9], f[i + 10], f[i + 11], f[i + 12], f[i + 13], f[i + 14], f[i + 15]);
    static N.Matrix4x4 NM(float[] f, int i) => new N.Matrix4x4(
        f[i], f[i + 1], f[i + 2], f[i + 3], f[i + 4], f[i + 5], f[i + 6], f[i + 7],
        f[i + 8], f[i + 9], f[i + 10], f[i + 11], f[i + 12], f[i + 13], f[i + 14], f[i + 15]);

    static int Main(string[] args)
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        int iterations = args.Length > 0 ? int.Parse(args[0]) : 200000;
        s_random = new Random(args.Length > 1 ? int.Parse(args[1]) : 12345);

        for (int it = 0; it < iterations; it++)
        {
            // Math, in units in the last place. .NET calls the platform's C
            // library, which is itself within an ulp or so of exact.
            double x = AnyDouble(it), y = AnyDouble(it + 1);
            Scalar("Math.Asin", Z.Math.Asin, Math.Asin, x, 4);
            Scalar("Math.Acos", Z.Math.Acos, Math.Acos, x, 4);
            Scalar("Math.Atan", Z.Math.Atan, Math.Atan, x, 2);
            Scalar("Math.Sinh", Z.Math.Sinh, Math.Sinh, x, 4);
            Scalar("Math.Cosh", Z.Math.Cosh, Math.Cosh, x, 4);
            Scalar("Math.Tanh", Z.Math.Tanh, Math.Tanh, x, 4);
            Scalar("Math.Asinh", Z.Math.Asinh, Math.Asinh, x, 4);
            Scalar("Math.Acosh", Z.Math.Acosh, Math.Acosh, Math.Abs(x) + 1, 4);
            Scalar("Math.Atanh", Z.Math.Atanh, Math.Atanh, x, 4);
            Scalar("Math.Cbrt", Z.Math.Cbrt, Math.Cbrt, x, 1);
            Record("Math.Log(a, b)", Ulps(Z.Math.Log(Math.Abs(x), Math.Abs(y)), Math.Log(Math.Abs(x), Math.Abs(y))),
                   $"{Show(Math.Abs(x))} {Show(Math.Abs(y))}", 0);
            Record("Math.IEEERemainder", Ulps(Z.Math.IEEERemainder(x, y), Math.IEEERemainder(x, y)), $"{Show(x)} {Show(y)}", 0);
            Record("Math.CopySign", Ulps(Z.Math.CopySign(x, y), Math.CopySign(x, y)), $"{Show(x)} {Show(y)}", 0);
            if (!double.IsNaN(x))
                Record("Math.Sign", Z.Math.Sign(x) == Math.Sign(x) ? 0 : 1, Show(x), 0);

            float f = (float)x;
            ScalarF("MathF.Asin", Z.MathF.Asin, MathF.Asin, f, 1);
            ScalarF("MathF.Acos", Z.MathF.Acos, MathF.Acos, f, 1);
            ScalarF("MathF.Atan", Z.MathF.Atan, MathF.Atan, f, 1);
            ScalarF("MathF.Tan", Z.MathF.Tan, MathF.Tan, f, 1);
            ScalarF("MathF.Sinh", Z.MathF.Sinh, MathF.Sinh, f, 1);
            ScalarF("MathF.Cosh", Z.MathF.Cosh, MathF.Cosh, f, 1);
            ScalarF("MathF.Cbrt", Z.MathF.Cbrt, MathF.Cbrt, f, 1);
            Record("MathF.IEEERemainder", UlpsF(Z.MathF.IEEERemainder(f, (float)y), MathF.IEEERemainder(f, (float)y)),
                   $"{f:R} {(float)y:R}", 0);

            // Complex, relative to the size of .NET's result.
            double a = AnyDouble(it), b = AnyDouble(it + 2), c = AnyDouble(it + 3), d = AnyDouble(it + 4);
            if (it % 3 == 0)
            {
                // Moderate values, where the functions are meant to be accurate.
                a = (s_random.NextDouble() * 2 - 1) * 4;
                b = (s_random.NextDouble() * 2 - 1) * 4;
            }
            const double Tight = 1e-15, Loose = 1e-13;
            Complex2("Complex *", (p, q) => p * q, (p, q) => p * q, a, b, c, d, Tight);
            Complex2("Complex /", (p, q) => p / q, (p, q) => p / q, a, b, c, d, Tight);
            Record("Complex / double", Relative(D(new Z.Complex(a, b) / c), D(new N.Complex(a, b) / c)),
                   $"({Show(a)}, {Show(b)}) / {Show(c)}", Tight);
            Record("double / Complex", Relative(D(a / new Z.Complex(c, d)), D(a / new N.Complex(c, d))),
                   $"{Show(a)} / ({Show(c)}, {Show(d)})", Tight);
            Record("Complex.Abs", Ulps(Z.Complex.Abs(new Z.Complex(a, b)), N.Complex.Abs(new N.Complex(a, b))),
                   $"({Show(a)}, {Show(b)})", 2);
            Complex1("Complex.Sqrt", Z.Complex.Sqrt, N.Complex.Sqrt, a, b, Tight);
            Complex1("Complex.Exp", Z.Complex.Exp, N.Complex.Exp, a, b, Tight);
            // Near |z| = 1 the real part, log |z|, magnifies an ulp of |z|.
            Complex1("Complex.Log", Z.Complex.Log, N.Complex.Log, a, b, Loose);
            Complex1("Complex.Log10", Z.Complex.Log10, N.Complex.Log10, a, b, Loose);
            Complex1("Complex.Sin", Z.Complex.Sin, N.Complex.Sin, a, b, Loose);
            Complex1("Complex.Cos", Z.Complex.Cos, N.Complex.Cos, a, b, Loose);
            // Near the poles .NET's Tan cancels (see Complex.cs; the sample
            // checks ours against the true value there), and its Atan does
            // for small z: compare those where .NET's is accurate.
            bool nearPole = Math.Abs(Math.Cos(a)) < 0.1 && Math.Abs(b) < 0.1;
            bool nearPoleOfTanh = Math.Abs(Math.Cos(b)) < 0.1 && Math.Abs(a) < 0.1;
            if (!nearPole)
                Complex1("Complex.Tan", Z.Complex.Tan, N.Complex.Tan, a, b, Loose);
            Complex1("Complex.Sinh", Z.Complex.Sinh, N.Complex.Sinh, a, b, Loose);
            Complex1("Complex.Cosh", Z.Complex.Cosh, N.Complex.Cosh, a, b, Loose);
            if (!nearPoleOfTanh)
                Complex1("Complex.Tanh", Z.Complex.Tanh, N.Complex.Tanh, a, b, Loose);
            Complex1("Complex.Asin", Z.Complex.Asin, N.Complex.Asin, a, b, Loose);
            Complex1("Complex.Acos", Z.Complex.Acos, N.Complex.Acos, a, b, Loose);
            // At infinity ours gives the limit, (±π/2, 0); .NET 10 gives NaN.
            if (double.IsFinite(a) && double.IsFinite(b) && N.Complex.Abs(new N.Complex(a, b)) > 0.1)
                Complex1("Complex.Atan", Z.Complex.Atan, N.Complex.Atan, a, b, Loose);
            Complex1("Complex.Reciprocal", Z.Complex.Reciprocal, N.Complex.Reciprocal, a, b, Tight);
            if (it % 3 == 0)
                Complex2("Complex.Pow", Z.Complex.Pow, N.Complex.Pow, a, b,
                         (s_random.NextDouble() * 2 - 1) * 3, (s_random.NextDouble() * 2 - 1) * 3, Loose);

            // Vectors and matrices: floats of moderate size.
            float[] v = new float[40];
            for (int i = 0; i < v.Length; i++)
                v[i] = AnyFloat();
            string input = string.Join(" ", Array.ConvertAll(v, e => e.ToString("R")));
            const double FloatTight = 1e-5, FloatLoose = 1e-4;

            var zm = ZM(v, 0);
            var nm = NM(v, 0);
            var zm2 = ZM(v, 16);
            var nm2 = NM(v, 16);
            Values("Matrix4x4 *", D(zm * zm2), D(nm * nm2), input, FloatTight);
            Values("Matrix4x4.GetDeterminant", D(zm.GetDeterminant()), D(nm.GetDeterminant()), input, FloatTight);
            bool zi = Z.Matrix4x4.Invert(zm, out var zinv);
            bool ni = N.Matrix4x4.Invert(nm, out var ninv);
            Record("Matrix4x4.Invert (result)", zi == ni ? 0 : 1, input, 0);
            // An inverse is as accurate as the matrix is well conditioned:
            // compare what each gives back when multiplied by the matrix.
            // An inverse is only as accurate as the matrix is well
            // conditioned, so this is how far M * inverse is from the
            // identity with ours, over (at least 1e-5) how far with .NET's.
            if (zi && ni)
                Record("Matrix4x4.Invert (ours vs .NET's error)",
                       Relative(D(zm * zinv), D(Z.Matrix4x4.Identity)) / Math.Max(Relative(D(nm * ninv), D(N.Matrix4x4.Identity)), 1e-5),
                       input, 10);
            Values("Matrix4x4.Transpose", D(Z.Matrix4x4.Transpose(zm)), D(N.Matrix4x4.Transpose(nm)), input, 0);
            Values("Matrix4x4.Lerp", D(Z.Matrix4x4.Lerp(zm, zm2, v[32])), D(N.Matrix4x4.Lerp(nm, nm2, v[32])), input, FloatTight);

            var za = Z.Vector3.Normalize(ZV3(v, 32));
            var na = N.Vector3.Normalize(NV3(v, 32));
            float angle = v[35], yaw = v[36], pitch = v[37], roll = v[38];
            Values("Matrix4x4.CreateFromAxisAngle", D(Z.Matrix4x4.CreateFromAxisAngle(za, angle)),
                   D(N.Matrix4x4.CreateFromAxisAngle(na, angle)), input, FloatTight);
            Values("Matrix4x4.CreateFromYawPitchRoll", D(Z.Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll)),
                   D(N.Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll)), input, FloatTight);
            Values("Matrix4x4.CreateRotationX", D(Z.Matrix4x4.CreateRotationX(angle, ZV3(v, 0))),
                   D(N.Matrix4x4.CreateRotationX(angle, NV3(v, 0))), input, FloatTight);
            Values("Matrix4x4.CreateRotationY", D(Z.Matrix4x4.CreateRotationY(angle, ZV3(v, 0))),
                   D(N.Matrix4x4.CreateRotationY(angle, NV3(v, 0))), input, FloatTight);
            Values("Matrix4x4.CreateRotationZ", D(Z.Matrix4x4.CreateRotationZ(angle, ZV3(v, 0))),
                   D(N.Matrix4x4.CreateRotationZ(angle, NV3(v, 0))), input, FloatTight);
            Values("Matrix4x4.CreateScale", D(Z.Matrix4x4.CreateScale(ZV3(v, 3), ZV3(v, 6))),
                   D(N.Matrix4x4.CreateScale(NV3(v, 3), NV3(v, 6))), input, FloatTight);
            Values("Matrix4x4.CreateLookAt", D(Z.Matrix4x4.CreateLookAt(ZV3(v, 0), ZV3(v, 3), ZV3(v, 6))),
                   D(N.Matrix4x4.CreateLookAt(NV3(v, 0), NV3(v, 3), NV3(v, 6))), input, FloatLoose);
            Values("Matrix4x4.CreateWorld", D(Z.Matrix4x4.CreateWorld(ZV3(v, 0), ZV3(v, 3), ZV3(v, 6))),
                   D(N.Matrix4x4.CreateWorld(NV3(v, 0), NV3(v, 3), NV3(v, 6))), input, FloatLoose);
            Values("Matrix4x4.CreateBillboard", D(Z.Matrix4x4.CreateBillboard(ZV3(v, 0), ZV3(v, 3), ZV3(v, 6), ZV3(v, 9))),
                   D(N.Matrix4x4.CreateBillboard(NV3(v, 0), NV3(v, 3), NV3(v, 6), NV3(v, 9))), input, FloatLoose);
            float near = Math.Abs(v[12]) + 0.1f, far = near + Math.Abs(v[13]) + 0.1f;
            float fov = (Math.Abs(v[14]) % 3.0f) + 0.05f;
            Values("Matrix4x4.CreatePerspectiveFieldOfView", D(Z.Matrix4x4.CreatePerspectiveFieldOfView(fov, Math.Abs(v[15]) + 0.1f, near, far)),
                   D(N.Matrix4x4.CreatePerspectiveFieldOfView(fov, Math.Abs(v[15]) + 0.1f, near, far)), input, FloatTight);
            Values("Matrix4x4.CreatePerspective", D(Z.Matrix4x4.CreatePerspective(v[16], v[17], near, far)),
                   D(N.Matrix4x4.CreatePerspective(v[16], v[17], near, far)), input, FloatTight);
            Values("Matrix4x4.CreatePerspectiveOffCenter", D(Z.Matrix4x4.CreatePerspectiveOffCenter(v[16], v[17], v[18], v[19], near, far)),
                   D(N.Matrix4x4.CreatePerspectiveOffCenter(v[16], v[17], v[18], v[19], near, far)), input, FloatTight);
            Values("Matrix4x4.CreateOrthographic", D(Z.Matrix4x4.CreateOrthographic(v[16], v[17], v[18], v[19])),
                   D(N.Matrix4x4.CreateOrthographic(v[16], v[17], v[18], v[19])), input, FloatTight);
            Values("Matrix4x4.CreateOrthographicOffCenter", D(Z.Matrix4x4.CreateOrthographicOffCenter(v[16], v[17], v[18], v[19], v[20], v[21])),
                   D(N.Matrix4x4.CreateOrthographicOffCenter(v[16], v[17], v[18], v[19], v[20], v[21])), input, FloatTight);
            var zplane = new Z.Plane(ZV3(v, 22), v[25]);
            var nplane = new N.Plane(NV3(v, 22), v[25]);
            Values("Matrix4x4.CreateReflection", D(Z.Matrix4x4.CreateReflection(zplane)),
                   D(N.Matrix4x4.CreateReflection(nplane)), input, FloatTight);
            Values("Matrix4x4.CreateShadow", D(Z.Matrix4x4.CreateShadow(ZV3(v, 26), zplane)),
                   D(N.Matrix4x4.CreateShadow(NV3(v, 26), nplane)), input, FloatTight);

            var zq = Z.Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
            var nq = N.Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
            var zq2 = Z.Quaternion.CreateFromAxisAngle(za, angle);
            var nq2 = N.Quaternion.CreateFromAxisAngle(na, angle);
            Values("Quaternion.CreateFromYawPitchRoll", D(zq), D(nq), input, FloatTight);
            Values("Quaternion *", D(zq * zq2), D(nq * nq2), input, FloatTight);
            Values("Quaternion /", D(zq / zq2), D(nq / nq2), input, FloatTight);
            Values("Quaternion.Concatenate", D(Z.Quaternion.Concatenate(zq, zq2)), D(N.Quaternion.Concatenate(nq, nq2)), input, FloatTight);
            Values("Quaternion.Inverse", D(Z.Quaternion.Inverse(zq)), D(N.Quaternion.Inverse(nq)), input, FloatTight);
            Values("Quaternion.Slerp", D(Z.Quaternion.Slerp(zq, zq2, v[39] / 10)), D(N.Quaternion.Slerp(nq, nq2, v[39] / 10)), input, FloatTight);
            Values("Quaternion.Lerp", D(Z.Quaternion.Lerp(zq, zq2, v[39] / 10)), D(N.Quaternion.Lerp(nq, nq2, v[39] / 10)), input, FloatTight);
            var zrot = Z.Matrix4x4.CreateFromQuaternion(zq);
            var nrot = N.Matrix4x4.CreateFromQuaternion(nq);
            Values("Matrix4x4.CreateFromQuaternion", D(zrot), D(nrot), input, FloatTight);
            Values("Quaternion.CreateFromRotationMatrix", D(Z.Quaternion.CreateFromRotationMatrix(zrot)),
                   D(N.Quaternion.CreateFromRotationMatrix(nrot)), input, FloatTight);

            // Decompose a matrix made of scale, rotation and translation.
            var zs = Z.Matrix4x4.CreateScale(ZV3(v, 3)) * zrot * Z.Matrix4x4.CreateTranslation(ZV3(v, 6));
            var ns = N.Matrix4x4.CreateScale(NV3(v, 3)) * nrot * N.Matrix4x4.CreateTranslation(NV3(v, 6));
            bool zd = Z.Matrix4x4.Decompose(zs, out var zscale, out var zdrot, out var ztrans);
            bool nd = N.Matrix4x4.Decompose(ns, out var nscale, out var ndrot, out var ntrans);
            Record("Matrix4x4.Decompose (result)", zd == nd ? 0 : 1, input, 0);
            if (zd && nd)
            {
                // The same transform, rebuilt from either decomposition
                // (a quaternion and its negation are the same rotation, and
                // a mirror image can sit on any axis).
                var zback = Z.Matrix4x4.CreateScale(zscale) * Z.Matrix4x4.CreateFromQuaternion(zdrot) * Z.Matrix4x4.CreateTranslation(ztrans);
                var nback = N.Matrix4x4.CreateScale(nscale) * N.Matrix4x4.CreateFromQuaternion(ndrot) * N.Matrix4x4.CreateTranslation(ntrans);
                Values("Matrix4x4.Decompose (rebuilt)", D(zback), D(nback), input, FloatLoose);
            }

            Values("Vector3.Transform (Quaternion)", D(Z.Vector3.Transform(ZV3(v, 0), zq)), D(N.Vector3.Transform(NV3(v, 0), nq)), input, FloatTight);
            Values("Vector2.Transform (Quaternion)", D(Z.Vector2.Transform(new Z.Vector2(v[0], v[1]), zq)),
                   D(N.Vector2.Transform(new N.Vector2(v[0], v[1]), nq)), input, FloatTight);
            Values("Vector4.Transform (Quaternion)", D(Z.Vector4.Transform(new Z.Vector4(v[0], v[1], v[2], v[3]), zq)),
                   D(N.Vector4.Transform(new N.Vector4(v[0], v[1], v[2], v[3]), nq)), input, FloatTight);
            Values("Vector3.Transform (Matrix4x4)", D(Z.Vector3.Transform(ZV3(v, 0), zm)), D(N.Vector3.Transform(NV3(v, 0), nm)), input, FloatTight);
            Values("Vector3.TransformNormal", D(Z.Vector3.TransformNormal(ZV3(v, 0), zm)), D(N.Vector3.TransformNormal(NV3(v, 0), nm)), input, FloatTight);
            Values("Vector4.Transform (Matrix4x4)", D(Z.Vector4.Transform(new Z.Vector4(v[0], v[1], v[2], v[3]), zm)),
                   D(N.Vector4.Transform(new N.Vector4(v[0], v[1], v[2], v[3]), nm)), input, FloatTight);
            Values("Vector4.Transform (Vector3, Matrix4x4)", D(Z.Vector4.Transform(ZV3(v, 0), zm)), D(N.Vector4.Transform(NV3(v, 0), nm)), input, FloatTight);
            Values("Vector2.Transform (Matrix4x4)", D(Z.Vector2.Transform(new Z.Vector2(v[0], v[1]), zm)),
                   D(N.Vector2.Transform(new N.Vector2(v[0], v[1]), nm)), input, FloatTight);
            Values("Vector3.Cross", D(Z.Vector3.Cross(ZV3(v, 0), ZV3(v, 3))), D(N.Vector3.Cross(NV3(v, 0), NV3(v, 3))), input, FloatTight);
            Values("Vector3.Reflect", D(Z.Vector3.Reflect(ZV3(v, 0), za)), D(N.Vector3.Reflect(NV3(v, 0), na)), input, FloatTight);
            Values("Vector3.Normalize", D(za), D(na), input, FloatTight);
            Values("Vector3.Lerp", D(Z.Vector3.Lerp(ZV3(v, 0), ZV3(v, 3), v[6])), D(N.Vector3.Lerp(NV3(v, 0), NV3(v, 3), v[6])), input, FloatTight);
            Values("Vector3.Distance", D(Z.Vector3.Distance(ZV3(v, 0), ZV3(v, 3))), D(N.Vector3.Distance(NV3(v, 0), NV3(v, 3))), input, FloatTight);
            Values("Vector3.Clamp", D(Z.Vector3.Clamp(ZV3(v, 0), ZV3(v, 3), ZV3(v, 3) + new Z.Vector3(5))),
                   D(N.Vector3.Clamp(NV3(v, 0), NV3(v, 3), NV3(v, 3) + new N.Vector3(5))), input, 0);

            Values("Plane.CreateFromVertices", D(Z.Plane.CreateFromVertices(ZV3(v, 0), ZV3(v, 3), ZV3(v, 6))),
                   D(N.Plane.CreateFromVertices(NV3(v, 0), NV3(v, 3), NV3(v, 6))), input, FloatLoose);
            Values("Plane.Normalize", D(Z.Plane.Normalize(zplane)), D(N.Plane.Normalize(nplane)), input, FloatTight);
            var zunit = Z.Plane.Normalize(zplane);
            var nunit = N.Plane.Normalize(nplane);
            Values("Plane.Transform (Quaternion)", D(Z.Plane.Transform(zunit, zq)), D(N.Plane.Transform(nunit, nq)), input, FloatTight);
            if (zi && ni)
                Values("Plane.Transform (Matrix4x4)", D(Z.Plane.Transform(zunit, zrot * Z.Matrix4x4.CreateTranslation(ZV3(v, 6)))),
                       D(N.Plane.Transform(nunit, nrot * N.Matrix4x4.CreateTranslation(NV3(v, 6)))), input, FloatLoose);
            Values("Plane.DotCoordinate", D(Z.Plane.DotCoordinate(zplane, ZV3(v, 0))), D(N.Plane.DotCoordinate(nplane, NV3(v, 0))), input, FloatTight);

            var z32 = new Z.Matrix3x2(v[0], v[1], v[2], v[3], v[4], v[5]);
            var n32 = new N.Matrix3x2(v[0], v[1], v[2], v[3], v[4], v[5]);
            var z32b = new Z.Matrix3x2(v[6], v[7], v[8], v[9], v[10], v[11]);
            var n32b = new N.Matrix3x2(v[6], v[7], v[8], v[9], v[10], v[11]);
            Values("Matrix3x2 *", D(z32 * z32b), D(n32 * n32b), input, FloatTight);
            bool z32i = Z.Matrix3x2.Invert(z32, out var z32inv);
            bool n32i = N.Matrix3x2.Invert(n32, out var n32inv);
            Record("Matrix3x2.Invert (result)", z32i == n32i ? 0 : 1, input, 0);
            if (z32i && n32i)
                Record("Matrix3x2.Invert (ours vs .NET's error)",
                       Relative(D(z32 * z32inv), D(Z.Matrix3x2.Identity)) / Math.Max(Relative(D(n32 * n32inv), D(N.Matrix3x2.Identity)), 1e-5),
                       input, 4);
            // Angles near quarter turns too, where both snap to exact values.
            float turn = it % 4 == 0 ? (float)(Math.Round(v[12]) * Math.PI / 2) : v[12];
            Values("Matrix3x2.CreateRotation", D(Z.Matrix3x2.CreateRotation(turn, new Z.Vector2(v[0], v[1]))),
                   D(N.Matrix3x2.CreateRotation(turn, new N.Vector2(v[0], v[1]))), input, FloatTight);
            Values("Matrix3x2.CreateSkew", D(Z.Matrix3x2.CreateSkew(v[13] / 10, v[14] / 10, new Z.Vector2(v[0], v[1]))),
                   D(N.Matrix3x2.CreateSkew(v[13] / 10, v[14] / 10, new N.Vector2(v[0], v[1]))), input, FloatTight);
            Values("Matrix3x2.CreateScale", D(Z.Matrix3x2.CreateScale(v[13], v[14], new Z.Vector2(v[0], v[1]))),
                   D(N.Matrix3x2.CreateScale(v[13], v[14], new N.Vector2(v[0], v[1]))), input, FloatTight);
            Values("Vector2.Transform (Matrix3x2)", D(Z.Vector2.Transform(new Z.Vector2(v[6], v[7]), z32)),
                   D(N.Vector2.Transform(new N.Vector2(v[6], v[7]), n32)), input, FloatTight);
        }

        // Formats, on values both print the same way.
        Record("Complex.ToString", new Z.Complex(1.5, -2).ToString() == new N.Complex(1.5, -2).ToString() ? 0 : 1, "", 0);
        Record("Vector3.ToString", new Z.Vector3(1, 2.5f, -3).ToString() == new N.Vector3(1, 2.5f, -3).ToString() ? 0 : 1, "", 0);
        Record("Quaternion.ToString", Z.Quaternion.Identity.ToString() == N.Quaternion.Identity.ToString() ? 0 : 1, "", 0);
        Record("Plane.ToString", new Z.Plane(0, 1, 0, 2).ToString() == new N.Plane(0, 1, 0, 2).ToString() ? 0 : 1, "", 0);
        Record("Matrix3x2.ToString", Z.Matrix3x2.Identity.ToString() == N.Matrix3x2.Identity.ToString() ? 0 : 1, "", 0);
        Record("Matrix4x4.ToString", Z.Matrix4x4.Identity.ToString() == N.Matrix4x4.Identity.ToString() ? 0 : 1, "", 0);

        int failures = 0;
        foreach (var (name, worst, inputText, limit) in s_results)
        {
            bool over = worst > limit;
            if (over)
                failures++;
            Console.Write($"{(over ? "OVER" : "ok  ")} {name,-42} {worst,-12:G4} (limit {limit:G3})");
            Console.WriteLine(over ? $"  at {inputText}" : "");
        }
        Console.WriteLine(failures == 0 ? "All within limits." : $"{failures} over their limit.");
        return failures;
    }
}
