namespace System.Runtime.InteropServices
{
    // Where .NET throws, this aborts with a message.
    public static unsafe class NativeMemory
    {
        public static void* Alloc(nuint byteCount) => Allocate(byteCount);

        public static void* Alloc(nuint elementCount, nuint elementSize) => Allocate(Size(elementCount, elementSize));

        public static void* AllocZeroed(nuint byteCount) => Allocate(byteCount);

        public static void* AllocZeroed(nuint elementCount, nuint elementSize) => Allocate(Size(elementCount, elementSize));

        public static void Free(void* ptr)
        {
            if (ptr != null)
                Platform.free(ptr);
        }

        // A block from calloc, larger than asked for so that an aligned
        // address fits in it with the block's own pointer just below that
        // address, where AlignedFree finds it. Plain C# on calloc and free,
        // so it works the same on every target; posix_memalign, Windows'
        // _aligned_malloc and C11's aligned_alloc each miss some of them.
        public static void* AlignedAlloc(nuint byteCount, nuint alignment)
        {
            if (alignment == 0 || (alignment & (alignment - 1)) != 0)
                Environment.FailFast("NativeMemory: the alignment isn't a power of two");
            // At least a pointer's alignment, so the pointer below is aligned.
            if (alignment < (nuint)sizeof(void*))
                alignment = (nuint)sizeof(void*);
            nuint extra = alignment - 1 + (nuint)sizeof(void*);
            if (byteCount > ~(nuint)0 - extra)
                Environment.FailFast("NativeMemory: the size overflows");
            byte* block = (byte*)Allocate(byteCount + extra);
            byte* aligned = (byte*)(((nuint)block + (nuint)sizeof(void*) + alignment - 1) & ~(alignment - 1));
            ((void**)aligned)[-1] = block;
            return aligned;
        }

        // Only for a pointer from AlignedAlloc, as in .NET.
        public static void AlignedFree(void* ptr)
        {
            if (ptr != null)
                Platform.free(((void**)ptr)[-1]);
        }

        private static nuint Size(nuint elementCount, nuint elementSize)
        {
            if (elementSize != 0 && elementCount > ~(nuint)0 / elementSize)
                Environment.FailFast("NativeMemory: the size overflows");
            return elementCount * elementSize;
        }

        private static void* Allocate(nuint byteCount)
        {
            // C allows no object larger than PTRDIFF_MAX; some systems (Rosetta)
            // crash on such a request instead of returning null.
            void* block = byteCount > (nuint)nint.MaxValue ? null : Platform.calloc(byteCount == 0 ? 1 : byteCount, 1);
            if (block == null)
                Environment.FailFast("NativeMemory: out of memory");
            return block;
        }
    }
}
