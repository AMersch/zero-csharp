// Calls the nativelib fixture, built as libzlink, through an import bound
// when linking (ZeroLinkLibrary), not loaded at run time.

using System.Runtime.InteropServices;

public static class Program
{
    [DllImport("zlink", EntryPoint = "nl_add")] static safe extern int Add(int a, int b);

    public static int Main()
    {
        Out.Line($"add {Add(2, 3)}");
        Out.Flush();
        return 0;
    }
}
