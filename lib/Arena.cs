// Growable arena (bump) allocator for zero mode.
// Compiled under the C# 15 updated memory safety rules (see build.sh).
//
// An arena hands out memory by moving a pointer forward and frees everything
// at once. Create one per lifetime: one for the whole run, one per request or
// frame, one for scratch work.
//
//   Arena arena = Arena.Create();
//   Point* p = arena.Alloc<Point>();
//   ...
//   unsafe { arena.Reset(); }      // everything allocated from it is gone
//
// - Arena is a handle (one pointer), so copies refer to the same arena, like
//   passing Arena* in C. A default(Arena) is "not created": every allocation
//   from it returns null.
// - Normal blocks grow in a Fibonacci sequence: 128, 128, 256, 384, 640, ...
// - An allocation that doesn't fit the next normal block gets a dedicated
//   block of its own and leaves that sequence alone.
// - Every allocation picks its alignment (any power of two, default 16).
// - Allocations fail by returning null; arena state is untouched on failure.
// - Save() and Restore() free only what was allocated in between, for
//   temporary work in an arena that also holds longer-lived data. Don't let
//   an older collection grow in between (see Restore).
// - Reset() keeps the capacity by merging all blocks into one, so a
//   steady-state workload (one scratch arena per token, per frame, ...)
//   stops calling malloc after the first round.
// - Not thread-safe: use one arena per thread.
// - Arena.DebugChecks = true (set it first thing in Main) makes arenas created
//   afterwards fill freed memory with 0xDD bytes, and makes List, Dict and
//   StrBuilder stop the program when used after their memory was freed. See
//   DebugChecks for what that does and doesn't catch.
//
// Allocating is safe: a pointer is harmless until it is dereferenced, and
// dereferencing needs an unsafe context anyway. Restore(), Reset() and
// Destroy() are the dangerous operations, so they are marked unsafe: the
// caller promises that nothing still points at the memory they free.

using System;
using System.Runtime.InteropServices;

public struct Arena
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

    struct State
    {
        public Block* First;
        public Block* Current;
        public byte* Ptr;
        public nuint InitialSize;
        public nuint LastNormal;   // Fibonacci state for normal blocks
        public nuint PrevNormal;
        public int BlockCount;
        public uint Generation;    // bumped by Reset, to catch marks from before it
        public bool Poison;        // DebugChecks was on when the arena was created
        public nuint Used;         // bytes handed out, padding excluded
        public nuint Capacity;     // sum of all block sizes
    }

    /// <summary>A point in an arena's history, from Save(). Restore() frees everything allocated after it.</summary>
    public struct Mark
    {
        internal void* Owner;
        internal void* Block;
        internal byte* Ptr;
        internal nuint Used;
        internal nuint LastNormal;
        internal nuint PrevNormal;
        internal uint Generation;
    }

    State* _state;

    /// <summary>
    /// Debugging aid, off by default. While on:
    /// - Arenas created from then on fill memory with 0xDD bytes when it is freed
    ///   (Restore, Reset, Destroy) and when a block is first obtained. A stale
    ///   pointer then reads 0xDDDDDDDD instead of plausible old data, and so does
    ///   memory allocated with zero: false and never written.
    /// - List, Dict and StrBuilder check on every use that their buffer is still
    ///   allocated, and end the program with a message if it isn't. That catches
    ///   a collection used after its arena was reset, and one that grew after a
    ///   Save() and was used after the Restore().
    /// It can't catch everything: once freed memory has been handed out again,
    /// a stale pointer into it looks valid. And it costs time: a fill on every
    /// free, a check on every collection operation.
    /// </summary>
    public static bool DebugChecks;

    const byte PoisonByte = 0xDD;

    // What a pointer read from poisoned memory looks like.
    static nuint PoisonPointer => sizeof(nuint) == 8 ? unchecked((nuint)0xDDDDDDDDDDDDDDDD) : 0xDDDDDDDD;

    [DllImport("libc")]
    static safe extern void* malloc(nuint size);

    [DllImport("libc")]
    static unsafe extern void free(void* ptr);

    [DllImport("libc")]
    static unsafe extern void* memset(void* destination, int value, nuint count);

    /// <summary>Returns a new arena with one block, or a not-created arena if malloc fails.</summary>
    public static Arena Create(nuint blockSize = 4096)
    {
        State* state = (State*)malloc((nuint)sizeof(State));
        if (state == null)
            return default;

        unsafe
        {
            // SAFETY: state holds sizeof(State) bytes.
            memset(state, 0, (nuint)sizeof(State));
            state->InitialSize = blockSize == 0 ? 4096 : blockSize;
            state->LastNormal = state->InitialSize;
            state->Poison = DebugChecks;

            if (AddBlock(state, state->InitialSize) == null)
            {
                free(state);
                return default;
            }
        }
        return new Arena { _state = state };
    }

    public bool IsCreated => _state != null;

    // SAFETY (for every `unsafe` block reading _state below): a non-null _state
    // points at a live State unless the arena was destroyed, and Destroy is
    // unsafe: its caller promises no copy of the handle is used afterwards.

    public int BlockCount
    {
        get
        {
            if (_state == null)
                return 0;
            unsafe { return _state->BlockCount; }
        }
    }

    /// <summary>Bytes handed out, padding excluded.</summary>
    public nuint Used
    {
        get
        {
            if (_state == null)
                return 0;
            unsafe { return _state->Used; }
        }
    }

    /// <summary>The sum of all block sizes.</summary>
    public nuint Capacity
    {
        get
        {
            if (_state == null)
                return 0;
            unsafe { return _state->Capacity; }
        }
    }

    public nuint NextBlockSize => _state == null ? 0 : NextNormalSize(_state);

    public nuint CurrentBlockSize
    {
        get
        {
            if (_state == null)
                return 0;
            unsafe { return _state->Current->Size; }
        }
    }

    public nuint CurrentBlockFree
    {
        get
        {
            if (_state == null)
                return 0;
            unsafe { return (nuint)(_state->Current->End - _state->Ptr); }
        }
    }

    /// <summary>Returns <paramref name="size"/> bytes aligned to <paramref name="align"/>, or null.</summary>
    public void* Alloc(nuint size, nuint align = DefaultAlign, bool zero = true)
    {
        State* state = _state;
        if (state == null || align == 0 || (align & (align - 1)) != 0)
            return null;

        unsafe
        {
            byte* p = AlignUp(state->Ptr, align);
            if (!Fits(state, p, size))
            {
                if (size > MaxSize - align)
                    return null;

                nuint needed = size + align - 1; // worst-case padding in a fresh block
                nuint normal = NextNormalSize(state);
                bool dedicated = needed > normal;

                if (AddBlock(state, dedicated ? needed : normal) == null)
                    return null;

                if (!dedicated)
                {
                    state->PrevNormal = state->LastNormal;
                    state->LastNormal = normal;
                }
                p = AlignUp(state->Ptr, align);
            }

            state->Ptr = p + size;
            state->Used += size;

            if (zero && size != 0)
                memset(p, 0, size); // SAFETY: [p, p + size) lies inside the current block.

            return p;
        }
    }

    public T* Alloc<T>(nuint align = DefaultAlign) where T : unmanaged
        => (T*)Alloc((nuint)sizeof(T), align);

    /// <summary>Returns room for <paramref name="count"/> elements, or null on overflow or OOM.</summary>
    public T* AllocArray<T>(int count, nuint align = DefaultAlign, bool zero = true) where T : unmanaged
    {
        if (count < 0)
            return null;

        nuint elementSize = (nuint)sizeof(T);
        if ((nuint)count > MaxSize / elementSize)
            return null;

        return (T*)Alloc((nuint)count * elementSize, align, zero);
    }

    /// <summary>Like AllocArray, wrapped in a span. Returns an empty span on failure.</summary>
    /// <safety>The span points into the arena. Don't use it after the memory is freed.</safety>
    public unsafe Span<T> AllocSpan<T>(int count, nuint align = DefaultAlign) where T : unmanaged
    {
        T* p = AllocArray<T>(count, align);
        if (p == null)
            return default;
        unsafe { return new Span<T>(p, count); }
    }

    /// <summary>
    /// Grows an allocation in place from <paramref name="oldSize"/> to
    /// <paramref name="newSize"/> bytes. This only works for the arena's most
    /// recent allocation, and only while its block has room; otherwise it returns
    /// false and changes nothing. The added bytes are not zeroed.
    /// </summary>
    /// <remarks>Lets a buffer that is still last in the arena grow without leaving its old copy behind.</remarks>
    public bool TryExtend(void* allocation, nuint oldSize, nuint newSize)
    {
        State* state = _state;
        if (state == null || allocation == null || newSize < oldSize)
            return false;

        unsafe
        {
            // Comparing addresses only; nothing is dereferenced through allocation.
            byte* end = (byte*)allocation + oldSize;
            nuint extra = newSize - oldSize;
            if (end != state->Ptr || extra > (nuint)(state->Current->End - end))
                return false;

            state->Ptr = end + extra;
            state->Used += extra;
        }
        return true;
    }

    /// <summary>
    /// Whether <paramref name="pointer"/> points into memory this arena has
    /// handed out and not freed since. Approximate: padding between allocations
    /// counts as handed out.
    /// </summary>
    public bool IsLive(void* pointer)
    {
        State* state = _state;
        if (state == null || pointer == null)
            return false;

        unsafe
        {
            // Comparing addresses only; nothing is dereferenced through pointer.
            byte* p = (byte*)pointer;
            for (Block* block = state->First; block != null; block = block->Next)
            {
                byte* end = block == state->Current ? state->Ptr : block->End;
                if (p >= block->Start && p < end)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// For collections: under DebugChecks, ends the program if
    /// <paramref name="buffer"/>, which the collection got from
    /// <paramref name="arena"/>, has been freed since.
    /// </summary>
    internal static void CheckLive(Arena arena, void* buffer)
    {
        if (!DebugChecks)
            return;
        // The collection's own header was freed and filled: its arena field is poison.
        if ((nuint)arena._state == PoisonPointer)
            Environment.FailFast("Arena.DebugChecks: collection used after its arena was reset or restored");
        if (arena._state != null && !arena.IsLive(buffer))
            Environment.FailFast("Arena.DebugChecks: collection's buffer was freed (it grew after Save() and was used after Restore(), or its arena was reset)");
    }

    /// <summary>Remembers the arena's current position, for Restore().</summary>
    public Mark Save()
    {
        State* state = _state;
        if (state == null)
            return default;

        unsafe
        {
            return new Mark
            {
                Owner = state,
                Block = state->Current,
                Ptr = state->Ptr,
                Used = state->Used,
                LastNormal = state->LastNormal,
                PrevNormal = state->PrevNormal,
                Generation = state->Generation,
            };
        }
    }

    /// <summary>
    /// Frees everything allocated since <paramref name="mark"/> was saved, and
    /// nothing from before it. Marks must be restored newest first. A mark from
    /// another arena, or from before a Reset(), ends the program.
    /// </summary>
    /// <safety>
    /// Every pointer, span and collection allocated since the mark becomes
    /// dangling. That includes the buffer of an older List, Dict or StrBuilder
    /// that grew after the mark: its new buffer is freed here while the
    /// collection still points at it, and later allocations overwrite it without
    /// any error. So between Save() and Restore(), only add to collections
    /// created after the mark. For temporary work that adds to long-lived
    /// collections, use a separate scratch arena instead.
    /// </safety>
    public unsafe void Restore(Mark mark)
    {
        State* state = _state;
        if (state == null)
            return;

        unsafe
        {
            if (mark.Owner != state || mark.Generation != state->Generation)
                Environment.FailFast("Arena.Restore: the mark doesn't belong to this arena's current contents");

            // SAFETY: a mark of this generation names a block that is still in
            // the chain; the blocks after it were added since and are freed once.
            Block* keep = (Block*)mark.Block;
            Block* block = keep->Next;
            while (block != null)
            {
                Block* next = block->Next;
                state->BlockCount--;
                state->Capacity -= block->Size;
                Poison(state, block->Start, block->End);
                free(block);
                block = next;
            }
            keep->Next = null;
            Poison(state, mark.Ptr, keep == state->Current ? state->Ptr : keep->End);

            state->Current = keep;
            state->Ptr = mark.Ptr;
            state->Used = mark.Used;
            state->LastNormal = mark.LastNormal;
            state->PrevNormal = mark.PrevNormal;
        }
    }

    /// <summary>
    /// Frees every allocation at once. With <paramref name="keepCapacity"/> the blocks are
    /// merged into a single block of the same total size; otherwise the arena shrinks back
    /// to one initial-size block and the growth sequence restarts.
    /// </summary>
    /// <safety>Every pointer, span and collection that points into the arena becomes dangling.</safety>
    public unsafe void Reset(bool keepCapacity = true)
    {
        State* state = _state;
        if (state == null)
            return;

        unsafe
        {
            state->Used = 0;
            state->Generation++;

            if (state->BlockCount == 1 && (keepCapacity || state->Capacity == state->InitialSize))
            {
                Poison(state, state->First->Start, state->Ptr);
                state->Ptr = state->First->Start;
                if (!keepCapacity)
                {
                    state->LastNormal = state->InitialSize;
                    state->PrevNormal = 0;
                }
                return;
            }

            nuint total = state->Capacity;
            FreeAllBlocks(state);

            if (keepCapacity && AddBlock(state, total) != null)
            {
                state->LastNormal = total;
                state->PrevNormal = 0;
                return;
            }

            // Shrink back (or fall back when the merged block can't be allocated).
            state->LastNormal = state->InitialSize;
            state->PrevNormal = 0;
            if (AddBlock(state, state->InitialSize) == null)
                Environment.FailFast("Arena.Reset: out of memory");
        }
    }

    /// <summary>Returns all memory to the system. This handle becomes not-created.</summary>
    /// <safety>
    /// Every pointer, span and collection that points into the arena becomes
    /// dangling, and so does every other copy of this handle.
    /// </safety>
    public unsafe void Destroy()
    {
        State* state = _state;
        if (state == null)
            return;

        unsafe
        {
            // SAFETY: the state and its blocks come from malloc and are freed exactly once here.
            FreeAllBlocks(state);
            free(state);
        }
        _state = null;
    }

    static unsafe void FreeAllBlocks(State* state)
    {
        unsafe
        {
            // SAFETY: blocks come from malloc in AddBlock and are freed exactly once here.
            Block* block = state->First;
            while (block != null)
            {
                Block* next = block->Next;
                Poison(state, block->Start, block == state->Current ? state->Ptr : block->End);
                free(block);
                block = next;
            }
            state->First = null;
            state->Current = null;
            state->Ptr = null;
            state->BlockCount = 0;
            state->Capacity = 0;
        }
    }

    static unsafe Block* AddBlock(State* state, nuint size)
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
            Poison(state, block->Start, block->End);

            if (state->Current == null)
                state->First = block;
            else
                state->Current->Next = block;

            state->Ptr = block->Start;
            state->Current = block;
            state->BlockCount++;
            state->Capacity += size;
        }
        return block;
    }

    // Fills [start, end) so that stale and uninitialized reads stand out.
    static unsafe void Poison(State* state, byte* start, byte* end)
    {
        unsafe
        {
            // SAFETY: callers pass a range inside one of the arena's blocks.
            if (state->Poison && end > start)
                memset(start, PoisonByte, (nuint)(end - start));
        }
    }

    static nuint NextNormalSize(State* state)
    {
        unsafe
        {
            nuint next = state->LastNormal + state->PrevNormal;
            if (next < state->LastNormal) // overflow
                next = state->LastNormal;
            return next == 0 ? state->InitialSize : next;
        }
    }

    static unsafe bool Fits(State* state, byte* p, nuint size)
    {
        byte* end;
        unsafe { end = state->Current->End; }
        return p <= end && size <= (nuint)(end - p);
    }

    static byte* AlignUp(byte* p, nuint align)
        => (byte*)(((nuint)p + (align - 1)) & ~(align - 1));
}
