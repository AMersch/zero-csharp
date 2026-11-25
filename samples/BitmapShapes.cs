// Test: the Commander X16's bitmap shapes, thoroughly, pixel by pixel: fills of every width at every
// position, lines in every direction, and filled triangles against a rule worked out exactly. All of it
// twice: with the video chip's helpers (VERA FX: the cache for fills, the line helper), where it has them
// and the emulator does, and without (CX16.UseVeraFx = false), the way an older chip works. Samples/
// BitmapDemo.cs is the picture and the basic checks. Exit code = number of failed checks.
// Build: ./build.sh --cx16 BitmapShapes

using System;

public static class Program
{
    static int s_failures;
    static int s_checks;
    static string s_firstFailure;

    static void Check(string name, bool ok, string how = "")
    {
        s_checks++;
        if (!ok)
        {
            s_failures++;
            s_firstFailure ??= how + name;      // joined only now: nothing is freed here, so a string made for every check is heap
        }
    }

    // The corners of the triangle being checked, made once for the same reason.
    static readonly int[] s_vx = new int[3];
    static readonly int[] s_vy = new int[3];

    // A pixel, read straight from the video memory: row after row, 320 to a row.
    static int Pixel(int x, int y) => CX16.VPeek(y * 320 + x);

    // Fills of every width from 1 to 9 at every position in a group of 8 pixels: tall, so that the video
    // chip's cache fills them four pixels at a time with the ends cut off by a mask; short, so that
    // they are written one by one. Each is checked along one row, with a pixel on either side.
    static bool FillsAreRight()
    {
        bool aligned = true;
        for (int tall = 0; tall < 2; tall++)
            for (int x = 100; x < 108; x++)
                for (int w = 1; w <= 9; w++)
                {
                    Bitmap.Fill(x, 62, w, tall == 1 ? 130 : 3, 77);
                    int row = tall == 1 ? 120 : 63;
                    bool right = Pixel(x - 1, row) == 0 && Pixel(x + w, row) == 0 && Pixel(x - 1, 62) == 0 && Pixel(x + w, 62) == 0;
                    for (int i = 0; i < w; i++)
                        right &= Pixel(x + i, row) == 77 && Pixel(x + i, 62) == 77;
                    if (tall == 1)
                        right &= Pixel(x, 191) == 77 && Pixel(x + w - 1, 191) == 77 && Pixel(x, 192) == 0 && Pixel(x, 61) == 0;
                    aligned &= right;
                    Bitmap.Fill(x - 1, 61, w + 2, 133, 0);
                }
        return aligned;
    }

    // A line drawn between two points, in an empty part of the picture, checked pixel by pixel: both
    // ends are set, and in every step along the longer side there is exactly one pixel, at most one
    // away from the exact line (the video chip's line helper rounds the slope to 9 bits).
    static bool LineIsRight(int x0, int y0, int x1, int y1)
    {
        Bitmap.Fill(0, 60, 320, 140, 0);
        Bitmap.Line(x0, y0, x1, y1, 77);
        int dx = x1 - x0, dy = y1 - y0;
        int adx = dx < 0 ? -dx : dx, ady = dy < 0 ? -dy : dy;
        bool ok = Pixel(x0, y0) == 77 && Pixel(x1, y1) == 77;
        bool alongX = adx >= ady;
        int major = alongX ? adx : ady, minor = alongX ? ady : adx;
        for (int i = 0; i <= major && ok; i++)
        {
            int q = major == 0 ? 0 : (2 * minor * i + major) / (2 * major);        // the exact line, rounded
            int along = i, across = q;
            int px = x0 + (alongX ? (dx < 0 ? -along : along) : (dx < 0 ? -across : across));
            int py = y0 + (alongX ? (dy < 0 ? -across : across) : (dy < 0 ? -along : along));
            int found = 0, off = 0;
            for (int k = -2; k <= 2; k++)
            {
                int cx = alongX ? px : px + k, cy = alongX ? py + k : py;
                if (Pixel(cx, cy) == 77)
                {
                    found++;
                    off = k < 0 ? -k : k;
                }
            }
            ok = found == 1 && off <= 1;
        }
        return ok;
    }

    // Lines in every direction: the eight octants, long and short, through (160, 130).
    static bool LinesAreRight()
    {
        bool lines = true;
        lines &= LineIsRight(160, 130, 260, 160);    // right and down, shallow
        lines &= LineIsRight(160, 130, 260, 100);    // right and up, shallow
        lines &= LineIsRight(160, 130, 60, 160);     // left and down, shallow
        lines &= LineIsRight(160, 130, 60, 100);     // left and up, shallow
        lines &= LineIsRight(160, 130, 190, 190);    // down and right, steep
        lines &= LineIsRight(160, 130, 190, 70);     // up and right, steep
        lines &= LineIsRight(160, 130, 130, 190);    // down and left, steep
        lines &= LineIsRight(160, 130, 130, 70);     // up and left, steep
        lines &= LineIsRight(160, 130, 220, 190);    // the diagonals
        lines &= LineIsRight(160, 130, 100, 70);
        lines &= LineIsRight(160, 130, 100, 190);
        lines &= LineIsRight(160, 130, 220, 70);
        lines &= LineIsRight(160, 70, 160, 190);     // straight down
        lines &= LineIsRight(160, 190, 160, 70);     // and up
        lines &= LineIsRight(160, 130, 163, 131);    // very short ones
        lines &= LineIsRight(160, 130, 161, 132);
        lines &= LineIsRight(60, 100, 319, 190);     // long, to the edge of the picture
        lines &= LineIsRight(0, 61, 319, 198);       // and across all of it
        Bitmap.Fill(0, 60, 320, 140, 0);
        return lines;
    }

    // The rule a filled triangle follows, worked out exactly (whole numbers, fractions kept as fractions): in
    // the row of a pixel, the triangle runs from its left edge to its right edge, measured at the middle of
    // the row, and the pixel is in if its own middle is from the left edge up to, not including, the right.
    // Coordinates are doubled so that the middles are whole numbers. `near` says that the middle is within
    // a fiftieth of a pixel of an edge: such a pixel is a tie that a rounding may fall either way.
    static bool InTriangle(int x, int y, int[] vx, int[] vy, out bool near)
    {
        near = false;
        long centerY = 2 * y + 1, centerX = 2 * x + 1;
        long leftNum = 0, leftDen = 0, rightNum = 0, rightDen = 0;
        int crossings = 0;
        for (int e = 0; e < 3; e++)
        {
            int p = e, q = (e + 1) % 3;
            long py = 2 * vy[p], qy = 2 * vy[q];
            if (py == qy || !((py <= centerY && centerY < qy) || (qy <= centerY && centerY < py)))
                continue;
            long num = 2L * vx[p] * (qy - py) + 2L * (vx[q] - vx[p]) * (centerY - py), den = qy - py;
            if (den < 0)
            {
                num = -num;
                den = -den;
            }
            long difference = centerX * den - num;            // the pixel's middle minus the edge, times den
            if ((difference < 0 ? -difference : difference) * 25 < den)
                near = true;
            if (crossings == 0)
            {
                leftNum = rightNum = num;
                leftDen = rightDen = den;
            }
            else if (num * leftDen < leftNum * den)
            {
                leftNum = num;
                leftDen = den;
            }
            else
            {
                rightNum = num;
                rightDen = den;
            }
            crossings++;
        }
        if (crossings < 2)
            return false;
        return leftNum <= centerX * leftDen && centerX * rightDen < rightNum;
    }

    // Whole-number division of a by b (b > 0), rounded up.
    static long CeilingDivide(long a, long b) => a >= 0 ? (a + b - 1) / b : -((-a) / b);

    // Where the rule puts the first pixel of the row y and the one after the last, for a triangle whose
    // corners are in vx and vy; false for a row the triangle doesn't reach.
    static bool RowSpan(int y, int[] vx, int[] vy, out int first, out int end)
    {
        first = end = 0;
        long centerY = 2 * y + 1, leftNum = 0, leftDen = 0, rightNum = 0, rightDen = 0;
        int crossings = 0;
        for (int e = 0; e < 3; e++)
        {
            int p = e, q = (e + 1) % 3;
            long py = 2 * vy[p], qy = 2 * vy[q];
            if (py == qy || !((py <= centerY && centerY < qy) || (qy <= centerY && centerY < py)))
                continue;
            long num = 2L * vx[p] * (qy - py) + 2L * (vx[q] - vx[p]) * (centerY - py), den = qy - py;
            if (den < 0)
            {
                num = -num;
                den = -den;
            }
            if (crossings == 0 || num * leftDen < leftNum * den)
            {
                if (crossings == 0)
                {
                    rightNum = num;
                    rightDen = den;
                }
                else
                {
                    rightNum = leftNum;
                    rightDen = leftDen;
                }
                leftNum = num;
                leftDen = den;
            }
            else
            {
                rightNum = num;
                rightDen = den;
            }
            crossings++;
        }
        if (crossings < 2)
            return false;
        first = (int)CeilingDivide(leftNum - leftDen, 2 * leftDen);
        end = (int)CeilingDivide(rightNum - rightDen, 2 * rightDen);
        return true;
    }

    // One pixel against the rule, a tie aside.
    static bool PixelAgrees(int x, int y, int[] vx, int[] vy)
    {
        if (x < 0 || x > 319)
            return true;
        bool inside = InTriangle(x, y, vx, vy, out bool tie);
        return tie || (Pixel(x, y) == 77) == inside;
    }

    // A triangle drawn in the cleared middle of the picture and compared with that rule, row by row, in the
    // part of the picture that was cleared (rows 60 to 199): at both ends of the row's run, a little way
    // outside and in, and now and then across it. (Every pixel would take minutes.)
    static bool TriangleIsRight(int x0, int y0, int x1, int y1, int x2, int y2)
    {
        Bitmap.Fill(0, 60, 320, 140, 0);
        Bitmap.FillTriangle(x0, y0, x1, y1, x2, y2, 77);
        int[] vx = s_vx, vy = s_vy;
        vx[0] = x0; vx[1] = x1; vx[2] = x2;
        vy[0] = y0; vy[1] = y1; vy[2] = y2;
        int minX = Math.Min(x0, Math.Min(x1, x2)), maxX = Math.Max(x0, Math.Max(x1, x2));
        int minY = Math.Max(60, Math.Min(y0, Math.Min(y1, y2)) - 1), maxY = Math.Min(199, Math.Max(y0, Math.Max(y1, y2)) + 1);
        for (int y = minY; y <= maxY; y++)
        {
            bool reached = RowSpan(y, vx, vy, out int first, out int end);
            if (!reached)
            {
                // A row the triangle doesn't reach is empty: look at both ends of its box and the middle.
                if (!PixelAgrees(minX - 1, y, vx, vy) || !PixelAgrees((minX + maxX) / 2, y, vx, vy) || !PixelAgrees(maxX + 1, y, vx, vy))
                    return false;
                continue;
            }
            for (int d = -2; d <= 2; d++)
                if (!PixelAgrees(first + d, y, vx, vy) || !PixelAgrees(end + d, y, vx, vy) || !PixelAgrees(end - 1 + d, y, vx, vy))
                    return false;
            for (int x = first + 3; x < end - 3; x += 9)
                if (!PixelAgrees(x, y, vx, vy))
                    return false;
            if (!PixelAgrees(minX - 1, y, vx, vy) || !PixelAgrees(maxX + 1, y, vx, vy))
                return false;
        }
        return true;
    }

    static bool TrianglesAreRight()
    {
        bool ok = true;
        ok &= TriangleIsRight(100, 70, 200, 70, 100, 150);      // a right angle, flat on the top, straight on the left
        ok &= TriangleIsRight(150, 70, 100, 150, 200, 150);     // flat on the bottom
        ok &= TriangleIsRight(60, 100, 250, 80, 150, 180);      // any
        ok &= TriangleIsRight(250, 80, 60, 100, 150, 180);      // the same corners in another order
        ok &= TriangleIsRight(150, 180, 250, 80, 60, 100);
        ok &= TriangleIsRight(100, 150, 110, 70, 115, 150);     // thin
        ok &= TriangleIsRight(30, 190, 300, 195, 160, 65);      // wide
        ok &= TriangleIsRight(50, 90, 250, 90, 150, 150);       // flat on the top, wide
        ok &= TriangleIsRight(100, 100, 103, 101, 101, 103);    // tiny
        ok &= TriangleIsRight(100, 100, 150, 101, 200, 100);    // one row high
        ok &= TriangleIsRight(100, 100, 120, 100, 140, 100);    // no height: nothing
        ok &= TriangleIsRight(-50, 100, 100, 70, 80, 190);      // out at the left
        ok &= TriangleIsRight(250, 100, 400, 70, 380, 190);     // and at the right
        ok &= TriangleIsRight(150, 20, 100, 150, 200, 150);     // above the part that is checked
        ok &= TriangleIsRight(150, 120, 100, 300, 200, 300);    // below the picture
        return ok;
    }

    static void Everything(string how)
    {
        Bitmap.Fill(0, 60, 320, 140, 0);
        Check("fills of every width at every alignment, tall and short", FillsAreRight(), how);
        Check("lines in every direction have both ends, one pixel a step, and stay within one pixel of the exact line", LinesAreRight(), how);
        Check("filled triangles agree with the rule, pixel by pixel", TrianglesAreRight(), how);

        // Two triangles with a side in common leave no gap along it.
        Bitmap.Fill(0, 60, 320, 140, 0);
        Bitmap.FillTriangle(60, 80, 200, 70, 250, 180, 77);
        Bitmap.FillTriangle(60, 80, 250, 180, 80, 170, 78);
        bool seam = true;
        for (int x = 75; x < 240; x++)
        {
            int along = 80 + 100 * (x - 60) / 190;
            for (int k = -1; k <= 1; k++)
                seam &= Pixel(x, along + k) != 0;
        }
        Check("two triangles with a side in common leave no gap", seam, how);

        Bitmap.Fill(0, 60, 320, 140, 0);
        Bitmap.Triangle(100, 80, 200, 80, 150, 160, 9);
        Check("a triangle's outline has its corners and sides", Pixel(100, 80) == 9 && Pixel(200, 80) == 9 && Pixel(150, 160) == 9
            && Pixel(150, 80) == 9 && Pixel(150, 120) == 0, how);
        Bitmap.Fill(0, 60, 320, 140, 0);
    }

    public static int Main()
    {
        Check("the bitmap mode is switched to", Bitmap.Enable() && Bitmap.IsEnabled);
        Bitmap.Clear(0);
        Check("the helpers are used where the chip has them", CX16.UseVeraFx == CX16.HasVeraFx);
        Everything("");

        CX16.UseVeraFx = false;
        Check("the helpers can be switched off", !CX16.UseVeraFx);
        Everything("without FX: ");
        CX16.UseVeraFx = true;
        Check("and on again", CX16.UseVeraFx == CX16.HasVeraFx);

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
