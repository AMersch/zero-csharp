// The Commander X16: its text screen (80x60 characters, 16 colors), sprites,
// the keyboard, joysticks, a clock, and sound. Only for programs built with
// ./build.sh --cx16; the functions are in cx16/runtime.c, and a program for
// another target that calls them won't link.
//
//   CX16.Clear(CX16.Black);
//   CX16.Border(CX16.Black);
//   CX16.Print(4, 2, "Hello from C#", CX16.Yellow);    // column, row
//   while (CX16.ReadKey() == 0)
//   {
//       CX16.WaitFrame();                                // 60 times a second
//       CX16.Tone(0, 440, 40);                           // A, on voice 0
//   }
//
// Out.Line and In.ReadLine work as everywhere, through the X16's own screen
// editor: text scrolls, and a typed line is handed over at RETURN. Print
// puts characters at a position without moving that cursor.
//
// The screen is in ISO text: a character is its Latin-1 code, so Print takes
// accents and signs as they are. The first Clear switches to it, and leaving
// the program switches back.
//
// The video chip (VERA) has 128 KB of memory of its own, which Peek and Poke
// don't reach: VPeek, VPoke and the sprite functions do. Everything here is a
// few such calls, and each costs a few hundred processor cycles (a call from
// C# passes 32-bit numbers through an 8-bit processor); Print loops in C.

using System;
using System.Runtime.InteropServices;

public static class CX16
{
    // The 16 colors of the video chip's palette at startup: the C64's.
    public const int Black = 0, White = 1, Red = 2, Cyan = 3, Purple = 4, Green = 5, Blue = 6, Yellow = 7;
    public const int Orange = 8, Brown = 9, LightRed = 10, DarkGray = 11, Gray = 12, LightGreen = 13;
    public const int LightBlue = 14, LightGray = 15;

    // Joystick: what Joystick returns, added up. Fire is the A button, or Alt
    // on the keyboard; Fire2 is B, or Ctrl.
    public const int Up = 1, Down = 2, Left = 4, Right = 8, Fire = 16, Fire2 = 32;

    // Sound: the shape of a voice's wave.
    public const int Pulse = 0, Sawtooth = 1, Triangle = 2, Noise = 3;

    // Where things are in the video chip's memory.
    const int SpriteShapes = 0x4000;        // 128 bytes a shape, up to 64 shapes
    const int SpriteAttributes = 0x1FC00;   // 8 bytes a sprite, 128 sprites
    const int VideoRegister = 0x9F29;       // bit 6: sprites on
    const int BorderRegister = 0x9F2C;      // the border's palette index

    [DllImport("libc")] static safe extern int cx16_peek(int address);
    [DllImport("libc")] static safe extern void cx16_poke(int address, int value);
    [DllImport("libc")] static safe extern int cx16_vpeek(int address);
    [DllImport("libc")] static safe extern void cx16_vpoke(int address, int value);
    [DllImport("libc")] static unsafe extern void cx16_vcopy(int address, byte* source, int count);
    [DllImport("libc")] static safe extern void cx16_clear(int background);
    [DllImport("libc")] static safe extern int cx16_columns();
    [DllImport("libc")] static safe extern int cx16_rows();
    [DllImport("libc")] static unsafe extern void cx16_print(int column, int row, char* text, int count, int color);
    [DllImport("libc")] static safe extern void cx16_wait_frame();
    [DllImport("libc")] static safe extern int cx16_key();
    [DllImport("libc")] static safe extern uint cx16_ticks();
    [DllImport("libc")] static safe extern int cx16_joystick(int number);
    [DllImport("libc")] static safe extern void cx16_voice(int voice, int frequency, int volume, int waveform);

    // ---- memory ------------------------------------------------------------------

    /// <summary>The byte at an address of the processor's memory (0-65535); $A000-$BFFF is the RAM bank the program runs with.</summary>
    public static int Peek(int address) => cx16_peek(address);

    /// <summary>Writes a byte to an address of the processor's memory. Nothing stops it writing into the program itself.</summary>
    public static void Poke(int address, int value) => cx16_poke(address, value);

    /// <summary>The byte at an address of the video chip's memory (0-131071).</summary>
    public static int VPeek(int address) => cx16_vpeek(address);

    /// <summary>Writes a byte to the video chip's memory.</summary>
    public static void VPoke(int address, int value) => cx16_vpoke(address, value);

    // ---- the screen ----------------------------------------------------------------

    /// <summary>Characters across the text screen: 80 at startup.</summary>
    public static int Columns => cx16_columns();

    /// <summary>Rows of the text screen: 60 at startup.</summary>
    public static int Rows => cx16_rows();

    /// <summary>Empties the screen, sets its background color, and puts Out's cursor in the top left corner. Out's text is white (black on white) from then on. The first call also switches to ISO text.</summary>
    public static void Clear(int background = Blue)
    {
        cx16_clear(background);
    }

    /// <summary>Sets the color of the frame around the screen.</summary>
    public static void Border(int color) => cx16_poke(BorderRegister, color);

    /// <summary>Writes text at a column and row, without moving Out's cursor. Returns the column after it. Text that reaches the end of a row goes on in the next. The color is the text's, plus 16 for each step of a background color: Print(0, 0, "x", White + 16 * Blue).</summary>
    public static int Print(int column, int row, string text, int color = White)
    {
        if (text == null || !InScreen(column, row, text.Length))
            return column;
        unsafe
        {
            // SAFETY: the pointer and length are the string's own, and
            // InScreen checked that the cells are on the screen.
            fixed (char* p = text)
                cx16_print(column, row, p, text.Length, color);
        }
        return column + text.Length;
    }

    /// <summary>Writes one character at a column and row.</summary>
    public static void Print(int column, int row, char character, int color = White)
    {
        if (!InScreen(column, row, 1))
            return;
        unsafe
        {
            // SAFETY: one character, a local's; InScreen checked the cell.
            cx16_print(column, row, &character, 1, color);
        }
    }

    /// <summary>Writes a number at a column and row. Returns the column after it.</summary>
    public static int Print(int column, int row, int value, int color = White)
    {
        Span<char> digits = stackalloc char[11];
        int at = digits.Length;
        uint rest = value < 0 ? (uint)(-value) : (uint)value;
        do
        {
            digits[--at] = (char)('0' + rest % 10);
            rest /= 10;
        } while (rest != 0);
        if (value < 0)
            digits[--at] = '-';

        int count = digits.Length - at;
        if (!InScreen(column, row, count))
            return column;
        unsafe
        {
            // SAFETY: at..at+count is inside digits, and InScreen checked
            // that the cells are on the screen.
            fixed (char* p = digits)
                cx16_print(column, row, p + at, count, color);
        }
        return column + count;
    }

    static bool InScreen(int column, int row, int count)
    {
        int columns = cx16_columns();
        return column >= 0 && column < columns && row >= 0 && row < cx16_rows()
            && row * columns + column + count <= columns * cx16_rows();
    }

    // ---- sprites -------------------------------------------------------------------

    /// <summary>Gives sprite 0-127 a shape and shows it in front of the text. The shape is 128 bytes: 16 rows of 16 pixels, 4 bits a pixel, two pixels to a byte, the left one in the high bits. A pixel's value picks one of 16 colors from the palette, from the 16 that start at 16 times the palette offset (0-15); 0 is see-through. There is room for 64 shapes (slot 0-63); sprites can share one.</summary>
    public static void Sprite(int sprite, int slot, ReadOnlySpan<byte> shape, int paletteOffset = 0)
    {
        if ((uint)sprite > 127 || (uint)slot > 63 || shape.Length != 128 || (uint)paletteOffset > 15)
            return;
        int address = SpriteShapes + slot * 128;
        unsafe
        {
            // SAFETY: the pointer and length come from the span; the 128
            // bytes land in the slot's block of video memory.
            fixed (byte* p = shape)
                cx16_vcopy(address, p, shape.Length);
        }
        int attributes = SpriteAttributes + sprite * 8;
        cx16_vpoke(attributes, address >> 5);           // address bits 12-5
        cx16_vpoke(attributes + 1, address >> 13);      // bits 16-13, and 4 bits a pixel
        cx16_vpoke(attributes + 6, 0x0C);               // in front of both layers
        cx16_vpoke(attributes + 7, 0x50 | paletteOffset);   // 16 pixels high and wide
        cx16_poke(VideoRegister, cx16_peek(VideoRegister) | 0x40);
    }

    /// <summary>Moves a sprite. Its top left corner is at the screen's top left corner when x and y are 0; the picture is 640 by 480.</summary>
    public static void MoveSprite(int sprite, int x, int y)
    {
        if ((uint)sprite > 127)
            return;
        int attributes = SpriteAttributes + sprite * 8;
        cx16_vpoke(attributes + 2, x);
        cx16_vpoke(attributes + 3, (x >> 8) & 3);
        cx16_vpoke(attributes + 4, y);
        cx16_vpoke(attributes + 5, (y >> 8) & 3);
    }

    /// <summary>Hides a sprite.</summary>
    public static void HideSprite(int sprite)
    {
        if ((uint)sprite <= 127)
            cx16_vpoke(SpriteAttributes + sprite * 8 + 6, 0);   // z-depth 0: off
    }

    // ---- timing --------------------------------------------------------------------

    /// <summary>Waits for the start of the next frame: 60 times a second, and the moment to move things without flicker.</summary>
    public static void WaitFrame() => cx16_wait_frame();

    /// <summary>Sixtieths of a second since the machine was switched on.</summary>
    public static uint Ticks => cx16_ticks();

    // ---- keyboard and joysticks -------------------------------------------------------

    /// <summary>The next key pressed, as its character ('a', 'A', '1', ' ', '\n' for RETURN), or 0 if no key waits. Keys without an ASCII character come as the KERNAL's own codes (cursor down is 17, F1 is 133).</summary>
    public static int ReadKey() => cx16_key();

    /// <summary>A joystick: Up, Down, Left, Right, Fire and Fire2, added up; 0 when it is at rest. Number 0 is the keyboard (cursor keys, Alt and Ctrl), 1-4 are the controller ports. A controller that isn't there reads as 128.</summary>
    public static int Joystick(int number = 0) => (uint)number > 4 ? 128 : cx16_joystick(number);

    // ---- sound -----------------------------------------------------------------------

    /// <summary>Plays a note on one of the 16 voices (0-15): a frequency in hertz (about 0-12000) at a volume of 0-63, in a wave shape: Pulse, Sawtooth, Triangle or Noise. Volume 0 silences it.</summary>
    public static void Tone(int voice, int hertz, int volume, int waveform = Pulse)
    {
        if ((uint)voice > 15 || (uint)waveform > 3)
            return;
        // The chip's frequency word: hertz * 2^17 / 48828 (its sample rate).
        int word = (int)((uint)hertz * 2684 / 1000);
        cx16_voice(voice, word, volume, waveform);
    }

    /// <summary>Silences a voice.</summary>
    public static void Silence(int voice) => Tone(voice, 0, 0);
}
