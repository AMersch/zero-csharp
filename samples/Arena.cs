// Example: Growable arena allocator (lib/Arena.cs) in --stdlib:zero.
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

        if (!Arena.Init(128))
        {
            Out.Line("Arena.Init failed");
            return 1;
        }
        Out.Line("Initial block size: ", (long)Arena.CurrentBlockSize);

        // Allocation is safe; reading and writing through the pointers needs unsafe.
        Point* p1 = Arena.Alloc<Point>();
        Point* p2 = Arena.Alloc<Point>();
        Point* p3 = Arena.Alloc<Point>();

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

        Entity* e = Arena.Alloc<Entity>();
        int* squares = Arena.AllocArray<int>(10);
        unsafe
        {
            e->Id = 1;
            e->Position.X = 100;
            for (int i = 0; i < 10; i++)
                squares[i] = i * i;
            Out.Line("squares[5] = ", squares[5]);
        }
        Out.Line("Used: ", (long)Arena.Used, " bytes | Blocks: ", Arena.BlockCount);

        // Start over with a single 128-byte block to show the growth sequence.
        unsafe { Arena.Reset(keepCapacity: false); } // SAFETY: p1..p3, e and squares are not used below.

        Out.Line();
        Out.Line("Normal blocks grow like Fibonacci: 128, 128, 256, 384, 640, 1024");
        Out.Line("  Block 1: ", (long)Arena.CurrentBlockSize);
        while (Arena.BlockCount < 6)
        {
            int before = Arena.BlockCount;
            if (Arena.AllocArray<byte>(100) == null)
                return 1;
            if (Arena.BlockCount != before)
                Out.Line("  Block ", Arena.BlockCount, ": ", (long)Arena.CurrentBlockSize);
        }

        // A large allocation gets its own block and doesn't disturb the sequence.
        nuint nextBefore = Arena.NextBlockSize;
        byte* big = Arena.AllocArray<byte>(4096);
        Out.Line();
        Out.Line("4096-byte allocation -> dedicated block of ", (long)Arena.CurrentBlockSize);
        Out.Line("Next normal block: before ", (long)nextBefore, ", after ", (long)Arena.NextBlockSize);

        // Alignment is per allocation, e.g. 64 bytes for SIMD-friendly buffers.
        float* weights = Arena.AllocArray<float>(16, align: 64);
        Out.Line("64-byte aligned buffer, address % 64 = ", (long)((nuint)weights % 64));

        // Bad requests return null instead of corrupting memory. (So does malloc
        // failure; that path is easiest to see with `ulimit -v`, not in a demo.)
        Out.Write("Bad alignment (3) returns null: ");
        Out.Write(Arena.Alloc(16, align: 3) == null);
        Out.Line();
        Out.Write("Negative count returns null: ");
        Out.Write(Arena.AllocArray<int>(-1) == null);
        Out.Line();

        // Reset keeps the capacity: all blocks merge into one, so the next round won't malloc.
        nuint capacity = Arena.Capacity;
        unsafe { Arena.Reset(); } // SAFETY: big and weights are not used below.
        Out.Line();
        Out.Write("After Reset: blocks ");
        Out.Write(Arena.BlockCount);
        Out.Write(", capacity kept: ");
        Out.Write(Arena.Capacity == capacity);
        Out.Line();
        Out.Line("Capacity: ", (long)Arena.Capacity, " bytes, used: ", (long)Arena.Used);

        Point* p4 = Arena.Alloc<Point>();
        unsafe
        {
            p4->X = 999;
            Out.Line("New point X: ", p4->X);
        }

        unsafe { Arena.Destroy(); } // SAFETY: nothing points into the arena after this.
        Out.Line("Arena destroyed.");
        return 0;
    }
}
