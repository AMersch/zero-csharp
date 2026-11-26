// Files: read or write a whole file in one call, or open one and read, write
// and seek in it.
//
//   File.WriteAllText("notes.txt", "first line\n");
//   if (File.TryReadAllText(arena, "notes.txt", out string text)) ...
//
//   File log = File.OpenAppend("log.txt");
//   if (log.IsOpen)
//   {
//       log.Write("started\n");
//       unsafe { log.Close(); } // SAFETY: log isn't used after this.
//   }
//
// Text is UTF-8 on disk and UTF-16 in a string, as with Out and In. Paths are
// passed to the system as UTF-8. Nothing here fails the program: a file that
// can't be opened, read or written shows up as a false or a 0.
//
// Underneath are the C library's stdio functions (fopen, fread, fwrite,
// fseeko, ftello, fclose, remove): the system's own on Linux, macOS, Windows
// and WebAssembly; dos/runtime.c and efi/machine.c supply them on DOS and
// UEFI, and cx16/runtime.c on the Commander X16 (see also lib/Disk.cs there). On Windows, win/runtime.c's fopen and remove take the UTF-8 path in
// place of the Universal CRT's, which would read it in the ANSI code page. On DOS, names are 8.3. Under wasmtime, a program sees only the
// directories it is given with --dir.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// An open file, or a file that couldn't be opened (IsOpen is false). A handle: copies
/// share the file. Also the functions that read or write a whole file at once.
/// </summary>
public struct File
{
    [DllImport("libc")] static unsafe extern nint fopen(byte* path, byte* mode);
    [DllImport("libc")] static unsafe extern nuint fread(void* buffer, nuint size, nuint count, nint stream);
    [DllImport("libc")] static unsafe extern nuint fwrite(void* buffer, nuint size, nuint count, nint stream);
    [DllImport("libc")] static unsafe extern int fseeko(nint stream, long offset, int origin);
    [DllImport("libc")] static unsafe extern long ftello(nint stream);
    [DllImport("libc")] static unsafe extern int fclose(nint stream);
    [DllImport("libc")] static unsafe extern int remove(byte* path);

    // The longest path, in UTF-8 bytes, that can be opened.
    const int MaxPath = 1024;

    nint _stream;

    /// <summary>Opens an existing file for reading. IsOpen is false if it can't be opened.</summary>
    public static File OpenRead(ReadOnlySpan<char> path) => Open(path, 'r');

    /// <summary>Creates a file for writing, emptying it if it exists. IsOpen is false if it can't be.</summary>
    public static File Create(ReadOnlySpan<char> path) => Open(path, 'w');

    /// <summary>Opens a file for writing at its end, creating it if needed. IsOpen is false if it can't be.</summary>
    public static File OpenAppend(ReadOnlySpan<char> path) => Open(path, 'a');

    static File Open(ReadOnlySpan<char> path, char how)
    {
        Span<byte> name = stackalloc byte[MaxPath];
        if (!ToCPath(path, name))
            return default;
        Span<byte> mode = stackalloc byte[3];
        mode[0] = (byte)how;
        mode[1] = (byte)'b'; // no newline translation on Windows
        mode[2] = 0;
        unsafe
        {
            // SAFETY: both are null-terminated; fopen only reads them.
            fixed (byte* n = name)
            fixed (byte* m = mode)
                return new File { _stream = fopen(n, m) };
        }
    }

    /// <summary>Whether the file was opened.</summary>
    public bool IsOpen => _stream != 0;

    /// <summary>
    /// Reads up to buffer.Length bytes and returns how many it read: fewer at the end of
    /// the file, and 0 once there is nothing left or the file isn't open.
    /// </summary>
    public int Read(Span<byte> buffer)
    {
        if (_stream == 0 || buffer.Length == 0)
            return 0;
        unsafe
        {
            // SAFETY: fread writes at most buffer.Length bytes into buffer.
            fixed (byte* p = buffer)
                return (int)fread(p, 1, (nuint)buffer.Length, _stream);
        }
    }

    /// <summary>Writes bytes as they are and returns how many were written.</summary>
    public int Write(ReadOnlySpan<byte> bytes)
    {
        if (_stream == 0 || bytes.Length == 0)
            return 0;
        unsafe
        {
            // SAFETY: fwrite reads bytes.Length bytes from bytes.
            fixed (byte* p = bytes)
                return (int)fwrite(p, 1, (nuint)bytes.Length, _stream);
        }
    }

    /// <summary>Writes text as UTF-8. Returns false if not all of it was written.</summary>
    public bool Write(ReadOnlySpan<char> text)
    {
        if (_stream == 0)
            return false;
        Span<byte> chunk = stackalloc byte[512];
        int next = 0;
        while (next < text.Length)
        {
            int length = Utf8.Encode(text, ref next, chunk);
            if (Write(chunk.Slice(0, length)) != length)
                return false;
        }
        return true;
    }

    /// <summary>Writes a string as UTF-8. Returns false if not all of it was written.</summary>
    public bool Write(string text) => Write(text.AsSpan());

    /// <summary>Where the next read or write happens, in bytes from the start; -1 if the file isn't open.</summary>
    public long Position
    {
        get
        {
            if (_stream == 0)
                return -1;
            unsafe { return ftello(_stream); } // SAFETY: the stream is open.
        }
    }

    /// <summary>Moves to <paramref name="position"/> bytes from the start. Returns false if it can't.</summary>
    public bool Seek(long position)
    {
        if (_stream == 0 || position < 0)
            return false;
        unsafe { return fseeko(_stream, position, 0) == 0; } // SAFETY: the stream is open.
    }

    /// <summary>The file's size in bytes, or -1 if it isn't open. The position stays where it was.</summary>
    public long Length
    {
        get
        {
            if (_stream == 0)
                return -1;
            unsafe
            {
                // SAFETY: the stream is open. 2 is SEEK_END.
                long here = ftello(_stream);
                if (here < 0 || fseeko(_stream, 0, 2) != 0)
                    return -1;
                long end = ftello(_stream);
                fseeko(_stream, here, 0);
                return end;
            }
        }
    }

    /// <summary>
    /// Writes out what is buffered and closes the file. Does nothing if it isn't open.
    /// </summary>
    /// <safety>
    /// Copies of this handle still point at the closed file. Don't use any of them
    /// after this, and don't close the file twice.
    /// </safety>
    public unsafe void Close()
    {
        if (_stream == 0)
            return;
        unsafe { fclose(_stream); } // SAFETY: the caller's contract.
        _stream = 0;
    }

    // ---- whole files ----------------------------------------------------------

    /// <summary>Whether a file exists and can be opened for reading.</summary>
    public static bool Exists(ReadOnlySpan<char> path)
    {
        File file = OpenRead(path);
        if (!file.IsOpen)
            return false;
        unsafe { file.Close(); } // SAFETY: file is local and not used again.
        return true;
    }

    /// <summary>Deletes a file. Returns false if it couldn't, for example because it doesn't exist.</summary>
    public static bool Delete(ReadOnlySpan<char> path)
    {
        Span<byte> name = stackalloc byte[MaxPath];
        if (!ToCPath(path, name))
            return false;
        unsafe
        {
            // SAFETY: name is null-terminated; remove only reads it.
            fixed (byte* n = name)
                return remove(n) == 0;
        }
    }

    /// <summary>
    /// Reads a whole file into <paramref name="arena"/>. Returns false, and an empty
    /// span, if the file can't be opened or read, or the arena is out of memory.
    /// </summary>
    /// <safety>The span points into the arena. Don't use it after the memory is freed.</safety>
    public static unsafe bool TryReadAllBytes(Arena arena, ReadOnlySpan<char> path, out Span<byte> bytes)
    {
        bytes = default;
        File file = OpenRead(path);
        if (!file.IsOpen)
            return false;
        bool ok = false;
        long length = file.Length;
        if (length >= 0 && length <= int.MaxValue)
        {
            byte* data = arena.AllocArray<byte>((int)length, 1, zero: false);
            if (data != null || length == 0)
            {
                unsafe { bytes = new Span<byte>(data, (int)length); } // SAFETY: data has room for length bytes.
                int read = file.Read(bytes);
                bytes = bytes.Slice(0, read);
                ok = read == length;
            }
        }
        unsafe { file.Close(); } // SAFETY: file is local and not used again.
        return ok;
    }

    /// <summary>
    /// Reads a whole UTF-8 file into a string in <paramref name="arena"/>. Bytes that
    /// aren't UTF-8 become U+FFFD. Returns false, and null, if the file can't be opened or
    /// read, or the arena is out of memory.
    /// </summary>
    public static bool TryReadAllText(Arena arena, ReadOnlySpan<char> path, out string text)
    {
        text = null;
        File file = OpenRead(path);
        if (!file.IsOpen)
            return false;
        long length = file.Length;
        // A UTF-8 file never decodes to more UTF-16 chars than it has bytes, so a
        // string of `length` chars has room. The file is read into the back half of
        // that room and decoded forwards into the front: char i is written after
        // byte i and everything before it has been read, so no unread byte is
        // overwritten.
        byte* obj = length >= 0 && length <= int.MaxValue / 2
            ? Str.AllocStringObject(arena, (int)length)
            : null;
        if (obj != null)
        {
            int count = (int)length;
            Span<char> chars;
            Span<byte> bytes;
            unsafe
            {
                // SAFETY: AllocStringObject made room for count chars, which is
                // 2 * count bytes; the bytes are the second half of them.
                byte* start = obj + RuntimeHelpers.OffsetToStringData;
                chars = new Span<char>(start, count);
                bytes = new Span<byte>(start + count, count);
            }
            int read = file.Read(bytes);
            if (read == count)
            {
                int used = Utf8.Decode(bytes, chars);
                unsafe { text = Str.MakeString(obj, used); } // SAFETY: obj has room for count >= used chars.
            }
        }
        unsafe { file.Close(); } // SAFETY: file is local and not used again.
        return text != null;
    }

    /// <summary>Writes bytes to a file, replacing it if it exists. Returns false if that failed.</summary>
    public static bool WriteAllBytes(ReadOnlySpan<char> path, ReadOnlySpan<byte> bytes)
    {
        File file = Create(path);
        if (!file.IsOpen)
            return false;
        bool ok = file.Write(bytes) == bytes.Length;
        unsafe { file.Close(); } // SAFETY: file is local and not used again.
        return ok;
    }

    /// <summary>Writes text to a file as UTF-8, replacing it if it exists. Returns false if that failed.</summary>
    public static bool WriteAllText(ReadOnlySpan<char> path, ReadOnlySpan<char> text) => WriteText(Create(path), text);

    /// <summary>Adds text to the end of a file as UTF-8, creating the file if needed. Returns false if that failed.</summary>
    public static bool AppendAllText(ReadOnlySpan<char> path, ReadOnlySpan<char> text) => WriteText(OpenAppend(path), text);

    static bool WriteText(File file, ReadOnlySpan<char> text)
    {
        if (!file.IsOpen)
            return false;
        bool ok = file.Write(text);
        unsafe { file.Close(); } // SAFETY: file is local and not used again.
        return ok;
    }

    // A path as the C library takes it: UTF-8, null-terminated. False if it is
    // too long or holds a null character.
    static bool ToCPath(ReadOnlySpan<char> path, Span<byte> dest)
    {
        if (path.Length == 0 || path.IndexOf('\0') >= 0)
            return false;
        int next = 0;
        int length = Utf8.Encode(path, ref next, dest.Slice(0, dest.Length - 1));
        if (next < path.Length)
            return false;
        dest[length] = 0;
        return true;
    }
}

// UTF-8 to and from UTF-16, for File. Unpaired surrogates and bytes that aren't
// UTF-8 become U+FFFD, as in Out and In.
static class Utf8
{
    // Encodes text from `next` on into dest, as far as whole characters fit.
    // Advances `next` past them and returns the number of bytes written.
    public static int Encode(ReadOnlySpan<char> text, ref int next, Span<byte> dest)
    {
        int length = 0;
        while (next < text.Length)
        {
            int codePoint = text[next];
            int width = 1;
            if (codePoint >= 0xD800 && codePoint <= 0xDBFF && next + 1 < text.Length
                && text[next + 1] >= 0xDC00 && text[next + 1] <= 0xDFFF)
            {
                codePoint = 0x10000 + ((codePoint - 0xD800) << 10) + (text[next + 1] - 0xDC00);
                width = 2;
            }
            else if (codePoint >= 0xD800 && codePoint <= 0xDFFF)
            {
                codePoint = 0xFFFD;
            }

            int size = codePoint < 0x80 ? 1 : codePoint < 0x800 ? 2 : codePoint < 0x10000 ? 3 : 4;
            if (length + size > dest.Length)
                break;
            switch (size)
            {
                case 1:
                    dest[length] = (byte)codePoint;
                    break;
                case 2:
                    dest[length] = (byte)(0xC0 | (codePoint >> 6));
                    dest[length + 1] = (byte)(0x80 | (codePoint & 0x3F));
                    break;
                case 3:
                    dest[length] = (byte)(0xE0 | (codePoint >> 12));
                    dest[length + 1] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
                    dest[length + 2] = (byte)(0x80 | (codePoint & 0x3F));
                    break;
                default:
                    dest[length] = (byte)(0xF0 | (codePoint >> 18));
                    dest[length + 1] = (byte)(0x80 | ((codePoint >> 12) & 0x3F));
                    dest[length + 2] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
                    dest[length + 3] = (byte)(0x80 | (codePoint & 0x3F));
                    break;
            }
            length += size;
            next += width;
        }
        return length;
    }

    // Decodes bytes into chars, front to back, and returns the number of chars.
    // chars must have room for bytes.Length chars, which is always enough. Each
    // char is written only after the bytes it comes from have been read, so
    // bytes may lie in the back half of chars' memory (File.TryReadAllText).
    public static int Decode(ReadOnlySpan<byte> bytes, Span<char> chars)
    {
        int count = 0;
        int i = 0;
        while (i < bytes.Length)
        {
            int lead = bytes[i++];
            int codePoint;
            if (lead < 0x80)
            {
                codePoint = lead;
            }
            else
            {
                int need, min;
                if (lead >= 0xC2 && lead <= 0xDF) { need = 1; min = 0x80; codePoint = lead & 0x1F; }
                else if (lead >= 0xE0 && lead <= 0xEF) { need = 2; min = 0x800; codePoint = lead & 0x0F; }
                else if (lead >= 0xF0 && lead <= 0xF4) { need = 3; min = 0x10000; codePoint = lead & 0x07; }
                else { need = 0; min = 0; codePoint = 0xFFFD; }

                for (; need > 0; need--)
                {
                    // A byte that can't continue the sequence is left for the next character.
                    if (i == bytes.Length || (bytes[i] & 0xC0) != 0x80)
                    {
                        codePoint = 0xFFFD;
                        break;
                    }
                    codePoint = (codePoint << 6) | (bytes[i++] & 0x3F);
                }
                if (codePoint < min || codePoint > 0x10FFFF || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
                    codePoint = 0xFFFD;
            }

            if (codePoint >= 0x10000)
            {
                codePoint -= 0x10000;
                chars[count++] = (char)(0xD800 + (codePoint >> 10));
                chars[count++] = (char)(0xDC00 + (codePoint & 0x3FF));
            }
            else
            {
                chars[count++] = (char)codePoint;
            }
        }
        return count;
    }
}
