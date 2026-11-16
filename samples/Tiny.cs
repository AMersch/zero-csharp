// Example: Smallest possible bflat binary (~16 KB stripped).
// Build: ./build.sh Tiny   (stage 2 adds -Os --no-pie and strips)

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
