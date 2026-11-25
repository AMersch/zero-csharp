// Example: a mouse pointer on a Commander X16. The arrow is an 8-bit sprite
// (16x16 pixels, 256 colors) that the program defines and the KERNAL's mouse
// driver then moves: in every frame's interrupt it sets sprite 0's position
// from the mouse. The program only reads the position and the buttons, and
// prints them. Any key stops it. Exit code = number of failed checks.
// Build: ./build.sh --cx16 Mouse    Run: ./run.sh --cx16 --window Mouse
//
// The picture is the arrow of a small assembler demo for the X16 (a 16x16
// sprite of palette colors 16, 38 and 50: black, and two grays and blues of
// the default palette). The headless emulator that ./test.sh --cx16 uses has
// no mouse and no interrupts, so there the checks read back the sprite's
// picture and registers from the video chip's memory; the pointer itself
// only moves in a window.

using System;

public static class Program
{
    static int s_failures;
    static int s_checks;
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

    // The arrow: one byte a pixel, 16 rows of 16; 0 is see-through.
    static ReadOnlySpan<byte> Arrow => new byte[]
    {
        16, 16, 16, 16, 16, 16, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        16, 38, 38, 38, 38, 38, 16, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        16, 50, 50, 50, 50, 38, 16, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        16, 50, 50, 50, 38, 16, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        16, 50, 50, 50, 50, 38, 16, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        16, 50, 16, 16, 50, 50, 38, 16, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 16, 16, 0, 16, 50, 50, 38, 16, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 16, 50, 50, 38, 16, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 16, 50, 50, 38, 16, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 16, 50, 16, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 16, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    };

    // The pointer's picture is kept in video memory at $14000.
    const int ArrowAddress = 0x14000;

    // docs:mouse
    // Writes a label and a number, and blanks the rest of its field.
    static void Show(int row, string label, int value)
    {
        int column = CX16.Print(4, row, label, CX16.LightGray);
        column = CX16.Print(column, row, value, CX16.White);
        CX16.Print(column, row, "     ", CX16.White);
    }

    static void Update()
    {
        int buttons = CX16.MouseButtons;
        Show(4, "x: ", CX16.MouseX);
        Show(5, "y: ", CX16.MouseY);
        CX16.Print(4, 7, (buttons & CX16.LeftButton) != 0 ? " LEFT " : " left ", CX16.White + 16 * ((buttons & CX16.LeftButton) != 0 ? CX16.Red : CX16.DarkGray));
        CX16.Print(11, 7, (buttons & CX16.RightButton) != 0 ? " RIGHT " : " right ", CX16.White + 16 * ((buttons & CX16.RightButton) != 0 ? CX16.Blue : CX16.DarkGray));
    }
    // docs:end

    public static int Main()
    {
        CX16.Clear(CX16.Black);
        CX16.Border(CX16.Black);
        CX16.Print(4, 2, "Move the mouse. Any key stops.", CX16.Yellow);

        // The picture, as sprite 0: where the KERNAL's mouse driver puts the pointer.
        Check("the arrow is accepted", CX16.Sprite(0, ArrowAddress, Arrow, 16, 16, 8));
        CX16.MouseOn(defaultCursor: false);     // keep the arrow above, not the KERNAL's own

        int frames = 0;
        while (frames < 60 || CX16.ReadKey() == 0)
        {
            CX16.WaitFrame();
            Update();
            frames++;
        }

        Check("the picture is in video memory", CX16.VPeek(ArrowAddress) == 16
            && CX16.VPeek(ArrowAddress + 16 + 1) == 38 && CX16.VPeek(ArrowAddress + 255) == 0);
        Check("sprite 0's picture address", CX16.VPeek(0x1FC00) == ((ArrowAddress >> 5) & 255)
            && (CX16.VPeek(0x1FC01) & 0x7F) == ArrowAddress >> 13);
        Check("8 bits a pixel", (CX16.VPeek(0x1FC01) & 0x80) != 0);
        Check("16 by 16, in front", CX16.VPeek(0x1FC07) == 0x50 && CX16.VPeek(0x1FC06) == 0x0C);
        Check("sprites are on", (CX16.Peek(0x9F29) & 0x40) != 0);

        // Shapes that don't fit are refused.
        Check("4 bits for 256 bytes", !CX16.Sprite(1, ArrowAddress, Arrow, 16, 16, 4));
        Check("an odd address", !CX16.Sprite(1, ArrowAddress + 1, Arrow, 16, 16, 8));
        Check("an odd size", !CX16.Sprite(1, ArrowAddress, Arrow, 15, 17, 8));
        Check("over the sound chip", !CX16.Sprite(1, 0x1F9A0, Arrow, 16, 16, 8));
        Check("a bigger shape", CX16.Sprite(1, 0x15000, new byte[32 * 32 / 2], 32, 32, 4)
            && CX16.VPeek(0x1FC08 + 7) == 0xA0);

        Check("the pointer is on the screen", (uint)CX16.MouseX < 640 && (uint)CX16.MouseY < 480);

        // Leave the machine as BASIC expects it.
        CX16.MouseOff();
        CX16.HideSprite(0);
        CX16.HideSprite(1);
        CX16.Border(CX16.LightBlue);
        CX16.Clear(CX16.Blue);
        if (s_failures == 0)
            Out.Line($"All {s_checks} checks passed.");
        else
            Out.Line($"{s_failures} of {s_checks} checks FAILED, the first: {s_firstFailure}");
        return s_failures;
    }
}
