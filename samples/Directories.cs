// Example: directories (lib/Directory.cs), and what lib/File.cs can do with
// a file beyond reading and writing it: information, copying, moving, and
// errors that say what went wrong. Works in dirs.tmp in the current directory
// and deletes it again. Linux, macOS, Windows and WebAssembly.
// Exit code = number of failed checks.
// Build: ./build.sh Directories

using System;

namespace DirectoriesSample;

public static class Program
{
    static int s_failures;
    static int s_sectionChecks;
    static int s_sectionFailures;

    static void Section(string section)
    {
        Out.Line($"{(s_sectionFailures == 0 ? "PASS" : "FAIL")} {section} ({s_sectionChecks} checks)");
        s_sectionChecks = 0;
        s_sectionFailures = 0;
    }

    static void Check(string name, bool ok)
    {
        s_sectionChecks++;
        if (!ok)
        {
            s_failures++;
            s_sectionFailures++;
            Out.Line($"  failed: {name}");
        }
    }

    static void Is(string name, FileError actual, FileError expected)
    {
        Check(name, actual == expected);
        if (actual != expected)
            Out.Line($"    got {(int)actual}, expected {(int)expected}");
    }

    // Whether the file holds exactly this text.
    static bool Holds(string path, string expected)
    {
        Arena arena = Arena.Create(4096);
        bool same = File.TryReadAllText(arena, path, out string text) && text == expected;
        unsafe { arena.Destroy(); } // SAFETY: text isn't used after this
        return same;
    }

    public static int Main()
    {
        Directory.Delete("dirs.tmp", recursive: true); // left by an earlier run

        // docs:create
        // Create makes the directories on the way too; one that's already
        // there is fine. Failures come back as a FileError.
        FileError error = Directory.Create("dirs.tmp/logs/2026");
        if (error != FileError.None)
            Out.Line($"couldn't create it: {(int)error}");
        // docs:end
        Is("create", error, FileError.None);
        Check("each exists", Directory.Exists("dirs.tmp") && Directory.Exists("dirs.tmp/logs")
            && Directory.Exists("dirs.tmp/logs/2026"));
        Is("create again", Directory.Create("dirs.tmp/logs/2026/"), FileError.None);
        File.WriteAllText("dirs.tmp/notes.txt", "hello");
        Is("a file has the name", Directory.Create("dirs.tmp/notes.txt"), FileError.AlreadyExists);
        Is("a file on the way", Directory.Create("dirs.tmp/notes.txt/x"), FileError.NotADirectory);
        Check("File.Exists: no for a directory", !File.Exists("dirs.tmp/logs"));
        Check("File.Exists: yes for a file", File.Exists("dirs.tmp/notes.txt"));
        Check("Directory.Exists: no for a file", !Directory.Exists("dirs.tmp/notes.txt"));
        Check("Directory.Exists: no for nothing", !Directory.Exists("dirs.tmp/none"));
        Is("an empty path", Directory.Create(""), FileError.InvalidPath);
        Section("Create");

        // docs:info
        // GetInfo: a file's length, whether it's a directory, and when it was
        // last written (milliseconds since 1970, as Clock.UnixMilliseconds).
        File.GetInfo("dirs.tmp/notes.txt", out FileInfo info);
        long age = Clock.UnixMilliseconds - info.LastWriteUnixMilliseconds;
        // docs:end
        Check("length", info.Length == 5 && !info.IsDirectory);
        Check("written just now", age > -86_400_000 && age < 86_400_000);
        Is("a directory", File.GetInfo("dirs.tmp/logs", out FileInfo folder), FileError.None);
        Check("is a directory", folder.IsDirectory);
        Is("missing", File.GetInfo("dirs.tmp/none", out _), FileError.NotFound);
        Section("Info");

        File.WriteAllText("dirs.tmp/snö☃.txt", "x");
        // docs:enumerate
        // Enumerate lists a directory for foreach: each entry's name (a span,
        // good until the next entry) and kind, without "." and "..".
        int files = 0, folders = 0;
        bool sawSnow = false;
        DirectoryEntries entries = Directory.Enumerate("dirs.tmp");
        foreach (DirectoryEntry entry in entries)
        {
            if (entry.IsDirectory)
                folders++;
            else if (entry.IsFile)
                files++;
            sawSnow |= entry.Name.SequenceEqual("snö☃.txt".AsSpan());
        }
        // docs:end
        Is("no error", entries.Error, FileError.None);
        Check("two files, one directory", files == 2 && folders == 1);
        Check("a name outside ASCII", sawSnow);
        entries.Dispose(); // foreach took it over: nothing left to close
        DirectoryEntries missing = Directory.Enumerate("dirs.tmp/none");
        Is("missing directory", missing.Error, FileError.NotFound);
        int any = 0;
        foreach (DirectoryEntry entry in missing)
            any++;
        Check("no entries", any == 0);
        Is("a file isn't a directory", Directory.Enumerate("dirs.tmp/notes.txt").Error, FileError.NotADirectory);
        DirectoryEntries unread = Directory.Enumerate("dirs.tmp");
        unread.Dispose(); // not enumerated: Dispose closes it
        Section("Enumerate");

        // docs:copy-move
        // Copy and Move refuse to replace a file unless told to overwrite.
        error = File.Copy("dirs.tmp/notes.txt", "dirs.tmp/copy.txt");
        FileError again = File.Copy("dirs.tmp/notes.txt", "dirs.tmp/copy.txt");
        // again is FileError.AlreadyExists
        File.Move("dirs.tmp/copy.txt", "dirs.tmp/logs/moved.txt");
        // docs:end
        Is("copy", error, FileError.None);
        Is("copy onto a file", again, FileError.AlreadyExists);
        Check("moved", !File.Exists("dirs.tmp/copy.txt") && Holds("dirs.tmp/logs/moved.txt", "hello"));
        Is("copy with overwrite", File.Copy("dirs.tmp/snö☃.txt", "dirs.tmp/logs/moved.txt", overwrite: true), FileError.None);
        Check("overwritten", Holds("dirs.tmp/logs/moved.txt", "x"));
        Is("copy nothing", File.Copy("dirs.tmp/none", "dirs.tmp/x"), FileError.NotFound);
        Is("copy a directory", File.Copy("dirs.tmp/logs", "dirs.tmp/x"), FileError.IsADirectory);
        Is("move onto a file", File.Move("dirs.tmp/notes.txt", "dirs.tmp/logs/moved.txt"), FileError.AlreadyExists);
        Is("move with overwrite", File.Move("dirs.tmp/logs/moved.txt", "dirs.tmp/notes.txt", overwrite: true), FileError.None);
        Check("moved over", Holds("dirs.tmp/notes.txt", "x") && !File.Exists("dirs.tmp/logs/moved.txt"));
        Is("move a directory with File", File.Move("dirs.tmp/logs", "dirs.tmp/x"), FileError.IsADirectory);
        Section("Copy and move");

        // docs:errors
        // OpenRead, Create and OpenAppend can say why they failed.
        File file = File.OpenRead("dirs.tmp/none", out error);
        // file.IsOpen is false, and error is FileError.NotFound
        // docs:end
        Check("not open", !file.IsOpen);
        Is("not found", error, FileError.NotFound);
        Check("a directory isn't opened", !File.OpenRead("dirs.tmp/logs", out error).IsOpen);
        Is("is a directory", error, FileError.IsADirectory);
        Check("CreateNew on a file", !File.CreateNew("dirs.tmp/notes.txt", out error).IsOpen);
        Is("already exists", error, FileError.AlreadyExists);
        file = File.CreateNew("dirs.tmp/new.txt", out error);
        Check("CreateNew", file.IsOpen && error == FileError.None);
        unsafe { file.Close(); } // SAFETY: not used again
        Is("remove a directory", File.Remove("dirs.tmp/logs"), FileError.IsADirectory);
        Is("remove nothing", File.Remove("dirs.tmp/none"), FileError.NotFound);
        Is("remove", File.Remove("dirs.tmp/new.txt"), FileError.None);
        Section("Errors");

        Is("delete a full directory", Directory.Delete("dirs.tmp/logs"), FileError.NotEmpty);
        Is("delete a file", Directory.Delete("dirs.tmp/notes.txt"), FileError.NotADirectory);
        Is("move a directory", Directory.Move("dirs.tmp/logs", "dirs.tmp/old"), FileError.None);
        Check("moved directory", Directory.Exists("dirs.tmp/old/2026") && !Directory.Exists("dirs.tmp/logs"));
        Is("move onto a directory", Directory.Move("dirs.tmp/old", "dirs.tmp/old/2026"), FileError.AlreadyExists);
        Is("delete an empty one", Directory.Delete("dirs.tmp/old/2026"), FileError.None);

        string start = Directory.GetCurrentDirectory();
        Check("current directory is rooted", start.Length > 0 && Path.IsRooted(start));
        Is("change directory", Directory.SetCurrentDirectory("dirs.tmp"), FileError.None);
        Check("in dirs.tmp", File.Exists("notes.txt")
            && Path.GetFileName(Directory.GetCurrentDirectory()).SequenceEqual("dirs.tmp".AsSpan()));
        Check("full path", Path.GetFullPath("old/../notes.txt") == Path.Combine(Directory.GetCurrentDirectory(), "notes.txt"));
        Is("change back", Directory.SetCurrentDirectory(start), FileError.None);
        Is("change to nothing", Directory.SetCurrentDirectory("dirs.tmp/none"), FileError.NotFound);

        // docs:delete
        // Delete with recursive: true deletes everything inside first.
        error = Directory.Delete("dirs.tmp", recursive: true);
        // docs:end
        Is("delete it all", error, FileError.None);
        Check("gone", !Directory.Exists("dirs.tmp"));
        Is("delete nothing", Directory.Delete("dirs.tmp"), FileError.NotFound);
        Section("Delete");

        return s_failures;
    }
}
