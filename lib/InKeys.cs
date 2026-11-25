// The rest of In: what Console needs beyond ReadLine and Read. Keys one at a
// time (Console.ReadKey), whether one waits (Console.KeyAvailable), a line as
// a new string (Console.ReadLine) and one character (Console.Read).
//
// A key is read without waiting for Enter by putting the terminal in raw mode
// for that one read: tcgetattr, cfmakeraw, tcsetattr, then back. The termios
// struct is only ever passed along, never looked into, so its layout (Linux's
// and macOS's differ) doesn't matter. The targets without a terminal driver
// (Windows, DOS, UEFI, the C64, WebAssembly, microcontrollers) have these
// functions in their runtime.c: there, in raw mode, read() returns keys as
// they are pressed, and the keys without a character as the escape sequences
// a terminal sends (ESC [ A for the up arrow). So one parser serves all.
//
// When standard input isn't a terminal (a pipe or a file, as in the tests),
// a key is the next character of the input, and escape sequences in it are
// keys too, as on .NET for Unix.

using System;
using System.Runtime.InteropServices;

public static partial class In
{
    [DllImport("libc")] static safe extern int isatty(int fd);
    [DllImport("libc")] static unsafe extern int tcgetattr(int fd, byte* termios);
    [DllImport("libc")] static unsafe extern int tcsetattr(int fd, int when, byte* termios);
    [DllImport("libc")] static unsafe extern void cfmakeraw(byte* termios);
    [DllImport("libc")] static unsafe extern int poll(PollFd* fds, nuint count, int timeout);

    struct PollFd
    {
        public int Fd;
        public short Events;
        public short Returned;
    }

    const short PollIn = 1;
    // Big enough for any system's struct termios (60 bytes on Linux, 72 on macOS).
    const int TermiosBytes = 256;

    static char s_lowSurrogate;   // the second half of a character ReadKey or Read returned the first of
    static bool s_keyAfterCR;     // a "\n" right after a "\r" is the same Enter

    /// <summary>The next key, as Console.ReadKey returns it.</summary>
    internal static ConsoleKeyInfo ReadKey(bool intercept)
    {
        Out.Flush();
        ConsoleKeyInfo key = NextKey();
        if (!intercept && key.KeyChar != '\0')
        {
            if (key.KeyChar == '\r')
                Out.Line();
            else if (key.KeyChar >= ' ')
            {
                Out.Write(key.KeyChar);
                Out.Flush();
            }
        }
        return key;
    }

    static ConsoleKeyInfo NextKey()
    {
        if (s_lowSurrogate != '\0')
        {
            char low = s_lowSurrogate;
            s_lowSurrogate = '\0';
            return new ConsoleKeyInfo(low, ConsoleKey.None, false, false, false);
        }
        while (true)
        {
            if (s_start == s_end)
                FillKeys();
            int b = Peek();
            if (b < 0)
                throw new InvalidOperationException("Console.ReadKey: the end of input");
            s_start++;
            if (b == '\n' && s_keyAfterCR)
            {
                s_keyAfterCR = false;
                continue;
            }
            s_keyAfterCR = b == '\r';
            if (b == 0x1B)
                return Escape();
            if (b < 0x80)
                return FromAscii(b, false);
            int codePoint = DecodeRest(b);
            if (codePoint >= 0x10000)
            {
                codePoint -= 0x10000;
                s_lowSurrogate = (char)(0xDC00 + (codePoint & 0x3FF));
                return new ConsoleKeyInfo((char)(0xD800 + (codePoint >> 10)), ConsoleKey.None, false, false, false);
            }
            return new ConsoleKeyInfo((char)codePoint, ConsoleKey.None, false, false, false);
        }
    }

    // What a terminal reads for one ASCII byte: letters and digits, Enter, Tab,
    // Backspace (8 or 127), Ctrl+letter (1-26), and the signs .NET names.
    static ConsoleKeyInfo FromAscii(int b, bool alt)
    {
        char c = (char)b;
        switch (b)
        {
            case '\r':
            case '\n':
                return new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, alt, false);
            case '\t':
                return new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, alt, false);
            case 8:
            case 127:
                return new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, alt, false);
            case 0x1B:
                return new ConsoleKeyInfo(c, ConsoleKey.Escape, false, alt, false);
            case ' ':
                return new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, alt, false);
        }
        if (b >= 1 && b <= 26)
            return new ConsoleKeyInfo(c, ConsoleKey.A + (b - 1), false, alt, true);
        if (c >= 'a' && c <= 'z')
            return new ConsoleKeyInfo(c, ConsoleKey.A + (c - 'a'), false, alt, false);
        if (c >= 'A' && c <= 'Z')
            return new ConsoleKeyInfo(c, ConsoleKey.A + (c - 'A'), true, alt, false);
        if (c >= '0' && c <= '9')
            return new ConsoleKeyInfo(c, ConsoleKey.D0 + (c - '0'), false, alt, false);
        ConsoleKey key = c switch
        {
            ';' => ConsoleKey.Oem1,
            '=' => ConsoleKey.OemPlus,
            ',' => ConsoleKey.OemComma,
            '-' => ConsoleKey.OemMinus,
            '.' => ConsoleKey.OemPeriod,
            '/' => ConsoleKey.Oem2,
            '`' => ConsoleKey.Oem3,
            '[' => ConsoleKey.Oem4,
            '\\' => ConsoleKey.Oem5,
            ']' => ConsoleKey.Oem6,
            '\'' => ConsoleKey.Oem7,
            _ => ConsoleKey.None,
        };
        return new ConsoleKeyInfo(c, key, false, alt, false);
    }

    // After ESC: a key's escape sequence, Alt with a key (ESC then the key), or
    // Escape itself when nothing follows in what was read.
    static ConsoleKeyInfo Escape()
    {
        int next = s_start < s_end ? Buffered(s_start) : -1;
        if (next == '[' || next == 'O')
        {
            int at = s_start + 1;
            int number = 0, modifier = 0;
            bool second = false;
            while (at < s_end && ((Buffered(at) >= '0' && Buffered(at) <= '9') || Buffered(at) == ';'))
            {
                int d = Buffered(at++);
                if (d == ';')
                    second = true;
                else if (second)
                    modifier = modifier * 10 + d - '0';
                else
                    number = number * 10 + d - '0';
            }
            if (at < s_end)
            {
                ConsoleKey key = SequenceKey((char)Buffered(at), number, next == 'O');
                if (key != ConsoleKey.None)
                {
                    s_start = at + 1;
                    // The modifier is 1 + a sum: 1 Shift, 2 Alt, 4 Control.
                    int bits = modifier > 0 ? modifier - 1 : 0;
                    bool shift = (bits & 1) != 0 || (key == ConsoleKey.Tab);
                    return new ConsoleKeyInfo(key == ConsoleKey.Tab ? '\t' : '\0', key, shift, (bits & 2) != 0, (bits & 4) != 0);
                }
            }
        }
        if (next >= 0 && next != 0x1B && next < 0x80)
        {
            s_start++;
            return FromAscii(next, true);
        }
        return new ConsoleKeyInfo((char)0x1B, ConsoleKey.Escape, false, false, false);
    }

    // The key an escape sequence ends with: ESC [ A, ESC O P, ESC [ 15 ~, ...
    static ConsoleKey SequenceKey(char final, int number, bool ss3)
    {
        switch (final)
        {
            case 'A': return ConsoleKey.UpArrow;
            case 'B': return ConsoleKey.DownArrow;
            case 'C': return ConsoleKey.RightArrow;
            case 'D': return ConsoleKey.LeftArrow;
            case 'H': return ConsoleKey.Home;
            case 'F': return ConsoleKey.End;
            case 'Z': return ss3 ? ConsoleKey.None : ConsoleKey.Tab; // Shift+Tab
            case 'P': return ConsoleKey.F1;
            case 'Q': return ConsoleKey.F2;
            case 'R': return ConsoleKey.F3;
            case 'S': return ConsoleKey.F4;
            case '~':
                switch (number)
                {
                    case 1: case 7: return ConsoleKey.Home;
                    case 2: return ConsoleKey.Insert;
                    case 3: return ConsoleKey.Delete;
                    case 4: case 8: return ConsoleKey.End;
                    case 5: return ConsoleKey.PageUp;
                    case 6: return ConsoleKey.PageDown;
                    case >= 11 and <= 15: return ConsoleKey.F1 + (number - 11);
                    case >= 17 and <= 21: return ConsoleKey.F6 + (number - 17);
                    case 23: return ConsoleKey.F11;
                    case 24: return ConsoleKey.F12;
                }
                break;
        }
        return ConsoleKey.None;
    }

    static int Buffered(int at)
    {
        unsafe
        {
            // SAFETY: callers keep at < s_end <= Capacity.
            return s_buffer.Bytes[at];
        }
    }

    // Reads what the keyboard has, at least one key, in raw mode when standard
    // input is a terminal; otherwise leaves it to Peek's ordinary read.
    static void FillKeys()
    {
        if (s_ended || isatty(0) != 1)
            return;
        nint count;
        unsafe
        {
            // SAFETY: both buffers hold TermiosBytes bytes, more than any
            // struct termios, and read() writes at most Capacity bytes into
            // the buffer.
            byte* saved = stackalloc byte[TermiosBytes];
            byte* raw = stackalloc byte[TermiosBytes];
            if (tcgetattr(0, saved) != 0)
                return;
            for (int i = 0; i < TermiosBytes; i++)
                raw[i] = saved[i];
            cfmakeraw(raw);
            if (tcsetattr(0, 0, raw) != 0) // 0: TCSANOW
                return;
            fixed (byte* p = s_buffer.Bytes)
                count = read(0, p, Capacity);
            tcsetattr(0, 0, saved);
        }
        if (count <= 0)
        {
            s_ended = true;
            return;
        }
        s_start = 0;
        s_end = (int)count;
    }

    /// <summary>Whether a key waits to be read, as Console.KeyAvailable has it.</summary>
    internal static bool KeyAvailable()
    {
        Out.Flush();
        if (s_lowSurrogate != '\0' || s_start < s_end)
            return true;
        if (s_ended)
            return false;
        if (isatty(0) != 1)
            return true; // a pipe or a file: there is a next character, or the end
        unsafe
        {
            // SAFETY: both buffers hold more than any struct termios; poll()
            // reads and writes the one PollFd it is given.
            byte* saved = stackalloc byte[TermiosBytes];
            byte* raw = stackalloc byte[TermiosBytes];
            if (tcgetattr(0, saved) != 0)
                return false;
            for (int i = 0; i < TermiosBytes; i++)
                raw[i] = saved[i];
            cfmakeraw(raw);
            tcsetattr(0, 0, raw);
            PollFd fd = new PollFd { Fd = 0, Events = PollIn };
            int ready = poll(&fd, 1, 0);
            tcsetattr(0, 0, saved);
            return ready > 0 && (fd.Returned & PollIn) != 0;
        }
    }

    /// <summary>The next line as a new string, without its line ending, or null at the end of input.</summary>
    internal static string ReadLineString()
    {
        Out.Flush();
        if (s_lowSurrogate == '\0' && Peek() < 0)
            return null;
        var line = new System.Text.StringBuilder();
        bool afterCR = false;
        while (true)
        {
            int c = ReadChar();
            if (c < 0 || c == '\n')
                break;
            if (afterCR)
                line.Append('\r');
            afterCR = c == '\r';
            if (!afterCR)
                line.Append((char)c);
        }
        return line.ToString();
    }

    /// <summary>The next UTF-16 char of standard input, or -1 at the end.</summary>
    internal static int ReadChar()
    {
        Out.Flush();
        if (s_lowSurrogate != '\0')
        {
            char low = s_lowSurrogate;
            s_lowSurrogate = '\0';
            return low;
        }
        int b = Peek();
        if (b < 0)
            return -1;
        s_start++;
        if (b < 0x80)
            return b;
        int codePoint = DecodeRest(b);
        if (codePoint >= 0x10000)
        {
            codePoint -= 0x10000;
            s_lowSurrogate = (char)(0xDC00 + (codePoint & 0x3FF));
            return 0xD800 + (codePoint >> 10);
        }
        return codePoint;
    }
}
