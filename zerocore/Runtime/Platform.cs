using System.Runtime.InteropServices;

namespace System.Runtime
{
    // Everything the library needs from outside: three C library functions.
    // "*" binds them at link time; SuppressGCTransition makes each a plain call.
    internal static unsafe class Platform
    {
        [DllImport("*"), SuppressGCTransition]
        internal static extern void* calloc(nuint count, nuint size);

        [DllImport("*"), SuppressGCTransition]
        internal static extern nint write(int fd, void* buffer, nuint count);

        [DllImport("*"), SuppressGCTransition]
        internal static extern void abort();
    }
}
