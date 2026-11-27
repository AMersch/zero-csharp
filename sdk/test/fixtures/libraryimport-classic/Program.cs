// [LibraryImport]s under C#'s memory safety rules before 15, in an unsafe type.
using System.Runtime.InteropServices;

public static unsafe partial class C
{
    [LibraryImport("libc")]
    public static partial int abs(int value);

    [LibraryImport("libc")]
    public static partial nuint strlen(byte* s);
}

public static class Program
{
    public static unsafe int Main()
    {
        byte* text = stackalloc byte[] { (byte)'z', (byte)'e', (byte)'r', (byte)'o', 0 };
        Out.Line($"abs {C.abs(-5)} strlen {C.strlen(text)}");
        Out.Flush();
        return 0;
    }
}
