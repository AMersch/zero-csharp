// Example and test: the Commander X16's tile layer (lib/Tiles.cs): a map of 64 by 64
// tiles of 8 by 8 pixels, four tiles drawn in code, a wall and a floor, and a layer
// that scrolls until a key is pressed, with the text screen in front of it. The
// program reads the video chip's registers and memory back (the headless emulator of
// ./test.sh --cx16 can't show them) for each function, and for map sizes, tile sizes
// and depths. Exit code = number of failed checks.
// Build: ./build.sh --cx16 TileDemo    Run: ./run.sh --cx16 --window TileDemo

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

    // docs:tiles
    // Four tiles of 8 by 8 pixels at 4 bits a pixel (32 bytes each; two pixels to a byte,
    // the left one in the high bits): 1 a checkerboard of colors 1 and 2, 2 bricks, 3 a solid
    // color, 4 a single pixel in the top left corner (to see flips with).
    static void MakeTiles()
    {
        byte[] picture = new byte[32];
        for (int row = 0; row < 8; row++)
            for (int pair = 0; pair < 4; pair++)
                picture[row * 4 + pair] = (row + pair) % 2 == 0 ? (byte)0x12 : (byte)0x21;
        Tiles.SetTile(1, picture);

        for (int row = 0; row < 8; row++)
            for (int pair = 0; pair < 4; pair++)
            {
                bool mortar = row == 3 || row == 7 || (pair == (row < 3 ? 1 : 3) && row != 3 && row != 7);
                picture[row * 4 + pair] = mortar ? (byte)0x77 : (byte)0x88;
            }
        Tiles.SetTile(2, picture);

        for (int i = 0; i < 32; i++)
            picture[i] = 0x55;
        Tiles.SetTile(3, picture);

        for (int i = 0; i < 32; i++)
            picture[i] = 0;
        picture[0] = 0x10;
        Tiles.SetTile(4, picture);
    }

    static void MakeMap()
    {
        Tiles.Fill(1);
        for (int column = 0; column < Tiles.Columns; column++)
        {
            Tiles.Set(column, 0, 2);
            Tiles.Set(column, 1, 2, paletteOffset: 1);
            Tiles.Set(column, 20, 3, paletteOffset: column % 4);
        }
        Tiles.Set(10, 10, 4);
        Tiles.Set(12, 10, 4, flipHorizontally: true);
        Tiles.Set(14, 10, 4, flipVertically: true);
    }
    // docs:end

    static int Entry(int column, int row, int columns) => (row * columns + column) * 2;

    public static int Main()
    {
        CX16.Clear(CX16.Black);
        Check("nothing is set up at first", Tiles.Columns == 0 && !Tiles.IsEnabled && !Tiles.SetTile(1, new byte[32]) && !Tiles.Set(0, 0, 1)
            && Tiles.Get(0, 0) == -1);

        Check("Enable 64 by 64, 8 pixels, 4 bits", Tiles.Enable(64, 64, 8, 4) && Tiles.IsEnabled);
        Check("its registers", CX16.Peek(0x9F2D) == 0x52 && CX16.Peek(0x9F2E) == 0 && CX16.Peek(0x9F2F) == 16
            && (CX16.Peek(0x9F29) & 0x10) != 0);
        Check("its geometry", Tiles.Columns == 64 && Tiles.Rows == 64 && Tiles.TileSize == 8 && Tiles.MapAddress == 0
            && Tiles.TileAddress == 0x2000 && Tiles.BytesPerTile == 32);

        MakeTiles();
        Check("a tile's picture is in the video memory", CX16.VPeek(0x2000 + 32) == 0x12 && CX16.VPeek(0x2000 + 32 + 4) == 0x21
            && CX16.VPeek(0x2000 + 96) == 0x55 && CX16.VPeek(0x2000 + 128) == 0x10 && CX16.VPeek(0x2000 + 129) == 0);
        Check("a picture of the wrong size is refused", !Tiles.SetTile(5, new byte[31]) && !Tiles.SetTile(5, new byte[33])
            && !Tiles.SetTile(1024, new byte[32]) && !Tiles.SetTile(-1, new byte[32]));

        MakeMap();
        Check("Fill and Set", Tiles.Get(0, 5) == 1 && Tiles.Get(63, 63) == 1 && Tiles.Get(7, 0) == 2 && Tiles.Get(3, 20) == 3);
        Check("a map entry", CX16.VPeek(Entry(7, 0, 64)) == 2 && CX16.VPeek(Entry(7, 0, 64) + 1) == 0x00
            && CX16.VPeek(Entry(7, 1, 64) + 1) == 0x10 && CX16.VPeek(Entry(3, 20, 64) + 1) == 0x30);
        Check("the flips", CX16.VPeek(Entry(10, 10, 64) + 1) == 0x00 && CX16.VPeek(Entry(12, 10, 64) + 1) == 0x04
            && CX16.VPeek(Entry(14, 10, 64) + 1) == 0x08);
        Check("a tile above 255", Tiles.Set(5, 5, 300, 2, true, true) && Tiles.Get(5, 5) == 300
            && CX16.VPeek(Entry(5, 5, 64)) == 44 && CX16.VPeek(Entry(5, 5, 64) + 1) == (0x20 | 0x0C | 1));
        Check("no such place or tile", !Tiles.Set(64, 0, 1) && !Tiles.Set(0, 64, 1) && !Tiles.Set(-1, 0, 1) && !Tiles.Set(0, 0, 1024)
            && !Tiles.Set(0, 0, 1, 16) && Tiles.Get(64, 0) == -1 && Tiles.Get(0, -1) == -1);
        Check("a refused Set changes nothing", Tiles.Get(0, 5) == 1 && Tiles.Get(63, 0) == 2);

        Tiles.Fill(7);
        Check("Fill sets all of the map", Tiles.Get(0, 0) == 7 && Tiles.Get(63, 63) == 7 && Tiles.Get(31, 40) == 7
            && CX16.VPeek(Entry(63, 63, 64) + 1) == 0);
        Tiles.Clear();
        Check("Clear", Tiles.Get(0, 0) == 0 && Tiles.Get(63, 63) == 0 && Tiles.Get(5, 5) == 0);

        Tiles.Scroll(300, 200);
        Check("Scroll", Tiles.ScrollX == 300 && Tiles.ScrollY == 200 && CX16.Peek(0x9F30) == 44 && CX16.Peek(0x9F31) == 1
            && CX16.Peek(0x9F32) == 200 && CX16.Peek(0x9F33) == 0);
        Tiles.Scroll(4096 + 5, -1);
        Check("Scroll wraps at 4096", Tiles.ScrollX == 5 && Tiles.ScrollY == 4095);
        Tiles.Scroll(0, 0);

        Tiles.Hide();
        Check("Hide", (CX16.Peek(0x9F29) & 0x10) == 0 && !Tiles.IsEnabled);
        Tiles.Show();
        Check("Show", (CX16.Peek(0x9F29) & 0x10) != 0 && Tiles.IsEnabled);

        // Other sizes.
        Check("32 by 32, 16 pixels, 8 bits", Tiles.Enable(32, 32, 16, 8) && CX16.Peek(0x9F2D) == 0x03 && CX16.Peek(0x9F2F) == ((1 << 2) | 3)
            && Tiles.BytesPerTile == 256 && Tiles.TileAddress == 0x800);
        Check("a 16 by 16 tile at 8 bits is 256 bytes", Tiles.SetTile(2, new byte[256]) && !Tiles.SetTile(2, new byte[32])
            && Tiles.Set(31, 31, 2) && Tiles.Get(31, 31) == 2 && !Tiles.Set(32, 0, 1));
        Check("128 by 128, 2 bits", Tiles.Enable(128, 128, 8, 2) && CX16.Peek(0x9F2D) == 0xA1 && Tiles.TileAddress == 0x8000
            && Tiles.BytesPerTile == 16 && CX16.Peek(0x9F2F) == (16 << 2));
        Check("a map at an address of its own", Tiles.Enable(64, 64, 8, 4, 0x10000, 0x12000) && CX16.Peek(0x9F2E) == 0x80
            && CX16.Peek(0x9F2F) == ((0x12000 >> 11) << 2) && Tiles.Set(1, 0, 3) && CX16.VPeek(0x10000 + 2) == 3);

        // What is refused leaves the layer as it was.
        int config = CX16.Peek(0x9F2D), columns = Tiles.Columns;
        Check("sizes that aren't", !Tiles.Enable(48, 64) && !Tiles.Enable(64, 64, 12) && !Tiles.Enable(64, 64, 8, 1)
            && !Tiles.Enable(64, 64, 8, 3) && !Tiles.Enable(512, 64));
        Check("a map too big for the video memory", !Tiles.Enable(256, 256, 8, 4));
        Check("addresses that aren't aligned", !Tiles.Enable(64, 64, 8, 4, 100) && !Tiles.Enable(64, 64, 8, 4, 0, 3000)
            && !Tiles.Enable(64, 64, 8, 4, 0x1F800, -1));
        Check("and nothing changed", CX16.Peek(0x9F2D) == config && Tiles.Columns == columns);

        // The scene, for a window.
        Tiles.Enable(64, 64, 8, 4);
        MakeTiles();
        MakeMap();
        CX16.Print(2, 2, "Tiles: a map that scrolls", CX16.Yellow);

        // What a screen mode change does to it.
        CX16.SetMode(CX16.Text40x30);
        Check("a screen mode switches the layer off", !Tiles.IsEnabled);
        CX16.SetMode(CX16.Text80x60);
        Check("and Enable brings it back", Tiles.Enable(64, 64, 8, 4) && Tiles.IsEnabled);
        MakeTiles();
        MakeMap();
        CX16.Print(2, 2, "Tiles: a map that scrolls", CX16.Yellow);

        int frames = 0;
        while (frames < 60 || CX16.ReadKey() == 0)
        {
            CX16.WaitFrame();
            Tiles.Scroll(frames, frames / 2);
            frames++;
        }
        Check("it scrolled", Tiles.ScrollX == (frames - 1) % 4096);

        // Leave the machine as BASIC expects it.
        Tiles.Hide();
        CX16.Clear(CX16.Blue);
        if (s_failures == 0)
            Out.Line("All " + s_checks.ToString() + " checks passed.");
        else
            Out.Line(s_failures.ToString() + " of " + s_checks.ToString() + " checks FAILED, the first: " + s_firstFailure);
        return s_failures;
    }
}
