// Test: the Commander X16's screen modes. Each mode is switched to and checked
// for its columns and rows (which are also its width and height in 8-pixel units),
// for text going to the last cell of the screen, and for what a mode change keeps:
// the sprites stay on and ISO text stays ISO. The text layer is read back through
// the video chip's own layer registers, since the modes put its map in different
// shapes. Mode $80 (320x240 pixels with 256 colors) is only switched to: the
// bitmap isn't drawn in yet. Exit code = number of failed checks.
// Build: ./build.sh --cx16 ScreenModes

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

    // Where a cell of the text layer is in the video memory, from the layer's registers.
    static int Cell(int column, int row)
    {
        int map = CX16.Peek(0x9F35) << 9;                       // layer 1's map base, 16:9
        int width = 32 << ((CX16.Peek(0x9F34) >> 4) & 3);       // and its width in tiles
        return map + (row * width + column) * 2;
    }

    static void TryMode(int mode, int columns, int rows)
    {
        bool ok = CX16.SetMode(mode);
        Check("mode " + mode.ToString() + " is accepted", ok && CX16.Mode == mode);
        Check("its columns and rows", CX16.Columns == columns && CX16.Rows == rows);
        CX16.Print(columns - 1, rows - 1, 'Z', CX16.Yellow);
        CX16.Print(0, 0, "ab", CX16.White);
        Check("text in the last cell and the first", CX16.VPeek(Cell(columns - 1, rows - 1)) == 'Z'
            && (CX16.VPeek(Cell(columns - 1, rows - 1) + 1) & 15) == CX16.Yellow && CX16.VPeek(Cell(1, 0)) == 'b');
        Check("past the screen is refused", CX16.Print(columns - 1, rows, "x") == columns - 1 && CX16.Print(columns, 0, "x") == columns);
    }

    // The picture of a character in the font, 8 bytes: ISO text and PETSCII have different ones
    // (an a, and a graphic character, for code 97).
    static int Glyph(int character)
    {
        int sum = 0;
        for (int i = 0; i < 8; i++)
            sum = sum * 31 + CX16.VPeek(0x1F000 + character * 8 + i);
        return sum;
    }

    public static int Main()
    {
        CX16.Clear(CX16.Black);          // ISO text from here on
        Check("it starts in 80x60", CX16.Mode == CX16.Text80x60 && CX16.Columns == 80 && CX16.Rows == 60);
        int iso = Glyph(97);

        TryMode(CX16.Text80x30, 80, 30);
        TryMode(CX16.Text40x60, 40, 60);
        TryMode(CX16.Text40x30, 40, 30);
        TryMode(CX16.Text40x15, 40, 15);
        TryMode(CX16.Text20x30, 20, 30);
        TryMode(CX16.Text20x15, 20, 15);
        TryMode(CX16.Graphics320x240, 40, 30);
        TryMode(CX16.Text80x60, 80, 60);

        // What a mode change keeps.
        byte[] shape = new byte[128];
        shape[0] = 0x11;
        CX16.Sprite(3, 1, shape);
        Check("a sprite is shown", (CX16.Peek(0x9F29) & 0x40) != 0);
        CX16.SetMode(CX16.Text40x30);
        Check("sprites stay on", (CX16.Peek(0x9F29) & 0x40) != 0);
        Check("the sprite is still there", CX16.VPeek(0x1FC00 + 3 * 8 + 7) == 0x50 && CX16.VPeek(0x4000 + 128) == 0x11);
        Check("ISO text stays ISO", Glyph(97) == iso);
        CX16.SetMode(CX16.Text80x60);

        // A number that isn't a mode.
        Check("a mode that doesn't exist", !CX16.SetMode(0x42) && CX16.Mode == CX16.Text80x60);
        Check("another one", !CX16.SetMode(0x81) && CX16.Columns == 80);

        // Leave the machine as BASIC expects it.
        CX16.HideSprite(3);
        CX16.Clear(CX16.Blue);
        if (s_failures == 0)
            Out.Line("All " + s_checks.ToString() + " checks passed.");
        else
            Out.Line(s_failures.ToString() + " of " + s_checks.ToString() + " checks FAILED, the first: " + s_firstFailure);
        return s_failures;
    }
}
