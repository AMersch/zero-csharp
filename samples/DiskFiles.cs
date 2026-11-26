// Test: files on a Commander X16's disk (the SD card; in the emulator, the
// folder x16emu was given with -fsroot). The files are the C library's, on top
// of the KERNAL's, with a position kept by cx16/runtime.c: a file opened for
// reading can be moved anywhere (by reading on, or opening it again), and a
// file for writing only appends. Files are binary: no byte is changed. A path
// may name its device: "#9:name". Samples/Files.cs is the same for other
// targets, with 64 KB arenas and random access to written files. Exit code =
// number of failed checks.
// Build: ./build.sh --cx16 DiskFiles

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

    const string Path = "disk.tmp";

    static bool SameBytes(Span<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i])
                return false;
        return true;
    }

    public static int Main()
    {
        File.Delete(Path);
        Check("a file that isn't there", !File.Exists(Path) && !File.OpenRead(Path).IsOpen);

        // Bytes come back as they went in, newlines and carriage returns too.
        byte[] raw = { 10, 13, 0xE9, 0, 65, 13, 10 };
        Check("WriteAllBytes", File.WriteAllBytes(Path, raw));
        Check("it exists now", File.Exists(Path));
        File input = File.OpenRead(Path);
        byte[] store = new byte[16];
        Span<byte> back = store;
        int n = input.Read(back);
        Check("the bytes that were written", n == raw.Length && SameBytes(back.Slice(0, n), raw));
        Check("read at the end gives 0", input.Read(back) == 0);
        Check("position", input.Position == raw.Length);

        // Moving around in a file that is read: back to the start, forward, to the end.
        Check("length", input.Length == raw.Length && input.Position == raw.Length);
        Check("seek back to the start", input.Seek(0) && input.Position == 0);
        Check("read from the start again", input.Read(back.Slice(0, 3)) == 3 && back[2] == 0xE9);
        Check("seek forward", input.Seek(5) && input.Read(back.Slice(0, 2)) == 2 && back[0] == 13 && back[1] == 10);
        Check("seek back into the middle", input.Seek(2) && input.Read(back.Slice(0, 1)) == 1 && back[0] == 0xE9);
        Check("seek past the end", !input.Seek(100));
        unsafe { input.Close(); }

        // Writing and appending.
        Check("WriteAllText", File.WriteAllText(Path, "first\n"));
        Check("AppendAllText", File.AppendAllText(Path, "second\n"));
        input = File.OpenRead(Path);
        Check("both lines are there", input.Length == 13);
        unsafe { input.Close(); }
        File output = File.Create(Path);
        Check("Create writes over what was there", output.IsOpen && output.Write(new byte[] { 1, 2, 3 }) == 3);
        unsafe { output.Close(); }
        input = File.OpenRead(Path);
        Check("and nothing of it is left", input.Length == 3);
        unsafe { input.Close(); }

        // The device in the path.
        // (The RAM bank is where this program's code may be: no disk call may change it.)
        int bank = CX16.Peek(0);
        input = File.OpenRead("#8:" + Path);
        Check("an explicit device 8", input.IsOpen);
        unsafe { input.Close(); }
        Check("a device that isn't there", !File.OpenRead("#9:" + Path).IsOpen);
        Check("a device that isn't a number", !File.OpenRead("#x:" + Path).IsOpen);
        Check("and the disk still works", File.Exists(Path));
        Check("the RAM bank is as before", CX16.Peek(0) == bank);

        Check("Delete", File.Delete(Path) && !File.Exists(Path));
        Check("Delete again", !File.Delete(Path));

        if (s_failures == 0)
            Out.Line("All " + s_checks.ToString() + " checks passed.");
        else
            Out.Line(s_failures.ToString() + " of " + s_checks.ToString() + " checks FAILED, the first: " + s_firstFailure);
        return s_failures;
    }
}
