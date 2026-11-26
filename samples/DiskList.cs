// Test: listing folders and commands to the drive on a Commander X16 (lib/Disk.cs), with files whose names
// are longer than the old drives' 16 characters. It makes a few files and a folder, finds them in the
// listing, and removes them. ./test.sh --cx16 runs it against the emulator's host folder and, where there is
// an SD card image next to the emulator, against that, which is the drive of a real X16. Exit code = number
// of failed checks.
// Build: ./build.sh --cx16 DiskList

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

    static readonly char[] s_name = new char[80];

    static bool Is(int length, string name)
    {
        if (length != name.Length)
            return false;
        for (int i = 0; i < length; i++)
            if (s_name[i] != name[i])
                return false;
        return true;
    }

    // How often a name is in a folder's listing, with this size (-1: any), as a file or a folder.
    static int Found(string folder, string name, int size, bool isFolder)
    {
        int found = 0;
        if (!Disk.OpenDirectory(folder))
            return -1;
        while (Disk.Next())
            if (Is(Disk.CopyName(s_name), name) && Disk.IsFolder == isFolder && (size < 0 || Disk.Size == size))
                found++;
        return found;
    }

    const string Long = "a name of forty characters, no more.data";       // has a comma: refused
    const string LongName = "LongName-of-more-than-sixteen.data";
    const string Short = "list1.tmp";

    public static int Main()
    {
        File.Delete(Short);
        File.Delete(LongName);
        Disk.Command("rd:list.dir");

        Check("a name with a comma", !File.WriteAllText(Long, "x"));
        Check("not there yet", !File.Exists(Short) && !File.Exists(LongName));
        Check("looking didn't make it", Found("", Short, -1, false) == 0);

        Check("write", File.WriteAllText(Short, "hello"));
        Check("write a long name", File.WriteAllBytes(LongName, new byte[300]));
        Check("it exists", File.Exists(Short) && File.Exists(LongName));
        Check("listed with its size", Found("", Short, 5, false) == 1);
        Check("a long name, listed", Found("", LongName, 300, false) == 1);
        Check("the program itself", Found("", "none such", -1, false) == 0);

        Check("make a folder", Disk.Command("md:list.dir") < 2);
        Check("again: it is there", Disk.Command("md:list.dir") >= 20);
        Check("the folder is listed", Found("", "list.dir", -1, true) == 1);

        Check("stop in the middle", Disk.OpenDirectory() && Disk.Next());
        Check("its name, cut to the buffer", Disk.CopyName(s_name) > 0 && Disk.CopyName(new char[1]) == 1);
        Disk.CloseDirectory();
        Check("nothing after closing", !Disk.Next());

        Check("all of it was read", Disk.IsComplete);

        // The loop may use files: the folder was read before it.
        File input = File.OpenRead(Short);
        byte[] store = new byte[8];
        Span<byte> two = new Span<byte>(store, 0, 2);
        bool open = input.IsOpen && input.Read(two) == 2;
        int count = 0;
        Disk.OpenDirectory();
        while (Disk.Next())
        {
            count++;
            if (count == 2)
                open &= input.Read(two) == 2 && store[0] == (byte)'l' && store[1] == (byte)'l';
        }
        open &= input.Read(store) == 1 && store[0] == (byte)'o' && input.Read(store) == 0;
        Check("an open file, read inside the loop", open && count >= 3);
        if (input.IsOpen)
            unsafe { input.Close(); } // SAFETY: input isn't used after this.

        // Files opened, read and deleted while going through the folder.
        int seen = 0, deleted = 0, entries = 0;
        File.WriteAllText("list2.tmp", "two");
        Disk.OpenDirectory();
        while (Disk.Next())
        {
            entries++;
            int length = Disk.CopyName(s_name);
            if (Is(length, Short) || Is(length, "list2.tmp") || Is(length, LongName))
            {
                string name = Is(length, Short) ? Short : Is(length, LongName) ? LongName : "list2.tmp";
                if (File.Exists(name))
                    seen++;
                if (name != Short && name != LongName && File.Delete(name))
                    deleted++;
            }
        }
        Check("files used and deleted inside the loop", seen == 3 && deleted == 1 && entries >= 4 && !File.Exists("list2.tmp"));

        Check("no such device", !Disk.OpenDirectory("#11:") && Disk.Command("#11:i") == -1);
        Check("and the card still works", File.Exists(Short) && Found("", Short, 5, false) == 1);

        Check("remove the files", File.Delete(Short) && File.Delete(LongName));
        Check("remove the folder", Disk.Command("rd:list.dir") < 2);
        Check("gone", Found("", Short, -1, false) == 0 && Found("", LongName, -1, false) == 0 && Found("", "list.dir", -1, true) == 0);
        Check("deleting what isn't there", !File.Delete(Short));

        if (s_failures == 0)
            Out.Line("All " + s_checks.ToString() + " checks passed.");
        else
            Out.Line(s_failures.ToString() + " of " + s_checks.ToString() + " checks FAILED, the first: " + s_firstFailure);
        return s_failures;
    }
}
