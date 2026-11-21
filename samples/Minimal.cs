// Example: Minimal zero-mode "Hello World" (~39 KB, no .NET runtime).
// Build: ./build.sh Minimal
//
// Under the updated memory safety rules, creating a pointer (stackalloc) is
// safe; only the call that reads through it needs an unsafe block.

using System.Runtime.InteropServices;

namespace MinimalSample;

public static class Program
{
    [DllImport("libc")]
    static unsafe extern int puts(byte* s);

    public static int Main()
    {
        byte* msg = stackalloc byte[] {
            (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o',
            (byte)' ', (byte)'f', (byte)'r', (byte)'o', (byte)'m',
            (byte)' ', (byte)'z', (byte)'e', (byte)'r', (byte)'o',
            (byte)'-', (byte)'m', (byte)'o', (byte)'d', (byte)'e', (byte)'!', 0
        };
        unsafe { puts(msg); } // SAFETY: msg is NUL-terminated.
        return 0;
    }
}
