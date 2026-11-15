// Buffered stdout writer for --stdlib:zero.
//
// zerolib's Console makes one syscall per character, and only prints string
// and int. This writer buffers a line, prints long and bool too, and hands
// the line to libc write() in one call.

using System;
using System.Runtime.InteropServices;

public static class Out
{
    [DllImport("libc")]
    static unsafe extern nint write(int fd, byte* buffer, nuint count);

    const int Capacity = 256;

    struct Buffer
    {
        public fixed byte Bytes[Capacity];
    }

    static Buffer s_buffer;
    static int s_length;

    public static void Write(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            Put(c <= 0x7F ? (byte)c : (byte)'?');
        }
    }

    public static void Write(long value)
    {
        // Work on the magnitude as ulong so long.MinValue is handled too.
        ulong magnitude = value < 0 ? (ulong)(-(value + 1)) + 1 : (ulong)value;
        if (value < 0)
            Put((byte)'-');

        Span<byte> digits = stackalloc byte[20];
        int n = 0;
        do
        {
            digits[n++] = (byte)('0' + (int)(magnitude % 10));
            magnitude /= 10;
        } while (magnitude != 0);

        while (n > 0)
            Put(digits[--n]);
    }

    public static void Write(bool value) => Write(value ? "true" : "false");

    public static void Line()
    {
        Put((byte)'\n');
        Flush();
    }

    public static void Line(string text)
    {
        Write(text);
        Line();
    }

    public static void Line(string text, long value)
    {
        Write(text);
        Write(value);
        Line();
    }

    public static void Line(string text1, long value, string text2)
    {
        Write(text1);
        Write(value);
        Write(text2);
        Line();
    }

    public static void Line(string text1, long value1, string text2, long value2)
    {
        Write(text1);
        Write(value1);
        Write(text2);
        Write(value2);
        Line();
    }

    public static void Flush()
    {
        if (s_length == 0)
            return;

        unsafe
        {
            // SAFETY: s_length <= Capacity, so write() reads only initialized buffer bytes.
            fixed (byte* p = s_buffer.Bytes)
                write(1, p, (nuint)s_length);
        }
        s_length = 0;
    }

    static void Put(byte b)
    {
        if (s_length == Capacity)
            Flush();

        unsafe
        {
            // SAFETY: Flush() above guarantees s_length < Capacity.
            // (Upstream bflat 10.0.0-rc.1 miscompiles this store on x64; the fork
            // ships the .NET 10 GA JIT, which fixes it.)
            s_buffer.Bytes[s_length++] = b;
        }
    }
}
