// The Commander X16's tile layer: a map of tiles, each a small picture from a set,
// that scrolls smoothly: the background of most games. Only for programs built with
// ./build.sh --cx16; the functions are in cx16/runtime.c, and a program for another
// target that calls them won't link.
//
//   Tiles.Enable(64, 64, 8, 4);                     // a map of 64 by 64 tiles of 8x8 pixels, 16 colors
//   Tiles.SetTile(1, brick);                        // tile 1's pixels: 32 bytes of 4 bits each
//   Tiles.Fill(1);
//   Tiles.Set(5, 3, 2, paletteOffset: 1);           // column, row, tile
//   Tiles.Scroll(frame, 0);                         // the picture moves left
//
// The tiles are layer 0 of the video chip, behind the text (layer 1) and the
// sprites. It is also the layer the bitmap uses (see Bitmap), so each takes it from
// the other; and a screen mode change (CX16.SetMode) switches it off, so Enable is
// called again after one.
//
// A map is 32, 64, 128 or 256 tiles each way, two bytes a tile, from MapAddress in
// the video memory; the tiles' pictures are from TileAddress on, 2, 4 or 8 bits a
// pixel and 8x8 or 16x16 pixels. A tile's index is 0-1023. Pixel color 0 is see-through
// (so tile 0, empty at first, shows what is behind the layer); with 4 bits a pixel
// a map entry's palette offset (0-15) adds 16 times itself to colors 1-15; colors above
// 15 are not changed. With the defaults the map starts at $00000 and the tiles
// follow it, in the 75 KB below the sprites' part of the video memory ($13000), the
// same as the bitmap's; see the how-to for the whole plan.
//
// Measured, in processor cycles at 8 MHz (a frame is 133,000): Fill 13,000 for a map of
// 64 by 64 (120,000 on a chip without VERA FX, which fills four bytes at a time with its
// cache); Set and Get about 1,000 a tile; Scroll 400; SetTile 2,700 for 32 bytes. So a
// frame can change a hundred tiles and scroll, and a whole map is refilled in a tenth of one.

using System;
using System.Runtime.InteropServices;

public static class Tiles
{
    // The video chip's registers for layer 0, and the one that switches layers on.
    const int Config = 0x9F2D, MapBase = 0x9F2E, TileBase = 0x9F2F;
    const int HScroll = 0x9F30, VScroll = 0x9F32, Video = 0x9F29;
    const int EndOfVideoMemory = 0x1F9C0;      // the video chip's own registers follow

    static int s_columns, s_rows, s_tileSize, s_bits, s_mapAddress, s_tileAddress;

    [DllImport("libc")] static safe extern int cx16_peek(int address);
    [DllImport("libc")] static safe extern void cx16_poke(int address, int value);
    [DllImport("libc")] static safe extern int cx16_vpeek(int address);
    [DllImport("libc")] static safe extern void cx16_vpoke2(int address, int first, int second);
    [DllImport("libc")] static safe extern void cx16_vfill2(int address, int count, int first, int second);
    [DllImport("libc")] static unsafe extern void cx16_vcopy(int address, byte* source, int count);

    static int SizeCode(int tiles) => tiles == 32 ? 0 : tiles == 64 ? 1 : tiles == 128 ? 2 : tiles == 256 ? 3 : -1;

    /// <summary>
    /// Sets layer 0 up as a tile layer and switches it on, with every tile of the map set to 0
    /// (see-through at first). Columns and rows are 32, 64, 128 or 256; tileSize is 8 or 16
    /// pixels; bitsPerPixel is 2, 4 or 8. The map goes to mapAddress (a multiple of 512, default
    /// 0) and the tile pictures to tileAddress (a multiple of 2048, default the next such
    /// address after the map). Returns false, and changes nothing, if the numbers don't fit,
    /// or the map or the first tile would reach into the video chip's registers.
    /// </summary>
    public static bool Enable(int columns = 64, int rows = 64, int tileSize = 8, int bitsPerPixel = 4,
        int mapAddress = 0, int tileAddress = -1)
    {
        int w = SizeCode(columns), h = SizeCode(rows);
        int depth = bitsPerPixel == 2 ? 1 : bitsPerPixel == 4 ? 2 : bitsPerPixel == 8 ? 3 : -1;
        if (w < 0 || h < 0 || depth < 0 || (tileSize != 8 && tileSize != 16) || mapAddress < 0 || (mapAddress & 511) != 0)
            return false;
        int mapBytes = columns * rows * 2;
        if (tileAddress < 0)
            tileAddress = (mapAddress + mapBytes + 2047) & ~2047;
        if ((tileAddress & 2047) != 0 || mapAddress > EndOfVideoMemory - mapBytes || tileAddress >= EndOfVideoMemory)
            return false;

        cx16_poke(Config, h << 6 | w << 4 | depth);
        cx16_poke(MapBase, mapAddress >> 9);
        cx16_poke(TileBase, (tileAddress >> 11) << 2 | (tileSize == 16 ? 3 : 0));
        cx16_poke(Video, cx16_peek(Video) | 0x10);
        s_columns = columns;
        s_rows = rows;
        s_tileSize = tileSize;
        s_bits = bitsPerPixel;
        s_mapAddress = mapAddress;
        s_tileAddress = tileAddress;
        Scroll(0, 0);
        Clear();
        return true;
    }

    /// <summary>Whether layer 0 is on and was set up by Enable.</summary>
    public static bool IsEnabled => s_columns != 0 && (cx16_peek(Video) & 0x10) != 0 && (cx16_peek(Config) & 4) == 0;

    /// <summary>Switches the layer off, keeping its map and tiles (Show brings them back).</summary>
    public static void Hide() => cx16_poke(Video, cx16_peek(Video) & ~0x10);

    /// <summary>Switches the layer on again after Hide.</summary>
    public static void Show()
    {
        if (s_columns != 0)
            cx16_poke(Video, cx16_peek(Video) | 0x10);
    }

    /// <summary>Tiles across the map, as given to Enable; 0 before it.</summary>
    public static int Columns => s_columns;

    /// <summary>Tiles down the map.</summary>
    public static int Rows => s_rows;

    /// <summary>A tile's width and height in pixels: 8 or 16.</summary>
    public static int TileSize => s_tileSize;

    /// <summary>Where the map is in the video memory.</summary>
    public static int MapAddress => s_mapAddress;

    /// <summary>Where tile 0's picture is in the video memory; the others follow, BytesPerTile apart.</summary>
    public static int TileAddress => s_tileAddress;

    /// <summary>The bytes of one tile's picture: 8x8 pixels at 4 bits are 32 bytes; 16x16 at 8 bits, 256.</summary>
    public static int BytesPerTile => s_tileSize * s_tileSize * s_bits / 8;

    /// <summary>
    /// Sets the picture of tile 0-1023: tile size squared pixels, row after row, the leftmost pixel in
    /// the highest bits of its byte, BytesPerTile bytes in all. Returns false, and changes nothing, if
    /// the layer isn't set up, the index is out of range, the length is wrong, or the picture would
    /// reach into the video chip's registers.
    /// </summary>
    public static bool SetTile(int index, ReadOnlySpan<byte> pixels)
    {
        if (s_columns == 0 || (uint)index > 1023 || pixels.Length != BytesPerTile)
            return false;
        int address = s_tileAddress + index * BytesPerTile;
        if (address > EndOfVideoMemory - pixels.Length)
            return false;
        unsafe
        {
            // SAFETY: the pointer and length are the span's own, and the video memory it goes
            // to was checked above.
            fixed (byte* p = pixels)
                cx16_vcopy(address, p, pixels.Length);
        }
        return true;
    }

    /// <summary>Puts a tile (0-1023) at a column and row of the map, with a palette offset (0-15) and flips. Returns false if there is no such place or tile.</summary>
    public static bool Set(int column, int row, int tile, int paletteOffset = 0, bool flipHorizontally = false, bool flipVertically = false)
    {
        if ((uint)column >= (uint)s_columns || (uint)row >= (uint)s_rows || (uint)tile > 1023 || (uint)paletteOffset > 15)
            return false;
        cx16_vpoke2(s_mapAddress + (row * s_columns + column) * 2, tile,
            paletteOffset << 4 | (flipVertically ? 8 : 0) | (flipHorizontally ? 4 : 0) | (tile >> 8));
        return true;
    }

    /// <summary>The tile (0-1023) at a column and row, or -1 if there is no such place.</summary>
    public static int Get(int column, int row)
    {
        if ((uint)column >= (uint)s_columns || (uint)row >= (uint)s_rows)
            return -1;
        int address = s_mapAddress + (row * s_columns + column) * 2;
        return cx16_vpeek(address) | (cx16_vpeek(address + 1) & 3) << 8;
    }

    /// <summary>Sets every place of the map to a tile (0-1023), with no palette offset and no flips.</summary>
    public static void Fill(int tile)
    {
        if (s_columns == 0 || (uint)tile > 1023)
            return;
        cx16_vfill2(s_mapAddress, s_columns * s_rows, tile, tile >> 8);
    }

    /// <summary>Sets every place of the map to tile 0.</summary>
    public static void Clear() => Fill(0);

    /// <summary>Scrolls the layer: the picture moves left by x pixels and up by y (0-4095; what is beyond wraps round to the map's other side).</summary>
    public static void Scroll(int x, int y)
    {
        x &= 4095;
        y &= 4095;
        cx16_poke(HScroll, x);
        cx16_poke(HScroll + 1, x >> 8);
        cx16_poke(VScroll, y);
        cx16_poke(VScroll + 1, y >> 8);
    }

    /// <summary>How far the layer is scrolled to the left, as Scroll set it.</summary>
    public static int ScrollX => cx16_peek(HScroll) | (cx16_peek(HScroll + 1) & 15) << 8;

    /// <summary>How far the layer is scrolled up.</summary>
    public static int ScrollY => cx16_peek(VScroll) | (cx16_peek(VScroll + 1) & 15) << 8;
}
