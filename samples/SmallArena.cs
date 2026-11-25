// Test: an arena, spans and NativeMemory, sized for a small machine. Arena.cs checks more
// and doesn't fit in a Commodore 64 (./build.sh --c64). The arena here is
// 1 KB, because the machine this is for has about 10 KB of heap once the
// program is loaded. It runs on every target.
// Exit code = number of failed checks.
// Build: ./build.sh SmallArena

using System;
using System.Runtime.InteropServices;

namespace SmallArenaSample;

public static class Program
{
    static int s_failures;
    static int s_sectionChecks;
    static int s_sectionFailures;

    static void End(string section)
    {
        Console.Write(s_sectionFailures == 0 ? "PASS " : "FAIL ");
        Console.Write(section);
        Console.Write(" (");
        Console.Write(s_sectionChecks);
        Console.WriteLine(" checks)");
        s_failures += s_sectionFailures;
        s_sectionChecks = 0;
        s_sectionFailures = 0;
    }

    static void Check(string name, bool ok)
    {
        s_sectionChecks++;
        if (!ok)
        {
            s_sectionFailures++;
            Console.Write("  failed: ");
            Console.WriteLine(name);
        }
    }

    static int Sum(ReadOnlySpan<int> values)
    {
        int sum = 0;
        foreach (int value in values)
            sum += value;
        return sum;
    }

    // NativeMemory sits on the target's calloc and free. The blocks are small:
    // a C64 has about 10 KB of heap.
    static bool NativeAllocAndFree()
    {
        unsafe
        {
            int* items = (int*)NativeMemory.Alloc(50, sizeof(int));
            for (int i = 0; i < 50; i++)
                items[i] = i * 3;
            short* zeroed = (short*)NativeMemory.AllocZeroed(50, sizeof(short));
            bool ok = items != null && zeroed != null;
            for (int i = 0; i < 50; i++)
                ok &= items[i] == i * 3 && zeroed[i] == 0;
            NativeMemory.Free(zeroed);
            NativeMemory.Free(items);
            return ok;
        }
    }

    static bool NativeZeroesReusedMemory()
    {
        unsafe
        {
            byte* dirty = (byte*)NativeMemory.Alloc(300);
            for (int i = 0; i < 300; i++)
                dirty[i] = 0xAB;
            NativeMemory.Free(dirty);
            byte* clean = (byte*)NativeMemory.AllocZeroed(300);
            bool zero = clean != null;
            for (int i = 0; i < 300; i++)
                zero &= clean[i] == 0;
            NativeMemory.Free(clean);
            return zero;
        }
    }

    static bool NativeEmpty()
    {
        unsafe
        {
            void* empty = NativeMemory.Alloc(0);
            void* noElements = NativeMemory.AllocZeroed(0, 8);
            bool valid = empty != null && noElements != null;
            NativeMemory.Free(noElements);
            NativeMemory.Free(empty);
            NativeMemory.Free(null);
            return valid;
        }
    }

    // A block bigger than the arena's 1 KB, freed and allocated again: on a
    // C64 this only works if Free gives the memory back.
    static bool NativeFreeGivesBack()
    {
        unsafe
        {
            bool ok = true;
            for (int round = 0; round < 20; round++)
            {
                byte* block = (byte*)NativeMemory.Alloc(2048);
                ok &= block != null;
                block[0] = (byte)round;
                block[2047] = (byte)round;
                ok &= block[0] == round && block[2047] == round;
                NativeMemory.Free(block);
            }
            return ok;
        }
    }

    // Alignments up to 256 bytes; the C64's blocks are only 4-aligned.
    static bool NativeAligned()
    {
        unsafe
        {
            bool ok = true;
            for (nuint alignment = 1; alignment <= 256; alignment *= 2)
            {
                byte* block = (byte*)NativeMemory.AlignedAlloc(40, alignment);
                ok &= ((nuint)block & (alignment - 1)) == 0 && block[0] == 0 && block[39] == 0;
                block[0] = 1;
                block[39] = 2;
                ok &= block[0] == 1 && block[39] == 2;
                NativeMemory.AlignedFree(block);
            }
            NativeMemory.AlignedFree(null);
            return ok;
        }
    }

    // maxCapacity caps the bytes of all blocks together.
    static void Limits()
    {
        Arena unlimited = Arena.Create(256);
        Check("no limit by default", unlimited.MaxCapacity == 0);
        unsafe { unlimited.Destroy(); } // SAFETY: nothing was allocated.

        Arena cut = Arena.Create(4096, maxCapacity: 512);
        Check("a first block past the limit is cut down", cut.Capacity == 512 && cut.MaxCapacity == 512);
        unsafe { cut.Destroy(); } // SAFETY: nothing was allocated.

        // The next normal block would be 256 bytes, more than the 144 left:
        // it shrinks to 144 instead of failing. Alignment 1, so that no
        // padding (which depends on where malloc's memory starts) gets in.
        Arena shrink = Arena.Create(256, maxCapacity: 400);
        bool first = shrink.Alloc(250, 1) != null && shrink.BlockCount == 1;
        Check("a block shrinks to what is left",
            first && shrink.Alloc(100, 1) != null && shrink.BlockCount == 2 && shrink.Capacity == 400);
        unsafe { shrink.Destroy(); } // SAFETY: nothing allocated is used.

        Arena arena = Arena.Create(256, maxCapacity: 1024);
        Check("a request past the limit fails", arena.Alloc(2000) == null && arena.BlockCount == 1 && arena.Used == 0);
        int count = 0;
        while (arena.Alloc(100) != null)
            count++;
        Check("fills up to the limit exactly", count >= 8 && arena.Capacity == 1024 && arena.PeakCapacity == 1024);
        unsafe { arena.Reset(); } // SAFETY: nothing allocated is used.
        Check("Reset keeps capacity within the limit", arena.BlockCount == 1 && arena.Capacity == 1024 && arena.Alloc(1000) != null);
        unsafe { arena.Destroy(); } // SAFETY: nothing allocated is used.
        End("maxCapacity");
    }

    // docs:static-buffer
    // 512 bytes in the program's data, like a global array in C: not on
    // the heap, not on the stack.
    struct Pool
    {
        public fixed byte Bytes[512];
    }

    static Pool s_pool;
    // docs:end

    // An arena in a static buffer: memory the program has from its start.
    static void InStaticBuffer()
    {
        // docs:from-static-buffer
        Arena arena;
        unsafe
        {
            // SAFETY: s_pool is static, so it outlives the arena, and
            // nothing else uses it.
            fixed (byte* memory = s_pool.Bytes)
                arena = Arena.FromBuffer(memory, 512, "pool");
        }
        // docs:end
        int* numbers = arena.AllocArray<int>(20);
        bool inside;
        unsafe
        {
            // SAFETY: only addresses are compared.
            fixed (byte* start = s_pool.Bytes)
                inside = (byte*)numbers >= start && (byte*)(numbers + 20) <= start + 512;
        }
        Check("a static buffer works the same", arena.IsCreated && inside && arena.Alloc(600) == null);
        unsafe { arena.Destroy(); } // SAFETY: numbers isn't used after this.
    }

    // An arena in a buffer of its own: no malloc, and it never grows.
    static void InBuffer()
    {
        Span<byte> memory = stackalloc byte[512];
        Arena arena;
        // SAFETY: the arena isn't used after this method returns.
        unsafe { arena = Arena.FromBuffer(memory); }
        Check("created in the buffer", arena.IsCreated && arena.BlockCount == 1);
        Check("capacity is the buffer less bookkeeping",
            arena.Capacity > 300 && arena.Capacity < 512 && arena.MaxCapacity == arena.Capacity);

        int* numbers = arena.AllocArray<int>(10);
        bool inside;
        unsafe
        {
            // SAFETY: only addresses are compared.
            fixed (byte* start = memory)
                inside = (byte*)numbers >= start && (byte*)(numbers + 10) <= start + 512;
        }
        Check("allocations come from the buffer", inside);
        unsafe
        {
            // SAFETY: numbers holds 10 ints.
            for (int i = 0; i < 10; i++)
                numbers[i] = i;
        }

        nuint used = arena.Used;
        Check("a request past the buffer fails", arena.Alloc(1000) == null && arena.Used == used);
        Arena.Mark mark = arena.Save();
        while (arena.Alloc(32) != null) { }
        Check("never grows", arena.BlockCount == 1);
        unsafe { arena.Restore(mark); } // SAFETY: what came after the mark isn't used.
        int sum = 0;
        unsafe { for (int i = 0; i < 10; i++) sum += numbers[i]; } // SAFETY: numbers is still allocated.
        Check("Restore keeps what came before", arena.Used == used && sum == 45);
        unsafe { arena.Reset(keepCapacity: false); } // SAFETY: numbers isn't used after this.
        Check("Reset empties it", arena.Used == 0 && arena.Alloc(200) != null);
        unsafe { arena.Destroy(); } // SAFETY: nothing allocated is used.
        Check("Destroy ends the handle", !arena.IsCreated);

        Span<byte> tiny = stackalloc byte[16];
        Arena none;
        unsafe { none = Arena.FromBuffer(tiny); } // SAFETY: as above.
        Check("too small a buffer: not created", !none.IsCreated && none.Alloc(1) == null);
        InStaticBuffer();
        End("FromBuffer");
    }

    public static int Main()
    {
        Arena arena = Arena.Create(1024);
        Check("created, empty", arena.IsCreated && arena.Used == 0 && arena.BlockCount == 1);
        Span<int> numbers;
        unsafe { numbers = arena.AllocSpan<int>(10); } // SAFETY: used before the arena is reset.
        Check("AllocSpan is zeroed", numbers.Length == 10 && Sum(numbers) == 0);
        for (int i = 0; i < numbers.Length; i++)
            numbers[i] = i * i;
        Check("span of ints", numbers[9] == 81 && Sum(numbers) == 285 && Sum(numbers.Slice(2, 3)) == 29);
        nuint used = arena.Used;
        Check("Used grows", used >= 40);
        Arena.Mark mark = arena.Save();
        unsafe { arena.AllocSpan<long>(20); } // SAFETY: not used.
        Check("more is used", arena.Used > used);
        unsafe { arena.Restore(mark); } // SAFETY: nothing allocated after the mark is used again.
        Check("Restore gives it back", arena.Used == used && numbers[9] == 81);
        unsafe { arena.AllocSpan<byte>(3000); } // SAFETY: not used.
        Check("a big request gets a block of its own", arena.BlockCount == 2);
        unsafe { arena.Reset(); } // SAFETY: `numbers` isn't used after this.
        Check("Reset", arena.Used == 0);
        Span<byte> stack = stackalloc byte[16];
        stack.Fill(7);
        stack.Slice(4, 4).Clear();
        int stackSum = 0;
        foreach (byte value in stack)
            stackSum += value;
        Check("stackalloc, Fill, Clear", stackSum == 84);
        End("arena and spans");

        Limits();
        InBuffer();

        Check("NativeMemory.Alloc, AllocZeroed, Free", NativeAllocAndFree());
        Check("AllocZeroed zeroes a freed block's memory", NativeZeroesReusedMemory());
        Check("Alloc(0) is a valid pointer, Free(null) does nothing", NativeEmpty());
        Check("Free gives the memory back", NativeFreeGivesBack());
        Check("AlignedAlloc, AlignedFree", NativeAligned());
        End("NativeMemory");

        Console.WriteLine();
        if (s_failures == 0)
            Console.WriteLine("All checks passed.");
        else
        {
            Console.Write(s_failures);
            Console.WriteLine(" checks FAILED.");
        }
        return s_failures;
    }
}
