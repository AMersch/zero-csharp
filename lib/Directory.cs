// Directories: make, delete, move and list them, and the current directory.
//
//   FileError error = Directory.Create("logs/2026/11");   // and its parents
//   foreach (DirectoryEntry entry in Directory.Enumerate("logs"))
//       Out.Line(entry.Name, entry.IsDirectory ? "/" : "");
//   Directory.Delete("logs", recursive: true);
//
// Linux, macOS, Windows and WebAssembly (see lib/FileSystem.cs). Failures come
// back as a FileError rather than ending the program.

using System;
using System.Runtime.InteropServices;

/// <summary>Functions on directories, and the program's current directory.</summary>
public static class Directory
{
    [DllImport("libc")] static unsafe extern void* calloc(nuint count, nuint size);
    [DllImport("libc")] static unsafe extern void free(void* memory);

    /// <summary>Whether a directory exists at the path (following symbolic links).</summary>
    public static bool Exists(ReadOnlySpan<char> path)
        => FileSystem.Stat(path, true, out FileSystem.Status status) == FileError.None
            && (status.Mode & FileSystem.TypeMask) == FileSystem.TypeDirectory;

    /// <summary>
    /// Creates the directory, and any directories on the way to it that don't
    /// exist yet. A directory that's already there is fine (None); a file with
    /// the name is AlreadyExists, and a file on the way is NotADirectory.
    /// </summary>
    public static FileError Create(ReadOnlySpan<char> path)
    {
        Span<byte> name = stackalloc byte[FileSystem.MaxPath];
        int length = FileSystem.ToC(path, name);
        if (length < 0)
            return FileError.InvalidPath;
        int root = FileSystem.Utf8Length(Path.GetRoot(path));
        // Each name in turn, cut off with a '\0' after it.
        for (int i = root; i <= length; i++)
        {
            if (i < length && !IsSeparator(name[i]))
                continue;
            if (i == root || IsSeparator(name[i - 1]))
                continue; // nothing new since the last separator
            byte saved = name[i];
            name[i] = 0;
            FileError error = MakeOne(name, i == length);
            name[i] = saved;
            if (error != FileError.None)
                return error;
        }
        return FileError.None;
    }

    /// <summary>
    /// Deletes the directory. It must be empty (NotEmpty otherwise) unless
    /// <paramref name="recursive"/>, which deletes everything in it first.
    /// Symbolic links inside are deleted, not followed; a path that is itself
    /// a symbolic link to a directory deletes only the link.
    /// </summary>
    public static FileError Delete(ReadOnlySpan<char> path, bool recursive = false)
    {
        Span<byte> name = stackalloc byte[FileSystem.MaxPath];
        int length = FileSystem.ToC(path, name);
        if (length < 0)
            return FileError.InvalidPath;
        FileError error = StatC(name, false, out FileSystem.Status status);
        if (error != FileError.None)
            return error;
        uint type = status.Mode & FileSystem.TypeMask;
        if (type == FileSystem.TypeLink)
            return RemoveLink(name);
        if (type != FileSystem.TypeDirectory)
            return FileError.NotADirectory;
        while (length > 1 && IsSeparator(name[length - 1]))
            name[--length] = 0; // "a/" is "a", for the names added below
        return recursive ? DeleteTree(name, length) : RemoveDirectory(name);
    }

    /// <summary>
    /// Moves (or renames) the directory. AlreadyExists if something has the new
    /// name. Other if the two are on different file systems, which the system
    /// can't move a directory between.
    /// </summary>
    public static FileError Move(ReadOnlySpan<char> from, ReadOnlySpan<char> to)
    {
        FileError error = FileSystem.Stat(from, false, out FileSystem.Status status);
        if (error != FileError.None)
            return error;
        if ((status.Mode & FileSystem.TypeMask) != FileSystem.TypeDirectory)
            return FileError.NotADirectory;
        if (FileSystem.Stat(to, false, out _) == FileError.None)
            return FileError.AlreadyExists;
        return File.Rename(from, to, out _);
    }

    /// <summary>
    /// The current directory, as an absolute path; an empty string if the
    /// system won't say. Relative paths start from here.
    /// </summary>
    public static string GetCurrentDirectory()
    {
        Span<byte> buffer = stackalloc byte[FileSystem.MaxPath];
        unsafe
        {
            // SAFETY: getcwd writes at most MaxPath bytes, '\0' included.
            fixed (byte* b = buffer)
                if (FileSystem.getcwd(b, FileSystem.MaxPath) == null)
                    return "";
        }
        return FileSystem.ToString(buffer.Slice(0, FileSystem.Length(buffer)));
    }

    /// <summary>Makes the path the current directory.</summary>
    public static FileError SetCurrentDirectory(ReadOnlySpan<char> path)
    {
        Span<byte> name = stackalloc byte[FileSystem.MaxPath];
        if (FileSystem.ToC(path, name) < 0)
            return FileError.InvalidPath;
        FileSystem.ClearError();
        unsafe
        {
            // SAFETY: '\0'-terminated; chdir only reads it.
            fixed (byte* n = name)
                return FileSystem.chdir(n) == 0 ? FileError.None : FileSystem.LastError();
        }
    }

    /// <summary>
    /// The entries in the directory, for foreach: each one's name and kind, in
    /// the order the system gives them, without "." and "..". If the directory
    /// can't be read, Error says why and there are no entries. Enumerate it
    /// once; foreach closes it at the end, as does Dispose if it isn't
    /// enumerated.
    /// </summary>
    public static DirectoryEntries Enumerate(ReadOnlySpan<char> path)
    {
        var entries = new DirectoryEntries();
        unsafe
        {
            // SAFETY: state is StateSize bytes from calloc, and every offset
            // used is inside it; it's freed here on failure, or by Dispose.
            byte* state = (byte*)calloc(1, DirectoryEntries.StateSize);
            if (state == null)
            {
                entries.Error = FileError.Other;
                return entries;
            }
            var name = new Span<byte>(state + DirectoryEntries.PathAt, FileSystem.MaxPath);
            int length = FileSystem.ToC(path, name);
            if (length < 0)
            {
                free(state);
                entries.Error = FileError.InvalidPath;
                return entries;
            }
            FileSystem.ClearError();
            void* dir = FileSystem.opendir(state + DirectoryEntries.PathAt);
            if (dir == null)
            {
                entries.Error = FileSystem.LastError();
                free(state);
                return entries;
            }
            // Each entry's name goes after the directory's, for lstat when the
            // system doesn't say what kind of entry it is.
            if (!IsSeparator(name[length - 1]))
                name[length++] = (byte)'/';
            *(void**)state = dir;
            *(int*)(state + DirectoryEntries.PathLengthAt) = length;
            entries.State = state;
        }
        return entries;
    }

    internal static unsafe void FreeState(byte* state)
    {
        unsafe
        {
            // SAFETY: Enumerate's state, freed once: the caller forgets it.
            FileSystem.closedir(*(void**)state);
            free(state);
        }
    }

    // ---- helpers --------------------------------------------------------------

    static bool IsSeparator(byte b) => b == '/' || (b == '\\' && FileSystem.Backslashes);

    static FileError StatC(Span<byte> name, bool follow, out FileSystem.Status status)
    {
        unsafe
        {
            // SAFETY: callers pass '\0'-terminated names.
            fixed (byte* n = name)
                return FileSystem.Stat(n, follow, out status);
        }
    }

    // mkdir for one directory on the way; last: the one asked for.
    static FileError MakeOne(Span<byte> name, bool last)
    {
        FileSystem.ClearError();
        int result;
        unsafe
        {
            // SAFETY: '\0'-terminated; mkdir only reads it.
            fixed (byte* n = name)
                result = FileSystem.mkdir(n, 0x1FF); // 0777, less the umask
        }
        if (result == 0)
            return FileError.None;
        FileError error = FileSystem.LastError();
        // Already there, or the system won't say (WASI outside the directories
        // it was given): fine if it's a directory.
        if (StatC(name, true, out FileSystem.Status status) != FileError.None)
            return error;
        if ((status.Mode & FileSystem.TypeMask) == FileSystem.TypeDirectory)
            return FileError.None;
        return last ? FileError.AlreadyExists : FileError.NotADirectory;
    }

    static FileError RemoveDirectory(Span<byte> name)
    {
        FileSystem.ClearError();
        unsafe
        {
            // SAFETY: '\0'-terminated; rmdir only reads it.
            fixed (byte* n = name)
                return FileSystem.rmdir(n) == 0 ? FileError.None : FileSystem.LastError();
        }
    }

    static FileError RemoveFile(Span<byte> name)
    {
        FileSystem.ClearError();
        unsafe
        {
            // SAFETY: '\0'-terminated; remove only reads it.
            fixed (byte* n = name)
                return FileSystem.remove(n) == 0 ? FileError.None : FileSystem.LastError();
        }
    }

    // A symbolic link: remove, or on Windows, where a link to a directory is a
    // directory to the system, rmdir.
    static FileError RemoveLink(Span<byte> name)
    {
        FileError error = RemoveFile(name);
        if (error == FileError.None)
            return error;
        return RemoveDirectory(name) == FileError.None ? FileError.None : error;
    }

    // Deletes what's in the directory, then the directory. name has room for
    // MaxPath bytes; names are added after its first `length` and taken away.
    static FileError DeleteTree(Span<byte> name, int length)
    {
        FileError result = FileError.None;
        unsafe
        {
            // SAFETY: name is '\0'-terminated at length; an entry from readdir64
            // is good until the next call, and its name is read before that.
            void* dir;
            FileSystem.ClearError();
            fixed (byte* n = name)
                dir = FileSystem.opendir(n);
            if (dir == null)
                return FileSystem.LastError();
            name[length] = (byte)'/';
            while (true)
            {
                FileSystem.ClearError();
                byte* entry = FileSystem.readdir64(dir);
                if (entry == null)
                {
                    if (FileSystem.HasError)
                        result = FileSystem.LastError();
                    break;
                }
                var entryName = new ReadOnlySpan<byte>(entry + FileSystem.EntryName, FileSystem.Length(entry + FileSystem.EntryName));
                if (FileSystem.IsDotOrDotDot(entryName))
                    continue;
                int end = length + 1 + entryName.Length;
                if (end + 1 > FileSystem.MaxPath)
                {
                    result = FileError.InvalidPath;
                    break;
                }
                entryName.CopyTo(name.Slice(length + 1));
                name[end] = 0;
                byte type = entry[FileSystem.EntryType];
                if (type == FileSystem.TypeUnknownEntry)
                    type = EntryTypeOf(name);
                FileError error;
                if (type == FileSystem.TypeDirectoryEntry)
                    error = DeleteTree(name, end);
                else if (type == FileSystem.TypeLinkEntry)
                    error = RemoveLink(name);
                else
                    error = RemoveFile(name);
                if (error != FileError.None)
                {
                    result = error;
                    break;
                }
            }
            FileSystem.closedir(dir);
        }
        name[length] = 0;
        return result != FileError.None ? result : RemoveDirectory(name);
    }

    // What lstat says the entry is, as readdir64's d_type would.
    internal static byte EntryTypeOf(Span<byte> name)
    {
        if (StatC(name, false, out FileSystem.Status status) != FileError.None)
            return FileSystem.TypeUnknownEntry;
        return (status.Mode & FileSystem.TypeMask) switch
        {
            FileSystem.TypeDirectory => FileSystem.TypeDirectoryEntry,
            FileSystem.TypeLink => FileSystem.TypeLinkEntry,
            FileSystem.TypeFile => FileSystem.TypeFileEntry,
            _ => FileSystem.TypeUnknownEntry,
        };
    }
}

/// <summary>
/// The entries of a directory, from Directory.Enumerate, for foreach. Error
/// says why the directory couldn't be read, or None.
/// </summary>
public ref struct DirectoryEntries
{
    // The state, from calloc: the DIR*, the directory's path length, the
    // current name as UTF-16, and the directory's path with room for a name.
    internal const int PathLengthAt = 8, NameAt = 16, NameChars = 1024;
    internal const int PathAt = NameAt + NameChars * 2;
    internal const int PathRoom = FileSystem.MaxPath + 1024;
    internal const nuint StateSize = PathAt + PathRoom;

    internal unsafe byte* State;
    DirectoryEntry _current;

    /// <summary>Why the directory couldn't be opened or read to the end, or None.</summary>
    public FileError Error { get; internal set; }

    /// <summary>For foreach. The enumerator takes the open directory over, so this copy no longer needs disposing.</summary>
    public DirectoryEntries GetEnumerator()
    {
        DirectoryEntries enumerator = this;
        unsafe { State = null; } // SAFETY: the copy owns it now
        return enumerator;
    }

    /// <summary>The entry MoveNext moved to. Its Name is good until the next MoveNext.</summary>
    public DirectoryEntry Current => _current;

    /// <summary>Moves to the next entry; false when there are no more.</summary>
    public bool MoveNext()
    {
        unsafe
        {
            // SAFETY: State is Enumerate's, until Dispose; an entry from
            // readdir64 is good until the next call, and is copied before it.
            if (State == null)
                return false;
            while (true)
            {
                FileSystem.ClearError();
                byte* entry = FileSystem.readdir64(*(void**)State);
                if (entry == null)
                {
                    if (FileSystem.HasError)
                        Error = FileSystem.LastError();
                    return false;
                }
                var name = new ReadOnlySpan<byte>(entry + FileSystem.EntryName, FileSystem.Length(entry + FileSystem.EntryName));
                if (FileSystem.IsDotOrDotDot(name) || name.Length > NameChars)
                    continue;
                var chars = new Span<char>(State + NameAt, NameChars);
                int length = Utf8.Decode(name, chars);
                byte type = entry[FileSystem.EntryType];
                if (type == FileSystem.TypeUnknownEntry)
                    type = TypeFromPath(name);
                _current = new DirectoryEntry(chars.Slice(0, length), type);
                return true;
            }
        }
    }

    /// <summary>Closes the directory. foreach calls it; call it yourself only if you don't enumerate.</summary>
    public void Dispose()
    {
        unsafe
        {
            // SAFETY: State is Enumerate's, freed once: it's forgotten here.
            if (State == null)
                return;
            Directory.FreeState(State);
            State = null;
        }
    }

    // lstat on the directory's path and the name.
    byte TypeFromPath(ReadOnlySpan<byte> name)
    {
        unsafe
        {
            // SAFETY: the path buffer is PathRoom bytes of State.
            var path = new Span<byte>(State + PathAt, PathRoom);
            int length = *(int*)(State + PathLengthAt);
            if (length + name.Length + 1 > PathRoom)
                return FileSystem.TypeUnknownEntry;
            name.CopyTo(path.Slice(length));
            path[length + name.Length] = 0;
            byte type = Directory.EntryTypeOf(path);
            path[length] = 0;
            return type;
        }
    }
}

/// <summary>An entry in a directory: its name, and what kind of entry it is.</summary>
public readonly ref struct DirectoryEntry
{
    readonly byte _type;

    internal DirectoryEntry(ReadOnlySpan<char> name, byte type)
    {
        Name = name;
        _type = type;
    }

    /// <summary>The entry's name, without the directory's path. Good until the next entry.</summary>
    public ReadOnlySpan<char> Name { get; }

    /// <summary>Whether it's a directory. A symbolic link to one is IsSymbolicLink instead.</summary>
    public bool IsDirectory => _type == FileSystem.TypeDirectoryEntry;

    /// <summary>Whether it's an ordinary file.</summary>
    public bool IsFile => _type == FileSystem.TypeFileEntry;

    /// <summary>Whether it's a symbolic link (on Windows, also a junction).</summary>
    public bool IsSymbolicLink => _type == FileSystem.TypeLinkEntry;
}
