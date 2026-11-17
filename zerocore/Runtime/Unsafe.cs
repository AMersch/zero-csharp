namespace System.Runtime.CompilerServices
{
    // The compiler replaces every call to these with the operation itself, so
    // the bodies (which just call themselves) never run.
    public static unsafe class Unsafe
    {
        [Intrinsic]
        public static T As<T>(object o) where T : class => As<T>(o);

        [Intrinsic]
        public static ref TTo As<TFrom, TTo>(ref TFrom source) => ref As<TFrom, TTo>(ref source);

        [Intrinsic]
        public static ref T Add<T>(ref T source, int elementOffset) => ref Add(ref source, elementOffset);

        [Intrinsic]
        public static ref T Add<T>(ref T source, nint elementOffset) => ref Add(ref source, elementOffset);

        [Intrinsic]
        public static ref T AddByteOffset<T>(ref T source, nint byteOffset) => ref AddByteOffset(ref source, byteOffset);

        [Intrinsic]
        public static void* AsPointer<T>(ref T value) => AsPointer(ref value);

        [Intrinsic]
        public static ref T AsRef<T>(void* source) => ref AsRef<T>(source);

        [Intrinsic]
        public static int SizeOf<T>() => SizeOf<T>();

        [Intrinsic]
        public static bool AreSame<T>(ref T left, ref T right) => AreSame(ref left, ref right);
    }

    public static unsafe class RuntimeHelpers
    {
        // Where a string's characters start, counted from the object's address.
        public static int OffsetToStringData => sizeof(nint) + sizeof(int);

        // new int[] { 1, 2, 3 }: copies the constant data into the array. The
        // compiler expands the whole sequence the C# compiler emits for an array
        // initializer; called any other way, there is nothing this could do.
        [Intrinsic]
        public static void InitializeArray(Array array, RuntimeFieldHandle fldHandle)
            => Environment.FailFast("RuntimeHelpers.InitializeArray outside an array initializer");

        // ReadOnlySpan<int> x = [1, 2, 3]: a span over the constant data, likewise.
        [Intrinsic]
        public static ReadOnlySpan<T> CreateSpan<T>(RuntimeFieldHandle fldHandle)
        {
            Environment.FailFast("RuntimeHelpers.CreateSpan outside a span initializer");
            return default;
        }

        [Intrinsic]
        public static bool IsReferenceOrContainsReferences<T>() => IsReferenceOrContainsReferences<T>();
    }
}

namespace System.Runtime.InteropServices
{
    using System.Runtime.CompilerServices;

    public static unsafe class MemoryMarshal
    {
        // The first element of an array; its data starts two pointers into the
        // object (method table, then the length padded to pointer size).
        public static ref T GetArrayDataReference<T>(T[] array)
            => ref Unsafe.As<byte, T>(ref Unsafe.AddByteOffset(ref Unsafe.As<Internal.Runtime.RawData>(array).Data, (nint)sizeof(nint)));

        public static ref T GetReference<T>(Span<T> span) => ref span._reference;

        public static ref T GetReference<T>(ReadOnlySpan<T> span) => ref span._reference;

        public static Span<T> CreateSpan<T>(ref T reference, int length) => new Span<T>(ref reference, length);
    }
}
