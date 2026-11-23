using System.Diagnostics.CodeAnalysis;
using System.Runtime;
using System.Runtime.CompilerServices;

namespace System.Diagnostics
{
    // Debug.Assert and Debug.Fail: checks for Debug builds. [Conditional("DEBUG")]
    // makes the compiler drop every call, arguments included, where DEBUG isn't
    // defined (the SDK's Release builds), so they cost nothing there.
    //
    // What counts is whether DEBUG is defined where the call is compiled. lib/
    // and zerocore are compiled once, without it, so a Debug.Assert inside them
    // never runs, even in a program's Debug build: checks there stay always on
    // (lib/'s guards, Arena.DebugChecks). Debug.Assert is for programs' own code.
    //
    // A failure ends the program the way every zero-mode failure does, through
    // Environment.FailFast, after printing where it happened:
    //
    //     Debug.Assert failed: count > 0: the queue is empty
    //       in Dequeue at Queue.cs:42
    //
    // The caller-info parameters are filled in by the compiler; don't pass them.
    // The source file's path is a string constant in the binary, so a Debug
    // build carries the directory it was built in; only its file name is printed.
    public static unsafe class Debug
    {
        [Conditional("DEBUG")]
        public static void Assert(
            [DoesNotReturnIf(false)] bool condition,
            string message = null,
            [CallerArgumentExpression(nameof(condition))] string expression = null,
            [CallerMemberName] string member = null,
            [CallerFilePath] string file = null,
            [CallerLineNumber] int line = 0)
        {
            if (!condition)
                Stop("Debug.Assert failed: ", expression, message, member, file, line);
        }

        [Conditional("DEBUG")]
        [DoesNotReturn]
        public static void Fail(
            string message,
            [CallerMemberName] string member = null,
            [CallerFilePath] string file = null,
            [CallerLineNumber] int line = 0)
        {
            Stop("Debug.Fail: ", message, null, member, file, line);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [DoesNotReturn]
        private static void Stop(string what, string text, string message, string member, string file, int line)
        {
            Write(what);
            Write(text);
            if (message != null)
            {
                Write(": ");
                Write(message);
            }
            Write("\n  in ");
            Write(member ?? "?");
            Write(" at ");
            if (file == null)
                Write("?");
            else
                Write(file, FileNameStart(file));
            Write(":");
            WriteNumber(line);
            Write("\n");
            Environment.FailFast(null);
        }

        // Where the file name starts: after the path's last '/' or '\' (the
        // build may have run on Windows, or in a Linux container for a Mac).
        private static int FileNameStart(string path)
        {
            int start = path.Length;
            while (start > 0 && path[start - 1] != '/' && path[start - 1] != '\\')
                start--;
            return start;
        }

        // text from index start on, as UTF-8, to stderr, without allocating.
        private static void Write(string text, int start = 0)
        {
            if (text == null)
                return;
            byte* buffer = stackalloc byte[256];
            int length = 0;
            for (int i = start; i < text.Length; i++)
            {
                int c = text[i];
                if (c >= 0xD800 && c <= 0xDBFF && i + 1 < text.Length && text[i + 1] >= 0xDC00 && text[i + 1] <= 0xDFFF)
                    c = 0x10000 + ((c - 0xD800) << 10) + (text[++i] - 0xDC00);
                else if (c >= 0xD800 && c <= 0xDFFF)
                    c = 0xFFFD;
                if (length > 252)
                {
                    Platform.write(2, buffer, (nuint)length);
                    length = 0;
                }
                if (c < 0x80)
                    buffer[length++] = (byte)c;
                else if (c < 0x800)
                {
                    buffer[length++] = (byte)(0xC0 | (c >> 6));
                    buffer[length++] = (byte)(0x80 | (c & 0x3F));
                }
                else if (c < 0x10000)
                {
                    buffer[length++] = (byte)(0xE0 | (c >> 12));
                    buffer[length++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                    buffer[length++] = (byte)(0x80 | (c & 0x3F));
                }
                else
                {
                    buffer[length++] = (byte)(0xF0 | (c >> 18));
                    buffer[length++] = (byte)(0x80 | ((c >> 12) & 0x3F));
                    buffer[length++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                    buffer[length++] = (byte)(0x80 | (c & 0x3F));
                }
            }
            Platform.write(2, buffer, (nuint)length);
        }

        private static void WriteNumber(int value)
        {
            byte* buffer = stackalloc byte[12];
            int start = 12;
            uint n = value < 0 ? (uint)(-(long)value) : (uint)value;
            do
            {
                buffer[--start] = (byte)('0' + n % 10);
                n /= 10;
            } while (n != 0);
            if (value < 0)
                buffer[--start] = (byte)'-';
            Platform.write(2, buffer + start, (nuint)(12 - start));
        }
    }
}
