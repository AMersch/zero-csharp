// Example: a growable list in the Commander X16's banked RAM, from C#. 5,000
// 12-byte structs, grown from a capacity of 64 (in place, then moved when
// something else was handed out after the list), removal, ranges, and a list
// that is too big for the banks. Exit code = number of failed checks.
// Build: ./build.sh --cx16 BankedListDemo    Run: ./run.sh --cx16 BankedListDemo
//
using System;

public struct Vec
{
    public int X, Y, Z;
}

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

    public static int Main()
    {
        int mark = Banks.Mark();

        // A growable list: 5,000 structs, from a capacity of 64 up.
        BankedList<Vec> list = BankedList<Vec>.Create();
        for (int i = 0; i < 5000; i++)
            list.Add(new Vec { X = i, Y = i * 2, Z = 1 });
        Check("list count", list.Count == 5000 && list.Capacity == 8192);
        bool ok = true;
        for (int i = 0; i < list.Count && ok; i++)
            ok = list[i].X == i && list[i].Y == i * 2;
        Check("list contents after growing", ok);
        // Growing in place: the list was the last thing handed out, so only its
        // own header and elements are in use.
        Check("grown in place", Banks.Used == 12 + list.Capacity * 12);

        list[100] = new Vec { X = -5 };
        Check("list set", list[100].X == -5 && list[101].X == 101);

        // Something handed out after the list stops it growing in place: its
        // elements are copied to a new region.
        BankedList<int> small = BankedList<int>.Create(4);
        Banks.Alloc(100);
        for (int i = 0; i < 10; i++)
            small.Add(i * i);
        Check("moved by growth", small.Count == 10 && small[3] == 9 && small[9] == 81);

        Check("remove", small.RemoveAt(0) && small.Count == 9 && small[0] == 1 && small[8] == 81);
        Check("remove out of range", !small.RemoveAt(9) && !small.RemoveAt(-1));
        Span<int> three = stackalloc int[3];
        Check("list read", small.Read(2, three) && three[0] == 9 && three[2] == 25);
        Check("list read past the end", !small.Read(8, three));
        Check("add a range", small.AddRange(new int[] { 100, 200, 300 }) && small.Count == 12
            && small[9] == 100 && small[11] == 300 && small[0] == 1);
        small.Clear();
        Check("clear", small.Count == 0 && small.Capacity >= 12);

        int before = Banks.Used;
        BankedList<byte> toobig = BankedList<byte>.Create(Banks.Available);
        Check("a refused list", !toobig.IsCreated && toobig.Count == 0 && !toobig.Add(1)
            && Banks.Used == before);
        Banks.Release(mark);


        if (s_failures == 0)
            Out.Line($"All {s_checks} checks passed.");
        else
            Out.Line($"{s_failures} of {s_checks} checks FAILED, the first: {s_firstFailure}");
        return s_failures;
    }
}
