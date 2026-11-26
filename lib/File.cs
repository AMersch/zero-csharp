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
// can't be opened, read or written shows up as a false or a 0, or, from the
// functions that return one, a FileError saying why.
//
// Copy, Move, Remove, GetInfo, CreateNew and the FileError forms of the open
// functions work on Linux, macOS, Windows and WebAssembly; they use the calls
// lib/FileSystem.cs describes. The rest works everywhere.
//
// Underneath are the C library's stdio functions (fopen, fread, fwrite,
// fseeko, ftello, fclose, remove): the system's own on Linux, macOS, Windows
// and WebAssembly; dos/runtime.c and efi/machine.c supply them on DOS and
// UEFI, and cx16/runtime.c on the Commander X16 (see also lib/Disk.cs there).
// On Windows, win/runtime.c's fopen and remove take the UTF-8 path in
// place of the Universal CRT's, which would read it in the ANSI code page.
// On DOS, names are 8.3. Under wasmtime, a program sees only the directories
// it is given with --dir.

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
    [DllImport("libc")] static unsafe extern int setvbuf(nint stream, byte* buffer, int how, nuint size);
    [DllImport("libc")] static unsafe extern int fflush(nint stream);

    // The longest path, in UTF-8 bytes, that can be opened.
    const int MaxPath = 1024;

    nint _stream;

    /// <summary>Opens an existing file for reading. IsOpen is false if it can't be opened.</summary>
    public static File OpenRead(ReadOnlySpan<char> path) => Open(path, 'r');

    /// <summary>Creates a file for writing, emptying it if it exists. IsOpen is false if it can't be.</summary>
    public static File Create(ReadOnlySpan<char> path) => Open(path, 'w');

    /// <summary>Opens a file for writing at its end, creating it if needed. IsOpen is false if it can't be.</summary>
    public static File OpenAppend(ReadOnlySpan<char> path) => Open(path, 'a');

    /// <summary>OpenRead, saying why it failed: NotFound, AccessDenied, IsADirectory...</summary>
    public static File OpenRead(ReadOnlySpan<char> path, out FileError error) => Open(path, 'r', false, out error);

    /// <summary>Create, saying why it failed.</summary>
    public static File Create(ReadOnlySpan<char> path, out FileError error) => Open(path, 'w', false, out error);

    /// <summary>
    /// Creates a new file for writing, failing with AlreadyExists if anything
    /// has the name already: no other program can create it in between.
    /// </summary>
    public static File CreateNew(ReadOnlySpan<char> path, out FileError error) => Open(path, 'w', true, out error);

    /// <summary>OpenAppend, saying why it failed.</summary>
    public static File OpenAppend(ReadOnlySpan<char> path, out FileError error) => Open(path, 'a', false, out error);

    static File Open(ReadOnlySpan<char> path, char how)
    {
        Span<byte> name = stackalloc byte[MaxPath];
        if (!ToCPath(path, name))
            return default;
        return OpenC(name, how, false);
    }

    static File OpenC(Span<byte> name, char how, bool exclusive, bool plus = false)
    {
        // C's mode: r, w or a; + to read and write both; b for no newline
        // translation on Windows; x to fail if the file exists.
        Span<byte> mode = stackalloc byte[5];
        int at = 0;
        mode[at++] = (byte)how;
        if (plus)
            mode[at++] = (byte)'+';
        mode[at++] = (byte)'b';
        if (exclusive)
            mode[at++] = (byte)'x';
        mode[at] = 0;
        unsafe
        {
            // SAFETY: both are null-terminated; fopen only reads them.
            fixed (byte* n = name)
            fixed (byte* m = mode)
                return new File { _stream = fopen(n, m) };
        }
    }

    // Opening with an error: the system's error number says why fopen failed,
    // and a directory, which fopen opens for reading on Linux and macOS, is
    // refused.
    internal static File Open(ReadOnlySpan<char> path, char how, bool exclusive, out FileError error, bool plus = false)
    {
        Span<byte> name = stackalloc byte[FileSystem.PathBytes];
        if (FileSystem.ToC(path, name) < 0)
        {
            error = FileError.InvalidPath;
            return default;
        }
        // fopen's "x" makes creating a new file atomic, but semihosting (on
        // microcontrollers) ignores it and empties the file: look first too.
        if (exclusive && Exists(path))
        {
            error = FileError.AlreadyExists;
            return default;
        }
        FileSystem.ClearError();
        File file = OpenC(name, how, exclusive, plus);
        error = file.IsOpen ? FileError.None : FileSystem.LastError();
        // A directory: Linux and macOS open one for reading, Windows says
        // access denied. Either way, say what it is.
        if (error == FileError.AlreadyExists)
            return file;
        unsafe
        {
            // SAFETY: name is null-terminated; the file is closed only here.
            fixed (byte* n = name)
                if (FileSystem.Stat(n, true, out FileSystem.Status status) == FileError.None
                    && (status.Mode & FileSystem.TypeMask) == FileSystem.TypeDirectory)
                {
                    if (file.IsOpen)
                        file.Close();
                    error = FileError.IsADirectory;
                    return default;
                }
        }
        return file;
    }

    /// <summary>Whether the file was opened.</summary>
    public bool IsOpen => _stream != 0;

    // Turns the C library's buffering off for this file: FileStream buffers
    // in C# instead, so no C code keeps a pointer into memory that may be an
    // arena's. Call it before the first read or write. _IONBF is 4 in the
    // Universal CRT, 2 elsewhere.
    internal void Unbuffer()
    {
        int how = FileSystem.System == SystemKind.Windows ? 4 : 2;
        unsafe
        {
            // SAFETY: the stream is open; with _IONBF the C library takes no buffer.
            if (_stream != 0)
                setvbuf(_stream, null, how, 0);
        }
    }

    // Writes out what the C library has buffered. Between a write and a
    // read on the same file, C requires this or a seek.
    internal bool Flush()
    {
        unsafe { return _stream != 0 && fflush(_stream) == 0; } // SAFETY: the stream is open
    }

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

    /// <summary>
    /// Whether a file exists at the path: not a directory (following symbolic
    /// links). On DOS, UEFI and microcontrollers, whether it can be opened for
    /// reading.
    /// </summary>
    public static bool Exists(ReadOnlySpan<char> path)
    {
        if (FileSystem.HasCalls)
            return FileSystem.Stat(path, true, out FileSystem.Status status) == FileError.None
                && (status.Mode & FileSystem.TypeMask) != FileSystem.TypeDirectory;
        File file = OpenRead(path);
        if (!file.IsOpen)
            return false;
        unsafe { file.Close(); } // SAFETY: file is local and not used again.
        return true;
    }

    /// <summary>
    /// What the system knows about the file or directory: its length, kind and
    /// when it was last written (following symbolic links).
    /// </summary>
    public static FileError GetInfo(ReadOnlySpan<char> path, out FileInfo info)
    {
        FileError error = FileSystem.Stat(path, true, out FileSystem.Status status);
        info = error != FileError.None ? default : new FileInfo
        {
            Length = status.Size,
            IsDirectory = (status.Mode & FileSystem.TypeMask) == FileSystem.TypeDirectory,
            LastWriteUnixMilliseconds = status.UnixMilliseconds,
        };
        return error;
    }

    /// <summary>
    /// Moves (or renames) the file. With <paramref name="overwrite"/> a file
    /// already at <paramref name="to"/> is replaced; without it that's
    /// AlreadyExists. Between file systems, the file is copied and then deleted.
    /// </summary>
    public static FileError Move(ReadOnlySpan<char> from, ReadOnlySpan<char> to, bool overwrite = false)
    {
        FileError error = FileSystem.Stat(from, false, out FileSystem.Status status);
        if (error != FileError.None)
            return error;
        if ((status.Mode & FileSystem.TypeMask) == FileSystem.TypeDirectory)
            return FileError.IsADirectory;
        if (FileSystem.Stat(to, false, out FileSystem.Status existing) == FileError.None)
        {
            if (!overwrite)
                return FileError.AlreadyExists;
            if ((existing.Mode & FileSystem.TypeMask) == FileSystem.TypeDirectory)
                return FileError.IsADirectory;
        }
        error = Rename(from, to, out bool crossDevice);
        if (!crossDevice)
            return error;
        error = Copy(from, to, overwrite);
        return error != FileError.None ? error : Remove(from);
    }

    /// <summary>
    /// Copies the file's bytes to <paramref name="to"/>. With
    /// <paramref name="overwrite"/> a file already there is replaced; without
    /// it that's AlreadyExists.
    /// </summary>
    public static FileError Copy(ReadOnlySpan<char> from, ReadOnlySpan<char> to, bool overwrite = false)
    {
        File source = OpenRead(from, out FileError error);
        if (!source.IsOpen)
            return error;
        File target = Open(to, 'w', !overwrite, out error);
        if (!target.IsOpen)
        {
            unsafe { source.Close(); } // SAFETY: local, not used again.
            return error;
        }
        Span<byte> chunk = stackalloc byte[FileSystem.ChunkBytes];
        int n;
        FileSystem.ClearError();
        while ((n = source.Read(chunk)) > 0)
        {
            if (target.Write(chunk.Slice(0, n)) != n)
            {
                error = FileSystem.LastError();
                break;
            }
        }
        unsafe
        {
            // SAFETY: both are local and not used again. fclose writes out
            // what's buffered, and can fail doing it.
            source.Close();
            FileSystem.ClearError();
            if (fclose(target._stream) != 0 && error == FileError.None)
                error = FileSystem.LastError();
        }
        return error;
    }

    /// <summary>Deletes a file, saying why it couldn't: NotFound, AccessDenied, IsADirectory...</summary>
    public static FileError Remove(ReadOnlySpan<char> path)
    {
        Span<byte> name = stackalloc byte[FileSystem.PathBytes];
        if (FileSystem.ToC(path, name) < 0)
            return FileError.InvalidPath;
        unsafe
        {
            // SAFETY: name is null-terminated; the calls only read it.
            fixed (byte* n = name)
            {
                if (FileSystem.Stat(n, false, out FileSystem.Status status) == FileError.None
                    && (status.Mode & FileSystem.TypeMask) == FileSystem.TypeDirectory)
                    return FileError.IsADirectory;
                FileSystem.ClearError();
                return remove(n) == 0 ? FileError.None : FileSystem.LastError();
            }
        }
    }

    // rename(2), for files and directories. crossDevice: it failed only
    // because the two are on different file systems.
    internal static FileError Rename(ReadOnlySpan<char> from, ReadOnlySpan<char> to, out bool crossDevice)
    {
        crossDevice = false;
        Span<byte> source = stackalloc byte[FileSystem.PathBytes];
        Span<byte> target = stackalloc byte[FileSystem.PathBytes];
        if (FileSystem.ToC(from, source) < 0 || FileSystem.ToC(to, target) < 0)
            return FileError.InvalidPath;
        unsafe
        {
            // SAFETY: both are null-terminated; rename only reads them.
            fixed (byte* s = source)
            fixed (byte* t = target)
            {
                FileSystem.ClearError();
                if (FileSystem.rename(s, t) == 0)
                    return FileError.None;
            }
        }
        crossDevice = FileSystem.LastErrorIsCrossDevice();
        return FileSystem.LastError();
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

/// <summary>What File.GetInfo says about a file or directory.</summary>
public struct FileInfo
{
    /// <summary>The file's length in bytes.</summary>
    public long Length;

    /// <summary>Whether it's a directory.</summary>
    public bool IsDirectory;

    /// <summary>When it was last written, in milliseconds since 1970-01-01 UTC, as Clock.UnixMilliseconds.</summary>
    public long LastWriteUnixMilliseconds;
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
