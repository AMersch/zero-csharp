// Test: Console.ReadKey and WriteLine, sized for a small machine. Keys.cs
// checks far more, and is far too big for a Commodore 64 (./build.sh --c64).
// tests/input/SmallKeys.txt is piped in on most targets; on the C64, c64/run.sh
// types it on the keyboard, and ReadKey takes each key from the KERNAL's
// keyboard buffer as it is pressed. It runs on every target.
// Build: ./build.sh SmallKeys
//
// The Small samples print with Out.Write and Out.Line, not $"...": formatting
// a line costs a small machine several KB, and these are about what fits.

using System;

namespace SmallKeysSample;

public static class Program
{
    public static int Main()
    {
        Console.WriteLine("Keys, then q:");
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.KeyChar == 'q')
                break;
            Console.Write("key ");
            Console.Write((int)key.Key);
            Console.Write(" char ");
            Console.Write((int)key.KeyChar);
            Console.Write(" shift ");
            Console.WriteLine((key.Modifiers & ConsoleModifiers.Shift) != 0);
        }
        Console.WriteLine("done");
        return 0;
    }
}
