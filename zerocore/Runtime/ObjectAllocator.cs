namespace System.Runtime
{
    // Where objects come from. By default every object (new, boxing, closures,
    // delegates, arrays, string concatenation) is calloc'd and never freed.
    // Set lets a library send them somewhere else for a while, typically an
    // arena that is later reset all at once (lib/Arena.cs: AllocateObjectsHere).
    //
    // The function gets the context it was set with and a size, and returns
    // that many zeroed bytes, aligned to 8 bytes, or null (which ends the
    // program: "Out of memory").
    //
    // The setting belongs to the thread that makes it: each thread allocates
    // with its own, and threads that never set one use calloc. It lives in a
    // pthread key, read only while some thread has a setting, so a program
    // that never sets one pays a single check per allocation.
    public static unsafe class ObjectAllocator
    {
        private struct Setting
        {
            public delegate*<void*, nuint, void*> Allocate;
            public void* Context;
        }

        private static nuint s_key;      // pthread_key_t, once s_keyState is Ready
        private static int s_keyState;   // NoKey, Creating or Ready
        private const int NoKey = 0, Creating = 1, Ready = 2;
        internal static int s_threadsWithSetting;

        // This thread's setting, or null if it has never set one.
        internal static void* CurrentSetting()
        {
            if (System.Threading.Volatile.Read(ref s_keyState) != Ready)
                return null;
            return Platform.pthread_getspecific(s_key);
        }

        internal static void* Allocate(void* setting, nuint size)
        {
            Setting* current = (Setting*)setting;
            return current->Allocate(current->Context, size);
        }

        internal static bool HasAllocate(void* setting)
            => setting != null && ((Setting*)setting)->Allocate != null;

        private static void CreateKey()
        {
            if (System.Threading.Interlocked.CompareExchange(ref s_keyState, Creating, NoKey) == NoKey)
            {
                nuint key = 0;
                if (Platform.pthread_key_create(&key, null) != 0)
                    Environment.FailFast("ObjectAllocator: no thread-local key");
                s_key = key;
                System.Threading.Volatile.Write(ref s_keyState, Ready);
            }
            while (System.Threading.Volatile.Read(ref s_keyState) != Ready)
            {
                // another thread is creating it
            }
        }

        /// <summary>Sends this thread's allocations to <paramref name="allocate"/> (null: back to calloc). Get gives the previous setting.</summary>
        public static void Set(delegate*<void*, nuint, void*> allocate, void* context)
        {
            CreateKey();
            Setting* setting = (Setting*)Platform.pthread_getspecific(s_key);
            if (setting == null)
            {
                // One small record per thread that ever sets an allocator.
                setting = (Setting*)Platform.calloc(1, (nuint)sizeof(Setting));
                if (setting == null || Platform.pthread_setspecific(s_key, setting) != 0)
                    Environment.FailFast("ObjectAllocator: out of memory");
            }
            bool had = setting->Allocate != null;
            setting->Allocate = allocate;
            setting->Context = context;
            if (!had && allocate != null)
                System.Threading.Interlocked.Increment(ref s_threadsWithSetting);
            else if (had && allocate == null)
                System.Threading.Interlocked.Decrement(ref s_threadsWithSetting);
        }

        /// <summary>
        /// Whether any static field holds a reference into [start, end): for
        /// checking that nothing static was left pointing at objects about to
        /// be freed. Slow (it reads every static); for debug checks.
        /// </summary>
        public static bool StaticsReferInto(void* start, void* end)
            => Internal.Runtime.CompilerHelpers.StartupCodeHelpers.StaticsReferInto((byte*)start, (byte*)end);

        /// <summary>This thread's current setting, so it can be put back.</summary>
        public static void Get(out delegate*<void*, nuint, void*> allocate, out void* context)
        {
            Setting* setting = (Setting*)CurrentSetting();
            allocate = setting == null ? null : setting->Allocate;
            context = setting == null ? null : setting->Context;
        }
    }
}
