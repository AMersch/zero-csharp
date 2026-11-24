using System;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Internal.Runtime
{
    // [ThreadStatic] fields. The compiler puts each type's thread-static fields
    // in an object of their own, one per thread, and calls
    // GetThreadStaticBaseForType to get the current thread's. The module's
    // thread static region (StartupCodeHelpers.InitializeModule records it)
    // has the method table of that object for each such type, at its index.
    //
    // Each thread's objects are kept in an array behind a pthread key, made on
    // the thread's first access, and freed with them when the thread ends.
    // They come straight from calloc, whatever ObjectAllocator setting the
    // thread has: in an arena, they would dangle once the arena is reset.
    //
    // bflat's compiler uses this, the "uninlined" form, on every target; its
    // inlined thread statics (one block reached through native TLS) aren't
    // turned on, so GetInlinedThreadStaticBaseSlow is never called.
    internal static unsafe class ThreadStatics
    {
        internal static MethodTable** s_region;
        internal static int s_count;

        private static nuint s_key;
        private static int s_keyState; // 0 none, 1 being created, 2 ready

        // module: the module's TypeManager slot. A bflat program is one
        // module, so the region recorded at startup is the one.
        internal static object GetThreadStaticBaseForType(void* module, int typeTlsIndex)
        {
            if ((uint)typeTlsIndex >= (uint)s_count)
                Environment.FailFast("ThreadStatics: no thread static region for the type");
            nint* storage = Storage();
            nint threadStaticBase = storage[typeTlsIndex];
            if (threadStaticBase == 0)
            {
                object created = RuntimeExports.AllocateCalloc(s_region[typeTlsIndex]);
                threadStaticBase = Unsafe.As<object, nint>(ref created);
                storage[typeTlsIndex] = threadStaticBase;
            }
            return Unsafe.As<nint, object>(ref threadStaticBase);
        }

        private static object GetInlinedThreadStaticBaseSlow(ref object threadStorage)
        {
            Environment.FailFast("ThreadStatics: inlined thread statics aren't supported");
            return null;
        }

        // This thread's array of thread static bases, one slot per type.
        private static nint* Storage()
        {
            if (Volatile.Read(ref s_keyState) != 2)
                CreateKey();
            nint* storage = (nint*)Platform.pthread_getspecific(s_key);
            if (storage == null)
            {
                storage = (nint*)Platform.calloc((nuint)s_count, (nuint)sizeof(nint));
                if (storage == null || Platform.pthread_setspecific(s_key, storage) != 0)
                    Environment.FailFast("ThreadStatics: can't keep the thread's storage");
            }
            return storage;
        }

        private static void CreateKey()
        {
            if (Interlocked.CompareExchange(ref s_keyState, 1, 0) == 0)
            {
                nuint key = 0;
                if (Platform.pthread_key_create(&key, (void*)(delegate* unmanaged<void*, void>)&FreeStorage) != 0)
                    Environment.FailFast("ThreadStatics: no thread key");
                s_key = key;
                Volatile.Write(ref s_keyState, 2);
            }
            while (Volatile.Read(ref s_keyState) != 2)
            {
                // another thread is creating it
            }
        }

        // Called by the system when a thread that used thread statics ends.
        // Only that thread could reach these objects; values copied out of
        // them are elsewhere.
        [UnmanagedCallersOnly]
        private static void FreeStorage(void* storage)
        {
            nint* bases = (nint*)storage;
            for (int i = 0; i < s_count; i++)
            {
                if (bases[i] != 0)
                    Platform.free((void*)bases[i]);
            }
            Platform.free(storage);
        }
    }
}
