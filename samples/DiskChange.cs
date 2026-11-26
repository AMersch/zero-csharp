// Test: a file on a Commander X16's disk that is read and written at once, through FileStream: CMDR-DOS's ",M"
// with its position command, behind cx16/runtime.c. ./test.sh --cx16 runs it against the emulator's host folder
// and a card image. Exit code = number of failed checks.
// Build: ./build.sh --cx16 DiskChange

using System;
using System.IO;

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

    // A file that is there, read and written at once.
    static void Change()
    {
        File.WriteAllText("c.tmp", "0123456789");
        byte[] store = new byte[16];
        Span<byte> four = new Span<byte>(store, 0, 4);
        using (var file = new FileStream("c.tmp", FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1))
        {
            Check("length", file.Length == 10);
            Check("read", file.Read(four) == 4 && store[0] == '0' && store[3] == '3');
            file.WriteByte((byte)'x');
            Check("write in the middle, read on", file.ReadByte() == '5' && file.Position == 6);
            file.Position = 1;
            Check("back", file.ReadByte() == '1');
            file.Position = 8;
            Check("to the end", file.Read(four) == 2 && store[1] == '9' && file.Read(four) == 0);
            file.WriteByte((byte)'+');
            file.Position = 13;
            file.WriteByte((byte)'!');
            Check("past the end", file.Length == 14);
        }
        Span<byte> all = store;
        File back = File.OpenRead("c.tmp");
        int n = back.Read(all);
        Check("what was written", n == 14 && store[4] == 'x' && store[5] == '5' && store[10] == '+' && store[11] == 0 && store[13] == '!');
        if (back.IsOpen)
            unsafe { back.Close(); } // SAFETY: back isn't used after this.
        File.Delete("c.tmp");
        // (TryOpen, as an exception can't be caught on a 6502.)
        bool opened = FileStream.TryOpen("c.tmp", FileMode.Open, FileAccess.ReadWrite, out FileStream stream, out FileError why, 1);
        Check("a file that isn't there isn't made", !opened && why == FileError.NotFound && !File.Exists("c.tmp"));
    }

    public static int Main()
    {
        Change();
        if (s_failures == 0)
            Out.Line("All checks passed.");
        else
            Out.Line(s_failures.ToString() + " of " + s_checks.ToString() + " checks FAILED, the first: " + s_firstFailure);
        return s_failures;
    }
}
