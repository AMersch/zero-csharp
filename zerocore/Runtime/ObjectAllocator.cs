namespace System.Runtime
{
    // Where objects come from. By default every object (new, boxing, closures,
    // delegates, arrays, string concatenation) is calloc'd and never freed.
    // Set lets a library send them somewhere else for a while, typically an
    // arena that is later reset all at once (lib/Arena.cs: AllocateObjectsHere).
    //
    // The function gets the context it was set with and a size, and returns
    // that many zeroed bytes, aligned for a pointer, or null (which ends the
    // program: "Out of memory").
    //
    // One setting for the whole program, not one per thread: set it only while
    // a single thread allocates.
    public static unsafe class ObjectAllocator
    {
        internal static delegate*<void*, nuint, void*> s_allocate;
        internal static void* s_context;

        /// <summary>Sends every allocation to <paramref name="allocate"/> (null: back to calloc); returns nothing, Get gives the previous setting.</summary>
        public static void Set(delegate*<void*, nuint, void*> allocate, void* context)
        {
            s_allocate = allocate;
            s_context = context;
        }

        /// <summary>
        /// Whether any static field holds a reference into [start, end): for
        /// checking that nothing static was left pointing at objects about to
        /// be freed. Slow (it reads every static); for debug checks.
        /// </summary>
        public static bool StaticsReferInto(void* start, void* end)
            => Internal.Runtime.CompilerHelpers.StartupCodeHelpers.StaticsReferInto((byte*)start, (byte*)end);

        /// <summary>The current setting, so it can be put back.</summary>
        public static void Get(out delegate*<void*, nuint, void*> allocate, out void* context)
        {
            allocate = s_allocate;
            context = s_context;
        }
    }
}
