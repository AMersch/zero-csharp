// The machine under UEFI, with no operating system: a graphics screen, the
// keyboard, a clock, files, power. Only for programs built with
// ./build.sh --uefi; the functions are in efi/machine.c, and a program for
// another target that calls them won't link.
//
// It has the same members as lib/Dos.cs, so a DOS program runs here by using
// one name for the other (samples/Vga.cs does):
//
//   #if UEFI
//   using Machine = Uefi;
//   #else
//   using Machine = Dos;
//   #endif
//
// The screen is the VGA's 320x200 with 256 colors, scaled up as far as the
// real screen allows and centered. Scan codes are the PC's, whatever the
// keyboard is. UEFI reports key presses but not releases, so KeyDown counts a
// key as held until its repeats stop. The PC speaker and In/Out exist on x64;
// on arm64 they do nothing (HasSpeaker says which).

using System;
using System.Runtime.InteropServices;

public static class Uefi
{
    public const int Width = 320, Height = 200;

    // Scan codes: a key's position on the keyboard, whatever is printed on it.
    public const int Esc = 0x01, Enter = 0x1C, Space = 0x39;
    public const int Up = 0x48, Down = 0x50, Left = 0x4B, Right = 0x4D;

    [DllImport("libc")] static safe extern int uefi_graphics();
    [DllImport("libc")] static safe extern void uefi_text();
    [DllImport("libc")] static unsafe extern byte* uefi_screen();
    [DllImport("libc")] static unsafe extern void uefi_show(byte* picture);
    [DllImport("libc")] static unsafe extern void* memset(void* destination, int value, nuint count);
    [DllImport("libc")] static safe extern void uefi_set_color(int index, int red, int green, int blue);
    [DllImport("libc")] static safe extern void uefi_wait_frame();
    [DllImport("libc")] static safe extern int uefi_poll_keys();
    [DllImport("libc")] static safe extern int uefi_key_down(int scanCode);
    [DllImport("libc")] static safe extern int uefi_read_key();
    [DllImport("libc")] static safe extern uint uefi_ticks();
    [DllImport("libc")] static safe extern void uefi_sleep(uint milliseconds);
    [DllImport("libc")] static safe extern int uefi_in(int port);
    [DllImport("libc")] static safe extern void uefi_out(int port, int value);
    [DllImport("libc")] static safe extern void uefi_sound(uint hertz);
    [DllImport("libc")] static safe extern int uefi_has_speaker();
    [DllImport("libc")] static unsafe extern nint uefi_file_open(byte* path, int mode);
    [DllImport("libc")] static unsafe extern int uefi_file_read(nint file, byte* buffer, uint count);
    [DllImport("libc")] static unsafe extern int uefi_file_write(nint file, byte* buffer, uint count);
    [DllImport("libc")] static safe extern int uefi_file_seek(nint file, int offset, int origin);
    [DllImport("libc")] static safe extern int uefi_file_size(nint file);
    [DllImport("libc")] static safe extern void uefi_file_close(nint file);
    [DllImport("libc")] static unsafe extern int uefi_file_delete(byte* path);
    [DllImport("libc")] static safe extern void uefi_shutdown();
    [DllImport("libc")] static safe extern void uefi_reboot();

    // ---- screen and keyboard ---------------------------------------------------

    /// <summary>Switches to graphics: a 320x200 picture with 256 colors. Returns false if the machine has no graphics output.</summary>
    public static bool Graphics() => uefi_graphics() != 0;

    /// <summary>Switches back to the text console.</summary>
    public static void Text() => uefi_text();

    /// <summary>What the screen shows, read back from it: Width * Height color numbers (255 where a pixel is no color defined with SetColor). Writing to it changes nothing on the screen; use Show.</summary>
    public static Span<byte> Screen
    {
        get
        {
            unsafe
            {
                // SAFETY: uefi_screen returns a buffer of Width * Height bytes that lives as long as the program.
                return new Span<byte>(uefi_screen(), Width * Height);
            }
        }
    }

    /// <summary>Shows a picture of Width * Height bytes, a color number per pixel, row after row.</summary>
    public static void Show(ReadOnlySpan<byte> picture)
    {
        if (picture.Length < Width * Height)
            return;
        unsafe
        {
            // SAFETY: the span holds at least the Width * Height bytes uefi_show reads.
            fixed (byte* p = picture)
                uefi_show(p);
        }
    }

    /// <summary>Sets every byte of a picture (or a part of one) to a color.</summary>
    public static void Fill(Span<byte> picture, byte color)
    {
        unsafe
        {
            // SAFETY: the pointer and length come from the span.
            fixed (byte* p = picture)
                memset(p, color, (nuint)picture.Length);
        }
    }

    /// <summary>Defines one of the 256 colors. Red, green and blue are 0-63, as on the VGA.</summary>
    public static void SetColor(int index, int red, int green, int blue) => uefi_set_color(index, red, green, blue);

    /// <summary>Waits for the next frame: 70 a second, as the VGA's graphics mode.</summary>
    public static void WaitFrame() => uefi_wait_frame();

    /// <summary>Takes in the key presses since the last call. Returns how many wait for ReadKey.</summary>
    public static int PollKeys() => uefi_poll_keys();

    /// <summary>Whether a key is held down, as of the last PollKeys (see the top of this file).</summary>
    public static bool KeyDown(int scanCode) => uefi_key_down(scanCode) != 0;

    /// <summary>The next key press: its scan code in the high byte, its character (0 for keys without one, or outside ASCII) in the low byte. 0 if no key was pressed.</summary>
    public static int ReadKey() => uefi_read_key();

    /// <summary>Milliseconds since some fixed moment.</summary>
    public static uint Ticks => uefi_ticks();

    public static void Sleep(uint milliseconds) => uefi_sleep(milliseconds);

    // ---- sound and ports (x64) -------------------------------------------------

    /// <summary>Whether Sound and In/Out reach a PC's hardware: true on x64, false on arm64.</summary>
    public static bool HasSpeaker => uefi_has_speaker() != 0;

    /// <summary>Starts a tone on the PC speaker at this pitch; it sounds until the next call. 0 turns it off.</summary>
    public static void Sound(uint hertz) => uefi_sound(hertz);

    /// <summary>Reads a byte from a hardware port (x64; 0 on arm64).</summary>
    public static int In(int port) => uefi_in(port);

    /// <summary>Writes a byte to a hardware port (x64; nothing on arm64).</summary>
    public static void Out(int port, int value) => uefi_out(port, value);

    // ---- mouse -----------------------------------------------------------------

    /// <summary>There is no mouse support yet: always 0 buttons.</summary>
    public static int MouseStart() => 0;

    /// <summary>With no mouse, the middle of the screen and no buttons.</summary>
    public static int Mouse(out int x, out int y)
    {
        x = Width / 2;
        y = Height / 2;
        return 0;
    }

    // ---- files -----------------------------------------------------------------
    // On the volume the program was loaded from, "/" or "\" between folders.

    public const int ForReading = 0, ForWriting = 1, ForAppending = 2;
    public const int FromStart = 0, FromHere = 1, FromEnd = 2;

    const int MaxPath = 256;

    /// <safety>buffer must have room for MaxPath * 3 + 1 bytes.</safety>
    static unsafe bool ToUtf8(string path, byte* buffer)
    {
        if (path == null || path.Length >= MaxPath)
            return false;
        int n = 0;
        unsafe
        {
            for (int i = 0; i < path.Length; i++)
            {
                char c = path[i];
                if (c < 0x80)
                {
                    buffer[n++] = (byte)c;
                }
                else if (c < 0x800)
                {
                    buffer[n++] = (byte)(0xC0 | (c >> 6));
                    buffer[n++] = (byte)(0x80 | (c & 0x3F));
                }
                else
                {
                    // A surrogate pair becomes two 3-byte sequences; efi/machine.c reads them back as UTF-16 units.
                    buffer[n++] = (byte)(0xE0 | (c >> 12));
                    buffer[n++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                    buffer[n++] = (byte)(0x80 | (c & 0x3F));
                }
            }
            buffer[n] = 0;
        }
        return true;
    }

    /// <summary>Opens a file. ForReading needs it to exist; ForWriting creates it or empties it; ForAppending creates it or continues at its end. Returns 0 if that fails.</summary>
    public static nint FileOpen(string path, int mode)
    {
        unsafe
        {
            byte* name = stackalloc byte[MaxPath * 3 + 1];
            // SAFETY: name has room for MaxPath * 3 + 1 bytes and ToUtf8 ends it with a 0.
            return ToUtf8(path, name) ? uefi_file_open(name, mode) : 0;
        }
    }

    /// <summary>Reads up to buffer.Length bytes. Returns how many were read (0 at the end of the file), or -1.</summary>
    public static int FileRead(nint file, Span<byte> buffer)
    {
        unsafe
        {
            // SAFETY: the pointer and length come from the span.
            fixed (byte* p = buffer)
                return uefi_file_read(file, p, (uint)buffer.Length);
        }
    }

    /// <summary>Writes the bytes. Returns how many were written, or -1.</summary>
    public static int FileWrite(nint file, ReadOnlySpan<byte> bytes)
    {
        unsafe
        {
            // SAFETY: the pointer and length come from the span.
            fixed (byte* p = bytes)
                return uefi_file_write(file, p, (uint)bytes.Length);
        }
    }

    /// <summary>Moves to a position, counted FromStart, FromHere or FromEnd. Returns the new position, or -1.</summary>
    public static int FileSeek(nint file, int offset, int origin) => uefi_file_seek(file, offset, origin);

    public static int FileSize(nint file) => uefi_file_size(file);

    public static void FileClose(nint file) => uefi_file_close(file);

    /// <summary>Deletes a file. Returns false if there was none or it couldn't be deleted.</summary>
    public static bool FileDelete(string path)
    {
        unsafe
        {
            byte* name = stackalloc byte[MaxPath * 3 + 1];
            // SAFETY: as in FileOpen.
            return ToUtf8(path, name) && uefi_file_delete(name) != 0;
        }
    }

    // ---- power -----------------------------------------------------------------

    /// <summary>Turns the machine off. Doesn't return.</summary>
    public static void Shutdown() => uefi_shutdown();

    /// <summary>Restarts the machine. Doesn't return.</summary>
    public static void Reboot() => uefi_reboot();
}
