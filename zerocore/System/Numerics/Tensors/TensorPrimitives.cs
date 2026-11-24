using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

// The same check, for x != x (NaN) in the Max and Min operators.
#pragma warning disable CS1718

namespace System.Numerics.Tensors
{
    // .NET's TensorPrimitives (the System.Numerics.Tensors package) for
    // float: operations on whole spans, written to a destination span or
    // reduced to one value. The names and argument checks are .NET's, and so
    // are the results of the arithmetic. Exp, Sigmoid, Tanh and SoftMax have
    // vector forms a few ulps from exact, as .NET's do (but exact near 0,
    // where .NET's Tanh is off by 6e-8); the other functions are MathF's,
    // one element at a time. SoftMax subtracts the largest value first, so
    // large inputs don't overflow to NaN. Sums may round
    // differently, since they add in another order (as .NET's do from one
    // processor to another).
    //
    // Element-wise arithmetic, the sums and the four fast functions use the
    // processor's vectors where the target has them: NEON on arm64, AVX on
    // x64 built with -m x86-64-v3 (AVX2 and FMA for the functions). The
    // arithmetic gives the same bits as the scalar loop, since each element is
    // still one IEEE operation; sums add in another order.
    //
    // A destination may be the same span as an input (the operation is then
    // done in place) but not overlap one partly; that, a destination shorter
    // than the input, and inputs of different lengths end the program with
    // .NET's ArgumentException message.
    public static unsafe class TensorPrimitives
    {
        // ---- checks ----------------------------------------------------------

        private static void CheckDestination(int length, Span<float> destination)
        {
            if (destination.Length < length)
                throw new ArgumentException("Destination is too short.");
        }

        private static void CheckSameLength(int x, int y)
        {
            if (x != y)
                throw new ArgumentException("Input span arguments must all have the same length.");
        }

        private static void CheckNotEmpty(int length)
        {
            if (length == 0)
                throw new ArgumentException("Input span arguments must not be empty.");
        }

        private static void CheckOverlap(ReadOnlySpan<float> input, Span<float> destination)
        {
            fixed (float* i = input)
            fixed (float* d = destination)
            {
                if (i != d && i < d + destination.Length && d < i + input.Length)
                    throw new ArgumentException("The destination span may only overlap with an input span if the two spans start at the same memory location.");
            }
        }

        // ---- operators ---------------------------------------------------------

        // An operation on one element, and on 4 (NEON) or 8 (AVX) at once.
        // The vector forms are only called when that instruction set is there.
        private interface IBinary
        {
            static abstract bool Vectorizable { get; }
            static abstract float Invoke(float x, float y);
            static abstract Vector128<float> Invoke(Vector128<float> x, Vector128<float> y);
            static abstract Vector256<float> Invoke(Vector256<float> x, Vector256<float> y);
        }

        private struct AddOperator : IBinary
        {
            public static bool Vectorizable => true;
            public static float Invoke(float x, float y) => x + y;
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y) => AdvSimd.Add(x, y);
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y) => Avx.Add(x, y);
        }

        private struct SubtractOperator : IBinary
        {
            public static bool Vectorizable => true;
            public static float Invoke(float x, float y) => x - y;
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y) => AdvSimd.Subtract(x, y);
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y) => Avx.Subtract(x, y);
        }

        private struct MultiplyOperator : IBinary
        {
            public static bool Vectorizable => true;
            public static float Invoke(float x, float y) => x * y;
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y) => AdvSimd.Multiply(x, y);
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y) => Avx.Multiply(x, y);
        }

        private struct DivideOperator : IBinary
        {
            public static bool Vectorizable => true;
            public static float Invoke(float x, float y) => x / y;
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y) => AdvSimd.Arm64.Divide(x, y);
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y) => Avx.Divide(x, y);
        }

        // The rest have no vector form here; they run the scalar loop.
        private abstract class ScalarOnly
        {
            public static bool Vectorizable => false;
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y) => x;
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y) => x;
        }

        // IEEE maximum and minimum, as .NET's float.Max and Min: NaN wins,
        // and +0 is greater than -0.
        private sealed class MaxOperator : ScalarOnly, IBinary
        {
            public static float Invoke(float x, float y)
            {
                if (x != y)
                    return x != x ? x : y < x ? x : y;
                return float.IsNegative(y) ? x : y;
            }
        }

        private sealed class MinOperator : ScalarOnly, IBinary
        {
            public static float Invoke(float x, float y)
            {
                if (x != y)
                    return x != x ? x : x < y ? x : y;
                return float.IsNegative(x) ? x : y;
            }
        }

        // The one of larger magnitude; for equal magnitudes the positive one.
        private sealed class MaxMagnitudeOperator : ScalarOnly, IBinary
        {
            public static float Invoke(float x, float y)
            {
                float ax = MathF.Abs(x), ay = MathF.Abs(y);
                if (ax > ay || ax != ax)
                    return x;
                if (ax == ay)
                    return float.IsNegative(x) ? y : x;
                return y;
            }
        }

        // The one of smaller magnitude; for equal magnitudes the negative one.
        private sealed class MinMagnitudeOperator : ScalarOnly, IBinary
        {
            public static float Invoke(float x, float y)
            {
                float ax = MathF.Abs(x), ay = MathF.Abs(y);
                if (ax < ay || ax != ax)
                    return x;
                if (ax == ay)
                    return float.IsNegative(x) ? x : y;
                return y;
            }
        }

        private sealed class PowOperator : ScalarOnly, IBinary
        {
            public static float Invoke(float x, float y) => MathF.Pow(x, y);
        }

        private sealed class Atan2Operator : ScalarOnly, IBinary
        {
            public static float Invoke(float y, float x) => MathF.Atan2(y, x);
        }

        private sealed class CopySignOperator : ScalarOnly, IBinary
        {
            public static float Invoke(float x, float y) => MathF.CopySign(x, y);
        }

        // In double, where the squares can't overflow: correctly rounded
        // nearly always, as .NET's float.Hypot.
        private sealed class HypotOperator : ScalarOnly, IBinary
        {
            public static float Invoke(float x, float y)
            {
                if (float.IsInfinity(x) || float.IsInfinity(y))
                    return float.PositiveInfinity;
                double dx = x, dy = y;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }
        }

        private interface IUnary
        {
            static abstract float Invoke(float x);
        }

        private struct AbsOperator : IUnary { public static float Invoke(float x) => MathF.Abs(x); }
        private struct NegateOperator : IUnary { public static float Invoke(float x) => -x; }
        private struct LogOperator : IUnary { public static float Invoke(float x) => MathF.Log(x); }
        private struct Log2Operator : IUnary { public static float Invoke(float x) => MathF.Log2(x); }
        private struct Log10Operator : IUnary { public static float Invoke(float x) => MathF.Log10(x); }
        private struct SqrtOperator : IUnary { public static float Invoke(float x) => MathF.Sqrt(x); }
        private struct CbrtOperator : IUnary { public static float Invoke(float x) => MathF.Cbrt(x); }
        private struct SinOperator : IUnary { public static float Invoke(float x) => MathF.Sin(x); }
        private struct CosOperator : IUnary { public static float Invoke(float x) => MathF.Cos(x); }
        private struct TanOperator : IUnary { public static float Invoke(float x) => MathF.Tan(x); }
        private struct AsinOperator : IUnary { public static float Invoke(float x) => MathF.Asin(x); }
        private struct AcosOperator : IUnary { public static float Invoke(float x) => MathF.Acos(x); }
        private struct AtanOperator : IUnary { public static float Invoke(float x) => MathF.Atan(x); }
        private struct SinhOperator : IUnary { public static float Invoke(float x) => MathF.Sinh(x); }
        private struct CoshOperator : IUnary { public static float Invoke(float x) => MathF.Cosh(x); }
        private struct AsinhOperator : IUnary { public static float Invoke(float x) => MathF.Asinh(x); }
        private struct AcoshOperator : IUnary { public static float Invoke(float x) => MathF.Acosh(x); }
        private struct AtanhOperator : IUnary { public static float Invoke(float x) => MathF.Atanh(x); }
        private struct CeilingOperator : IUnary { public static float Invoke(float x) => MathF.Ceiling(x); }
        private struct FloorOperator : IUnary { public static float Invoke(float x) => MathF.Floor(x); }
        private struct TruncateOperator : IUnary { public static float Invoke(float x) => MathF.Truncate(x); }
        private struct RoundOperator : IUnary { public static float Invoke(float x) => MathF.Round(x); }
        private struct ReciprocalOperator : IUnary { public static float Invoke(float x) => 1 / x; }

        // ---- loops -------------------------------------------------------------

        private static void Unary<TOp>(ReadOnlySpan<float> x, Span<float> destination) where TOp : IUnary
        {
            CheckDestination(x.Length, destination);
            CheckOverlap(x, destination);
            for (int i = 0; i < x.Length; i++)
                destination[i] = TOp.Invoke(x[i]);
        }

        private static void Binary<TOp>(ReadOnlySpan<float> x, ReadOnlySpan<float> y, Span<float> destination) where TOp : IBinary
        {
            CheckSameLength(x.Length, y.Length);
            CheckDestination(x.Length, destination);
            CheckOverlap(x, destination);
            CheckOverlap(y, destination);
            fixed (float* px = x)
            fixed (float* py = y)
            fixed (float* pd = destination)
                Binary<TOp>(px, 1, py, 1, pd, x.Length);
        }

        private static void Binary<TOp>(ReadOnlySpan<float> x, float y, Span<float> destination) where TOp : IBinary
        {
            CheckDestination(x.Length, destination);
            CheckOverlap(x, destination);
            fixed (float* px = x)
            fixed (float* pd = destination)
                Binary<TOp>(px, 1, &y, 0, pd, x.Length);
        }

        private static void Binary<TOp>(float x, ReadOnlySpan<float> y, Span<float> destination) where TOp : IBinary
        {
            CheckDestination(y.Length, destination);
            CheckOverlap(y, destination);
            fixed (float* py = y)
            fixed (float* pd = destination)
                Binary<TOp>(&x, 0, py, 1, pd, y.Length);
        }

        // A step of 0 makes an argument a scalar, used for every element.
        private static void Binary<TOp>(float* x, int xStep, float* y, int yStep, float* d, int n) where TOp : IBinary
        {
            int i = 0;
            if (TOp.Vectorizable && AdvSimd.Arm64.IsSupported)
            {
                for (; i + 4 <= n; i += 4)
                {
                    Vector128<float> vx = xStep == 0 ? AdvSimd.DuplicateToVector128(*x) : AdvSimd.LoadVector128(x + i);
                    Vector128<float> vy = yStep == 0 ? AdvSimd.DuplicateToVector128(*y) : AdvSimd.LoadVector128(y + i);
                    AdvSimd.Store(d + i, TOp.Invoke(vx, vy));
                }
            }
            else if (TOp.Vectorizable && Avx.IsSupported)
            {
                for (; i + 8 <= n; i += 8)
                {
                    Vector256<float> vx = xStep == 0 ? Vector256.Create(*x) : Avx.LoadVector256(x + i);
                    Vector256<float> vy = yStep == 0 ? Vector256.Create(*y) : Avx.LoadVector256(y + i);
                    Avx.Store(d + i, TOp.Invoke(vx, vy));
                }
            }
            for (; i < n; i++)
                d[i] = TOp.Invoke(x[i * xStep], y[i * yStep]);
        }

        // first(second(x, y), z): (x * y) + z for MultiplyAdd, (x + y) * z
        // for AddMultiply. Two roundings, as in .NET (FusedMultiplyAdd has one).
        private static void Ternary<TFirst, TSecond>(ReadOnlySpan<float> x, ReadOnlySpan<float> y, ReadOnlySpan<float> z, Span<float> destination)
            where TFirst : IBinary where TSecond : IBinary
        {
            CheckSameLength(x.Length, y.Length);
            CheckSameLength(x.Length, z.Length);
            CheckDestination(x.Length, destination);
            CheckOverlap(x, destination);
            CheckOverlap(y, destination);
            CheckOverlap(z, destination);
            fixed (float* px = x)
            fixed (float* py = y)
            fixed (float* pz = z)
            fixed (float* pd = destination)
                Ternary<TFirst, TSecond>(px, 1, py, 1, pz, 1, pd, x.Length);
        }

        private static void Ternary<TFirst, TSecond>(ReadOnlySpan<float> x, ReadOnlySpan<float> y, float z, Span<float> destination)
            where TFirst : IBinary where TSecond : IBinary
        {
            CheckSameLength(x.Length, y.Length);
            CheckDestination(x.Length, destination);
            CheckOverlap(x, destination);
            CheckOverlap(y, destination);
            fixed (float* px = x)
            fixed (float* py = y)
            fixed (float* pd = destination)
                Ternary<TFirst, TSecond>(px, 1, py, 1, &z, 0, pd, x.Length);
        }

        private static void Ternary<TFirst, TSecond>(ReadOnlySpan<float> x, float y, ReadOnlySpan<float> z, Span<float> destination)
            where TFirst : IBinary where TSecond : IBinary
        {
            CheckSameLength(x.Length, z.Length);
            CheckDestination(x.Length, destination);
            CheckOverlap(x, destination);
            CheckOverlap(z, destination);
            fixed (float* px = x)
            fixed (float* pz = z)
            fixed (float* pd = destination)
                Ternary<TFirst, TSecond>(px, 1, &y, 0, pz, 1, pd, x.Length);
        }

        private static void Ternary<TFirst, TSecond>(float* x, int xStep, float* y, int yStep, float* z, int zStep, float* d, int n)
            where TFirst : IBinary where TSecond : IBinary
        {
            int i = 0;
            if (AdvSimd.Arm64.IsSupported)
            {
                for (; i + 4 <= n; i += 4)
                {
                    Vector128<float> vx = xStep == 0 ? AdvSimd.DuplicateToVector128(*x) : AdvSimd.LoadVector128(x + i);
                    Vector128<float> vy = yStep == 0 ? AdvSimd.DuplicateToVector128(*y) : AdvSimd.LoadVector128(y + i);
                    Vector128<float> vz = zStep == 0 ? AdvSimd.DuplicateToVector128(*z) : AdvSimd.LoadVector128(z + i);
                    AdvSimd.Store(d + i, TSecond.Invoke(TFirst.Invoke(vx, vy), vz));
                }
            }
            else if (Avx.IsSupported)
            {
                for (; i + 8 <= n; i += 8)
                {
                    Vector256<float> vx = xStep == 0 ? Vector256.Create(*x) : Avx.LoadVector256(x + i);
                    Vector256<float> vy = yStep == 0 ? Vector256.Create(*y) : Avx.LoadVector256(y + i);
                    Vector256<float> vz = zStep == 0 ? Vector256.Create(*z) : Avx.LoadVector256(z + i);
                    Avx.Store(d + i, TSecond.Invoke(TFirst.Invoke(vx, vy), vz));
                }
            }
            for (; i < n; i++)
                d[i] = TSecond.Invoke(TFirst.Invoke(x[i * xStep], y[i * yStep]), z[i * zStep]);
        }

        // ---- element-wise ------------------------------------------------------

        public static void Add(ReadOnlySpan<float> x, ReadOnlySpan<float> y, Span<float> destination) => Binary<AddOperator>(x, y, destination);
        public static void Add(ReadOnlySpan<float> x, float y, Span<float> destination) => Binary<AddOperator>(x, y, destination);
        public static void Subtract(ReadOnlySpan<float> x, ReadOnlySpan<float> y, Span<float> destination) => Binary<SubtractOperator>(x, y, destination);
        public static void Subtract(ReadOnlySpan<float> x, float y, Span<float> destination) => Binary<SubtractOperator>(x, y, destination);
        public static void Subtract(float x, ReadOnlySpan<float> y, Span<float> destination) => Binary<SubtractOperator>(x, y, destination);
        public static void Multiply(ReadOnlySpan<float> x, ReadOnlySpan<float> y, Span<float> destination) => Binary<MultiplyOperator>(x, y, destination);
        public static void Multiply(ReadOnlySpan<float> x, float y, Span<float> destination) => Binary<MultiplyOperator>(x, y, destination);
        public static void Divide(ReadOnlySpan<float> x, ReadOnlySpan<float> y, Span<float> destination) => Binary<DivideOperator>(x, y, destination);
        public static void Divide(ReadOnlySpan<float> x, float y, Span<float> destination) => Binary<DivideOperator>(x, y, destination);
        public static void Divide(float x, ReadOnlySpan<float> y, Span<float> destination) => Binary<DivideOperator>(x, y, destination);

        // -x is x * -1 exactly, -0 and NaN included.
        public static void Negate(ReadOnlySpan<float> x, Span<float> destination) => Binary<MultiplyOperator>(x, -1f, destination);

        /// <summary>(x * y) + addend, rounded twice.</summary>
        public static void MultiplyAdd(ReadOnlySpan<float> x, ReadOnlySpan<float> y, ReadOnlySpan<float> addend, Span<float> destination) =>
            Ternary<MultiplyOperator, AddOperator>(x, y, addend, destination);
        public static void MultiplyAdd(ReadOnlySpan<float> x, ReadOnlySpan<float> y, float addend, Span<float> destination) =>
            Ternary<MultiplyOperator, AddOperator>(x, y, addend, destination);
        public static void MultiplyAdd(ReadOnlySpan<float> x, float y, ReadOnlySpan<float> addend, Span<float> destination) =>
            Ternary<MultiplyOperator, AddOperator>(x, y, addend, destination);

        /// <summary>(x + y) * multiplier.</summary>
        public static void AddMultiply(ReadOnlySpan<float> x, ReadOnlySpan<float> y, ReadOnlySpan<float> multiplier, Span<float> destination) =>
            Ternary<AddOperator, MultiplyOperator>(x, y, multiplier, destination);
        public static void AddMultiply(ReadOnlySpan<float> x, ReadOnlySpan<float> y, float multiplier, Span<float> destination) =>
            Ternary<AddOperator, MultiplyOperator>(x, y, multiplier, destination);
        public static void AddMultiply(ReadOnlySpan<float> x, float y, ReadOnlySpan<float> multiplier, Span<float> destination) =>
            Ternary<AddOperator, MultiplyOperator>(x, y, multiplier, destination);

        public static void Max(ReadOnlySpan<float> x, ReadOnlySpan<float> y, Span<float> destination) => Binary<MaxOperator>(x, y, destination);
        public static void Min(ReadOnlySpan<float> x, ReadOnlySpan<float> y, Span<float> destination) => Binary<MinOperator>(x, y, destination);
        public static void MaxMagnitude(ReadOnlySpan<float> x, ReadOnlySpan<float> y, Span<float> destination) => Binary<MaxMagnitudeOperator>(x, y, destination);
        public static void MinMagnitude(ReadOnlySpan<float> x, ReadOnlySpan<float> y, Span<float> destination) => Binary<MinMagnitudeOperator>(x, y, destination);
        public static void Pow(ReadOnlySpan<float> x, ReadOnlySpan<float> y, Span<float> destination) => Binary<PowOperator>(x, y, destination);
        public static void Pow(ReadOnlySpan<float> x, float y, Span<float> destination) => Binary<PowOperator>(x, y, destination);
        public static void Atan2(ReadOnlySpan<float> y, ReadOnlySpan<float> x, Span<float> destination) => Binary<Atan2Operator>(y, x, destination);
        public static void CopySign(ReadOnlySpan<float> x, ReadOnlySpan<float> sign, Span<float> destination) => Binary<CopySignOperator>(x, sign, destination);
        public static void Hypot(ReadOnlySpan<float> x, ReadOnlySpan<float> y, Span<float> destination) => Binary<HypotOperator>(x, y, destination);

        public static void Abs(ReadOnlySpan<float> x, Span<float> destination) => Unary<AbsOperator>(x, destination);
        public static void Exp(ReadOnlySpan<float> x, Span<float> destination) => VectorUnary<FastExp>(x, destination);
        public static void Log(ReadOnlySpan<float> x, Span<float> destination) => Unary<LogOperator>(x, destination);
        public static void Log2(ReadOnlySpan<float> x, Span<float> destination) => Unary<Log2Operator>(x, destination);
        public static void Log10(ReadOnlySpan<float> x, Span<float> destination) => Unary<Log10Operator>(x, destination);
        public static void Sqrt(ReadOnlySpan<float> x, Span<float> destination) => Unary<SqrtOperator>(x, destination);
        public static void Cbrt(ReadOnlySpan<float> x, Span<float> destination) => Unary<CbrtOperator>(x, destination);
        public static void Sin(ReadOnlySpan<float> x, Span<float> destination) => Unary<SinOperator>(x, destination);
        public static void Cos(ReadOnlySpan<float> x, Span<float> destination) => Unary<CosOperator>(x, destination);
        public static void Tan(ReadOnlySpan<float> x, Span<float> destination) => Unary<TanOperator>(x, destination);
        public static void Asin(ReadOnlySpan<float> x, Span<float> destination) => Unary<AsinOperator>(x, destination);
        public static void Acos(ReadOnlySpan<float> x, Span<float> destination) => Unary<AcosOperator>(x, destination);
        public static void Atan(ReadOnlySpan<float> x, Span<float> destination) => Unary<AtanOperator>(x, destination);
        public static void Sinh(ReadOnlySpan<float> x, Span<float> destination) => Unary<SinhOperator>(x, destination);
        public static void Cosh(ReadOnlySpan<float> x, Span<float> destination) => Unary<CoshOperator>(x, destination);
        public static void Tanh(ReadOnlySpan<float> x, Span<float> destination) => VectorUnary<FastTanh>(x, destination);
        public static void Asinh(ReadOnlySpan<float> x, Span<float> destination) => Unary<AsinhOperator>(x, destination);
        public static void Acosh(ReadOnlySpan<float> x, Span<float> destination) => Unary<AcoshOperator>(x, destination);
        public static void Atanh(ReadOnlySpan<float> x, Span<float> destination) => Unary<AtanhOperator>(x, destination);
        public static void Ceiling(ReadOnlySpan<float> x, Span<float> destination) => Unary<CeilingOperator>(x, destination);
        public static void Floor(ReadOnlySpan<float> x, Span<float> destination) => Unary<FloorOperator>(x, destination);
        public static void Truncate(ReadOnlySpan<float> x, Span<float> destination) => Unary<TruncateOperator>(x, destination);

        /// <summary>Rounds to the nearest whole number; halfway cases go to the even one.</summary>
        public static void Round(ReadOnlySpan<float> x, Span<float> destination) => Unary<RoundOperator>(x, destination);

        public static void Reciprocal(ReadOnlySpan<float> x, Span<float> destination) => Unary<ReciprocalOperator>(x, destination);

        /// <summary>1 / (1 + e^-x) for each element.</summary>
        public static void Sigmoid(ReadOnlySpan<float> x, Span<float> destination)
        {
            CheckNotEmpty(x.Length);
            VectorUnary<FastSigmoid>(x, destination);
        }

        /// <summary>
        /// e^x / Σ e^x over the span: probabilities that add up to 1. Computed
        /// as e^(x - max) / Σ e^(x - max), which is the same but doesn't
        /// overflow for x above 88 (.NET's gives NaN there). x - max is rounded
        /// in float; its rounding error, recovered exactly (TwoSum), corrects
        /// the result, which would otherwise lose up to 66 ulps on the small
        /// probabilities.
        /// </summary>
        public static void SoftMax(ReadOnlySpan<float> x, Span<float> destination)
        {
            CheckNotEmpty(x.Length);
            CheckDestination(x.Length, destination);
            CheckOverlap(x, destination);
            int n = x.Length;
            fixed (float* px = x)
            fixed (float* pd = destination)
            {
                if (!FastExpAvailable || !MaxIfFinite(px, n, out float max))
                {
                    SoftMaxScalar(px, pd, n);
                    return;
                }
                float sum = 0;
                int i = 0;
                if (AdvSimd.Arm64.IsSupported)
                {
                    Vector128<float> vmax = AdvSimd.DuplicateToVector128(max), negMax = AdvSimd.DuplicateToVector128(-max);
                    Vector128<float> vsum = default;
                    for (; i + 4 <= n; i += 4)
                    {
                        if (!AllAtLeast(px + i, 4, max + ExpLo))
                        {
                            for (int j = i; j < i + 4; j++)
                                sum += pd[j] = (float)Math.Exp((double)px[j] - max);
                            continue;
                        }
                        Vector128<float> v = AdvSimd.LoadVector128(px + i);
                        Vector128<float> d = AdvSimd.Subtract(v, vmax);
                        Vector128<float> bb = AdvSimd.Subtract(d, v);
                        Vector128<float> error = AdvSimd.Add(AdvSimd.Subtract(v, AdvSimd.Subtract(d, bb)), AdvSimd.Subtract(negMax, bb));
                        Vector128<float> e = ExpVector(d);
                        e = AdvSimd.FusedMultiplyAdd(e, e, error);
                        AdvSimd.Store(pd + i, e);
                        vsum = AdvSimd.Add(vsum, e);
                    }
                    sum += HorizontalSum(vsum);
                }
                else
                {
                    Vector256<float> vmax = Vector256.Create(max), negMax = Vector256.Create(-max);
                    Vector256<float> vsum = default;
                    for (; i + 8 <= n; i += 8)
                    {
                        if (!AllAtLeast(px + i, 8, max + ExpLo))
                        {
                            for (int j = i; j < i + 8; j++)
                                sum += pd[j] = (float)Math.Exp((double)px[j] - max);
                            continue;
                        }
                        Vector256<float> v = Avx.LoadVector256(px + i);
                        Vector256<float> d = Avx.Subtract(v, vmax);
                        Vector256<float> bb = Avx.Subtract(d, v);
                        Vector256<float> error = Avx.Add(Avx.Subtract(v, Avx.Subtract(d, bb)), Avx.Subtract(negMax, bb));
                        Vector256<float> e = ExpVector(d);
                        e = Fma.MultiplyAdd(e, error, e);
                        Avx.Store(pd + i, e);
                        vsum = Avx.Add(vsum, e);
                    }
                    sum += HorizontalSum(vsum);
                }
                for (; i < n; i++)
                    sum += pd[i] = (float)Math.Exp((double)px[i] - max);
                Binary<DivideOperator>(pd, 1, &sum, 0, pd, n);
            }
        }

        // Without the vector exp, or with an infinity or NaN in x: in double,
        // where x - max is exact.
        private static void SoftMaxScalar(float* x, float* d, int n)
        {
            double max = x[0];
            for (int i = 1; i < n; i++)
                max = MaxOperator.Invoke((float)max, x[i]);
            double sum = 0;
            for (int i = 0; i < n; i++)
                sum += Math.Exp(x[i] - max);
            for (int i = 0; i < n; i++)
                d[i] = (float)(Math.Exp(x[i] - max) / sum);
        }

        // The largest value, when every value is finite (vector Max doesn't
        // order NaN the way MaxOperator does, and SoftMaxScalar handles those).
        private static bool MaxIfFinite(float* x, int n, out float max)
        {
            int i = 0;
            float finite = 0; // stays 0 while x * 0 does: NaN for an infinity or a NaN
            max = x[0];
            if (AdvSimd.Arm64.IsSupported)
            {
                Vector128<float> vmax = AdvSimd.DuplicateToVector128(x[0]), check = default;
                for (; i + 4 <= n; i += 4)
                {
                    Vector128<float> v = AdvSimd.LoadVector128(x + i);
                    vmax = AdvSimd.Max(vmax, v);
                    check = AdvSimd.Add(check, AdvSimd.Multiply(v, default));
                }
                max = AdvSimd.Arm64.MaxAcross(vmax).ToScalar();
                finite = HorizontalSum(check);
            }
            else if (Avx.IsSupported)
            {
                Vector256<float> vmax = Vector256.Create(x[0]), check = default;
                for (; i + 8 <= n; i += 8)
                {
                    Vector256<float> v = Avx.LoadVector256(x + i);
                    vmax = Avx.Max(vmax, v);
                    check = Avx.Add(check, Avx.Multiply(v, default));
                }
                float* lanes = stackalloc float[8];
                Avx.Store(lanes, vmax);
                for (int j = 0; j < 8; j++)
                    max = lanes[j] > max ? lanes[j] : max;
                finite = HorizontalSum(check);
            }
            for (; i < n; i++)
            {
                finite += x[i] * 0;
                max = x[i] > max ? x[i] : max;
            }
            return finite == 0;
        }

        public static void ConvertToHalf(ReadOnlySpan<float> source, Span<Half> destination)
        {
            if (destination.Length < source.Length)
                throw new ArgumentException("Destination is too short.");
            for (int i = 0; i < source.Length; i++)
                destination[i] = (Half)source[i];
        }

        public static void ConvertToSingle(ReadOnlySpan<Half> source, Span<float> destination)
        {
            CheckDestination(source.Length, destination);
            for (int i = 0; i < source.Length; i++)
                destination[i] = (float)source[i];
        }

        // ---- reductions ----------------------------------------------------------

        public static float Sum(ReadOnlySpan<float> x)
        {
            fixed (float* px = x)
                return Sum(px, x.Length);
        }

        // Four running sums of 4 (or 8) lanes each, added at the end.
        private static float Sum(float* x, int n)
        {
            int i = 0;
            float sum = 0;
            if (AdvSimd.Arm64.IsSupported)
            {
                Vector128<float> s0 = default, s1 = default, s2 = default, s3 = default;
                for (; i + 16 <= n; i += 16)
                {
                    s0 = AdvSimd.Add(s0, AdvSimd.LoadVector128(x + i));
                    s1 = AdvSimd.Add(s1, AdvSimd.LoadVector128(x + i + 4));
                    s2 = AdvSimd.Add(s2, AdvSimd.LoadVector128(x + i + 8));
                    s3 = AdvSimd.Add(s3, AdvSimd.LoadVector128(x + i + 12));
                }
                sum = HorizontalSum(AdvSimd.Add(AdvSimd.Add(s0, s1), AdvSimd.Add(s2, s3)));
            }
            else if (Avx.IsSupported)
            {
                Vector256<float> s0 = default, s1 = default;
                for (; i + 16 <= n; i += 16)
                {
                    s0 = Avx.Add(s0, Avx.LoadVector256(x + i));
                    s1 = Avx.Add(s1, Avx.LoadVector256(x + i + 8));
                }
                sum = HorizontalSum(Avx.Add(s0, s1));
            }
            for (; i < n; i++)
                sum += x[i];
            return sum;
        }

        /// <summary>Σ x[i] * y[i]; 0 for empty spans.</summary>
        public static float Dot(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
        {
            CheckSameLength(x.Length, y.Length);
            fixed (float* px = x)
            fixed (float* py = y)
                return Dot(px, py, x.Length);
        }

        private static float Dot(float* x, float* y, int n)
        {
            int i = 0;
            float sum = 0;
            if (AdvSimd.Arm64.IsSupported)
            {
                Vector128<float> s0 = default, s1 = default, s2 = default, s3 = default;
                for (; i + 16 <= n; i += 16)
                {
                    s0 = AdvSimd.Add(s0, AdvSimd.Multiply(AdvSimd.LoadVector128(x + i), AdvSimd.LoadVector128(y + i)));
                    s1 = AdvSimd.Add(s1, AdvSimd.Multiply(AdvSimd.LoadVector128(x + i + 4), AdvSimd.LoadVector128(y + i + 4)));
                    s2 = AdvSimd.Add(s2, AdvSimd.Multiply(AdvSimd.LoadVector128(x + i + 8), AdvSimd.LoadVector128(y + i + 8)));
                    s3 = AdvSimd.Add(s3, AdvSimd.Multiply(AdvSimd.LoadVector128(x + i + 12), AdvSimd.LoadVector128(y + i + 12)));
                }
                sum = HorizontalSum(AdvSimd.Add(AdvSimd.Add(s0, s1), AdvSimd.Add(s2, s3)));
            }
            else if (Avx.IsSupported)
            {
                Vector256<float> s0 = default, s1 = default;
                for (; i + 16 <= n; i += 16)
                {
                    s0 = Avx.Add(s0, Avx.Multiply(Avx.LoadVector256(x + i), Avx.LoadVector256(y + i)));
                    s1 = Avx.Add(s1, Avx.Multiply(Avx.LoadVector256(x + i + 8), Avx.LoadVector256(y + i + 8)));
                }
                sum = HorizontalSum(Avx.Add(s0, s1));
            }
            for (; i < n; i++)
                sum += x[i] * y[i];
            return sum;
        }

        public static float SumOfSquares(ReadOnlySpan<float> x)
        {
            fixed (float* px = x)
                return Dot(px, px, x.Length);
        }

        public static float SumOfMagnitudes(ReadOnlySpan<float> x)
        {
            float sum = 0;
            for (int i = 0; i < x.Length; i++)
                sum += MathF.Abs(x[i]);
            return sum;
        }

        /// <summary>The Euclidean length, √Σ x².</summary>
        public static float Norm(ReadOnlySpan<float> x) => MathF.Sqrt(SumOfSquares(x));

        /// <summary>√Σ (x - y)²; the spans must not be empty.</summary>
        public static float Distance(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
        {
            CheckNotEmpty(x.Length);
            CheckSameLength(x.Length, y.Length);
            float sum = 0;
            for (int i = 0; i < x.Length; i++)
            {
                float d = x[i] - y[i];
                sum += d * d;
            }
            return MathF.Sqrt(sum);
        }

        /// <summary>Dot(x, y) / (Norm(x) * Norm(y)); the spans must not be empty.</summary>
        public static float CosineSimilarity(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
        {
            CheckNotEmpty(x.Length);
            CheckSameLength(x.Length, y.Length);
            fixed (float* px = x)
            fixed (float* py = y)
                return Dot(px, py, x.Length) / (MathF.Sqrt(Dot(px, px, x.Length)) * MathF.Sqrt(Dot(py, py, x.Length)));
        }

        public static float Product(ReadOnlySpan<float> x)
        {
            CheckNotEmpty(x.Length);
            float product = 1;
            for (int i = 0; i < x.Length; i++)
                product *= x[i];
            return product;
        }

        /// <summary>Π (x - y).</summary>
        public static float ProductOfDifferences(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
        {
            CheckNotEmpty(x.Length);
            CheckSameLength(x.Length, y.Length);
            float product = 1;
            for (int i = 0; i < x.Length; i++)
                product *= x[i] - y[i];
            return product;
        }

        /// <summary>Π (x + y).</summary>
        public static float ProductOfSums(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
        {
            CheckNotEmpty(x.Length);
            CheckSameLength(x.Length, y.Length);
            float product = 1;
            for (int i = 0; i < x.Length; i++)
                product *= x[i] + y[i];
            return product;
        }

        // The largest value, NaN if there is one, +0 over -0; the span must
        // not be empty. Min, MaxMagnitude and MinMagnitude likewise.
        public static float Max(ReadOnlySpan<float> x) => Fold<MaxOperator>(x);
        public static float Min(ReadOnlySpan<float> x) => Fold<MinOperator>(x);
        public static float MaxMagnitude(ReadOnlySpan<float> x) => Fold<MaxMagnitudeOperator>(x);
        public static float MinMagnitude(ReadOnlySpan<float> x) => Fold<MinMagnitudeOperator>(x);

        private static float Fold<TOp>(ReadOnlySpan<float> x) where TOp : IBinary
        {
            CheckNotEmpty(x.Length);
            float result = x[0];
            for (int i = 1; i < x.Length && result == result; i++)
                result = TOp.Invoke(result, x[i]);
            return result;
        }

        // The index of the value Max (Min, ...) would return: the first NaN,
        // else the first of equal values (+0 counting as larger than -0);
        // -1 for an empty span.
        public static int IndexOfMax(ReadOnlySpan<float> x) => IndexOf<MaxOperator>(x);
        public static int IndexOfMin(ReadOnlySpan<float> x) => IndexOf<MinOperator>(x);
        public static int IndexOfMaxMagnitude(ReadOnlySpan<float> x) => IndexOf<MaxMagnitudeOperator>(x);
        public static int IndexOfMinMagnitude(ReadOnlySpan<float> x) => IndexOf<MinMagnitudeOperator>(x);

        private static int IndexOf<TOp>(ReadOnlySpan<float> x) where TOp : IBinary
        {
            if (x.Length == 0)
                return -1;
            float result = x[0];
            int index = 0;
            for (int i = 1; i < x.Length && result == result; i++)
            {
                float next = TOp.Invoke(result, x[i]);
                // A different value, or the same one with another sign
                // (bit comparison: -0 and +0 differ, NaNs are all one).
                if (!next.Equals(result) || (next == 0 && float.IsNegative(next) != float.IsNegative(result)))
                {
                    result = next;
                    index = i;
                }
            }
            return index;
        }

        // ---- the fast functions ----------------------------------------------------
        //
        // Exp, Sigmoid, Tanh and SoftMax are the ones neural networks spend
        // their time in, so they have vector forms, built on Cephes' expf
        // (as lib/Kernels.cs): reduce by ln 2, a degree-6 polynomial, scale by
        // 2^n through the exponent bits. A few ulps, like .NET's. A block of 4
        // or 8 with a value outside the range where that works (a NaN, or a
        // result that overflows or is subnormal) is done by the scalar
        // function instead, so special values come out exactly as MathF's.

        private const float ExpLo = -87f, ExpHi = 88f;
        private const float Log2E = 1.44269504088896341f;
        private const float NegLn2Hi = -0.693359375f, NegLn2Lo = 2.12194440e-4f; // -ln 2 in two parts
        private const float P0 = 1.9875691500e-4f, P1 = 1.3981999507e-3f, P2 = 8.3334519073e-3f;
        private const float P3 = 4.1665795894e-2f, P4 = 1.6666665459e-1f, P5 = 5.0000001201e-1f;

        private static bool FastExpAvailable => AdvSimd.Arm64.IsSupported || (Avx2.IsSupported && Fma.IsSupported);

        private interface IVectorUnary
        {
            static abstract float Invoke(float x);
            static abstract bool InRange(float x);
            static abstract Vector128<float> Invoke(Vector128<float> x);
            static abstract Vector256<float> Invoke(Vector256<float> x);
        }

        private struct FastExp : IVectorUnary
        {
            public static float Invoke(float x) => MathF.Exp(x);
            public static bool InRange(float x) => x >= ExpLo && x <= ExpHi;
            public static Vector128<float> Invoke(Vector128<float> x) => ExpVector(x);
            public static Vector256<float> Invoke(Vector256<float> x) => ExpVector(x);
        }

        // 1 / (1 + e^-x). The scalar form is in double, where e^-x doesn't
        // overflow before the result underflows.
        private struct FastSigmoid : IVectorUnary
        {
            public static float Invoke(float x) => (float)(1 / (1 + Math.Exp(-x)));
            public static bool InRange(float x) => x >= -ExpHi && x <= -ExpLo;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector128<float> Invoke(Vector128<float> x)
            {
                Vector128<float> one = AdvSimd.DuplicateToVector128(1f);
                return AdvSimd.Arm64.Divide(one, AdvSimd.Add(one, ExpVector(AdvSimd.Subtract(default, x))));
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector256<float> Invoke(Vector256<float> x)
            {
                Vector256<float> one = Vector256.Create(1f);
                return Avx.Divide(one, Avx.Add(one, ExpVector(Avx.Subtract(default, x))));
            }
        }

        // Below |x| = 0.625 Cephes' odd polynomial, x + x^3 P(x^2), exact for
        // tiny x (where .NET's is off by 6e-8); above it 1 - 2 / (e^2|x| + 1),
        // with |x| capped at 40, where tanh is 1 in float. The sign is x's.
        private struct FastTanh : IVectorUnary
        {
            private const float T0 = -5.70498872745e-3f, T1 = 2.06390887954e-2f, T2 = -5.37397155531e-2f;
            private const float T3 = 1.33314422036e-1f, T4 = -3.33332819422e-1f;

            public static float Invoke(float x) => MathF.Tanh(x);
            public static bool InRange(float x) => x == x;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector128<float> Invoke(Vector128<float> x)
            {
                Vector128<float> a = AdvSimd.Abs(x);
                Vector128<float> z = AdvSimd.Multiply(a, a);
                Vector128<float> p = AdvSimd.DuplicateToVector128(T0);
                p = AdvSimd.FusedMultiplyAdd(AdvSimd.DuplicateToVector128(T1), p, z);
                p = AdvSimd.FusedMultiplyAdd(AdvSimd.DuplicateToVector128(T2), p, z);
                p = AdvSimd.FusedMultiplyAdd(AdvSimd.DuplicateToVector128(T3), p, z);
                p = AdvSimd.FusedMultiplyAdd(AdvSimd.DuplicateToVector128(T4), p, z);
                Vector128<float> small = AdvSimd.FusedMultiplyAdd(a, AdvSimd.Multiply(p, z), a);

                Vector128<float> one = AdvSimd.DuplicateToVector128(1f);
                Vector128<float> capped = AdvSimd.Min(a, AdvSimd.DuplicateToVector128(40f));
                Vector128<float> e = ExpVector(AdvSimd.Add(capped, capped));
                Vector128<float> large = AdvSimd.Subtract(one, AdvSimd.Arm64.Divide(AdvSimd.DuplicateToVector128(2f), AdvSimd.Add(e, one)));

                Vector128<float> t = AdvSimd.BitwiseSelect(AdvSimd.CompareLessThan(a, AdvSimd.DuplicateToVector128(0.625f)), small, large);
                return AdvSimd.BitwiseSelect(AdvSimd.DuplicateToVector128(-0f), x, t);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector256<float> Invoke(Vector256<float> x)
            {
                Vector256<float> signBit = Vector256.Create(-0f);
                Vector256<float> a = Avx.AndNot(signBit, x);
                Vector256<float> z = Avx.Multiply(a, a);
                Vector256<float> p = Vector256.Create(T0);
                p = Fma.MultiplyAdd(p, z, Vector256.Create(T1));
                p = Fma.MultiplyAdd(p, z, Vector256.Create(T2));
                p = Fma.MultiplyAdd(p, z, Vector256.Create(T3));
                p = Fma.MultiplyAdd(p, z, Vector256.Create(T4));
                Vector256<float> small = Fma.MultiplyAdd(Avx.Multiply(p, z), a, a);

                Vector256<float> one = Vector256.Create(1f);
                Vector256<float> capped = Avx.Min(a, Vector256.Create(40f));
                Vector256<float> e = ExpVector(Avx.Add(capped, capped));
                Vector256<float> large = Avx.Subtract(one, Avx.Divide(Vector256.Create(2f), Avx.Add(e, one)));

                Vector256<float> t = Avx.BlendVariable(large, small, Avx.CompareLessThan(a, Vector256.Create(0.625f)));
                return Avx.Or(Avx.And(signBit, x), t);
            }
        }

        private static void VectorUnary<TOp>(ReadOnlySpan<float> x, Span<float> destination) where TOp : IVectorUnary
        {
            CheckDestination(x.Length, destination);
            CheckOverlap(x, destination);
            int n = x.Length, i = 0;
            fixed (float* px = x)
            fixed (float* pd = destination)
            {
                if (AdvSimd.Arm64.IsSupported)
                {
                    for (; i + 4 <= n; i += 4)
                    {
                        if (AllInRange<TOp>(px + i, 4))
                            AdvSimd.Store(pd + i, TOp.Invoke(AdvSimd.LoadVector128(px + i)));
                        else
                            for (int j = i; j < i + 4; j++)
                                pd[j] = TOp.Invoke(px[j]);
                    }
                }
                else if (Avx2.IsSupported && Fma.IsSupported)
                {
                    for (; i + 8 <= n; i += 8)
                    {
                        if (AllInRange<TOp>(px + i, 8))
                            Avx.Store(pd + i, TOp.Invoke(Avx.LoadVector256(px + i)));
                        else
                            for (int j = i; j < i + 8; j++)
                                pd[j] = TOp.Invoke(px[j]);
                    }
                }
                for (; i < n; i++)
                    pd[i] = TOp.Invoke(px[i]);
            }
        }

        private static bool AllInRange<TOp>(float* x, int count) where TOp : IVectorUnary
        {
            bool all = true;
            for (int j = 0; j < count; j++)
                all &= TOp.InRange(x[j]);
            return all;
        }

        private static bool AllAtLeast(float* x, int count, float low)
        {
            bool all = true;
            for (int j = 0; j < count; j++)
                all &= x[j] >= low;
            return all;
        }

        // e^x for x in [ExpLo, ExpHi].
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<float> ExpVector(Vector128<float> x)
        {
            Vector128<float> fx = AdvSimd.RoundToNearest(AdvSimd.Multiply(x, AdvSimd.DuplicateToVector128(Log2E)));
            Vector128<float> r = AdvSimd.FusedMultiplyAdd(x, fx, AdvSimd.DuplicateToVector128(NegLn2Hi));
            r = AdvSimd.FusedMultiplyAdd(r, fx, AdvSimd.DuplicateToVector128(NegLn2Lo));
            Vector128<float> p = AdvSimd.DuplicateToVector128(P0);
            p = AdvSimd.FusedMultiplyAdd(AdvSimd.DuplicateToVector128(P1), p, r);
            p = AdvSimd.FusedMultiplyAdd(AdvSimd.DuplicateToVector128(P2), p, r);
            p = AdvSimd.FusedMultiplyAdd(AdvSimd.DuplicateToVector128(P3), p, r);
            p = AdvSimd.FusedMultiplyAdd(AdvSimd.DuplicateToVector128(P4), p, r);
            p = AdvSimd.FusedMultiplyAdd(AdvSimd.DuplicateToVector128(P5), p, r);
            Vector128<float> y = AdvSimd.FusedMultiplyAdd(AdvSimd.Add(r, AdvSimd.DuplicateToVector128(1f)), p, AdvSimd.Multiply(r, r));
            // 2^n: fx is a whole number, so fx + 127 is the exponent field.
            Vector128<int> n = AdvSimd.ConvertToInt32RoundToEven(AdvSimd.Add(fx, AdvSimd.DuplicateToVector128(127f)));
            return AdvSimd.Multiply(y, AdvSimd.ShiftLeftLogical(n.AsUInt32(), 23).AsSingle());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<float> ExpVector(Vector256<float> x)
        {
            Vector256<float> fx = Avx.RoundToNearestInteger(Avx.Multiply(x, Vector256.Create(Log2E)));
            Vector256<float> r = Fma.MultiplyAdd(fx, Vector256.Create(NegLn2Hi), x);
            r = Fma.MultiplyAdd(fx, Vector256.Create(NegLn2Lo), r);
            Vector256<float> p = Vector256.Create(P0);
            p = Fma.MultiplyAdd(p, r, Vector256.Create(P1));
            p = Fma.MultiplyAdd(p, r, Vector256.Create(P2));
            p = Fma.MultiplyAdd(p, r, Vector256.Create(P3));
            p = Fma.MultiplyAdd(p, r, Vector256.Create(P4));
            p = Fma.MultiplyAdd(p, r, Vector256.Create(P5));
            Vector256<float> y = Fma.MultiplyAdd(p, Avx.Multiply(r, r), Avx.Add(r, Vector256.Create(1f)));
            Vector256<int> n = Avx.ConvertToVector256Int32(Avx.Add(fx, Vector256.Create(127f)));
            return Avx.Multiply(y, Avx2.ShiftLeftLogical(n, 23).AsSingle());
        }

        // ---- helpers -------------------------------------------------------------

        private static float HorizontalSum(Vector128<float> v)
        {
            v = AdvSimd.Arm64.AddPairwise(v, v);
            v = AdvSimd.Arm64.AddPairwise(v, v); // every lane now holds the total
            return *(float*)&v;
        }

        private static float HorizontalSum(Vector256<float> v)
        {
            float* lanes = stackalloc float[8];
            Avx.Store(lanes, v);
            return ((lanes[0] + lanes[4]) + (lanes[1] + lanes[5])) + ((lanes[2] + lanes[6]) + (lanes[3] + lanes[7]));
        }
    }
}
