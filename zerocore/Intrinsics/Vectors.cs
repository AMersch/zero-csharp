using System.Runtime.CompilerServices;

// SIMD vector types. The compiler treats them as registers; the fields only
// give them their size.
namespace System.Runtime.Intrinsics
{
    [Intrinsic]
    public readonly struct Vector64<T> where T : struct
    {
        private readonly ulong _00;
    }

    [Intrinsic]
    public readonly struct Vector128<T> where T : struct
    {
        private readonly ulong _00;
        private readonly ulong _01;
    }

    [Intrinsic]
    public readonly struct Vector256<T> where T : struct
    {
        private readonly Vector128<T> _lower;
        private readonly Vector128<T> _upper;
    }

    // As with Unsafe, the compiler replaces each call with one instruction, so
    // the bodies never run. To use an operation that's missing, declare it here
    // with the name and signature it has in .NET.
    public static class Vector64
    {
        [Intrinsic]
        public static Vector64<byte> AsByte<T>(this Vector64<T> vector) where T : struct => AsByte(vector);

        [Intrinsic]
        public static Vector64<ulong> CreateScalar(ulong value) => CreateScalar(value);

        [Intrinsic]
        public static T ToScalar<T>(this Vector64<T> vector) where T : struct => ToScalar(vector);
    }

    public static class Vector128
    {
        [Intrinsic]
        public static Vector128<float> AsSingle<T>(this Vector128<T> vector) where T : struct => AsSingle(vector);

        [Intrinsic]
        public static Vector128<int> AsInt32<T>(this Vector128<T> vector) where T : struct => AsInt32(vector);

        [Intrinsic]
        public static Vector128<uint> AsUInt32<T>(this Vector128<T> vector) where T : struct => AsUInt32(vector);

        [Intrinsic]
        public static Vector128<float> Create(float value) => Create(value);

        [Intrinsic]
        public static Vector128<int> Create(int value) => Create(value);
    }

    public static class Vector256
    {
        [Intrinsic]
        public static Vector256<float> AsSingle<T>(this Vector256<T> vector) where T : struct => AsSingle(vector);

        [Intrinsic]
        public static Vector256<int> AsInt32<T>(this Vector256<T> vector) where T : struct => AsInt32(vector);

        [Intrinsic]
        public static Vector256<float> Create(float value) => Create(value);

        [Intrinsic]
        public static Vector256<int> Create(int value) => Create(value);
    }
}
