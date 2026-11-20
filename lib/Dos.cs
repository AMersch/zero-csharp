// The PC under DOS: the VGA's 320x200, 256-color graphics mode, the keyboard,
// the mouse, the speaker, a clock and files. Only for programs built with
// ./build.sh --dos; the functions are in dos/runtime.c, and a program for
// another target that calls them won't link.
//
//   Dos.Graphics();                        // 320x200, one byte per pixel
//   Dos.SetColor(1, 63, 0, 0);             // color 1: bright red (0-63 each)
//   Span<byte> screen = Dos.Screen;        // screen[y * 320 + x] = color
//   while (!Dos.KeyDown(Dos.Esc)) { Dos.PollKeys(); ...; Dos.WaitFrame(); }
//   Dos.Text();                            // back to the text screen
//
// Draw into a buffer of your own and Dos.Show it right after WaitFrame:
// writing the screen while it's being shown flickers. Dos.Show and Dos.Fill
// use the processor's block instructions; on a 386 a whole screen takes them
// a few milliseconds, against a hundred for Span's CopyTo and Fill.

using System;
using System.Runtime.InteropServices;

public static class Dos
{
    public const int Width = 320, Height = 200;

    // Scan codes: a key's position on the keyboard, whatever is printed on it.
    public const int Esc = 0x01, Enter = 0x1C, Space = 0x39;
    public const int Up = 0x48, Down = 0x50, Left = 0x4B, Right = 0x4D;

    [DllImport("libc")] static safe extern void dos_video_mode(int mode);
    [DllImport("libc")] static unsafe extern byte* dos_framebuffer();
    [DllImport("libc")] static unsafe extern void dos_show(byte* picture, uint count);
    [DllImport("libc")] static unsafe extern void* memset(void* destination, int value, nuint count);
    [DllImport("libc")] static safe extern void dos_set_color(int index, int red, int green, int blue);
    [DllImport("libc")] static safe extern void dos_wait_frame();
    [DllImport("libc")] static safe extern int dos_poll_keys();
    [DllImport("libc")] static safe extern int dos_key_down(int scanCode);
    [DllImport("libc")] static safe extern int dos_read_key();
    [DllImport("libc")] static safe extern uint dos_ticks();
    [DllImport("libc")] static safe extern void dos_sleep(uint milliseconds);
    [DllImport("libc")] static safe extern int dos_in(int port);
    [DllImport("libc")] static safe extern void dos_out(int port, int value);
    [DllImport("libc")] static safe extern void dos_sound(uint hertz);
    [DllImport("libc")] static safe extern int dos_mouse_start();
    [DllImport("libc")] static unsafe extern int dos_mouse_read(int* x, int* y);
    [DllImport("libc")] static unsafe extern nint dos_file_open(byte* path, int mode);
    [DllImport("libc")] static unsafe extern int dos_file_read(nint file, byte* buffer, uint count);
    [DllImport("libc")] static unsafe extern int dos_file_write(nint file, byte* buffer, uint count);
    [DllImport("libc")] static safe extern int dos_file_seek(nint file, int offset, int origin);
    [DllImport("libc")] static safe extern int dos_file_size(nint file);
    [DllImport("libc")] static safe extern void dos_file_close(nint file);
    [DllImport("libc")] static unsafe extern int dos_file_delete(byte* path);

    /// <summary>Switches to 320x200 with 256 colors.</summary>
    public static void Graphics() => dos_video_mode(0x13);

    /// <summary>Switches back to the 80x25 text screen.</summary>
    public static void Text() => dos_video_mode(0x03);

    /// <summary>The screen in graphics mode: Width * Height bytes, a color number per pixel, row after row.</summary>
    public static Span<byte> Screen
    {
        get
        {
            unsafe
            {
                // SAFETY: in mode 0x13 the VGA maps 64000 bytes of screen at this address.
                return new Span<byte>(dos_framebuffer(), Width * Height);
            }
        }
    }

    /// <summary>Copies a picture of Width * Height bytes to the screen.</summary>
    public static void Show(ReadOnlySpan<byte> picture)
    {
        unsafe
        {
            // SAFETY: the pointer and length come from the span; dos_show copies no more than the screen holds.
            fixed (byte* p = picture)
                dos_show(p, (uint)picture.Length);
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

    /// <summary>Defines one of the 256 colors. Red, green and blue are 0-63.</summary>
    public static void SetColor(int index, int red, int green, int blue) => dos_set_color(index, red, green, blue);

    /// <summary>Waits for the next screen refresh: 70 a second in graphics mode. Returns at the moment to copy a finished picture to Screen.</summary>
    public static void WaitFrame() => dos_wait_frame();

    /// <summary>Takes in what happened on the keyboard since the last call. Returns how many key presses wait for ReadKey.</summary>
    public static int PollKeys() => dos_poll_keys();

    /// <summary>Whether a key is held down, as of the last PollKeys.</summary>
    public static bool KeyDown(int scanCode) => dos_key_down(scanCode) != 0;

    /// <summary>The next key press: its scan code in the high byte, its character (0 for keys without one) in the low byte. 0 if no key was pressed.</summary>
    public static int ReadKey() => dos_read_key();

    /// <summary>Milliseconds since some fixed moment.</summary>
    public static uint Ticks => dos_ticks();

    public static void Sleep(uint milliseconds) => dos_sleep(milliseconds);

    // ---- sound -----------------------------------------------------------------

    /// <summary>Starts a tone on the PC speaker at this pitch; it sounds until the next call. 0 turns it off.</summary>
    public static void Sound(uint hertz) => dos_sound(hertz);

    // ---- mouse -----------------------------------------------------------------

    /// <summary>Looks for a mouse driver (CTMOUSE, MOUSE.COM, ...). Returns the number of buttons, or 0 without one. Call it after Graphics(): drivers reset themselves when the screen mode changes.</summary>
    public static int MouseStart() => dos_mouse_start();

    /// <summary>Where the mouse is on the graphics screen (0 to Width - 1 and Height - 1). Returns the buttons held: 1 left, 2 right, 4 middle, added up.</summary>
    public static int Mouse(out int x, out int y)
    {
        int column, row, buttons;
        unsafe
        {
            // SAFETY: both pointers are to locals.
            buttons = dos_mouse_read(&column, &row);
        }
        x = column / 2; // drivers count 640 across in the 320-pixel mode
        y = row;
        return buttons;
    }

    // ---- files -----------------------------------------------------------------
    // Names are DOS names: at most 8 characters, a dot and 3 more ("SCORES.DAT"),
    // with backslashes and drive letters for other places than the current one.

    public const int ForReading = 0, ForWriting = 1, ForAppending = 2;
    public const int FromStart = 0, FromHere = 1, FromEnd = 2;

    // Names longer than DOS allows (even with a path) don't open anyway.
    const int MaxPath = 128;

    /// <safety>buffer must have room for MaxPath bytes.</safety>
    static unsafe bool ToDosName(string path, byte* buffer)
    {
        if (path == null || path.Length >= MaxPath)
            return false;
        unsafe
        {
            for (int i = 0; i < path.Length; i++)
                buffer[i] = path[i] < 0x80 ? (byte)path[i] : (byte)'?';
            buffer[path.Length] = 0;
        }
        return true;
    }

    /// <summary>Opens a file. ForReading needs it to exist; ForWriting creates it or empties it; ForAppending creates it or continues at its end. Returns 0 if that fails.</summary>
    public static nint FileOpen(string path, int mode)
    {
        unsafe
        {
            byte* name = stackalloc byte[MaxPath];
            // SAFETY: name has MaxPath bytes and ToDosName ends it with a 0.
            return ToDosName(path, name) ? dos_file_open(name, mode) : 0;
        }
    }

    /// <summary>Reads up to buffer.Length bytes. Returns how many were read (0 at the end of the file), or -1.</summary>
    public static int FileRead(nint file, Span<byte> buffer)
    {
        unsafe
        {
            // SAFETY: the pointer and length come from the span.
            fixed (byte* p = buffer)
                return dos_file_read(file, p, (uint)buffer.Length);
        }
    }

    /// <summary>Writes the bytes. Returns how many were written (fewer if the disk is full), or -1.</summary>
    public static int FileWrite(nint file, ReadOnlySpan<byte> bytes)
    {
        unsafe
        {
            // SAFETY: the pointer and length come from the span.
            fixed (byte* p = bytes)
                return dos_file_write(file, p, (uint)bytes.Length);
        }
    }

    /// <summary>Moves to a position, counted FromStart, FromHere or FromEnd. Returns the new position, or -1.</summary>
    public static int FileSeek(nint file, int offset, int origin) => dos_file_seek(file, offset, origin);

    public static int FileSize(nint file) => dos_file_size(file);

    public static void FileClose(nint file) => dos_file_close(file);

    /// <summary>Deletes a file. Returns false if there was none or it couldn't be deleted.</summary>
    public static bool FileDelete(string path)
    {
        unsafe
        {
            byte* name = stackalloc byte[MaxPath];
            // SAFETY: as in FileOpen.
            return ToDosName(path, name) && dos_file_delete(name) != 0;
        }
    }

    // ---- ports -----------------------------------------------------------------

    /// <summary>Reads a byte from a hardware port.</summary>
    public static int In(int port) => dos_in(port);

    /// <summary>Writes a byte to a hardware port.</summary>
    public static void Out(int port, int value) => dos_out(port, value);
}
