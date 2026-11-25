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

    /// <summary>The banks a program can use: 63 with the emulator's 512 KB, up to 255 with 2 MB. The KERNAL's bank 0 isn't counted.</summary>
    public static int Count => cx16_bank_count() - 1;

    /// <summary>The bytes in all those banks: 516096 with the emulator's 512 KB.</summary>
    public static int Size => Count * BankSize;

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
