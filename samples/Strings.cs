// Example: string support in zero mode. The core library (zerocore) has ==, +,
// string switch and static string fields; lib/Str.cs, lib/StrBuilder.cs and
// lib/Out.cs add span operations, parsing, $"..." formatting and arena strings.
// Exit code = number of failed checks.
// Build: ./build.sh Strings

using System;

namespace StringsSample;

public static class Program
{
    static int s_failures;
    static int s_sectionChecks;
    static int s_sectionFailures;

    static void End(string section)
    {
        Out.Line($"{(s_sectionFailures == 0 ? "PASS" : "FAIL")} {section} ({s_sectionChecks} checks)");
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
            Out.Line($"  failed: {name}");
        }
    }

    static void Expect(string name, ReadOnlySpan<char> got, ReadOnlySpan<char> want)
    {
        s_sectionChecks++;
        if (!got.SequenceEqual(want))
        {
            s_sectionFailures++;
            Out.Line($"  failed: {name}: got \"{got}\", want \"{want}\"");
        }
    }

    static bool Parses(string text, long want) => Str.TryParse(text, out long value) && value == want;

    static bool Rejects(string text) => !Str.TryParse(text, out long _);

    // Exact: Str.TryParse rounds correctly, like the C# compiler does for literals.
    static bool ParsesDouble(string text, double want) => Str.TryParse(text, out double got) && got == want;

    // NaN is the only value that is neither <= 0 nor > 0.
    static bool IsNaN(double x) => !(x <= 0) && !(x > 0);

    static bool RejectsDouble(string text) => !Str.TryParse(text, out double _);

    static int Classify(string word)
    {
        switch (word)
        {
            case "alpha": return 1;
            case "beta": return 2;
            case "gamma": return 3;
            case "delta": return 4;
            case "epsilon": return 5;
            case "zeta": return 6;
            case "eta": return 7;
            default: return 0;
        }
    }

    // Static fields of reference types need the core library to allocate their
    // storage at startup (zerocore/Runtime/Startup.cs).
    static string s_label = "initial";
    static string[] s_words = { "zero", "one" };

    // The arena every string in this sample is built in. A static, so the
    // helper methods can reach it; an Arena is a handle, cheap to pass or store.
    static Arena s_arena;

    public static int Main()
    {
        // ---- before the arena exists: allocating calls fail cleanly ----
        Check("Str.Format returns null", Str.Format(s_arena, $"x={1}") is null);
        Check("StrBuilder not created", !StrBuilder.Create(s_arena).IsCreated);
        Check("default StrBuilder rejects Append", !default(StrBuilder).Append("x"));
        Check("span operations still work", "abc".SequenceEqual("abc"));
        End("no arena: allocating calls fail cleanly");

        s_arena = Arena.Create(1 << 16);
        if (!s_arena.IsCreated)
        {
            Out.Line("Arena.Create failed");
            return 1;
        }

        // ---- read-only operations: no allocation ----
        Check("Length via span", "hello".AsSpan().Length == 5);
        Check("equal", "abc".SequenceEqual("abc"));
        Check("different char", !"abc".SequenceEqual("abd"));
        Check("different length", !"abc".SequenceEqual("ab"));
        Check("null acts as empty", ((string)null).SequenceEqual(""));
        Check("StartsWith", "hello world".StartsWith("hello"));
        Check("StartsWith empty", "hello".StartsWith(""));
        Check("StartsWith longer", !"hi".StartsWith("hello"));
        Check("EndsWith", "hello world".EndsWith("world"));
        Check("IndexOf char", "hello".IndexOf('l') == 2);
        Check("LastIndexOf char", "hello".LastIndexOf('l') == 3);
        Check("IndexOf string", "hello".IndexOf("lo") == 3);
        Check("IndexOf missing", "hello".IndexOf("xyz") == -1);
        Check("IndexOf empty", "hello".IndexOf("") == 0);
        Check("Contains", "hello".Contains("ell") && !"hello".Contains('z'));
        Expect("AsSpan(start)", "hello world".AsSpan(6), "world");
        Expect("AsSpan(start, length)", "hello world".AsSpan(0, 5), "hello");
        Check("Compare less", Str.Compare("abc", "abd") < 0);
        Check("Compare greater", Str.Compare("b", "a") > 0);
        Check("Compare prefix", Str.Compare("ab", "abc") < 0);
        Check("Compare equal", Str.Compare("abc", "abc") == 0);

        End("spans: compare, search, slice");

        Expect("Trim", "  hi \t\n".Trim(), "hi");
        Expect("TrimStart", "  hi \t\n".TrimStart(), "hi \t\n");
        Expect("TrimEnd", "  hi \t\n".TrimEnd(), "  hi");
        Check("Trim all whitespace", "   ".Trim().Length == 0);
        Expect("Split keeps empty parts", Join("a,b,,c"), "a|b||c");
        Expect("Split without separator", Join("abc"), "abc");
        Expect("Split empty string", Join(""), "");
        Expect("Split trailing separator", Join("a,"), "a|");
        // docs:split
        int parts = 0;
        foreach (ReadOnlySpan<char> part in "x,y,z".Split(','))
            parts++;
        Check("Split count", parts == 3);
        // docs:end

        End("spans: trim and split");

        Check("123", Parses("123", 123));
        Check("-42", Parses("-42", -42));
        Check("+7", Parses("+7", 7));
        Check("long.MaxValue", Parses("9223372036854775807", 9223372036854775807));
        Check("long.MinValue", Parses("-9223372036854775808", -9223372036854775807 - 1));
        Check("overflow", Rejects("9223372036854775808"));
        Check("empty", Rejects(""));
        Check("sign only", Rejects("-"));
        Check("trailing junk", Rejects("12a"));
        Check("whitespace", Rejects(" 1"));

        End("parse");

        int parsedInt;
        ulong parsedULong;
        double parsedDouble;
        Check("int max", Str.TryParse("2147483647", out parsedInt) && parsedInt == 2147483647);
        Check("int min", Str.TryParse("-2147483648", out parsedInt) && parsedInt == -2147483647 - 1);
        Check("int overflow", !Str.TryParse("2147483648", out parsedInt));
        Check("ulong max", Str.TryParse("18446744073709551615", out parsedULong) && parsedULong == 18446744073709551615UL);
        Check("ulong overflow", !Str.TryParse("18446744073709551616", out parsedULong));
        Check("ulong negative", !Str.TryParse("-1", out parsedULong));
        Check("hex", Str.TryParseHex("ff", out parsedULong) && parsedULong == 255);
        Check("hex 0x", Str.TryParseHex("0xFF", out parsedULong) && parsedULong == 255);
        Check("hex max", Str.TryParseHex("FFFFFFFFFFFFFFFF", out parsedULong) && parsedULong == 18446744073709551615UL);
        Check("hex leading zeros", Str.TryParseHex("00000000000000000001", out parsedULong) && parsedULong == 1);
        Check("hex overflow", !Str.TryParseHex("10000000000000000", out parsedULong));
        Check("hex rejects", !Str.TryParseHex("0x", out parsedULong) && !Str.TryParseHex("fg", out parsedULong));
        End("parse: int, ulong, hex");

        Check("3.14", ParsesDouble("3.14", 3.14));
        Check("-0.5", ParsesDouble("-0.5", -0.5));
        Check("1e3", ParsesDouble("1e3", 1000));
        Check("1.5E-3", ParsesDouble("1.5E-3", 0.0015));
        Check(".5 and 5.", ParsesDouble(".5", 0.5) && ParsesDouble("5.", 5));
        Check("0.1 exact", Str.TryParse("0.1", out parsedDouble) && parsedDouble == 0.1);
        Check("123456.789 exact", Str.TryParse("123456.789", out parsedDouble) && parsedDouble == 123456.789);
        Check("1e22 exact", Str.TryParse("1e22", out parsedDouble) && parsedDouble == 1e22);
        Check("long mantissa", ParsesDouble("3.14159265358979323846264338", 3.14159265358979323846264338));
        Check("double max", ParsesDouble("1.7976931348623157e308", 1.7976931348623157e308));
        Check("tiny", ParsesDouble("2.2250738585072014e-308", 2.2250738585072014e-308));
        Check("denormal", Str.TryParse("4.9e-324", out parsedDouble) && parsedDouble > 0);
        Check("overflow to Infinity", Str.TryParse("1e400", out parsedDouble) && parsedDouble > 1e308);
        Check("underflow to 0", Str.TryParse("-1e-400", out parsedDouble) && parsedDouble == 0);
        Check("NaN", Str.TryParse("NaN", out parsedDouble) && IsNaN(parsedDouble));
        Check("Infinity", Str.TryParse("-Infinity", out parsedDouble) && parsedDouble < -1e308);
        Check("round trip with Format", ParsesDouble(Str.Format(s_arena, $"{-1234.5678:F4}"), -1234.5678));
        Check("rejects", RejectsDouble("") && RejectsDouble(".") && RejectsDouble("e5") && RejectsDouble("1e")
            && RejectsDouble("1e+") && RejectsDouble("--1") && RejectsDouble("1.2.3") && RejectsDouble(" 1"));
        End("parse: double");

        // ---- the core library's string operators ----
        string foo = "foo", bar = "bar", none = null;
        Check("==", foo == "foo" && !(foo == bar));
        Check("!=", foo != bar && !(foo != "foo"));
        Check("null ==", none == null && none != foo && foo != none);
        Check("Equals", foo.Equals("foo") && string.Equals(foo, "foo") && !foo.Equals(none));
        Check("IsNullOrEmpty", string.IsNullOrEmpty(none) && string.IsNullOrEmpty("") && !string.IsNullOrEmpty(foo));
        Check("+", foo + bar == "foobar");
        Check("+ with null", foo + none == "foo" && none + none == "");
        Check("+ seven operands", foo + "," + bar + "," + foo + "," + bar == "foo,bar,foo,bar");
        string built = "";
        for (int i = 0; i < 3; i++)
            built += "ab";
        Check("+= in a loop", built == "ababab");
        Check("$ with string holes", $"{foo}-{bar}" == "foo-bar");
        Check("switch", Classify("alpha") == 1 && Classify("eta") == 7 && Classify("omega") == 0 && Classify(none) == 0);
        End("core library: ==, +, switch");

        Check("preinitialized static string", s_label == "initial");
        s_label = Str.Format(s_arena, $"changed {1}");
        Check("assign static string", s_label == "changed 1");
        Check("static string array", s_words.Length == 2 && s_words[1] == "one");
        s_words[0] = "nil";
        Check("store into static array", s_words[0] == "nil");
        End("core library: static string fields");

        // ---- formatting: $"..." into new arena strings ----
        long min = -9223372036854775807 - 1;
        ulong max = 18446744073709551615UL;
        uint big = 4000000000;
        Expect("int", Str.Format(s_arena, $"x={42}"), "x=42");
        Expect("negative", Str.Format(s_arena, $"{-7}"), "-7");
        Expect("long.MinValue", Str.Format(s_arena, $"{min}"), "-9223372036854775808");
        Expect("ulong.MaxValue", Str.Format(s_arena, $"{max}"), "18446744073709551615");
        Expect("uint", Str.Format(s_arena, $"{big}"), "4000000000");
        Expect("hex", Str.Format(s_arena, $"{255:X}"), "FF");
        Expect("hex lower padded", Str.Format(s_arena, $"{255:x4}"), "00ff");
        Expect("min digits", Str.Format(s_arena, $"{7:D3}"), "007");
        Expect("min digits negative", Str.Format(s_arena, $"{-7:D3}"), "-007");

        End("format: integers");

        double zero = 0;
        Expect("F2", Str.Format(s_arena, $"{3.14159:F2}"), "3.14");
        Expect("F0 rounds half up", Str.Format(s_arena, $"{2.5:F0}"), "3");
        Expect("F2 rounding", Str.Format(s_arena, $"{123456.789:F2}"), "123456.79");
        Expect("float F3", Str.Format(s_arena, $"{1.5f:F3}"), "1.500");
        Expect("default trims zeros", Str.Format(s_arena, $"{0.1}"), "0.1");
        Expect("default whole number", Str.Format(s_arena, $"{1.0}"), "1");
        Expect("default negative", Str.Format(s_arena, $"{-2.25}"), "-2.25");
        Expect("default small", Str.Format(s_arena, $"{0.000001}"), "0.000001");
        Expect("default too small", Str.Format(s_arena, $"{0.0000001}"), "0");
        Expect("huge switches to e", Str.Format(s_arena, $"{1e20:F2}"), "1.00e+20");
        Expect("NaN", Str.Format(s_arena, $"{zero / zero}"), "NaN");
        Expect("Infinity", Str.Format(s_arena, $"{1 / zero}"), "Infinity");
        Expect("-Infinity", Str.Format(s_arena, $"{-1 / zero}"), "-Infinity");

        End("format: doubles");

        string name = "bob";
        string nothing = null;
        ReadOnlySpan<char> span = "world".AsSpan();
        Expect("string", Str.Format(s_arena, $"hi {name}!"), "hi bob!");
        Expect("null string", Str.Format(s_arena, $"[{nothing}]"), "[]");
        Expect("span", Str.Format(s_arena, $"hello {span}"), "hello world");
        Expect("char", Str.Format(s_arena, $"{'c'}"), "c");
        Expect("bool", Str.Format(s_arena, $"{true}/{false}"), "true/false");
        Expect("right-align", Str.Format(s_arena, $"[{42,5}]"), "[   42]");
        Expect("left-align", Str.Format(s_arena, $"[{42,-5}]"), "[42   ]");
        Expect("align string", Str.Format(s_arena, $"[{"ab",4}]"), "[  ab]");
        Expect("align + format", Str.Format(s_arena, $"[{3.5,6:F1}]"), "[   3.5]");
        Expect("too long for width", Str.Format(s_arena, $"[{"toolong",3}]"), "[toolong]");

        End("format: other values and alignment");

        // ---- arena strings are real strings ----
        // docs:real-string
        string s = Str.Format(s_arena, $"n={5}");
        Check("Length", s.Length == 3);
        Check("indexer", s[0] == 'n' && s[2] == '5');
        unsafe
        {
            // SAFETY: s has Length chars plus a terminator.
            fixed (char* p = s)
                Check("null-terminated like a literal", p[3] == '\0');
        }
        // docs:end
        Expect("Concat 2", Str.Concat(s_arena, "ab", "cd"), "abcd");
        Expect("Concat 3", Str.Concat(s_arena, "a", "b", "c"), "abc");
        Expect("Concat with slice", Str.Concat(s_arena, "hello world".AsSpan(0, 5), "!"), "hello!");
        Expect("ToArenaString", Str.ToArenaString(s_arena, "xyz"), "xyz");
        Check("ToArenaString copies", !ReferenceEquals(Str.ToArenaString(s_arena, name), name));

        // docs:restore
        // Temporary strings: free them with Restore and keep the rest.
        nuint usedBefore = s_arena.Used;
        Arena.Mark mark = s_arena.Save();
        bool temporaryOk;
        {   // temporary can't be named after this block, so not after the Restore
            string temporary = Str.Format(s_arena, $"{name} has {name.Length} letters");
            temporaryOk = temporary.EndsWith("3 letters");
        }
        unsafe { s_arena.Restore(mark); } // SAFETY: nothing from the block was kept.
        // docs:end
        Check("Save/Restore frees temporary strings", temporaryOk && s_arena.Used == usedBefore);
        Check("earlier strings survive Restore", s == "n=5");

        // docs:scratch
        // Or build in a scratch arena and copy only the result out.
        Arena scratch = Arena.Create(256);
        StrBuilder draft = StrBuilder.Create(scratch);
        for (int i = 0; i < 50; i++)
            draft.Append($"{i} ");
        usedBefore = s_arena.Used;
        string kept = draft.ToArenaString(s_arena);
        // docs:end
        unsafe { scratch.Destroy(); } // SAFETY: draft is not used below; kept lives in s_arena.
        Check("scratch arena: result copied out", kept.StartsWith("0 1 2 ") && kept.EndsWith("48 49 "));
        Check("scratch arena: only the result is kept", s_arena.Used - usedBefore == (nuint)(System.Runtime.CompilerServices.RuntimeHelpers.OffsetToStringData + (kept.Length + 1) * 2));

        End("arena strings");

        // ---- StrBuilder ----
        StrBuilder sb = StrBuilder.Create(s_arena, 16);
        for (int i = 0; i < 200; i++)
            sb.Append($"{i},");
        Check("grows past capacity: 690 chars", sb.Length == 690);
        Check("contents start", sb.AsSpan().StartsWith("0,1,2,3,"));
        Check("contents end", sb.AsSpan().EndsWith("198,199,"));
        sb.Clear();
        sb.Append("sum");
        sb.Append('=');
        sb.Append(-12);
        Expect("Append span, char, long", sb.AsSpan(), "sum=-12");
        StrBuilder copy = sb;
        copy.Append("!");
        Expect("copies share contents", sb.AsSpan(), "sum=-12!");
        Expect("ToArenaString", sb.ToArenaString(), "sum=-12!");
        sb.Clear();
        Check("Clear", sb.Length == 0 && sb.IsCreated);

        End("StrBuilder");

        // ---- Out: $"..." straight to stdout ----
        Out.Line();
        // docs:table
        Out.Line($"{"item",-8}|{"qty",5}|{"price",9}");
        Out.Line($"{"apple",-8}|{3,5}|{1.25,9:F2}");
        Out.Line($"{"melon",-8}|{12,5}|{0.5,9:F2}");
        // docs:end
        Out.Line($"UTF-8: héllo wörld, 世界, 🎉 ({"🎉".Length} UTF-16 chars)");
        Out.Line();
        Out.Line($"failures: {s_failures}");
        return s_failures;
    }

    static bool ReferenceEquals(string a, string b) => (object)a == (object)b;

    // Split, joined with '|', as a new arena string.
    static string Join(string text)
    {
        StrBuilder sb = StrBuilder.Create(s_arena);
        bool first = true;
        foreach (ReadOnlySpan<char> part in text.Split(','))
        {
            if (!first)
                sb.Append('|');
            sb.Append(part);
            first = false;
        }
        return sb.ToArenaString();
    }
}
