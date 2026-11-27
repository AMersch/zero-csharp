// [LibraryImport]s the SDK's generator refuses: ZERO013 for what needs
// marshalling, ZERO012 for what it can't implement.
using System.Runtime.InteropServices;

public struct Flags
{
    public bool On;
}

public record struct Switch(bool Up);

public static partial class Bad
{
    [LibraryImport("libc")]
    public static partial int puts(string text);

    [LibraryImport("libc")]
    public static partial bool isatty(int fd);

    [LibraryImport("libc")]
    public static partial void frexp_ref(double value, ref int exponent);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int close(int fd);

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int getpid();

    [LibraryImport("libc")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial int getppid();

    [LibraryImport("libc")]
    public static partial void flags(Flags value);

    [LibraryImport("libc")]
    public static partial void money(decimal value);

    [LibraryImport("libc")]
    public static partial void toggle(Switch value);

    [LibraryImport("libc")]
    public static unsafe partial void call(delegate*<int, void> callback);

    [LibraryImport("libc")]
    public static partial int generic<T>(int value);
}

public static class NotPartial
{
    public static partial class Inner
    {
        [LibraryImport("libc")]
        public static partial int abs(int value);
    }
}

public static class Program
{
    public static int Main() => 0;
}
