// Console.Write shows its text at once, as in .NET: "a " comes before the
// "b " written straight to standard error, though no newline sent it on its
// way. Then a failure: what Out still holds ("c ", without a newline) comes
// before the failure's message.
using System;
using System.Runtime.InteropServices;

static class Program
{
    [DllImport("libc")]
    static unsafe extern nint write(int fd, byte* buffer, nuint count);

    static int Main()
    {
        Console.Write("a ");
        unsafe
        {
            // SAFETY: two bytes on the stack, both written.
            byte* b = stackalloc byte[2];
            b[0] = (byte)'b';
            b[1] = (byte)' ';
            write(2, b, 2);
        }
        Out.Write("c ");
        int[] a = new int[1];
        int i = Environment.TickCount64 > 0 ? 5 : 0; // not a constant the compiler sees
        a[i] = 1;
        Console.WriteLine("never");
        return 0;
    }
}
