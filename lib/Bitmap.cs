// The Commander X16's bitmap: 320 by 240 pixels of 256 colors, behind the text.
// Only for programs built with ./build.sh --cx16; the functions are in
// cx16/runtime.c, and a program for another target that calls them won't link.
//
//   Bitmap.Enable();                                  // the screen mode with a bitmap
//   Bitmap.Clear(0);
//   Bitmap.Line(10, 10, 300, 200, 5);                 // the colors of the palette
//   Bitmap.Fill(100, 50, 40, 30, 8);                  // x, y, width, height
//   Bitmap.FillTriangle(20, 100, 120, 90, 70, 180, 12);
//   Bitmap.Text(8, 8, "drawn", 1);
//   CX16.SetColor(40, 15, 8, 0);                      // color 40 is now orange
//
// The picture is one byte for each pixel, row after row from the start of the
// video memory ($00000-$12BFF), where the KERNAL keeps it; sprite shapes are
// above ($13000), and the text, in 40 by 30 characters, in front of the picture:
// text color 0 doesn't hide it. Everything here does nothing in another screen
// mode (see CX16.SetMode), and what is outside the picture is cut off. Drawing is
// done by C routines on the video chip's data port, and where the chip has VERA FX (see
// CX16.HasVeraFx) with its helpers: Clear and Fill write four pixels at a time with its
// cache, and a line is stepped along by its line helper. Measured, in processor cycles
// at 8 MHz (a frame is 133,000), with FX / without it: Clear 127,000 / 460,000; Fill of
// the whole picture 133,000 / 650,000, of 100 by 50 pixels 25,000 / 100,000; Line across
// the picture 6,700 / 116,000 (a line from the helper is within a pixel of the exact one,
// both ends exact; a line that leaves the picture is drawn without it); Circle 133,000
// for a radius of 100; FillTriangle (by the chip's polygon helper) 25,000 / 24,000 for one
// of 10 by 10 pixels, 40,000 / 67,000 for 50 by 50, 93,000 / 140,000 for 100 by 100; Text
// 12,000 a character with a background, 15,000 without; Plot
// 1,500, most of it the call from C#: a lot of pixels want Fill, Line or Text. So a
// frame holds a whole picture's fill, or a few dozen lines; the whole picture can be
// cleared and redrawn about once a frame.

using System;
using System.Runtime.InteropServices;

public static class Bitmap
{
    public const int Width = 320, Height = 240;

    [DllImport("libc")] static safe extern void cx16_bitmap_clear(int color);
    [DllImport("libc")] static safe extern void cx16_bitmap_plot(int x, int y, int color);
    [DllImport("libc")] static safe extern int cx16_bitmap_get(int x, int y);
    [DllImport("libc")] static safe extern void cx16_bitmap_fill(int x, int y, int width, int height, int color);
    [DllImport("libc")] static unsafe extern void cx16_bitmap_columns(int x, int height, byte* first, byte* second, byte* middle, int count, int above, int below);
    [DllImport("libc")] static safe extern void cx16_bitmap_triangle(int x0, int y0, int x1, int y1, int x2, int y2, int color);
    [DllImport("libc")] static safe extern void cx16_bitmap_circle(int centerX, int centerY, int radius, int color);
    [DllImport("libc")] static safe extern void cx16_bitmap_line(int x0, int y0, int x1, int y1, int color);
    [DllImport("libc")] static unsafe extern void cx16_bitmap_text(int x, int y, char* text, int count, int foreground, int background);

    /// <summary>Switches to the screen mode with the bitmap (CX16.Graphics320x240): 40 by 30 text over a 320 by 240 picture. Returns whether it worked.</summary>
    public static bool Enable() => CX16.SetMode(CX16.Graphics320x240);

    /// <summary>Whether the bitmap is on, that is, whether the screen is in its mode.</summary>
    public static bool IsEnabled => CX16.Mode == CX16.Graphics320x240;

    /// <summary>Sets every pixel to a color (0-255).</summary>
    public static void Clear(int color = 0) => cx16_bitmap_clear(color);

    /// <summary>Sets a pixel. Nothing happens outside the picture.</summary>
    public static void Plot(int x, int y, int color) => cx16_bitmap_plot(x, y, color);

    /// <summary>The color of a pixel, or -1 outside the picture.</summary>
    public static int Get(int x, int y) => cx16_bitmap_get(x, y);

    /// <summary>Fills a rectangle with its top left corner at x, y.</summary>
    public static void Fill(int x, int y, int width, int height, int color) => cx16_bitmap_fill(x, y, width, height, color);

    /// <summary>
    /// Draws a picture made of columns, as a raycaster's is (samples/Maze.cs). Column x + i, from the top
    /// of the screen down to height: the color above down to the row first[i], middle[i] from there to
    /// the row second[i], and the color below from there on. As many columns as the shortest of the three
    /// spans. One call for them all is far faster than one for each. What is outside the screen is cut off.
    /// </summary>
    public static void Columns(int x, int height, ReadOnlySpan<byte> first, ReadOnlySpan<byte> second,
        ReadOnlySpan<byte> middle, int above, int below)
    {
        int count = Math.Min(first.Length, Math.Min(second.Length, middle.Length));
        unsafe
        {
            // SAFETY: the runtime reads count bytes of each span, none of which is shorter.
            fixed (byte* f = first)
            fixed (byte* s = second)
            fixed (byte* m = middle)
                cx16_bitmap_columns(x, height, f, s, m, count, above, below);
        }
    }

    /// <summary>Draws the outline of a rectangle, a pixel thick, in the same place as Fill's.</summary>
    public static void Rectangle(int x, int y, int width, int height, int color)
    {
        if (width <= 0 || height <= 0)
            return;
        cx16_bitmap_fill(x, y, width, 1, color);
        if (height > 1)
            cx16_bitmap_fill(x, y + height - 1, width, 1, color);
        if (height > 2)
        {
            cx16_bitmap_fill(x, y + 1, 1, height - 2, color);
            if (width > 1)
                cx16_bitmap_fill(x + width - 1, y + 1, 1, height - 2, color);
        }
    }

    /// <summary>Draws a line between two points, both included.</summary>
    public static void Line(int x0, int y0, int x1, int y1, int color) => cx16_bitmap_line(x0, y0, x1, y1, color);

    /// <summary>Draws the outline of a triangle, three lines, the corners included.</summary>
    public static void Triangle(int x0, int y0, int x1, int y1, int x2, int y2, int color)
    {
        cx16_bitmap_line(x0, y0, x1, y1, color);
        cx16_bitmap_line(x1, y1, x2, y2, color);
        cx16_bitmap_line(x2, y2, x0, y0, color);
    }

    /// <summary>Fills a triangle with corners at three points, in any order. A pixel is inside when its center is, so that triangles with a side in common leave no gap and don't overlap: one that is flat on the top or on the left gets that row or column, the one next to it doesn't. Nothing is drawn for a triangle with no height. What is outside the picture is cut off; a corner more than 8000 pixels away is taken for one at 8000. With VERA FX a triangle wholly on the picture is drawn by the chip's polygon helper, whose edges are within 1/32 of a pixel of the exact ones: a pixel whose center is that close to a side may differ from what the chip without FX draws. Two triangles that share a side still agree on it when both are wholly on the picture; one drawn by the helper next to one that is cut by the picture's edge may leave or double a pixel along the common side.</summary>
    public static void FillTriangle(int x0, int y0, int x1, int y1, int x2, int y2, int color)
        => cx16_bitmap_triangle(x0, y0, x1, y1, x2, y2, color);

    /// <summary>Draws the outline of a circle, a pixel thick.</summary>
    public static void Circle(int centerX, int centerY, int radius, int color) => cx16_bitmap_circle(centerX, centerY, radius, color);

    /// <summary>Draws text with its top left corner at x, y, 8 pixels to a character, in the glyphs of the character set (ISO text, as CX16.Print has it). With a background color (0-255), the characters' boxes are filled with it; without, the picture shows between the strokes. Returns the x after the text.</summary>
    public static int Text(int x, int y, string text, int foreground, int background = -1)
    {
        if (text == null)
            return x;
        unsafe
        {
            // SAFETY: the pointer and length are the string's own.
            fixed (char* p = text)
                cx16_bitmap_text(x, y, p, text.Length, foreground, background);
        }
        return x + text.Length * 8;
    }
}
