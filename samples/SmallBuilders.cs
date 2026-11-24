// Test: making strings in an arena, sized for a small machine: Str.Concat,
// Str.Format with $"...", StrBuilder, Substring and +. The other half of
// SmallStrings.cs; Strings.cs checks more and doesn't fit in a Commodore 64
// (./build.sh --c64). Its holes have no format strings ({x:x4}): the first
// one in a program costs a C64 about 12 KB, and SmallText.cs and
// SmallFormats.cs check them. It runs on every target.
// Exit code = number of failed checks.
// Build: ./build.sh SmallBuilders

using System;
using System.Runtime.CompilerServices;

namespace SmallBuildersSample;

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

    // Hides a value from the optimizer, which would otherwise answer most
    // checks at build time.
    // Always false: see SmallIntegers.cs. It keeps a compiler that optimizes
    // the whole program at once from seeing through the method below.
    static bool s_never;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string Opaque(string text) => s_never ? null : text;

    public static int Main()
    {
        s_never = false;
        string hello = Opaque("hello");
        Arena arena = Arena.Create(1024);
        string joined = Str.Concat(arena, hello, ", ", "world");
        Check("Concat", joined == "hello, world");
        Check("Substring", joined.Substring(7) == "world" && joined.Substring(0, 5) == "hello");
        Check("+", hello + "!" == "hello!");
        string formatted = Str.Format(arena, $"{hello}: {42} items,{-7,4}|{(byte)255}");
        Check("Format", formatted == "hello: 42 items,  -7|255");
        StrBuilder builder = StrBuilder.Create(arena);
        for (int n = 0; n < 5; n++)
        {
            builder.Append(n);
            builder.Append(',');
        }
        Check("StrBuilder", builder.Length == 10 && builder.AsSpan().SequenceEqual("0,1,2,3,4,"));
        string kept = builder.ToArenaString();
        builder.Clear();
        Check("ToArenaString outlives Clear", kept == "0,1,2,3,4," && builder.Length == 0);
        End("building");

        string big = Str.Format(arena, $"{long.MinValue}|{ulong.MaxValue}|{(short)-1}");
        Check("64-bit numbers", big == "-9223372036854775808|18446744073709551615|-1");
        Check("alignment", Str.Format(arena, $"[{"ab",-4}][{"ab",4}][{7,3}]") == "[ab  ][  ab][  7]");
        Check("characters and booleans", Str.Format(arena, $"{'x'}{true}{false}") == "xTrueFalse");
        nuint before = arena.Used;
        string copy = Str.ToArenaString(arena, hello.AsSpan(1, 3));
        Check("ToArenaString", copy == "ell" && arena.Used > before);
        Arena none = default;
        Check("no arena: null, not a crash", Str.Format(none, $"x={1}") == null && Str.Concat(none, "a", "b") == null);
        End("formatting");

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
