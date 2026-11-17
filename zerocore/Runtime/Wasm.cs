namespace System
{
    internal static class Buffer
    {
        // Copying memory that holds object references. With a garbage collector
        // each reference stored would be reported to it; here it's a plain copy.
        internal static void BulkMoveWithWriteBarrier(ref byte destination, ref byte source, nuint byteCount)
            => SpanHelpers.Memmove(ref destination, ref source, byteCount);
    }
}

namespace System.Runtime
{
    // NativeAOT-LLVM, the WebAssembly compiler, requires this type for its
    // exception handling, substituting the method's result at build time.
    // Nothing here uses it: there are no exceptions to unwind.
    internal static class PreciseVirtualUnwindInfo
    {
        private static nuint GetUnwindInfoViaAbsoluteValueLimit() => 0;
    }
}
