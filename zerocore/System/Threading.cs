using System.Runtime.CompilerServices;

namespace System.Threading
{
    // Atomic operations on a variable shared between threads.
    //
    // On Linux the compiler replaces each call with the processor's atomic
    // instruction (lock cmpxchg / lock xadd on x64, an exclusive load/store loop
    // on arm64), so the bodies, which call themselves, never run. The
    // WebAssembly build is single-threaded and also serves DOS on a 386, which
    // has no such instruction (cmpxchg and xadd arrived with the 486). So there
    // the methods are ordinary code, and aren't marked [Intrinsic], which would
    // make the compiler replace them anyway.
    public static unsafe class Interlocked
    {
        /// <summary>Stores <paramref name="value"/> if the variable equals <paramref name="comparand"/>. Returns what the variable held.</summary>
#if !WASM
        [Intrinsic]
#endif
        public static int CompareExchange(ref int location, int value, int comparand)
        {
#if WASM
            int original = location;
            if (original == comparand)
                location = value;
            return original;
#else
            return CompareExchange(ref location, value, comparand);
#endif
        }

#if !WASM
        [Intrinsic]
#endif
        public static long CompareExchange(ref long location, long value, long comparand)
        {
#if WASM
            long original = location;
            if (original == comparand)
                location = value;
            return original;
#else
            return CompareExchange(ref location, value, comparand);
#endif
        }

        /// <summary>Stores <paramref name="value"/> and returns what the variable held.</summary>
#if !WASM
        [Intrinsic]
#endif
        public static int Exchange(ref int location, int value)
        {
#if WASM
            int original = location;
            location = value;
            return original;
#else
            return Exchange(ref location, value);
#endif
        }

#if !WASM
        [Intrinsic]
#endif
        public static long Exchange(ref long location, long value)
        {
#if WASM
            long original = location;
            location = value;
            return original;
#else
            return Exchange(ref location, value);
#endif
        }

        // Adds and returns the value from before the addition; Add, Increment
        // and Decrement are built on it.
#if !WASM
        [Intrinsic]
#endif
        private static int ExchangeAdd(ref int location, int value)
        {
#if WASM
            int original = location;
            location = original + value;
            return original;
#else
            return ExchangeAdd(ref location, value);
#endif
        }

#if !WASM
        [Intrinsic]
#endif
        private static long ExchangeAdd(ref long location, long value)
        {
#if WASM
            long original = location;
            location = original + value;
            return original;
#else
            return ExchangeAdd(ref location, value);
#endif
        }

        /// <summary>Adds and returns the new value.</summary>
        public static int Add(ref int location, int value) => ExchangeAdd(ref location, value) + value;
        public static long Add(ref long location, long value) => ExchangeAdd(ref location, value) + value;

        public static int Increment(ref int location) => Add(ref location, 1);
        public static long Increment(ref long location) => Add(ref location, 1);
        public static int Decrement(ref int location) => Add(ref location, -1);
        public static long Decrement(ref long location) => Add(ref location, -1);

        // Pointer-sized variables: the 32- or 64-bit operation, whichever fits.
        public static nint CompareExchange(ref nint location, nint value, nint comparand)
            => sizeof(nint) == 8
                ? (nint)CompareExchange(ref Unsafe.As<nint, long>(ref location), value, comparand)
                : CompareExchange(ref Unsafe.As<nint, int>(ref location), (int)value, (int)comparand);

        public static nint Exchange(ref nint location, nint value)
            => sizeof(nint) == 8
                ? (nint)Exchange(ref Unsafe.As<nint, long>(ref location), value)
                : Exchange(ref Unsafe.As<nint, int>(ref location), (int)value);

        /// <summary>Makes this thread's earlier writes visible to others before its later reads.</summary>
#if !WASM
        [Intrinsic]
#endif
        public static void MemoryBarrier()
        {
#if !WASM
            MemoryBarrier();
#endif
        }
    }

    // Reading and writing a variable another thread may change, without the
    // compiler caching the value or reordering the access. Each goes through a
    // struct with a `volatile` field, which is what makes the compiler emit the
    // right instructions. C# doesn't allow volatile 64-bit fields, so those go
    // through the pointer-sized struct on 64-bit targets.
    public static unsafe class Volatile
    {
        private struct VolatileBoolean { public volatile bool Value; }
        private struct VolatileByte { public volatile byte Value; }
        private struct VolatileInt32 { public volatile int Value; }
        private struct VolatileUInt32 { public volatile uint Value; }
        private struct VolatileIntPtr { public volatile nint Value; }
        private struct VolatileUIntPtr { public volatile nuint Value; }

        public static bool Read(ref bool location) => Unsafe.As<bool, VolatileBoolean>(ref location).Value;
        public static void Write(ref bool location, bool value) => Unsafe.As<bool, VolatileBoolean>(ref location).Value = value;

        public static byte Read(ref byte location) => Unsafe.As<byte, VolatileByte>(ref location).Value;
        public static void Write(ref byte location, byte value) => Unsafe.As<byte, VolatileByte>(ref location).Value = value;

        public static int Read(ref int location) => Unsafe.As<int, VolatileInt32>(ref location).Value;
        public static void Write(ref int location, int value) => Unsafe.As<int, VolatileInt32>(ref location).Value = value;

        public static uint Read(ref uint location) => Unsafe.As<uint, VolatileUInt32>(ref location).Value;
        public static void Write(ref uint location, uint value) => Unsafe.As<uint, VolatileUInt32>(ref location).Value = value;

        public static nint Read(ref nint location) => Unsafe.As<nint, VolatileIntPtr>(ref location).Value;
        public static void Write(ref nint location, nint value) => Unsafe.As<nint, VolatileIntPtr>(ref location).Value = value;

        public static nuint Read(ref nuint location) => Unsafe.As<nuint, VolatileUIntPtr>(ref location).Value;
        public static void Write(ref nuint location, nuint value) => Unsafe.As<nuint, VolatileUIntPtr>(ref location).Value = value;

        public static long Read(ref long location)
        {
            if (sizeof(nint) == 8)
                return Unsafe.As<long, VolatileIntPtr>(ref location).Value;
            long value = location; // 32-bit targets here are single-threaded
            return value;
        }

        public static void Write(ref long location, long value)
        {
            if (sizeof(nint) == 8)
                Unsafe.As<long, VolatileIntPtr>(ref location).Value = (nint)value;
            else
                location = value;
        }

        public static ulong Read(ref ulong location) => (ulong)Read(ref Unsafe.As<ulong, long>(ref location));
        public static void Write(ref ulong location, ulong value) => Write(ref Unsafe.As<ulong, long>(ref location), (long)value);
    }
}
