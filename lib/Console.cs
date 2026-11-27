// System.Console for zero mode: Write and WriteLine through Out (UTF-8;
// Write shows its text at once, as in .NET), ReadLine, Read and ReadKey
// through In. Static methods only, with
// no TextWriter behind them, so each overload a program doesn't call is
// trimmed away, which matters on a Commodore 64.
//
//   Console.WriteLine($"{name}: {score,5} points, {ratio:P1}");
//   ConsoleKeyInfo key = Console.ReadKey(intercept: true);
//   if (key.Key == ConsoleKey.UpArrow) ...
//
// Console.WriteLine($"...") formats every hole straight into Out's buffer, as
// Out.Line($"...") does: no string is built. The overloads that take an
// object or a format string ("{0,8:F2}") build one, which is never freed, as
// do ReadLine's strings. In a loop, read with In.ReadLine into a span.
//
// Not here: Console.Out and SetOut, Error, colors, the cursor and the window,
// Beep, and the encodings (input and output are UTF-8).

using System.Runtime.CompilerServices;

namespace System
{
    public static class Console
    {
        // Write shows its text at once, as .NET's Console does: a program that
        // prints "Working... " and then works shouldn't sit silent. That is a
        // write() per call; Out.Write is the buffered one, for output in bulk.
        // WriteLine writes its line in one go, as Out.Line does.
        public static void Write(string value) { global::Out.Write(value); global::Out.Flush(); }
        public static void Write(ReadOnlySpan<char> value) { global::Out.Write(value); global::Out.Flush(); }
        public static void Write(char value) { global::Out.Write(value); global::Out.Flush(); }
        public static void Write(bool value) { global::Out.Write(value); global::Out.Flush(); }
        public static void Write(int value) { global::Out.Write(value); global::Out.Flush(); }
        public static void Write(uint value) { global::Out.Write((long)value); global::Out.Flush(); }
        public static void Write(long value) { global::Out.Write(value); global::Out.Flush(); }
        public static void Write(ulong value) { Formatted(value); global::Out.Flush(); }
        public static void Write(float value) { Formatted(value); global::Out.Flush(); }
        public static void Write(double value) { global::Out.Write(value); global::Out.Flush(); }
        public static void Write(decimal value) { Formatted(value); global::Out.Flush(); }
        public static void Write(object value)
        {
            if (value != null)
                global::Out.Write(value.ToString());
            global::Out.Flush();
        }

        public static void Write(string format, params object[] args)
        {
            global::Out.Write(string.Format(format, args));
            global::Out.Flush();
        }

        // The handler has written the text into Out's buffer by now.
        public static void Write(ref global::Out.InterpolatedHandler handler) => global::Out.Flush();

        public static void WriteLine() => global::Out.Line();
        public static void WriteLine(string value) => global::Out.Line(value);
        public static void WriteLine(ReadOnlySpan<char> value) => global::Out.Line(value);

        public static void WriteLine(char value)
        {
            global::Out.Write(value);
            global::Out.Line();
        }

        public static void WriteLine(bool value)
        {
            global::Out.Write(value);
            global::Out.Line();
        }

        public static void WriteLine(int value)
        {
            global::Out.Write(value);
            global::Out.Line();
        }

        public static void WriteLine(uint value)
        {
            global::Out.Write((long)value);
            global::Out.Line();
        }

        public static void WriteLine(long value)
        {
            global::Out.Write(value);
            global::Out.Line();
        }

        public static void WriteLine(ulong value)
        {
            Formatted(value);
            global::Out.Line();
        }

        public static void WriteLine(float value)
        {
            Formatted(value);
            global::Out.Line();
        }

        public static void WriteLine(double value)
        {
            global::Out.Write(value);
            global::Out.Line();
        }

        public static void WriteLine(decimal value)
        {
            Formatted(value);
            global::Out.Line();
        }

        public static void WriteLine(object value)
        {
            if (value != null)
                global::Out.Write(value.ToString());
            global::Out.Line();
        }

        public static void WriteLine(string format, params object[] args) => global::Out.Line(string.Format(format, args));

        public static void WriteLine(ref global::Out.InterpolatedHandler handler) => global::Out.Line();

        /// <summary>The next line, without its line ending, as a new string (never freed); null at the end of input.</summary>
        public static string ReadLine() => global::In.ReadLineString();

        /// <summary>The next UTF-16 char of the input, or -1 at the end.</summary>
        public static int Read() => global::In.ReadChar();

        /// <summary>
        /// The next key, without waiting for Enter, shown on the screen. When the
        /// input isn't a terminal (a pipe, a file), the next character of it. At
        /// the end of input, ends the program with InvalidOperationException.
        /// </summary>
        public static ConsoleKeyInfo ReadKey() => global::In.ReadKey(false);

        /// <summary>The next key; with intercept, not shown on the screen.</summary>
        public static ConsoleKeyInfo ReadKey(bool intercept) => global::In.ReadKey(intercept);

        /// <summary>Whether a key waits for ReadKey, without waiting for one.</summary>
        public static bool KeyAvailable => global::In.KeyAvailable();

        static void Formatted<T>(T value) where T : ISpanFormattable
        {
            Span<char> buffer = stackalloc char[64];
            if (value.TryFormat(buffer, out int written, default, null))
                global::Out.Write(buffer.Slice(0, written));
            else
                global::Out.Write(value.ToString(null, null));
        }
    }

    /// <summary>A key ReadKey returns: its character ('\0' for keys without one), the key, and Shift, Alt and Control.</summary>
    public readonly struct ConsoleKeyInfo : IEquatable<ConsoleKeyInfo>
    {
        private readonly char _keyChar;
        private readonly ConsoleKey _key;
        private readonly ConsoleModifiers _modifiers;

        public ConsoleKeyInfo(char keyChar, ConsoleKey key, bool shift, bool alt, bool control)
        {
            _keyChar = keyChar;
            _key = key;
            _modifiers = (shift ? ConsoleModifiers.Shift : 0) | (alt ? ConsoleModifiers.Alt : 0) | (control ? ConsoleModifiers.Control : 0);
        }

        public char KeyChar => _keyChar;
        public ConsoleKey Key => _key;
        public ConsoleModifiers Modifiers => _modifiers;

        public bool Equals(ConsoleKeyInfo obj) => obj._keyChar == _keyChar && obj._key == _key && obj._modifiers == _modifiers;
        public override bool Equals(object value) => value is ConsoleKeyInfo info && Equals(info);
        public override int GetHashCode() => _keyChar | ((int)_key << 16) | ((int)_modifiers << 24);
        public static bool operator ==(ConsoleKeyInfo a, ConsoleKeyInfo b) => a.Equals(b);
        public static bool operator !=(ConsoleKeyInfo a, ConsoleKeyInfo b) => !a.Equals(b);
    }

    [Flags]
    public enum ConsoleModifiers
    {
        None = 0,
        Alt = 1,
        Shift = 2,
        Control = 4,
    }

    /// <summary>The keys, with .NET's values. Keys a terminal can't tell apart (the numeric keypad, media keys) aren't here.</summary>
    public enum ConsoleKey
    {
        None = 0,
        Backspace = 8,
        Tab = 9,
        Enter = 13,
        Escape = 27,
        Spacebar = 32,
        PageUp = 33,
        PageDown = 34,
        End = 35,
        Home = 36,
        LeftArrow = 37,
        UpArrow = 38,
        RightArrow = 39,
        DownArrow = 40,
        Insert = 45,
        Delete = 46,
        D0 = 48, D1 = 49, D2 = 50, D3 = 51, D4 = 52, D5 = 53, D6 = 54, D7 = 55, D8 = 56, D9 = 57,
        A = 65, B = 66, C = 67, D = 68, E = 69, F = 70, G = 71, H = 72, I = 73, J = 74, K = 75, L = 76, M = 77,
        N = 78, O = 79, P = 80, Q = 81, R = 82, S = 83, T = 84, U = 85, V = 86, W = 87, X = 88, Y = 89, Z = 90,
        F1 = 112, F2 = 113, F3 = 114, F4 = 115, F5 = 116, F6 = 117, F7 = 118, F8 = 119, F9 = 120, F10 = 121,
        F11 = 122, F12 = 123,
        Oem1 = 186,
        OemPlus = 187,
        OemComma = 188,
        OemMinus = 189,
        OemPeriod = 190,
        Oem2 = 191,
        Oem3 = 192,
        Oem4 = 219,
        Oem5 = 220,
        Oem6 = 221,
        Oem7 = 222,
    }
}
