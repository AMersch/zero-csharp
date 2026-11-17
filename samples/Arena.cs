// Example: Growable arena allocator (lib/Arena.cs) in zero mode.
// Build: ./build.sh Arena

namespace ArenaSample;

public struct Point
{
    public int X;
    public int Y;
    public Point* Next;
}

public struct Entity
{
    public int Id;
    public Point Position;
    public Point Velocity;
}

public static class Program
{
    public static int Main()
    {
        Out.Line("=== Growable Arena Demo ===");

        Arena arena = Arena.Create(128);
        if (!arena.IsCreated)
        {
            Out.Line("Arena.Create failed");
            return 1;
        }
        Out.Line("Initial block size: ", (long)arena.CurrentBlockSize);

        // Allocation is safe; reading and writing through the pointers needs unsafe.
        Point* p1 = arena.Alloc<Point>();
        Point* p2 = arena.Alloc<Point>();
        Point* p3 = arena.Alloc<Point>();

        unsafe
        {
            p1->X = 10; p1->Y = 20; p1->Next = p2;
            p2->X = 30; p2->Y = 40; p2->Next = p3;
            p3->X = 50; p3->Y = 60; p3->Next = null;

            Out.Line();
            Out.Line("Walking linked points p1 -> p2 -> p3:");
            int index = 1;
            for (Point* current = p1; current != null; current = current->Next)
            {
                Out.Write("  Point ");
                Out.Write(index++);
                Out.Write(": (");
                Out.Write(current->X);
                Out.Write(", ");
                Out.Write(current->Y);
                Out.Line(")");
            }
        }

        Entity* e = arena.Alloc<Entity>();
        int* squares = arena.AllocArray<int>(10);
        unsafe
        {
            e->Id = 1;
            e->Position.X = 100;
            for (int i = 0; i < 10; i++)
                squares[i] = i * i;
            Out.Line("squares[5] = ", squares[5]);
        }
        Out.Line("Used: ", (long)arena.Used, " bytes | Blocks: ", arena.BlockCount);

        // Start over with a single 128-byte block to show the growth sequence.
        unsafe { arena.Reset(keepCapacity: false); } // SAFETY: p1..p3, e and squares are not used below.

        Out.Line();
        Out.Line("Normal blocks grow like Fibonacci: 128, 128, 256, 384, 640, 1024");
        Out.Line("  Block 1: ", (long)arena.CurrentBlockSize);
        while (arena.BlockCount < 6)
        {
            int before = arena.BlockCount;
            if (arena.AllocArray<byte>(100) == null)
                return 1;
            if (arena.BlockCount != before)
                Out.Line("  Block ", arena.BlockCount, ": ", (long)arena.CurrentBlockSize);
        }

        // A large allocation gets its own block and doesn't disturb the sequence.
        nuint nextBefore = arena.NextBlockSize;
        byte* big = arena.AllocArray<byte>(4096);
        Out.Line();
        Out.Line("4096-byte allocation -> dedicated block of ", (long)arena.CurrentBlockSize);
        Out.Line("Next normal block: before ", (long)nextBefore, ", after ", (long)arena.NextBlockSize);

        // Alignment is per allocation, e.g. 64 bytes for SIMD-friendly buffers.
        float* weights = arena.AllocArray<float>(16, align: 64);
        Out.Line("64-byte aligned buffer, address % 64 = ", (long)((nuint)weights % 64));

        // Bad requests return null instead of corrupting memory. (So does malloc
        // failure; that path is easiest to see with `ulimit -v`, not in a demo.)
        Out.Write("Bad alignment (3) returns null: ");
        Out.Write(arena.Alloc(16, align: 3) == null);
        Out.Line();
        Out.Write("Negative count returns null: ");
        Out.Write(arena.AllocArray<int>(-1) == null);
        Out.Line();

        // Reset keeps the capacity: all blocks merge into one, so the next round won't malloc.
        nuint capacity = arena.Capacity;
        unsafe { arena.Reset(); } // SAFETY: big and weights are not used below.
        Out.Line();
        Out.Write("After Reset: blocks ");
        Out.Write(arena.BlockCount);
        Out.Write(", capacity kept: ");
        Out.Write(arena.Capacity == capacity);
        Out.Line();
        Out.Line("Capacity: ", (long)arena.Capacity, " bytes, used: ", (long)arena.Used);

        Point* p4 = arena.Alloc<Point>();
        unsafe
        {
            p4->X = 999;
            Out.Line("New point X: ", p4->X);
        }

        // Save/Restore frees only what came after the mark: temporary work in
        // an arena that also holds data to keep.
        nuint usedBefore = arena.Used;
        Arena.Mark mark = arena.Save();
        int* scratch = arena.AllocArray<int>(4000);
        unsafe { scratch[3999] = 7; }
        Out.Line();
        Out.Line($"Scratch work: used {(long)arena.Used} bytes in {arena.BlockCount} blocks");
        unsafe { arena.Restore(mark); } // SAFETY: scratch is not used below.
        Out.Write("After Restore: used as before: ");
        Out.Write(arena.Used == usedBefore);
        Out.Write(", blocks ");
        Out.Write(arena.BlockCount);
        unsafe
        {
            Out.Write(", p4 intact: ");
            Out.Write(p4->X == 999);
        }
        Out.Line();

        // The newest allocation can grow in place, so a buffer built up step by
        // step doesn't leave its smaller copies behind.
        int* growing = arena.AllocArray<int>(4);
        nuint usedSmall = arena.Used;
        bool extended = arena.TryExtend(growing, 4 * sizeof(int), 8 * sizeof(int));
        Out.Write("Extend newest allocation in place: ");
        Out.Write(extended && arena.Used == usedSmall + 4 * sizeof(int));
        Out.Line();
        arena.Alloc<Point>();
        Out.Write("Extend after another allocation: ");
        Out.Write(arena.TryExtend(growing, 8 * sizeof(int), 16 * sizeof(int)));
        Out.Line();

        // Arenas are independent: one per lifetime. Here a second arena holds a
        // list for a while and is dropped without touching the first.
        Arena frame = Arena.Create(256);
        List<int> numbers = List<int>.Create(frame);
        for (int i = 0; i < 100; i++)
            numbers.Add(i);
        Out.Line();
        Out.Line($"Second arena: list of {numbers.Count} ints, {(long)frame.Used} bytes used");
        usedBefore = arena.Used;
        unsafe { frame.Destroy(); } // SAFETY: numbers is not used below.
        Out.Write("First arena untouched: ");
        Out.Write(arena.Used == usedBefore);
        Out.Line();

        unsafe { arena.Destroy(); } // SAFETY: nothing points into the arena after this.
        Out.Line("Arena destroyed.");

        // Debugging aid: arenas created while DebugChecks is on fill freed memory
        // with 0xDD, so a stale pointer reads an obvious pattern instead of the
        // old value, and collections stop the program when used after a reset.
        Arena.DebugChecks = true;
        Arena debug = Arena.Create(256);
        int* stale = debug.AllocArray<int>(4);
        unsafe { stale[0] = 12345; }
        Out.Line();
        Out.Write("DebugChecks: pointer is live before Reset: ");
        Out.Write(debug.IsLive(stale));
        Out.Line();
        // SAFETY: stale is read below on purpose. Its memory still belongs to the
        // arena's block, so the read is harmless; it shows what a bug would see.
        unsafe { debug.Reset(); }
        uint seen;
        unsafe { seen = (uint)stale[0]; }
        Out.Line($"After Reset the stale pointer reads 0x{seen:X}, IsLive: {debug.IsLive(stale)}");
        unsafe { debug.Destroy(); } // SAFETY: stale is not used after this.
        Arena.DebugChecks = false;
        return 0;
    }
}
