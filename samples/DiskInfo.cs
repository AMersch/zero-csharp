// Test: File's errors, information about files, moving, copying and removing on a Commander X16's disk, with
// what lib/FileSystem.cs does there through cx16/runtime.c. ./test.sh --cx16 runs it against the emulator's host
// folder and a card image. Exit code = number of failed checks.
// Build: ./build.sh --cx16 DiskInfo

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

    static void Files()
    {
        File.Delete("a.tmp");
        File.Delete("b.tmp");
        Check("not found", !File.OpenRead("a.tmp", out FileError error).IsOpen && error == FileError.NotFound);
        Check("no information", File.GetInfo("a.tmp", out FileInfo info) == FileError.NotFound && !File.Exists("a.tmp"));
        Check("write", File.WriteAllText("a.tmp", "0123456789"));
        Check("create new over it", !File.CreateNew("a.tmp", out error).IsOpen && error == FileError.AlreadyExists);
        Check("information", File.GetInfo("a.tmp", out info) == FileError.None && info.Length == 10 && !info.IsDirectory
            && info.LastWriteUnixMilliseconds > 900000000000L && File.Exists("A.TMP"));
        Check("move", File.Move("a.tmp", "b.tmp") == FileError.None && !File.Exists("a.tmp") && Size("b.tmp") == 10);
        Check("move what isn't there", File.Move("a.tmp", "c.tmp") == FileError.NotFound);
        File.WriteAllText("a.tmp", "xy");
        Check("move onto a file", File.Move("a.tmp", "b.tmp") == FileError.AlreadyExists);
        Check("move over it", File.Move("a.tmp", "b.tmp", overwrite: true) == FileError.None && Size("b.tmp") == 2);
        Check("copy", File.Copy("b.tmp", "a.tmp") == FileError.None && Size("a.tmp") == 2);
        Check("remove", File.Remove("a.tmp") == FileError.None && File.Remove("a.tmp") == FileError.NotFound);
        File.Delete("b.tmp");

        // Removing a file of a folder must never take the one of the same name outside it.
        File.WriteAllText("keep.tmp", "kept");
        Check("a folder that isn't there", !File.Delete("no.dir/keep.tmp") && !File.Delete("//no.dir/:keep.tmp")
            && File.Move("no.dir/keep.tmp", "gone.tmp") != FileError.None && File.Exists("keep.tmp"));
        File.Delete("keep.tmp");
    }

    public static int Main()
    {
        Files();
        if (s_failures == 0)
            Out.Line("All checks passed.");
        else
            Out.Line(s_failures.ToString() + " of " + s_checks.ToString() + " checks FAILED, the first: " + s_firstFailure);
        return s_failures;
    }
}
