using System.Runtime.InteropServices;

namespace System.Runtime
{
    // Everything the library needs from outside: a few functions from the C
    // library and, for Math, the C math library.
    // "*" binds them at link time; SuppressGCTransition makes each a plain call.
    internal static unsafe class Platform
    {
        [DllImport("*"), SuppressGCTransition]
        internal static extern void* calloc(nuint count, nuint size);

        // Only for memory nothing else can reach any more: a finished thread's
        // thread-static storage (Internal.Runtime.ThreadStatics).
        [DllImport("*"), SuppressGCTransition]
        internal static extern void free(void* memory);

        // For NativeMemory.Realloc. DOS and UEFI get it from their runtime.c.
        [DllImport("*"), SuppressGCTransition]
        internal static extern void* realloc(void* memory, nuint size);

        [DllImport("*"), SuppressGCTransition]
        internal static extern void* memmove(void* destination, void* source, nuint count);

        [DllImport("*"), SuppressGCTransition]
        internal static extern nint write(int fd, void* buffer, nuint count);

        [DllImport("*"), SuppressGCTransition]
        internal static extern void abort();

        // For Environment. DOS, UEFI and Windows get get_nprocs, clock_gettime
        // (Linux's clock numbers; 1 is the monotonic clock) and getenv (UTF-8)
        // from their runtime.c, macOS and WebAssembly some of them; DOS and UEFI
        // get exit there too.
        [DllImport("*"), SuppressGCTransition]
        internal static extern void exit(int code);

        [DllImport("*"), SuppressGCTransition]
        internal static extern byte* getenv(byte* name);

        [DllImport("*"), SuppressGCTransition]
        internal static extern int get_nprocs();

        [DllImport("*"), SuppressGCTransition]
        internal static extern int clock_gettime(int clock, long* timespec);

        // Gives the processor to another thread: a thread waiting for another
        // one's static constructor (ClassConstructorRunner) calls it between
        // checks. Windows and DOS get it from their runtime.c.
        [DllImport("*"), SuppressGCTransition]
        internal static extern int sched_yield();

        // Thread-local storage, for ObjectAllocator. pthread_key_t is an int
        // on Linux and a long on macOS: nuint holds either. Windows, DOS and
        // WebAssembly get these from their runtime.c or C library.
        [DllImport("*"), SuppressGCTransition]
        internal static extern int pthread_key_create(nuint* key, void* destructor);

        [DllImport("*"), SuppressGCTransition]
        internal static extern void* pthread_getspecific(nuint key);

        [DllImport("*"), SuppressGCTransition]
        internal static extern int pthread_setspecific(nuint key, void* value);

        // ---- libm ----

        [DllImport("*"), SuppressGCTransition]
        internal static extern double fabs(double x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern double sqrt(double x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern double floor(double x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern double ceil(double x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern double trunc(double x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern double rint(double x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern double exp(double x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern double log(double x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern double log2(double x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern double log10(double x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern double sin(double x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern double cos(double x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern double tan(double x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern double pow(double x, double y);

        [DllImport("*"), SuppressGCTransition]
        internal static extern double atan2(double x, double y);

        [DllImport("*"), SuppressGCTransition]
        internal static extern float fabsf(float x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern float sqrtf(float x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern float floorf(float x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern float ceilf(float x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern float truncf(float x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern float rintf(float x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern float expf(float x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern float logf(float x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern float sinf(float x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern float cosf(float x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern float tanhf(float x);

        [DllImport("*"), SuppressGCTransition]
        internal static extern float powf(float x, float y);
    }
}
