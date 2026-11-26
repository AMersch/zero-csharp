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

        public static void Clear(void* ptr, nuint byteCount) => Platform.memset(ptr, 0, byteCount);

        // memmove: the two blocks may overlap, as .NET allows.
        public static void Copy(void* source, void* destination, nuint byteCount) => Platform.memmove(destination, source, byteCount);

        public static void Fill(void* ptr, nuint byteCount, byte value) => Platform.memset(ptr, value, byteCount);

        // Keeps the first byteCount bytes, or as many as the block had. Unlike
        // Alloc, the memory past the old end isn't zeroed: realloc doesn't
        // say how large the block was. A null ptr is Alloc; if it runs out,
        // the program ends with the old block still allocated.
        public static void* Realloc(void* ptr, nuint byteCount) => Reallocate(ptr, byteCount);

        // A block from calloc, larger than asked for so that an aligned
        // address fits in it with the block's own pointer just below that
        // address, where AlignedFree finds it. Plain C# on calloc and free,
        // so it works the same on every target; posix_memalign, Windows'
        // _aligned_malloc and C11's aligned_alloc each miss some of them.
        public static void* AlignedAlloc(nuint byteCount, nuint alignment)
        {
            nuint extra = Extra(ref alignment, byteCount);
            byte* block = (byte*)Allocate(byteCount + extra);
            byte* aligned = Align(block, alignment);
            ((void**)aligned)[-1] = block;
            return aligned;
        }

        // Only for a pointer from AlignedAlloc or AlignedRealloc, as in .NET.
        // The alignment may differ from the block's old one.
        public static void* AlignedRealloc(void* ptr, nuint byteCount, nuint alignment)
        {
            if (ptr == null)
                return AlignedAlloc(byteCount, alignment);
            nuint extra = Extra(ref alignment, byteCount);
            byte* old = (byte*)((void**)ptr)[-1];
            nuint offset = (nuint)((byte*)ptr - old);
            // Room for the data where it is now, at the old offset, which a
            // larger old alignment may put past the new padding: realloc keeps
            // only what fits in the new size.
            nuint room = offset > extra ? offset : extra;
            if (byteCount > ~(nuint)0 - room)
                Environment.FailFast("NativeMemory: the size overflows");
            byte* block = (byte*)Reallocate(old, byteCount + room);
            byte* aligned = Align(block, alignment);
            // Moved before the pointer goes below it, which may be where
            // some of the data is now.
            if (aligned != block + offset)
                Platform.memmove(aligned, block + offset, byteCount);
            ((void**)aligned)[-1] = block;
            return aligned;
        }

        public static void AlignedFree(void* ptr)
        {
            if (ptr != null)
                Platform.free(((void**)ptr)[-1]);
        }

        // The bytes an aligned block needs beyond byteCount; raises alignment
        // to at least a pointer's, so the pointer below the block is aligned.
        private static nuint Extra(ref nuint alignment, nuint byteCount)
        {
            if (alignment == 0 || (alignment & (alignment - 1)) != 0)
                Environment.FailFast("NativeMemory: the alignment isn't a power of two");
            if (alignment < (nuint)sizeof(void*))
                alignment = (nuint)sizeof(void*);
            nuint extra = alignment - 1 + (nuint)sizeof(void*);
            if (byteCount > ~(nuint)0 - extra)
                Environment.FailFast("NativeMemory: the size overflows");
            return extra;
        }

        // The first aligned address in block with room for a pointer below it.
        private static byte* Align(byte* block, nuint alignment)
            => (byte*)(((nuint)block + (nuint)sizeof(void*) + alignment - 1) & ~(alignment - 1));

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

        private static void* Reallocate(void* ptr, nuint byteCount)
        {
            // Never 0: realloc(p, 0) may free p and return null (glibc does).
            void* block = byteCount > (nuint)nint.MaxValue ? null : Platform.realloc(ptr, byteCount == 0 ? 1 : byteCount);
            if (block == null)
                Environment.FailFast("NativeMemory: out of memory");
            return block;
        }
    }
}
