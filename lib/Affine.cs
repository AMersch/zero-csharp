// Pictures rotated, scaled and sheared into the Commander X16's bitmap (see Bitmap),
// by the affine helper of the video chip's VERA FX. Only for programs built with
// ./build.sh --cx16; the functions are in cx16/runtime.c, and a program for another
// target that calls them won't link.
//
//   Affine.SetTile(1, picture);                 // 8x8 pixels, a byte each
//   Affine.SetMap(0, 0, 1);                     // tile 1 in the map's top left corner
//   Affine.Source();                            // the map and tiles where SetTile put them
//   Bitmap.Enable();
//   Affine.Draw(100, 60, 64, 64, 0, 0, 128, 0, 0, 128);     // twice the size
//
// A map of 128 is 16 KB, which doesn't fit where Map is: give both addresses, for example
// Affine.Source(128, tileAddress: 0x19000, mapAddress: 0x15000) for 128 tiles.
//
// The source is a square map of 2, 8, 32 or 128 tiles each way, a byte for each
// place (the tile's number, 0-255), and up to 256 tiles of 8 by 8 pixels with 8 bits
// each; so the source is a picture of 16 to 1024 pixels each way, put together from
// tiles. For each pixel drawn the position in the source moves on by (du, dv), and
// for each row by (uRow, vRow), all in 256ths of a source pixel: (256, 0, 0, 256)
// copies; (128, 0, 0, 128) draws twice as large; for a rotation by an angle
// (CX16.Sin and Cos give 32767 for 1), du = Cos >> 7, dv = Sin >> 7, uRow = -dv and
// vRow = du. Outside the map the source repeats, or, with clip, shows tile 0.
//
// Without VERA FX the same pixels are worked out in software, many times slower.

using System;
using System.Runtime.InteropServices;

public static class Affine
{
    /// <summary>Where SetTile and SetMap put the tiles (16 KB for 256 of them) and the map, unless told otherwise: in the part of the video memory the sprites' own addresses use.</summary>
    public const int Tiles = 0x15000, Map = 0x19000;

    [DllImport("libc")] static safe extern int cx16_texture(int tiles, int map, int size, int clip);
    [DllImport("libc")] static safe extern void cx16_affine(int x, int y, int width, int height, int u, int v, int du, int dv, int uRow, int vRow, int transparent);
    [DllImport("libc")] static safe extern void cx16_vpoke(int address, int value);
    [DllImport("libc")] static unsafe extern void cx16_vcopy(int address, byte* source, int count);

    const int TextScreen = 0x1B000;

    static int s_size = 8, s_tiles = Tiles, s_map = Map;

    /// <summary>
    /// Says what to draw from: a map of size by size tiles (2, 8, 32 or 128) and the tiles' pictures, at addresses
    /// of the video memory that are multiples of 2048. clip: outside the map show tile 0 instead of the map
    /// again. Returns false, and changes nothing, for a size or an address that isn't allowed, or a map that
    /// would reach into the text screen ($1B000 up). SetTile and SetMap write where this says.
    /// </summary>
    public static bool Source(int size = 8, int tileAddress = Tiles, int mapAddress = Map, bool clip = false)
    {
        if (mapAddress + size * size > TextScreen || cx16_texture(tileAddress, mapAddress, size, clip ? 1 : 0) == 0)
            return false;
        s_size = size;
        s_tiles = tileAddress;
        s_map = mapAddress;
        return true;
    }

    /// <summary>Sets the picture of tile 0-255: 64 bytes, 8 rows of 8 pixels, a color of the palette each. False for a tile that would reach into the text screen.</summary>
    public static bool SetTile(int index, ReadOnlySpan<byte> pixels)
    {
        if ((uint)index > 255 || pixels.Length != 64 || s_tiles + index * 64 + 64 > TextScreen)
            return false;
        unsafe
        {
            // SAFETY: the pointer and length are the span's own; the address was checked.
            fixed (byte* p = pixels)
                cx16_vcopy(s_tiles + index * 64, p, 64);
        }
        return true;
    }

    /// <summary>Puts a tile at a column and row of the map.</summary>
    public static bool SetMap(int column, int row, int tile)
    {
        if ((uint)column >= (uint)s_size || (uint)row >= (uint)s_size || (uint)tile > 255)
            return false;
        cx16_vpoke(s_map + row * s_size + column, tile);
        return true;
    }

    /// <summary>
    /// Draws a rectangle of the bitmap from the source: the pixel at (x, y) is the source's at (u, v), the next
    /// one along the row at (u + du, v + dv), the one below at (u + uRow, v + vRow), all in 256ths of a source
    /// pixel (so u = 8 * 256 is the ninth column). What is outside the bitmap is cut off. With transparent,
    /// source pixels of color 0 leave the picture as it was. Does nothing outside the bitmap's screen mode, or
    /// before Source has been called.
    /// </summary>
    public static void Draw(int x, int y, int width, int height, int u, int v, int du, int dv, int uRow, int vRow, bool transparent = false)
        => cx16_affine(x, y, width, height, u, v, du, dv, uRow, vRow, transparent ? 1 : 0);
}
