// Test: decimal as text with .NET's format strings, sized for a small
// machine: rounding half away from zero, trailing zeros kept, no "-0". On a
// Commodore 64 (./build.sh --c64) formatting is big, so integers
// (SmallFormats.cs), doubles (SmallDoubleFormats.cs) and decimal each have a
// sample; Formats.cs checks far more. Every expected string is what .NET 10
// prints. It runs on every target.
// Exit code = number of failed checks.
// Build: ./build.sh SmallDecimalFormats
//
// The Small samples print with Out.Write and Out.Line, not $"...": formatting
// a line costs a small machine several KB, and these are about what fits.

using System;
using System.Runtime.CompilerServices;

namespace SmallDecimalFormatsSample;

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

    static void Is(string text, string expected)
    {
        s_sectionChecks++;
        if (text != expected)
        {
            s_sectionFailures++;
            Out.Write("  failed: expected ");
            Out.Write(expected);
            Out.Write(", got ");
            Out.Line(text);
        }
    }

    // Hide a value from the optimizer. (Always false: see SmallIntegers.cs.)
    static bool s_never;
    [MethodImpl(MethodImplOptions.NoInlining)] static decimal M(decimal value) => s_never ? 0 : value;

    public static int Main()
    {
        s_never = false;

        Is(M(2.675m).ToString("F2"), "2.68");
        Is(M(1.50m).ToString(), "1.50");
        Is(M(-0.001m).ToString("F2"), "0.00");
        Is(M(1234567.891m).ToString("N"), "1,234,567.89");
        Is(M(-2.665m).ToString("F2"), "-2.67");
        Is(M(1234.5m).ToString("E"), "1.234500E+003");
        Is(M(0.00001m).ToString("G5"), "1E-05");
        End("decimal");


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
