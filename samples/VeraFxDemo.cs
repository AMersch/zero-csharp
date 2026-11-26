// Example and test: the rest of the video chip's helpers (VERA FX): the multiplier (CX16.Multiply and Dot) and
// the affine helper, which draws a picture made of tiles rotated and scaled into the bitmap (lib/Affine.cs).
// Everything is done with the helpers and again without them (CX16.UseVeraFx = false), which has to give the
// very same pixels. At the end a picture turns until a key is pressed. Exit code = number of failed checks.
// Build: ./build.sh --cx16 VeraFxDemo    Run: ./run.sh --cx16 --window VeraFxDemo

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

    static int Pixel(int x, int y) => CX16.VPeek(y * 320 + x);

    static readonly short[] s_a = new short[20];
    static readonly short[] s_b = new short[20];
    static readonly int[] s_with = new int[2];

    static bool ProductsAreRight()
    {
        short[] values = new short[] { 0, 1, -1, 7, -300, 1234, -567, 32767, -32768, 255, 256 };
        bool ok = true;
        for (int i = 0; i < values.Length; i++)
            for (int j = i; j < values.Length; j += 2)
                ok &= CX16.Multiply(values[i], values[j]) == values[i] * values[j];
        return ok;
    }

    static int PlainDot()
    {
        int sum = 0;
        for (int i = 0; i < s_a.Length; i++)
            sum += s_a[i] * s_b[i];
        return sum;
    }

    // The source: a map of 8 by 8 tiles. Tile k (1-3) is the color 16 * k + column in rows, so a pixel says which tile
    // it is from and where in it; tile 0 is color 9 with a see-through corner.
    static void MakeSource()
    {
        byte[] tile = new byte[64];
        for (int k = 0; k < 4; k++)
        {
            for (int row = 0; row < 8; row++)
                for (int column = 0; column < 8; column++)
                    tile[row * 8 + column] = (byte)(k == 0 ? (row < 4 && column < 4 ? 0 : 9) : 16 * k + row * 2 + column % 2);
            Affine.SetTile(k, tile);
        }
        for (int row = 0; row < 8; row++)
            for (int column = 0; column < 8; column++)
                Affine.SetMap(column, row, (column + 2 * row) % 4);
    }

    // The color the source has at a whole position, worked out from how MakeSource made it.
    static int SourceColor(int x, int y)
    {
        x &= 63;
        y &= 63;
        int k = (x / 8 + 2 * (y / 8)) % 4, column = x & 7, row = y & 7;
        return k == 0 ? (row < 4 && column < 4 ? 0 : 9) : 16 * k + row * 2 + column % 2;
    }

    // Draws, reads a 40 by 24 part back and returns a number that depends on every pixel of it.
    static int Drawn(int u, int v, int du, int dv, int uRow, int vRow, bool transparent)
    {
        Bitmap.Fill(0, 0, 48, 32, 200);
        Affine.Draw(4, 4, 40, 24, u, v, du, dv, uRow, vRow, transparent);
        int sum = 0;
        for (int y = 3; y < 29; y++)
            for (int x = 3; x < 45; x++)
                sum = sum * 31 + Pixel(x, y);
        return sum;
    }

    // Whether a drawing is what the rule says, pixel for pixel.
    static bool AsTheRuleSays(int u, int v, int du, int dv, int uRow, int vRow, bool transparent)
    {
        Drawn(u, v, du, dv, uRow, vRow, transparent);
        for (int row = 0; row < 24; row++)
            for (int column = 0; column < 40; column++)
            {
                int color = SourceColor((u + column * du + row * uRow) >> 8, (v + column * dv + row * vRow) >> 8);
                if (transparent && color == 0)
                    color = 200;
                if (Pixel(4 + column, 4 + row) != color)
                    return false;
            }
        return Pixel(3, 4) == 200 && Pixel(44, 4) == 200 && Pixel(4, 3) == 200 && Pixel(4, 28) == 200;
    }

    // A map of every size: the tile in its far corner, and the map again one width further on. Size 128 needs
    // addresses of its own.
    static bool MapSizeWorks(int size, bool chip)
    {
        bool ok = size == 128 ? Affine.Source(128, 0x19000, 0x15000) : Affine.Source(size);
        byte[] tile = s_tile;
        for (int k = 0; k < 3; k++)
        {
            for (int i = 0; i < 64; i++)
                tile[i] = (byte)(100 + 10 * k);
            ok &= Affine.SetTile(k, tile);
        }
        ok &= Affine.SetMap(0, 0, 1) && Affine.SetMap(size - 1, size - 1, 2) && Affine.SetMap(size - 1, 0, 0);
        int corner = (size - 1) * 8 * 256;
        for (int pass = 0; pass < 2; pass++)
        {
            CX16.UseVeraFx = pass == 0 && chip;
            Affine.Draw(0, 0, 8, 1, corner, corner, 256, 0, 0, 256);
            ok &= Pixel(0, 0) == 120 && Pixel(7, 0) == 120;
            Affine.Draw(0, 1, 8, 1, size * 8 * 256, size * 8 * 256, 256, 0, 0, 256);
            ok &= Pixel(0, 1) == 110;
            Affine.Draw(0, 2, 8, 1, corner, 0, 256, 0, 0, 256);
            ok &= Pixel(0, 2) == 100;
        }
        CX16.UseVeraFx = chip;
        return ok;
    }

    static readonly byte[] s_tile = new byte[64];

    public static int Main()
    {
        Bitmap.Enable();
        Bitmap.Clear(0);
        bool chip = CX16.HasVeraFx;

        // The multiplier.
        Check("products, with the chip", ProductsAreRight());
        for (int i = 0; i < s_a.Length; i++)
        {
            s_a[i] = (short)(i * 1711 - 16000);
            s_b[i] = (short)(9000 - i * 977);
        }
        int dot = CX16.Dot(s_a, s_b);
        Check("a dot product of 20", dot == PlainDot());
        Check("lengths that differ", CX16.Dot(s_a, new short[19]) == 0);
        CX16.UseVeraFx = false;
        Check("products, without", ProductsAreRight());
        Check("a dot product, without", CX16.Dot(s_a, s_b) == dot);
        CX16.UseVeraFx = chip;

        // Rotating and scaling.
        Check("a source that isn't one", !Affine.Source(9) && !Affine.Source(8, 0x15001) && !Affine.Source(8, 0x15000, 0x19100));
        Check("a source", Affine.Source(8));
        MakeSource();
        Check("no tile or place", !Affine.SetTile(256, new byte[64]) && !Affine.SetTile(0, new byte[63]) && !Affine.SetMap(8, 0, 1));
        Check("copied", AsTheRuleSays(0, 0, 256, 0, 0, 256, false));
        Check("twice the size, from the middle", AsTheRuleSays(20 * 256, 12 * 256, 128, 0, 0, 128, false));
        Check("half the size, wrapping round", AsTheRuleSays(50 * 256, 40 * 256, 512, 0, 0, 512, false));
        Check("turned a quarter", AsTheRuleSays(30 * 256, 5 * 256, 0, 256, -256, 0, false));
        Check("turned and scaled by fractions", AsTheRuleSays(3000, 700, 221, 128, -128, 221, false));
        Check("mirrored", AsTheRuleSays(63 * 256, 0, -256, 0, 0, 256, false));
        Check("see-through color 0", AsTheRuleSays(0, 0, 256, 0, 0, 256, true) && AsTheRuleSays(900, 300, 200, 90, -90, 200, true));

        // The helper and the software: the same pixels.
        s_with[0] = Drawn(3000, 700, 221, 128, -128, 221, false);
        Affine.Source(8, Affine.Tiles, Affine.Map, clip: true);
        s_with[1] = Drawn(40 * 256, 40 * 256, 300, 100, -100, 300, false);       // leaves the map: tile 0 there
        // Past the map: the map again, or with clip tile 0 (here column 78 is tile 1 if it repeats).
        Drawn(78 * 256, 0, 256, 0, 0, 256, false);
        Check("clipped: tile 0 outside the map", Pixel(4, 4) == 9 && SourceColor(78, 0) != 9);
        Affine.Source(8);
        Drawn(78 * 256, 0, 256, 0, 0, 256, false);
        Check("not clipped: the map again", Pixel(4, 4) == SourceColor(78, 0));
        CX16.UseVeraFx = false;
        Check("without the helper, the same", Drawn(3000, 700, 221, 128, -128, 221, false) == s_with[0]
            && AsTheRuleSays(900, 300, 200, 90, -90, 200, true));
        Affine.Source(8, Affine.Tiles, Affine.Map, clip: true);
        Check("without the helper, the same where clipped", Drawn(40 * 256, 40 * 256, 300, 100, -100, 300, false) == s_with[1]);
        Affine.Source(8);
        CX16.UseVeraFx = chip;

        Check("maps of 2 and 32 tiles", MapSizeWorks(2, chip) && MapSizeWorks(32, chip));
        Check("a map of 128 doesn't fit at the usual place", !Affine.Source(128));
        Check("a map of 128 tiles", MapSizeWorks(128, chip));
        Affine.Source(8);
        MakeSource();

        // Cut at the edges of the picture: what is left of it is where it would be.
        Bitmap.Clear(0);
        Affine.Draw(-5, -3, 20, 10, 0, 0, 256, 0, 0, 256);
        Check("cut at the top and left", Pixel(0, 0) == SourceColor(5, 3) && Pixel(14, 6) == SourceColor(19, 9) && Pixel(15, 0) == 0);
        Affine.Draw(310, 235, 20, 10, 0, 0, 256, 0, 0, 256);
        Check("cut at the bottom and right", Pixel(319, 239) == SourceColor(9, 4) && Pixel(0, 236) == 0);

        // A picture that turns, for a window.
        Bitmap.Clear(0);
        Bitmap.Text(8, 8, "affine: turning", 1);
        int frames = 0;
        while (frames < 4 || CX16.ReadKey() == 0)
        {
            int cos = CX16.Cos(frames * 3) >> 7, sin = CX16.Sin(frames * 3) >> 7;
            Affine.Draw(128, 88, 64, 64, 32 * 256 - 32 * cos + 32 * sin, 32 * 256 - 32 * sin - 32 * cos, cos, sin, -sin, cos);
            CX16.WaitFrame();
            frames++;
        }

        CX16.SetMode(CX16.Text80x60);
        CX16.Clear(CX16.Blue);
        if (s_failures == 0)
            Out.Line("All " + s_checks.ToString() + " checks passed.");
        else
            Out.Line(s_failures.ToString() + " of " + s_checks.ToString() + " checks FAILED, the first: " + s_firstFailure);
        return s_failures;
    }
}
