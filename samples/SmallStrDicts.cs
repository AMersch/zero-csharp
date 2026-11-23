// Test: StrDict<TValue>, the dictionary with string keys compared by their
// text, sized for a small machine. The other half of SmallDicts.cs;
// Collections.cs checks more and doesn't fit in a Commodore 64
// (./build.sh --c64). It runs on every target.
// Exit code = number of failed checks.
// Build: ./build.sh SmallStrDicts

using System;

namespace SmallStrDictsSample;

public static class Program
{
    static int s_failures;
    static int s_sectionChecks;
    static int s_sectionFailures;

    static void End(string section)
    {
        Out.Write(s_sectionFailures == 0 ? "PASS " : "FAIL ");
        Out.Write(section);
        Out.Write(" (");
        Out.Write(s_sectionChecks);
        Out.Line(" checks)");
        s_failures += s_sectionFailures;
        s_sectionChecks = 0;
        s_sectionFailures = 0;
    }

    static void Check(string name, bool ok)
    {
        s_sectionChecks++;
        if (!ok)
        {
            s_sectionFailures++;
            Out.Write("  failed: ");
            Out.Line(name);
        }
    }

    public static int Main()
    {
        Arena arena = Arena.Create(1024);
        StrDict<int> counts = StrDict<int>.Create(arena, 8);
        foreach (ReadOnlySpan<char> word in "the cat and the hat and the bat".Split(' '))
        {
            counts.TryGet(word, out int count);
            counts.Set(word, count + 1);
        }
        Check("StrDict counts words", counts.Count == 5 && counts.TryGet("the", out int the) && the == 3
            && counts.TryGet("and", out int and) && and == 2 && !counts.ContainsKey("dog"));
        counts.Set("the", 30);
        Check("Set replaces", counts.Count == 5 && counts.TryGet("the", out the) && the == 30);
        Check("Remove", counts.Remove("cat") && counts.Count == 4 && !counts.Remove("cat"));
        StrDict<int> none = default;
        Check("a dictionary that wasn't created", none.Count == 0 && !none.Set("x", 1) && !none.ContainsKey("x"));
        End("StrDict");

        Out.Line();
        if (s_failures == 0)
            Out.Line("All checks passed.");
        else
        {
            Out.Write(s_failures);
            Out.Line(" checks FAILED.");
        }
        return s_failures;
    }
}
