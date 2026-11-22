// A zero program that loads a native library built from fixtures/nativelib
// with dlopen (through the C library) and calls two of its exports. The
// library's zerocore and the program's own keep apart. Prints "add 5",
// "remember 3", then "own 1" (the program's own static is untouched).
using System.Runtime.InteropServices;

public static class Dl
{
    [DllImport("libc")]
    public static unsafe extern void* dlopen(byte* path, int mode);

    [DllImport("libc")]
    public static unsafe extern void* dlsym(void* library, byte* name);
}

public static class Own
{
    public static int[] Values;
}

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Out.Line("usage: nativelib-zero-caller <library>");
            return 2;
        }
        Own.Values = new int[] { 1 };
        int add, remembered;
        unsafe
        {
            // SAFETY: every string passed to dlopen and dlsym is NUL-terminated
            // on this method's stack, and the exports have these signatures.
            byte* path = stackalloc byte[1024];
            byte* addName = stackalloc byte[16];
            byte* rememberName = stackalloc byte[16];
            CString(path, args[0]);   // ASCII paths are enough for the test
            CString(addName, "nl_add");
            CString(rememberName, "nl_remember");
            void* library = Dl.dlopen(path, 2); // RTLD_NOW
            if (library == null)
            {
                Out.Line("can't load the library");
                return 1;
            }
            var addExport = (delegate* unmanaged<int, int, int>)Dl.dlsym(library, addName);
            var rememberExport = (delegate* unmanaged<int, int>)Dl.dlsym(library, rememberName);
            if (addExport == null || rememberExport == null)
            {
                Out.Line("an export is missing");
                return 1;
            }
            add = addExport(2, 3);
            rememberExport(5);
            rememberExport(6);
            remembered = rememberExport(7);
        }
        Out.Write("add ");
        Out.Write(add);
        Out.Line();
        Out.Write("remember ");
        Out.Write(remembered);
        Out.Line();
        Out.Write("own ");
        Out.Write(Own.Values.Length);
        Out.Line();
        return 0;
    }

    // ASCII text as a NUL-terminated C string at to.
    private static unsafe void CString(byte* to, string text)
    {
        unsafe
        {
            // SAFETY: the callers' buffers hold text and its NUL.
            for (int i = 0; i < text.Length; i++)
                to[i] = (byte)text[i];
            to[text.Length] = 0;
        }
    }
}
