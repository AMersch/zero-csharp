// The Commander X16's banked RAM: up to 2 MB beyond the 38 KB a program runs
// in. Only for programs built with ./build.sh --cx16; the functions are in
// cx16/runtime.c, and a program for another target that calls them won't link.
//
//   byte[] tile = new byte[64];
//   Banks.Write(100000, tile);                     // into the banked RAM
//   Banks.Read(100000, tile);                      // and back
//
// A program's pointers are 16 bits, so the processor sees only 8 KB of this
// at a time (a window at $A000, with a register that picks the bank) and
// nothing in a program, not an object or an array, can live in it. Banks is
// a store that bytes are copied to and from: a table, a level, tiles, a
// picture, anything a program would rather not keep in its 38 KB.
//
// Addresses here are plain numbers from 0 to Size - 1, running over the
// banks one after another: a copy that reaches the end of one bank goes on
// in the next. Bank 0, which belongs to the KERNAL, is left out, so address 0
// is the first byte of bank 1. The banks start out holding whatever was in
// them: not zeros.
//
// Alloc hands out regions of the banks, for BankedArray (lib/BankedArray.cs)
// and for programs that want to share the banks between their parts: it moves
// a pointer forward, and Reset takes everything back at once, like an Arena.
// A program that mixes Alloc with addresses it picked itself has to keep the
// two apart.

using System;
using System.Runtime.InteropServices;

public static class Banks
{
    /// <summary>The size of one bank in bytes.</summary>
    public const int BankSize = 8192;

    [DllImport("libc")] static safe extern int cx16_bank_count();
    [DllImport("libc")] static unsafe extern void cx16_bank_read(int bank, int offset, byte* destination, int count);
    [DllImport("libc")] static unsafe extern void cx16_bank_write(int bank, int offset, byte* source, int count);
    [DllImport("libc")] static safe extern void cx16_bank_fill(int bank, int offset, int value, int count);
    [DllImport("libc")] static unsafe extern int cx16_bank_load(int bank, int offset, char* name, int length);

    /// <summary>The banks a program can use: 63 with the emulator's 512 KB, up to 255 with 2 MB. The KERNAL's bank 0 isn't counted.</summary>
    public static int Count => cx16_bank_count() - 1;

    /// <summary>The bytes in all those banks: 516096 with the emulator's 512 KB.</summary>
    public static int Size => Count * BankSize;

    static int s_used;

    /// <summary>The bytes Alloc has handed out.</summary>
    public static int Used => s_used;

    /// <summary>The bytes Alloc can still hand out.</summary>
    public static int Available => Size - s_used;

    /// <summary>Reserves count bytes of the banks and returns their address, or -1 if that many aren't left. The bytes hold whatever was in them.</summary>
    public static int Alloc(int count)
    {
        if (count < 0 || count > Available)
            return -1;
        int address = s_used;
        s_used += count;
        return address;
    }

    /// <summary>Makes a region from Alloc bigger, in place, if it is the last one handed out and the banks have the room. Returns false, and changes nothing, otherwise.</summary>
    public static bool TryExtend(int address, int oldCount, int newCount)
    {
        if (address < 0 || oldCount < 0 || newCount < oldCount || address > s_used - oldCount
            || address + oldCount != s_used || newCount - oldCount > Available)
            return false;
        s_used = address + newCount;
        return true;
    }

    /// <summary>Takes back everything Alloc handed out. Whatever used it, such as a BankedArray, must not be used again.</summary>
    public static void Reset() => s_used = 0;

    /// <summary>The state of Alloc, for Release.</summary>
    public static int Mark() => s_used;

    /// <summary>Takes back what Alloc handed out since a Mark. Returns false, and changes nothing, if that is already gone.</summary>
    public static bool Release(int mark)
    {
        if (mark < 0 || mark > s_used)
            return false;
        s_used = mark;
        return true;
    }

    /// <summary>Whether count bytes from an address are all in the banks.</summary>
    public static bool Contains(int address, int count)
        => address >= 0 && count >= 0 && count <= Size - address;

    /// <summary>Copies bytes from the banks to a span. Returns false, and copies nothing, if they aren't all in the banks.</summary>
    public static bool Read(int address, Span<byte> destination)
    {
        if (!Contains(address, destination.Length))
            return false;
        int done = 0;
        while (done < destination.Length)
        {
            int at = address + done;
            int offset = at % BankSize;
            int n = Math.Min(BankSize - offset, destination.Length - done);
            unsafe
            {
                // SAFETY: done..done+n is inside the span, and the bank and
                // offset were checked by Contains: bank 1 and up, and n
                // stops at the end of the bank.
                fixed (byte* p = destination)
                    cx16_bank_read(at / BankSize + 1, offset, p + done, n);
            }
            done += n;
        }
        return true;
    }

    /// <summary>Copies a span's bytes to the banks. Returns false, and copies nothing, if they won't all fit.</summary>
    public static bool Write(int address, ReadOnlySpan<byte> source)
    {
        if (!Contains(address, source.Length))
            return false;
        int done = 0;
        while (done < source.Length)
        {
            int at = address + done;
            int offset = at % BankSize;
            int n = Math.Min(BankSize - offset, source.Length - done);
            unsafe
            {
                // SAFETY: as in Read; the pointer is the span's own.
                fixed (byte* p = source)
                    cx16_bank_write(at / BankSize + 1, offset, p + done, n);
            }
            done += n;
        }
        return true;
    }

    /// <summary>
    /// Reads a whole file into the banks, from address on, and returns how many bytes
    /// it was; -1 if it couldn't. It is much smaller than File, for a program that only
    /// loads: pictures, levels, tables. The name is ASCII, in the current folder of the
    /// drive the program came from ("level1.bin", "data/level1.bin").
    /// The caller has to leave room: how long the file is isn't known before it is
    /// read, and a file longer than the banks from address on overwrites what is there.
    /// </summary>
    public static int Load(int address, string name)
    {
        if (address < 0 || address >= Size || name == null)
            return -1;
        unsafe
        {
            // SAFETY: the runtime reads name.Length chars of the string, and
            // the bank and offset are inside the banks.
            fixed (char* p = name)
                return cx16_bank_load(address / BankSize + 1, address % BankSize, p, name.Length);
        }
    }

    /// <summary>Copies count bytes from one address in the banks to another, front to back, so a copy to a lower address may overlap the bytes it comes from. Returns false, and copies nothing, if either end isn't in the banks.</summary>
    public static bool Copy(int source, int destination, int count)
    {
        if (!Contains(source, count) || !Contains(destination, count))
            return false;
        Span<byte> chunk = stackalloc byte[64];
        for (int done = 0; done < count; done += chunk.Length)
        {
            Span<byte> part = chunk.Slice(0, Math.Min(chunk.Length, count - done));
            Read(source + done, part);
            Write(destination + done, part);
        }
        return true;
    }

    /// <summary>Sets count bytes from an address to a value. Returns false, and writes nothing, if they aren't all in the banks.</summary>
    public static bool Fill(int address, int count, byte value)
    {
        if (!Contains(address, count))
            return false;
        while (count > 0)
        {
            int offset = address % BankSize;
            int n = Math.Min(BankSize - offset, count);
            cx16_bank_fill(address / BankSize + 1, offset, value, n);
            address += n;
            count -= n;
        }
        return true;
    }

    /// <summary>The byte at an address, or -1 if it isn't in the banks.</summary>
    public static int Peek(int address)
    {
        Span<byte> one = stackalloc byte[1];
        return Read(address, one) ? one[0] : -1;
    }

    /// <summary>Writes a byte. Returns false if the address isn't in the banks.</summary>
    public static bool Poke(int address, int value)
    {
        Span<byte> one = stackalloc byte[1];
        one[0] = (byte)value;
        return Write(address, one);
    }
}
