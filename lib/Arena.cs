// Growable arena (bump) allocator for --stdlib:zero.
// Compiled under the C# 15 updated memory safety rules (see build.sh).
//
// - Normal blocks grow in a Fibonacci sequence: 128, 128, 256, 384, 640, ...
// - An allocation that doesn't fit the next normal block gets a dedicated
//   block of its own and leaves that sequence alone.
// - Every allocation picks its alignment (any power of two, default 16).
// - Allocations fail by returning null; arena state is untouched on failure.
// - Reset() keeps the capacity by merging all blocks into one, so a
//   steady-state workload (one scratch arena per token, per frame, ...)
//   stops calling malloc after the first round.
//
// Allocating is safe: a pointer is harmless until it is dereferenced, and
// dereferencing needs an unsafe context anyway. Reset() and Destroy() are
// the dangerous operations, so they are marked unsafe: the caller promises
// that nothing still points into the arena.

using System;
using System.Runtime.InteropServices;

public static class Arena
{
    public const nuint DefaultAlign = 16;

    // The core library's nuint has no MaxValue.
    static nuint MaxSize => ~(nuint)0;

    // 32 bytes, so block data starts 16-byte aligned behind malloc's pointer.
    struct Block
    {
        public Block* Next;
        public byte* Start;
        public byte* End;
        public nuint Size;
    }

    static Block* s_first;
    static Block* s_current;
    static byte* s_ptr;
    static nuint s_initialSize;
    static nuint s_lastNormal;   // Fibonacci state for normal blocks
    static nuint s_prevNormal;
    static int s_blockCount;
    static nuint s_used;         // bytes handed out, padding excluded
    static nuint s_capacity;     // sum of all block sizes

    [DllImport("libc")]
    static safe extern void* malloc(nuint size);

    [DllImport("libc")]
    static unsafe extern void free(void* ptr);

    [DllImport("libc")]
    static unsafe extern void* memset(void* destination, int value, nuint count);

    public static bool IsInitialized => s_first != null;
    public static int BlockCount => s_blockCount;
    public static nuint Used => s_used;
    public static nuint Capacity => s_capacity;
    public static nuint NextBlockSize => NextNormalSize();

    public static nuint CurrentBlockSize
    {
        get
        {
            if (s_current == null)
                return 0;
            unsafe { return s_current->Size; }
        }
    }

    public static nuint CurrentBlockFree
    {
        get
        {
            if (s_current == null)
                return 0;
            unsafe { return (nuint)(s_current->End - s_ptr); }
        }
    }

    /// <summary>
    /// Creates the first block. Returns false if the arena is already initialized
    /// (call Destroy() first) or if malloc fails.
    /// </summary>
    public static bool Init(nuint blockSize = 4096)
    {
        if (s_first != null)
            return false;

        s_initialSize = blockSize == 0 ? 4096 : blockSize;
        s_lastNormal = s_initialSize;
        s_prevNormal = 0;
        return AddBlock(s_initialSize) != null;
    }

    /// <summary>Returns <paramref name="size"/> bytes aligned to <paramref name="align"/>, or null.</summary>
    public static void* Alloc(nuint size, nuint align = DefaultAlign, bool zero = true)
    {
        if (s_current == null || align == 0 || (align & (align - 1)) != 0)
            return null;

        byte* p = AlignUp(s_ptr, align);
        if (!Fits(p, size))
        {
            if (size > MaxSize - align)
                return null;

            nuint needed = size + align - 1; // worst-case padding in a fresh block
            nuint normal = NextNormalSize();
            bool dedicated = needed > normal;

            if (AddBlock(dedicated ? needed : normal) == null)
                return null;

            if (!dedicated)
            {
                s_prevNormal = s_lastNormal;
                s_lastNormal = normal;
            }
            p = AlignUp(s_ptr, align);
        }

        s_ptr = p + size;
        s_used += size;

        if (zero && size != 0)
            unsafe { memset(p, 0, size); } // SAFETY: [p, p + size) lies inside the current block.

        return p;
    }

    public static T* Alloc<T>(nuint align = DefaultAlign) where T : unmanaged
        => (T*)Alloc((nuint)sizeof(T), align);

    /// <summary>Returns room for <paramref name="count"/> elements, or null on overflow or OOM.</summary>
    public static T* AllocArray<T>(int count, nuint align = DefaultAlign, bool zero = true) where T : unmanaged
    {
        if (count < 0)
            return null;

        nuint elementSize = (nuint)sizeof(T);
        if ((nuint)count > MaxSize / elementSize)
            return null;

        return (T*)Alloc((nuint)count * elementSize, align, zero);
    }

    /// <summary>Like AllocArray, wrapped in a span. Returns an empty span on failure.</summary>
    /// <safety>The span points into the arena. Don't use it after Reset() or Destroy().</safety>
    public static unsafe Span<T> AllocSpan<T>(int count, nuint align = DefaultAlign) where T : unmanaged
    {
        T* p = AllocArray<T>(count, align);
        if (p == null)
            return default;
        unsafe { return new Span<T>(p, count); }
    }

    /// <summary>
    /// Frees every allocation at once. With <paramref name="keepCapacity"/> the blocks are
    /// merged into a single block of the same total size; otherwise the arena shrinks back
    /// to one initial-size block and the growth sequence restarts.
    /// </summary>
    /// <safety>Every pointer, span and collection that points into the arena becomes dangling.</safety>
    public static unsafe void Reset(bool keepCapacity = true)
    {
        if (s_first == null)
            return;

        s_used = 0;

        if (s_blockCount == 1 && (keepCapacity || s_capacity == s_initialSize))
        {
            unsafe { s_ptr = s_first->Start; }
            if (!keepCapacity)
            {
                s_lastNormal = s_initialSize;
                s_prevNormal = 0;
            }
            return;
        }

        nuint total = s_capacity;
        unsafe { FreeAllBlocks(); }

        if (keepCapacity && AddBlock(total) != null)
        {
            s_lastNormal = total;
            s_prevNormal = 0;
            return;
        }

        // Shrink back (or fall back when the merged block can't be allocated).
        s_lastNormal = s_initialSize;
        s_prevNormal = 0;
        AddBlock(s_initialSize);
    }

    /// <summary>Returns all memory to the system.</summary>
    /// <safety>Every pointer, span and collection that points into the arena becomes dangling.</safety>
    public static unsafe void Destroy()
    {
        unsafe { FreeAllBlocks(); }
        s_used = 0;
    }

    static unsafe void FreeAllBlocks()
    {
        Block* block = s_first;
        while (block != null)
        {
            unsafe
            {
                // SAFETY: blocks come from malloc in AddBlock and are freed exactly once here.
                Block* next = block->Next;
                free(block);
                block = next;
            }
        }
        s_first = null;
        s_current = null;
        s_ptr = null;
        s_blockCount = 0;
        s_capacity = 0;
    }

    static Block* AddBlock(nuint size)
    {
        if (size > MaxSize - (nuint)sizeof(Block))
            return null;

        byte* memory = (byte*)malloc((nuint)sizeof(Block) + size);
        if (memory == null)
            return null; // leave the arena exactly as it was

        Block* block = (Block*)memory;
        unsafe
        {
            // SAFETY: memory holds sizeof(Block) + size bytes.
            block->Next = null;
            block->Start = memory + sizeof(Block);
            block->End = block->Start + size;
            block->Size = size;

            if (s_current == null)
                s_first = block;
            else
                s_current->Next = block;

            s_ptr = block->Start;
        }

        s_current = block;
        s_blockCount++;
        s_capacity += size;
        return block;
    }

    static nuint NextNormalSize()
    {
        nuint next = s_lastNormal + s_prevNormal;
        if (next < s_lastNormal) // overflow
            next = s_lastNormal;
        return next == 0 ? s_initialSize : next;
    }

    static bool Fits(byte* p, nuint size)
    {
        byte* end;
        unsafe { end = s_current->End; }
        return p <= end && size <= (nuint)(end - p);
    }

    static byte* AlignUp(byte* p, nuint align)
        => (byte*)(((nuint)p + (align - 1)) & ~(align - 1));
}
