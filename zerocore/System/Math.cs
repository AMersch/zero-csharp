using System.Runtime;
using System.Runtime.CompilerServices;

// x != x is the test for NaN, the one value that isn't equal to itself.
#pragma warning disable CS1718

namespace System
{
    // Math on double. Where the processor has an instruction for an operation
    // (square root everywhere; rounding on arm64 and newer x64) the compiler
    // uses it; otherwise the call goes to the C math library, which is why
    // these have real bodies instead of calling themselves.
    public static class Math
    {
        public const double PI = 3.14159265358979323846;
        public const double E = 2.7182818284590452354;

        // What 32-bit targets (WebAssembly, DOS) call for a checked 64-bit
        // multiplication, which has no instruction there: `checked` code on
        // long, and the size of a `stackalloc` of nint. The compiler finds
        // them by name.
        private static ulong MultiplyChecked(ulong i, ulong j)
        {
            if (i != 0 && j > 0xFFFFFFFFFFFFFFFF / i)
                Environment.FailFast("OverflowException");
            return unchecked(i * j);
        }

        private static long MultiplyChecked(long i, long j)
        {
            bool negative = (i < 0) != (j < 0);
            ulong a = i < 0 ? unchecked(0 - (ulong)i) : (ulong)i;
            ulong b = j < 0 ? unchecked(0 - (ulong)j) : (ulong)j;
            ulong limit = negative ? 0x8000000000000000 : 0x7FFFFFFFFFFFFFFF;
            if (a != 0 && b > limit / a)
                Environment.FailFast("OverflowException");
            ulong product = unchecked(a * b);
            return negative ? unchecked((long)(0 - product)) : (long)product;
        }

        public static int Abs(int value) => value < 0 ? -value : value;
        public static long Abs(long value) => value < 0 ? -value : value;

        [Intrinsic]
        public static double Abs(double value) => Platform.fabs(value);

        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static uint Min(uint a, uint b) => a < b ? a : b;
        public static uint Max(uint a, uint b) => a > b ? a : b;
        public static long Min(long a, long b) => a < b ? a : b;
        public static long Max(long a, long b) => a > b ? a : b;
        public static ulong Min(ulong a, ulong b) => a < b ? a : b;
        public static ulong Max(ulong a, ulong b) => a > b ? a : b;

        // As in .NET, the result is NaN if either argument is.
        public static double Min(double a, double b) => a != a ? a : b != b ? b : a < b ? a : b;
        public static double Max(double a, double b) => a != a ? a : b != b ? b : a > b ? a : b;

        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static long Clamp(long value, long min, long max) => value < min ? min : value > max ? max : value;
        public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

        [Intrinsic]
        public static double Sqrt(double value) => Platform.sqrt(value);

        [Intrinsic]
        public static double Floor(double value) => Platform.floor(value);

        [Intrinsic]
        public static double Ceiling(double value) => Platform.ceil(value);

        [Intrinsic]
        public static double Truncate(double value) => Platform.trunc(value);

        /// <summary>Rounds to the nearest whole number; halfway cases go to the even one, as in .NET.</summary>
        [Intrinsic]
        public static double Round(double value) => Platform.rint(value);

        public static double Pow(double x, double y) => Platform.pow(x, y);
        public static double Exp(double value) => Platform.exp(value);
        public static double Log(double value) => Platform.log(value);
        public static double Log2(double value) => Platform.log2(value);
        public static double Log10(double value) => Platform.log10(value);
        public static double Sin(double value) => Platform.sin(value);
        public static double Cos(double value) => Platform.cos(value);
        public static double Tan(double value) => Platform.tan(value);
        public static double Atan2(double y, double x) => Platform.atan2(y, x);
    }

    // The same on float.
    public static class MathF
    {
        public const float PI = 3.14159265f;
        public const float E = 2.71828183f;

        [Intrinsic]
        public static float Abs(float value) => Platform.fabsf(value);

        public static float Min(float a, float b) => a != a ? a : b != b ? b : a < b ? a : b;
        public static float Max(float a, float b) => a != a ? a : b != b ? b : a > b ? a : b;

        [Intrinsic]
        public static float Sqrt(float value) => Platform.sqrtf(value);

        [Intrinsic]
        public static float Floor(float value) => Platform.floorf(value);

        [Intrinsic]
        public static float Ceiling(float value) => Platform.ceilf(value);

        [Intrinsic]
        public static float Truncate(float value) => Platform.truncf(value);

        [Intrinsic]
        public static float Round(float value) => Platform.rintf(value);

        public static float Pow(float x, float y) => Platform.powf(x, y);
        public static float Exp(float value) => Platform.expf(value);
        public static float Log(float value) => Platform.logf(value);
        public static float Sin(float value) => Platform.sinf(value);
        public static float Cos(float value) => Platform.cosf(value);
        public static float Tanh(float value) => Platform.tanhf(value);
    }
}
