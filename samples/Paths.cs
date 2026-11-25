// Example: paths (lib/Path.cs): the parts of a path, joining paths, and
// tidying one up. Only text, so it runs everywhere; Windows, DOS and UEFI also
// take '\' between names, and the checks for those run there.
// Exit code = number of failed checks.
// Build: ./build.sh Paths

using System;

namespace PathsSample;

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

    static void Same(string name, ReadOnlySpan<char> actual, string expected)
    {
        bool ok = actual.SequenceEqual(expected.AsSpan());
        Check(name, ok);
        if (!ok)
        {
            Out.Write("    got \"");
            Out.Write(actual);
            Out.Line($"\", expected \"{expected}\"");
        }
    }

    // A path with '/' written as the system's separator.
    static string S(string path)
    {
        if (Path.Separator == '/')
            return path;
        var b = new System.Text.StringBuilder();
        foreach (char c in path)
            b.Append(c == '/' ? '\\' : c);
        return b.ToString();
    }

    static string Repeat(char c, int count)
    {
        var b = new System.Text.StringBuilder();
        for (int i = 0; i < count; i++)
            b.Append(c);
        return b.ToString();
    }

    public static int Main()
    {
        // docs:parts
        // The parts of a path are slices of it: nothing is allocated.
        ReadOnlySpan<char> path = "logs/2026/today.txt";
        ReadOnlySpan<char> name = Path.GetFileName(path);      // today.txt
        ReadOnlySpan<char> stem =
            Path.GetFileNameWithoutExtension(path);             // today
        ReadOnlySpan<char> extension = Path.GetExtension(path); // .txt
        ReadOnlySpan<char> folder = Path.GetDirectoryName(path); // logs/2026
        // docs:end
        Same("file name", name, "today.txt");
        Same("without extension", stem, "today");
        Same("extension", extension, ".txt");
        Same("directory", folder, "logs/2026");
        Same("name ending in /", Path.GetFileName("a/b/"), "");
        Same("name alone", Path.GetFileName("c.txt"), "c.txt");
        Same("name of /", Path.GetFileName("/"), "");
        Same("last extension", Path.GetExtension("archive.tar.gz"), ".gz");
        Same("dot file's extension", Path.GetExtension("a/.profile"), ".profile");
        Same("dot file's stem", Path.GetFileNameWithoutExtension(".profile"), "");
        Same("trailing dot", Path.GetExtension("file."), "");
        Same("dot in a directory", Path.GetExtension("a.d/file"), "");
        Check("HasExtension", Path.HasExtension("a.txt") && !Path.HasExtension("a.d/b"));
        Same("directory of a name", Path.GetDirectoryName("c.txt"), "");
        Same("directory of /a", Path.GetDirectoryName("/a"), "/");
        Same("directory of /", Path.GetDirectoryName("/"), "");
        Same("doubled separator", Path.GetDirectoryName("a//b"), "a");
        Same("trailing separator", Path.GetDirectoryName("a/b/"), "a/b");
        Check("/a is rooted", Path.IsRooted("/a"));
        Check("a isn't rooted", !Path.IsRooted("a") && !Path.IsRooted(""));
        Same("root of /a", Path.GetRoot("/a"), "/");
        Section("Parts");

        // docs:combine
        // Combine puts the system's separator between two paths, unless the
        // first ends with one; a rooted second path replaces the first.
        string log = Path.Combine("logs", "today.txt"); // logs/today.txt
        string other = Path.Combine("logs", "/tmp/x");   // /tmp/x
        // docs:end
        Same("combine", log, S("logs/today.txt"));
        Same("rooted second", other, "/tmp/x");
        Same("first ends with /", Path.Combine("a/", "b"), "a/b");
        Same("empty first", Path.Combine("", "b"), "b");
        Same("empty second", Path.Combine("a", ""), "a");
        Same("three", Path.Combine("a", "b", "c.txt"), S("a/b/c.txt"));
        Span<char> small = stackalloc char[4];
        Check("TryCombine too small", !Path.TryCombine("abc", "def", small, out int none) && none == 0);
        Check("TryCombine", Path.TryCombine("a", "b", small, out int written)
            && small.Slice(0, written).SequenceEqual(S("a/b").AsSpan()));
        Same("change extension", Path.ChangeExtension("a/b.txt", ".md"), "a/b.md");
        Same("extension without dot", Path.ChangeExtension("a/b.txt", "md"), "a/b.md");
        Same("add an extension", Path.ChangeExtension("a.d/b", "x"), "a.d/b.x");
        Same("remove the extension", Path.ChangeExtension("a/b.txt", ""), "a/b");
        Section("Combine");

        // docs:normalize
        // Normalize tidies a path from its text: "." goes, ".." takes the name
        // before it away, and separators become the system's.
        string tidy = Path.Normalize("logs/./old/../today.txt"); // logs/today.txt
        // docs:end
        Same("normalize", tidy, S("logs/today.txt"));
        Same("leading ..", Path.Normalize("../a/../.."), S("../.."));
        Same("above the root", Path.Normalize("/../a"), S("/a"));
        Same("empty", Path.Normalize(""), ".");
        Same("all gone", Path.Normalize("a/.."), ".");
        Same("doubled and trailing", Path.Normalize("a//b/"), S("a/b"));
        Same("root", Path.Normalize("/"), S("/"));
        Same("dot dot dir", Path.Normalize("a/../../b"), S("../b"));
        Same("a name like ..", Path.Normalize("a/..b/c"), S("a/..b/c"));
        string x = Repeat('x', 300), y = Repeat('y', 300);
        Same("long", Path.Normalize(x + "/./" + y), S(x + "/" + y));
        Section("Normalize");

        if (Path.Separator == '\\')
        {
            Same("backslash file name", Path.GetFileName("C:\\x\\y.txt"), "y.txt");
            Same("drive directory", Path.GetDirectoryName("C:\\x\\y.txt"), "C:\\x");
            Same("drive root's directory", Path.GetDirectoryName("C:\\"), "");
            Same("drive-relative name", Path.GetFileName("C:y.txt"), "y.txt");
            Check("drive is rooted", Path.IsRooted("C:x") && Path.IsRooted("\\x"));
            Same("drive root", Path.GetRoot("C:\\x"), "C:\\");
            Same("share root", Path.GetRoot("\\\\server\\share\\a"), "\\\\server\\share\\");
            Same("drive normalize", Path.Normalize("C:/a/../b"), "C:\\b");
            Same("share normalize", Path.Normalize("\\\\server\\share\\a\\..\\..\\b"), "\\\\server\\share\\b");
            Same("drive combine", Path.Combine("C:", "x"), "C:x");
            Same("mixed separators", Path.Normalize("a\\b/c"), "a\\b\\c");
            Section("Windows paths");
        }
        else
        {
            Same("'\\' is part of a name", Path.GetFileName("a\\b"), "a\\b");
            Check("'\\' doesn't root", !Path.IsRooted("\\a") && !Path.IsRooted("C:a"));
            Section("Unix paths");
        }

        return s_failures;
    }
}
