// Example: C# on a Commander X16. Text and colors on the 80x60 screen, a
// sprite that bounces around it, and a tone, until a key is pressed. Exit
// code = number of failed checks.
// Build: ./build.sh --cx16 CX16Demo    Run: ./run.sh --cx16 --window CX16Demo
//
// The objects, the interface, the lambda are here to show that
// this is ordinary C#, compiled for an 8-bit processor. lib/CX16.cs has the
// machine: the screen, the video chip's sprites, the keyboard, the sound.
//
// At the end the program reads back what it put in the video chip's memory,
// and reports through Out, which is how ./test.sh --cx16 checks it (it types
// a key for the program to stop at).

using System;

namespace CX16DemoSample;

public interface IShape
{
    string Name { get; }
    int Area();
}

public class Rect : IShape
{
    public int W, H;
    public string Name => "Rect";
    public int Area() => W * H;
}

public class Circle : IShape
{
    public int Radius;
    public string Name => "Circle";
    public int Area() => 355 * Radius * Radius / 113;
}

public static class Program
{
    static int s_failures;
    static int s_checks;

    // The checks look at the screen, so their report waits until the screen
    // has been cleared: a failed check's name is kept, not printed.
    static string s_firstFailure;

    static void Check(string name, bool ok)
    {
        s_checks++;
        if (!ok)
        {
            s_failures++;
            s_firstFailure ??= name;
        }
    }

    // docs:sprite
    // A ball, 16 pixels wide and high: 4 bits a pixel, so 8 bytes a row. Each
    // pixel is 1 (the palette's white) inside the circle, 0 (see-through) outside.
    static void MakeBall(Span<byte> shape)
    {
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                int dx = 2 * x - 15, dy = 2 * y - 15;
                if (dx * dx + dy * dy <= 15 * 15)
                    shape[y * 8 + x / 2] |= (byte)(x % 2 == 0 ? 0x10 : 0x01);
            }
    }
    // docs:end

    static int Apply(Func<int, int> f, int x) => f(x);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Opaque(int value) => value;

    // docs:text
    static void Draw()
    {
        CX16.Clear(CX16.Black);
        CX16.Border(CX16.Black);

        string title = "C# on the Commander X16";
        for (int i = 0; i < title.Length; i++)
            CX16.Print(9 + i, 2, title[i], CX16.Red + i % 6);
        CX16.Print(4, 4, "zero-mode .NET, no runtime, 65C02", CX16.LightGray);

        // Interface calls on objects from the heap.
        IShape[] shapes = { new Rect { W = 6, H = 7 }, new Circle { Radius = 10 } };
        for (int i = 0; i < shapes.Length; i++)
        {
            int column = CX16.Print(4, 7 + i, shapes[i].Name, CX16.Yellow);
            column = CX16.Print(column, 7 + i, " area = ", CX16.Yellow);
            CX16.Print(column, 7 + i, shapes[i].Area());
        }

        // A loop with 32-bit numbers: the Fibonacci sequence.
        int a = 0, b = 1;
        int x = CX16.Print(4, 11, "Fib: ", CX16.LightGreen);
        for (int i = 0; i < 16 && x < 76; i++)
        {
            x = CX16.Print(x, 11, a) + 1;
            int next = a + b;
            a = b;
            b = next;
        }

        // A lambda that captures a local.
        int offset = 1900;
        Func<int, int> year = n => n + offset;
        x = CX16.Print(4, 13, "Lambda: 82 + 1900 = ", CX16.LightBlue);
        CX16.Print(x, 13, Apply(year, 82));

        x = CX16.Print(4, 15, "32-bit int: 123456 * 789 = ", CX16.LightRed);
        CX16.Print(4, 16, 123456 * Opaque(789));

        // A background color: the text's, plus 16 for each step of the background's.
        CX16.Print(4, 18, " white on blue ", CX16.White + 16 * CX16.Blue);
        CX16.Print(4, 19, "Latin-1: \u00e4\u00f6\u00fc \u00e9\u00e8 \u00a3", CX16.Cyan);

        CX16.Print(4, 22, "Press a key to stop", CX16.Gray);
    }
    // docs:end

    // docs:loop
    // One frame: wait for the video chip to finish the picture, then move the
    // ball, and let a note follow its height.
    static void Frame(ref int x, ref int y, ref int dx, ref int dy)
    {
        CX16.WaitFrame();

        x += dx;
        y += dy;
        if (x <= 0 || x >= 624)
            dx = -dx;
        if (y <= 0 || y >= 464)
            dy = -dy;
        CX16.MoveSprite(0, x, y);
        CX16.Tone(0, 220 + y, 20, CX16.Triangle);
    }
    // docs:end

    // The video chip's text layer: a cell is two bytes (the character, then
    // the colors), 128 cells to a row, from $1B000.
    static int Cell(int column, int row) => 0x1B000 + (row * 128 + column) * 2;

    public static int Main()
    {
        Draw();
        Span<byte> ball = stackalloc byte[128];
        MakeBall(ball);
        CX16.Sprite(0, 0, ball);

        int x = 100, y = 120, dx = 4, dy = 3, frames = 0;
        // The first second runs whatever is typed (a key pressed while the
        // program was loading waits in the keyboard's buffer).
        while (frames < 60 || CX16.ReadKey() == 0)
        {
            Frame(ref x, ref y, ref dx, ref dy);
            frames++;
        }

        // What is in the video chip's memory now, read back.
        Check("the screen is 80 by 60", CX16.Columns == 80 && CX16.Rows == 60);
        Check("title, first letter", CX16.VPeek(Cell(9, 2)) == 'C');
        Check("title, a small letter", CX16.VPeek(Cell(12, 2)) == 'o');
        Check("its color", (CX16.VPeek(Cell(9, 2) + 1) & 15) == CX16.Red);
        Check("number through an interface", CX16.VPeek(Cell(16, 7)) == '4'
            && CX16.VPeek(Cell(17, 7)) == '2');
        Check("32-bit product", CX16.VPeek(Cell(4, 16)) == '9'
            && CX16.VPeek(Cell(11, 16)) == '4');
        Check("a background color", CX16.VPeek(Cell(5, 18) + 1) == CX16.White + 16 * CX16.Blue);
        Check("Latin-1", CX16.VPeek(Cell(13, 19)) == 0xE4);
        Check("printing off the screen does nothing", CX16.Print(78, 59, "abc") == 78);

        Check("sprites are on", (CX16.Peek(0x9F29) & 0x40) != 0);
        Check("its shape was copied", CX16.VPeek(0x4000 + 7 * 8 + 3) == 0x11 && CX16.VPeek(0x4000) == 0);
        Check("it is where the last frame put it", CX16.VPeek(0x1FC00 + 4) == (y & 255)
            && CX16.VPeek(0x1FC00 + 2) == (x & 255) && CX16.VPeek(0x1FC00 + 3) == (x >> 8));
        Check("the voice plays", CX16.VPeek(0x1F9C0 + 2) == (0xC0 | 20));

        Check("frames were drawn", frames > 0);
        // The emulator's headless mode, which ./test.sh --cx16 uses, draws
        // nothing and has no frame interrupt: the clock and the joysticks,
        // which that interrupt drives, can only be tried in a window.
        Check("a joystick that doesn't exist", CX16.Joystick(9) == 128);

        // Leave the machine as BASIC expects it.
        CX16.Silence(0);
        CX16.HideSprite(0);
        CX16.Border(CX16.LightBlue);
        CX16.Clear(CX16.Blue);
        if (s_failures == 0)
            Out.Line($"All {s_checks} checks passed.");
        else
            Out.Line($"{s_failures} of {s_checks} checks FAILED, the first: {s_firstFailure}");
        return s_failures;
    }
}
