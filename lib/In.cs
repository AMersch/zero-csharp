// Buffered stdin reader for zero mode, the counterpart of Out.
//
//   Span<char> buffer = stackalloc char[80];
//   Out.Write($"Q{i}: {a} + {b} = ");          // In flushes Out before reading
//   int length = In.ReadLine(buffer);
//   if (length < 0) ...                         // end of input
//   if (length > buffer.Length) ...             // too long; the rest was skipped
//   ReadOnlySpan<char> answer = buffer.Slice(0, length);
//
// The line goes into the caller's buffer, so reading allocates nothing. To keep
// one past the next read, copy it: Str.ToArenaString(arena, answer).
// Input is decoded as UTF-8 (invalid bytes become U+FFFD), and the line ending,
// "\n" or "\r\n", is left out.

using System;
using System.Runtime.InteropServices;

public static class In
{
    [DllImport("libc")]
    static unsafe extern nint read(int fd, byte* buffer, nuint count);

    const int Capacity = 256;

    struct Buffer
    {
        public fixed byte Bytes[Capacity];
    }

    static Buffer s_buffer;
    static int s_start; // next unread byte
    static int s_end;   // end of the bytes read so far
    static bool s_ended;

    /// <summary>
    /// Reads one line into <paramref name="buffer"/> and returns its length, or -1
    /// at the end of input. A line longer than the buffer returns its full length:
    /// the buffer holds its first buffer.Length chars and the rest is skipped.
    /// </summary>
    public static int ReadLine(Span<char> buffer)
    {
        Out.Flush();
        if (Peek() < 0)
            return -1;

        int length = 0;
        bool afterCR = false;
        while (true)
        {
            int b = Peek();
            if (b < 0)
                break;
            s_start++;
            if (b == '\n')
            {
                if (afterCR)
                    length--;
                break;
            }
            afterCR = b == '\r';

            int codePoint = b < 0x80 ? b : DecodeRest(b);
            if (codePoint >= 0x10000)
            {
                codePoint -= 0x10000;
                Store(buffer, ref length, (char)(0xD800 + (codePoint >> 10)));
                Store(buffer, ref length, (char)(0xDC00 + (codePoint & 0x3FF)));
            }
            else
            {
                Store(buffer, ref length, (char)codePoint);
            }
        }
        return length;
    }

    static void Store(Span<char> buffer, ref int length, char c)
    {
        if (length < buffer.Length)
            buffer[length] = c;
        length++;
    }

    // The lead byte is consumed; consumes the continuation bytes that follow it.
    // A byte that can't continue the sequence is left for the next character.
    static int DecodeRest(int lead)
    {
        int need, min, codePoint;
        if (lead >= 0xC2 && lead <= 0xDF)
        {
            need = 1;
            min = 0x80;
            codePoint = lead & 0x1F;
        }
        else if (lead >= 0xE0 && lead <= 0xEF)
        {
            need = 2;
            min = 0x800;
            codePoint = lead & 0x0F;
        }
        else if (lead >= 0xF0 && lead <= 0xF4)
        {
            need = 3;
            min = 0x10000;
            codePoint = lead & 0x07;
        }
        else
        {
            return 0xFFFD; // a continuation byte, or a lead no valid character has
        }

        for (; need > 0; need--)
        {
            int b = Peek();
            if (b < 0 || (b & 0xC0) != 0x80)
                return 0xFFFD;
            s_start++;
            codePoint = (codePoint << 6) | (b & 0x3F);
        }

        // Overlong forms, UTF-16 surrogates and values past U+10FFFF aren't characters.
        if (codePoint < min || codePoint > 0x10FFFF || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
            return 0xFFFD;
        return codePoint;
    }

    // The next byte without consuming it, or -1 at the end of input.
    static int Peek()
    {
        if (s_start == s_end)
        {
            if (s_ended)
                return -1;

            nint count;
            unsafe
            {
                // SAFETY: read() writes at most Capacity bytes into the buffer.
                fixed (byte* p = s_buffer.Bytes)
                    count = read(0, p, Capacity);
            }
            if (count <= 0)
            {
                // 0 is the end of input; a read error ends it too.
                s_ended = true;
                return -1;
            }
            s_start = 0;
            s_end = (int)count;
        }

        unsafe
        {
            // SAFETY: s_start < s_end <= Capacity.
            return s_buffer.Bytes[s_start];
        }
    }
}
