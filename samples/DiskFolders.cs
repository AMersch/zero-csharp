// Test: File and Directory on a Commander X16's SD card, with what lib/FileSystem.cs does there through
// cx16/runtime.c: folders (samples/DiskInfo.cs has the files, DiskChange.cs the ones that are read and
// written at once). ./test.sh --cx16 runs it against the emulator's host folder and a card image; a few things only
// hold on a card, which it finds out by itself (the host folder lists its root for any folder). Exit code =
// number of failed checks.
// Build: ./build.sh --cx16 DiskFolders

using System;

public static class Program
{
    static int s_failures;
    static int s_checks;
    static string s_firstFailure;

    static void Check(string name, bool ok)
    {
        s_checks++;
        if (!ok)
        {
            s_failures++;
            s_firstFailure ??= name;
        }
    }

    static int Size(string path) => File.GetInfo(path, out FileInfo info) == FileError.None ? (int)info.Length : -1;

    static int Entries(string folder, out int folders)
    {
        int files = 0;
        folders = 0;
        DirectoryEntries entries = Directory.Enumerate(folder);
        foreach (DirectoryEntry entry in entries)
        {
            if (entry.IsDirectory)
                folders++;
            else
                files++;
        }
        return entries.Error == FileError.None ? files : -1;
    }

    // A folder with folders in it (on a card).
    static void Nested()
    {
        int folders;
        Check("a folder in a folder", Directory.Create("d.dir/in/deeper") == FileError.None && Directory.Exists("d.dir/in/deeper"));
        File.WriteAllText("d.dir/in/two.tmp", "22");
        Check("what is in it", Entries("d.dir", out folders) == 1 && folders == 1);
        Check("a folder that isn't empty", Directory.Delete("d.dir") == FileError.NotEmpty);
        Check("no such folder", Entries("none.dir", out folders) == -1);
        Check("move into a folder", File.Move("d.dir/one.tmp", "d.dir/in/one.tmp") == FileError.None && Size("d.dir/in/one.tmp") == 1);
    }

    static void Inside()
    {
        int folders;
        // Wherever the program was started from, the card's root or a folder of it.
        string start = Directory.GetCurrentDirectory();
        string dir = (start == "/" ? "" : start) + "/d.dir";
        Check("go in", Directory.SetCurrentDirectory("d.dir/in") == FileError.None);
        Check("where", Directory.GetCurrentDirectory() == dir + "/in");
        Check("from here", File.Exists("two.tmp") && File.Exists(dir + "/in/two.tmp") && Entries(".", out folders) == 2 && folders == 1);
        Disk.OpenDirectory("deeper");
        Check("a folder from here, for Disk", !Disk.Next());
        Disk.OpenDirectory(dir);
        Check("one from the root", Disk.Next());
        Check("back", Directory.SetCurrentDirectory(start) == FileError.None && Directory.GetCurrentDirectory() == start);
        Check("move a folder", Directory.Move("d.dir/in", "d.dir/out") == FileError.None && Size("d.dir/out/two.tmp") == 2);
    }

    static void Folders()
    {
        Check("not there yet", !Directory.Exists("d.dir"));
        Check("create", Directory.Create("d.dir") == FileError.None && Directory.Exists("d.dir") && !File.Exists("d.dir"));
        Check("again", Directory.Create("d.dir") == FileError.None);
        Check("a file in it", File.WriteAllText("d.dir/one.tmp", "1") && File.Exists("d.dir/one.tmp"));
        // The host folder lists its root, whatever folder is asked for: the rest needs a card.
        bool card = Size("d.dir/one.tmp") == 1 && Entries("d.dir", out int folders) == 1 && folders == 0;
        if (card)
        {
            Nested();
            Inside();
        }
        Check("delete with what is in it", !card || Directory.Delete("d.dir", recursive: true) == FileError.None);
        Check("gone", !card || !Directory.Exists("d.dir"));
        if (!card)
        {
            File.Delete("d.dir/one.tmp");
            Directory.Delete("d.dir");
        }
    }

    public static int Main()
    {
        Folders();
        if (s_failures == 0)
            Out.Line("All checks passed.");
        else
            Out.Line(s_failures.ToString() + " of " + s_checks.ToString() + " checks FAILED, the first: " + s_firstFailure);
        return s_failures;
    }
}
