// Test: System.Console, as .NET has it: Write and WriteLine with every kind
// of value and $"...", ReadLine, Read, and ReadKey with KeyAvailable.
// tests/input/Keys.txt is piped in, so ReadKey reads keys from it as .NET for
// Unix does when input isn't a terminal: letters, Enter, Tab, Ctrl+A,
// Backspace, a character beyond ASCII, and the escape sequences a terminal
// sends for arrows and function keys, with Shift, Alt and Control.
// In a terminal, run it and type: each key is printed as it is pressed.
// Build: ./build.sh Keys

using System;

namespace KeysSample;

public static class Program
{
    public static int Main()
    {
        // docs:write
        Console.WriteLine("Hello, {0}!", "world");
        Console.WriteLine($"[{42,5}] [{3.14159:F2}] [{true}]");
        Console.Write(1.5m);
        Console.Write(' ');
        Console.Write(0.1f);
        Console.Write(' ');
        Console.WriteLine(ulong.MaxValue);
        // docs:end
        Console.WriteLine((object)null);
        Console.WriteLine(-7L);

        // docs:read
        Console.Write("Your name? ");
        string name = Console.ReadLine();
        Console.WriteLine($"Hello, {name}.");
        int c = Console.Read();
        Console.WriteLine($"Read: {(char)c}");
        // docs:end

        Console.WriteLine($"KeyAvailable: {Console.KeyAvailable}");
        Console.Write("echoed: ");
        ConsoleKeyInfo first = Console.ReadKey();
        Console.WriteLine();
        Console.WriteLine($"first: {(int)first.Key} '{first.KeyChar}'");

        // docs:readkey
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.KeyChar == 'q')
                break;
            Console.WriteLine($"key {(int)key.Key,3} char {(int)key.KeyChar,4} "
                + $"shift {Has(key, ConsoleModifiers.Shift)} "
                + $"alt {Has(key, ConsoleModifiers.Alt)} "
                + $"ctrl {Has(key, ConsoleModifiers.Control)}");
        }
        // docs:end
        Console.WriteLine("done");
        return 0;
    }

    static int Has(ConsoleKeyInfo key, ConsoleModifiers modifier) =>
        (key.Modifiers & modifier) != 0 ? 1 : 0;
}
