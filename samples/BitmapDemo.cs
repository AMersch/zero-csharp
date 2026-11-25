// Example and test: the Commander X16's bitmap (lib/Bitmap.cs): a picture of lines,
// circles, rectangles, a gradient made with the palette and some text, 320 by 240
// pixels of 256 colors, with the 40 by 30 text screen in front. The program reads
// the picture back from the video memory (the headless emulator of ./test.sh --cx16
// can't show it) and then waits for a key (the test types one).
// Exit code = number of failed checks.
// Build: ./build.sh --cx16 BitmapDemo    Run: ./run.sh --cx16 --window BitmapDemo

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

    // A pixel, read straight from the video memory: row after row, 320 to a row.
    static int Pixel(int x, int y) => CX16.VPeek(y * 320 + x);

    // docs:picture
    static void Draw()
    {
        Bitmap.Clear(0);

        // A gradient of blue, made by giving 16 palette colors an amount of blue each.
        for (int i = 0; i < 16; i++)
        {
            CX16.SetColor(100 + i, 0, i / 2, i);
            Bitmap.Fill(i * 20, 24, 20, 16, 100 + i);
        }

        // Lines from the bottom left corner, and the outline of the picture.
        for (int x = 0; x < 320; x += 20)
            Bitmap.Line(0, 239, x, 60, 1 + (x / 20) % 15);
        Bitmap.Rectangle(0, 0, Bitmap.Width, Bitmap.Height, 15);

        // Circles, one inside another, in an orange that isn't in the palette yet.
        CX16.SetColor(40, 15, 8, 0);
        for (int r = 10; r <= 50; r += 10)
            Bitmap.Circle(240, 150, r, 40);
        Bitmap.Fill(236, 146, 9, 9, 2);

        Bitmap.Text(8, 8, "C# on the X16's bitmap", 1);
        Bitmap.Text(96, 200, " 320 x 240, 256 colors ", 0, 8);
    }
    // docs:end

    public static int Main()
    {
        Check("the bitmap mode is switched to", Bitmap.Enable() && Bitmap.IsEnabled && CX16.Columns == 40 && CX16.Rows == 30);
        CX16.VPoke(0x13000, 0xA5);          // in the sprites' part of the video memory: the bitmap mustn't reach it
        Draw();

        // What was drawn.
        Check("the gradient", Pixel(1, 24) == 100 && Pixel(318, 39) == 115 && Pixel(160, 30) == 108
            && CX16.GetColor(115) == 0x07F);
        Check("the outline", Pixel(0, 0) == 15 && Pixel(319, 0) == 15 && Pixel(0, 239) == 15 && Pixel(319, 239) == 15
            && Pixel(160, 0) == 15 && Pixel(0, 120) == 15 && Pixel(319, 120) == 15);
        Check("the circles", Pixel(290, 150) == 40 && Pixel(190, 150) == 40 && Pixel(240, 100) == 40 && Pixel(250, 150) == 40
            && Pixel(230, 150) == 40 && Pixel(240, 140) == 40 && Pixel(270, 150) == 40 && Pixel(240, 160) == 40);
        Check("the orange", CX16.GetColor(40) == 0xF80);
        Check("a filled square inside the circles", Pixel(236, 146) == 2 && Pixel(244, 154) == 2 && Pixel(245, 155) == 0);
        Check("a line from the corner reaches its end", Pixel(100, 60) == 1 + 5 && Pixel(300, 60) == 1 && Pixel(1, 238) != 0);
        Check("the background is cleared all the way down", Pixel(310, 230) == 0 && Pixel(200, 225) == 0 && Pixel(250, 225) == 0 && Pixel(318, 150) == 0 && Pixel(300, 100) == 0);
        Check("the sprites' memory is untouched", CX16.VPeek(0x13000) == 0xA5);

        // The shapes, one by one, in an empty part of the picture.
        Bitmap.Fill(0, 60, 320, 140, 0);
        Bitmap.Plot(10, 70, 200);
        Check("Plot and Get", Pixel(10, 70) == 200 && Bitmap.Get(10, 70) == 200 && Bitmap.Get(11, 70) == 0);
        Bitmap.Plot(-1, 70, 9); Bitmap.Plot(320, 70, 9); Bitmap.Plot(5, -1, 9); Bitmap.Plot(5, 240, 9);
        Check("outside the picture", Bitmap.Get(-1, 0) == -1 && Bitmap.Get(320, 0) == -1 && Bitmap.Get(0, 240) == -1
            && Pixel(0, 71) == 0 && Pixel(319, 70) == 0);

        Bitmap.Fill(100, 80, 40, 30, 9);
        Check("Fill", Pixel(100, 80) == 9 && Pixel(139, 109) == 9 && Pixel(99, 80) == 0 && Pixel(140, 80) == 0
            && Pixel(100, 110) == 0 && Pixel(100, 79) == 0);
        Bitmap.Fill(-10, 150, 20, 5, 3);
        Bitmap.Fill(310, 190, 50, 50, 4);
        Check("Fill cut at the edges", Pixel(0, 150) == 3 && Pixel(9, 154) == 3 && Pixel(10, 150) == 0 && Pixel(319, 199) == 4
            && Pixel(310, 190) == 4 && Pixel(309, 190) == 0 && Pixel(0, 191) == 0);
        Bitmap.Fill(5, 5, 0, 5, 9);
        Bitmap.Fill(5, 5, -3, 5, 9);
        Check("an empty Fill", Pixel(5, 5) == 15 || Pixel(5, 5) == 0);

        Bitmap.Line(5, 100, 25, 100, 2);
        Bitmap.Line(25, 101, 5, 101, 2);
        Check("a horizontal line, both ways", Pixel(5, 100) == 2 && Pixel(25, 100) == 2 && Pixel(26, 100) == 0
            && Pixel(5, 101) == 2 && Pixel(25, 101) == 2 && Pixel(4, 101) == 0);
        Bitmap.Line(30, 90, 130, 190, 5);
        Check("a diagonal", Pixel(30, 90) == 5 && Pixel(80, 140) == 5 && Pixel(130, 190) == 5 && Pixel(80, 139) == 0);
        Bitmap.Line(200, 70, 210, 170, 6);
        int steep = 0;
        for (int y = 70; y <= 170; y++)
        {
            int found = 0;
            for (int x = 199; x <= 211; x++)
                if (Pixel(x, y) == 6)
                    found++;
            if (found == 1)
                steep++;
        }
        Check("a steep line has one pixel in every row", steep == 101 && Pixel(200, 70) == 6 && Pixel(210, 170) == 6);
        Bitmap.Line(-5, 195, 5, 199, 7);
        Check("a line that starts outside", Pixel(0, 197) == 7 && Pixel(5, 199) == 7);

        Bitmap.Rectangle(200, 120, 30, 20, 10);
        Check("Rectangle", Pixel(200, 120) == 10 && Pixel(229, 120) == 10 && Pixel(200, 139) == 10 && Pixel(229, 139) == 10
            && Pixel(214, 120) == 10 && Pixel(200, 130) == 10 && Pixel(229, 130) == 10 && Pixel(210, 130) == 0);
        Bitmap.Circle(60, 150, 20, 11);
        Check("Circle", Pixel(80, 150) == 11 && Pixel(40, 150) == 11 && Pixel(60, 170) == 11 && Pixel(60, 130) == 11
            && Pixel(60, 150) == 0 && Pixel(70, 150) == 0);

        // Text: the pixels of an A, from the character set's own bytes.
        int next = Bitmap.Text(8, 175, "A", 1, 2);
        bool glyph = next == 16;
        for (int row = 0; row < 8; row++)
        {
            int bits = CX16.VPeek(0x1F000 + 65 * 8 + row);
            for (int bit = 0; bit < 8; bit++)
                glyph &= Pixel(8 + bit, 175 + row) == ((bits & (0x80 >> bit)) != 0 ? 1 : 2);
        }
        Check("Text on a background, with the glyph of A", glyph);
        Bitmap.Fill(40, 175, 8, 8, 6);
        Bitmap.Text(40, 175, "A", 1);
        bool see = Pixel(40, 175) == 6 || Pixel(41, 175) == 6;
        int strokes = 0;
        for (int row = 0; row < 8; row++)
            for (int bit = 0; bit < 8; bit++)
                if (Pixel(40 + bit, 175 + row) == 1)
                    strokes++;
        Check("Text without one lets the picture show", see && strokes > 8 && strokes < 40);

        // The palette.
        CX16.SetColor(77, 15, 9, 3);
        Check("SetColor and GetColor", CX16.GetColor(77) == 0xF93 && CX16.VPeek(0x1FA00 + 154) == 0x93 && CX16.VPeek(0x1FA00 + 155) == 0x0F);

        // Nothing is drawn in another mode.
        Draw();                             // the picture again, for the window
        CX16.SetMode(CX16.Text80x60);
        int before = Pixel(70, 100);
        Bitmap.Plot(70, 100, 99);
        Bitmap.Fill(70, 100, 5, 5, 99);
        Bitmap.Line(70, 100, 90, 100, 99);
        Check("nothing is drawn in another mode", Pixel(70, 100) == before && Bitmap.Get(70, 100) == -1 && !Bitmap.IsEnabled);
        Check("and it comes back", Bitmap.Enable() && Bitmap.IsEnabled);
        Draw();

        int frames = 0;
        while (frames < 60 || CX16.ReadKey() == 0)
        {
            CX16.WaitFrame();
            frames++;
        }

        // Leave the machine as BASIC expects it.
        CX16.SetMode(CX16.Text80x60);
        CX16.Clear(CX16.Blue);
        if (s_failures == 0)
            Out.Line("All " + s_checks.ToString() + " checks passed.");
        else
            Out.Line(s_failures.ToString() + " of " + s_checks.ToString() + " checks FAILED, the first: " + s_firstFailure);
        return s_failures;
    }
}
