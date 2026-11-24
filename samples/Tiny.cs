// Example: Smallest possible bflat binary (~12 KB).
// Build: ./build.sh Tiny   (build.sh adds -Os)

using System.Runtime.InteropServices;

namespace TinySample;

public static class Program
{
    [DllImport("libc")]
    static unsafe extern int puts(byte* s);

    public static int Main()
    {
        byte* msg = stackalloc byte[] { (byte)'H', (byte)'i', 0 };
        unsafe { puts(msg); } // SAFETY: msg is NUL-terminated.
        return 0;
    }
}
