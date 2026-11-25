// Test: NativeMemory, sized for a small machine: Alloc, Free, Realloc and
// the aligned ones, on the target's own calloc, realloc and free. Core.cs
// checks more. SmallArena had these until it grew too large to leave them
// heap on a Commodore 64. It runs on every target.
// Exit code = number of failed checks.
// Build: ./build.sh SmallNative

using System;
using System.Runtime.InteropServices;

namespace SmallNativeSample;

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

    // Realloc and AlignedRealloc keep what fits; on the C64 through the
    // runtime's own realloc.
    static bool NativeRealloc()
    {
        unsafe
        {
            byte* bytes = (byte*)NativeMemory.Realloc(null, 40);
            for (int i = 0; i < 40; i++)
                bytes[i] = (byte)i;
            bytes = (byte*)NativeMemory.Realloc(bytes, 400);
            bool ok = true;
            for (int i = 0; i < 40; i++)
                ok &= bytes[i] == i;
            bytes = (byte*)NativeMemory.Realloc(bytes, 20);
            for (int i = 0; i < 20; i++)
                ok &= bytes[i] == i;
            NativeMemory.Free(bytes);

            byte* aligned = (byte*)NativeMemory.AlignedAlloc(40, 8);
            for (int i = 0; i < 40; i++)
                aligned[i] = (byte)(i + 1);
            aligned = (byte*)NativeMemory.AlignedRealloc(aligned, 300, 256);
            ok &= ((nuint)aligned & 255) == 0;
            for (int i = 0; i < 40; i++)
                ok &= aligned[i] == i + 1;
            aligned = (byte*)NativeMemory.AlignedRealloc(aligned, 30, 4);
            for (int i = 0; i < 30; i++)
                ok &= aligned[i] == i + 1;
            NativeMemory.AlignedFree(aligned);
            return ok;
        }
    }

    public static int Main()
    {
        Check("NativeMemory.Alloc, AllocZeroed, Free", NativeAllocAndFree());
        Check("AllocZeroed zeroes a freed block's memory", NativeZeroesReusedMemory());
        Check("Alloc(0) is a valid pointer, Free(null) does nothing", NativeEmpty());
        Check("Free gives the memory back", NativeFreeGivesBack());
        Check("AlignedAlloc, AlignedFree", NativeAligned());
        Check("Realloc, AlignedRealloc", NativeRealloc());
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
