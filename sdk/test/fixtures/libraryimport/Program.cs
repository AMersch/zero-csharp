// [LibraryImport]s of the C library: numbers, an enum, a pointer, a returned
// struct and a callback, in a nested type, marked safe, unsafe or neither.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fixture;

public struct DivResult
{
    public int Quotient;
    public int Remainder;
}

public enum Sign
{
    Minus = -7,
}

public static partial class C
{
    // docs:imports
    [LibraryImport("libc", EntryPoint = "abs")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Abs(int value);

    [LibraryImport("libc")]
    internal static safe partial nuint strlen(byte* s);
    // docs:end

    [LibraryImport("libc", EntryPoint = "abs")]
    public static partial int AbsOf(Sign value);

    [LibraryImport("libc")]
    public static partial DivResult div(int numerator, int denominator);

    public static partial class Sort
    {
        [LibraryImport("libc")]
        public static unsafe partial void qsort(void* @base, nuint count, nuint size,
            delegate* unmanaged[Cdecl]<void*, void*, int> compare);
    }
}

public static class Program
{
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static int Compare(void* a, void* b)
    {
        unsafe { return *(int*)a - *(int*)b; } // SAFETY: qsort passes two of the ints.
    }

    public static int Main()
    {
        Out.Line($"abs {C.Abs(-5)} {C.AbsOf(Sign.Minus)}");
        DivResult d = C.div(17, 5);
        Out.Line($"div {d.Quotient} {d.Remainder}");
        unsafe
        {
            // SAFETY: the string is NUL-terminated, and qsort sorts the four
            // ints in place with Compare, which reads two of them.
            byte* text = stackalloc byte[] { (byte)'z', (byte)'e', (byte)'r', (byte)'o', 0 };
            Out.Line($"strlen {C.strlen(text)}");
            int* values = stackalloc int[] { 3, 1, 4, 2 };
            C.Sort.qsort(values, 4, sizeof(int), &Compare);
            Out.Line($"qsort {values[0]} {values[1]} {values[2]} {values[3]}");
        }
        Out.Flush();
        return 0;
    }
}
