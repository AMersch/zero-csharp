// The Commander X16: its text screen (80x60 characters, 16 colors), sprites,
// the keyboard, the mouse, joysticks, a clock, and sound. Only for programs built with
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

    // Mouse: what MouseButtons returns, added up.
    public const int LeftButton = 1, RightButton = 2, MiddleButton = 4;

    // Sound: the shape of a voice's wave.
    public const int Pulse = 0, Sawtooth = 1, Triangle = 2, Noise = 3;

    // Screen modes, for SetMode: text of so many columns and rows, or 320x240 pixels of 256
    // colors with 40x30 text above them.
    public const int Text80x60 = 0x00, Text80x30 = 0x01, Text40x60 = 0x02, Text40x30 = 0x03;
    public const int Text40x15 = 0x04, Text20x30 = 0x05, Text20x15 = 0x06, Graphics320x240 = 0x80;

    // Where things are in the video chip's memory.
    const int SpriteShapes = 0x13000;       // 128 bytes a shape, up to 64 shapes: $13000-$14FFF, in the KERNAL's sprite area
    const int SpriteAttributes = 0x1FC00;   // 8 bytes a sprite, 128 sprites
    const int VideoRegister = 0x9F29;       // bit 6: sprites on
    const int BorderRegister = 0x9F2C;      // the border's palette index

    [DllImport("libc")] static safe extern int cx16_peek(int address);
    [DllImport("libc")] static safe extern void cx16_poke(int address, int value);
    [DllImport("libc")] static safe extern int cx16_vpeek(int address);
    [DllImport("libc")] static safe extern void cx16_vpoke(int address, int value);
    [DllImport("libc")] static unsafe extern void cx16_vcopy(int address, byte* source, int count);
    [DllImport("libc")] static safe extern void cx16_clear(int background);
    [DllImport("libc")] static safe extern int cx16_mode(int mode);
    [DllImport("libc")] static safe extern int cx16_mode_number();
    [DllImport("libc")] static safe extern int cx16_columns();
    [DllImport("libc")] static safe extern int cx16_rows();
    [DllImport("libc")] static unsafe extern void cx16_print(int column, int row, char* text, int count, int color);
    [DllImport("libc")] static safe extern void cx16_wait_frame();
    [DllImport("libc")] static safe extern int cx16_key();
    [DllImport("libc")] static safe extern uint cx16_ticks();
    [DllImport("libc")] static safe extern int cx16_joystick(int number);
    [DllImport("libc")] static safe extern void cx16_mouse_config(int show);
    [DllImport("libc")] static safe extern int cx16_mouse(int field);
    [DllImport("libc")] static safe extern void cx16_set_date_time(int year, int month, int day, int hour, int minute, int second);
    [DllImport("libc")] static safe extern int cx16_entropy();
    [DllImport("libc")] static safe extern int cx16_in_emulator();
    [DllImport("libc")] static safe extern void cx16_start_recording();
    [DllImport("libc")] static safe extern int cx16_multiply(int a, int b);
    [DllImport("libc")] static unsafe extern int cx16_dot(short* a, short* b, int count);
    [DllImport("libc")] static safe extern int cx16_has_fx();
    [DllImport("libc")] static safe extern int cx16_use_fx(int on);
    [DllImport("libc")] static safe extern void cx16_set_color(int index, int red, int green, int blue);
    [DllImport("libc")] static safe extern int cx16_get_color(int index);
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

    /// <summary>The screen mode: one of Text80x60 (the one at startup) and the others above.</summary>
    public static int Mode => cx16_mode_number();

    /// <summary>
    /// Switches the screen mode. Returns false, and changes nothing, for a number that isn't a mode.
    /// The screen is cleared and Out's cursor goes to the top left. Sprites stay on, and the mouse
    /// (see MouseOn) is given the new screen's size; the sprites' positions, shapes and
    /// colors are as they were, but a program that has changed the video chip's own registers
    /// has to do that again. In the modes with 40 or fewer columns the characters are 16 pixels wide,
    /// and in Graphics320x240 the whole picture is 320 by 240 pixels, sprites and the mouse
    /// included.
    /// </summary>
    public static bool SetMode(int mode) => cx16_mode(mode) != 0;

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

    // ---- multiplying with the video chip --------------------------------------------------------

    /// <summary>a times b, two 16-bit numbers, as a 32-bit one, by the video chip's multiplier where it has one (see HasVeraFx). About five times faster than C#'s a * b with the chip (400 against 2,200 cycles, the 6502 has no multiply instruction), but only for 16-bit factors; for many products to be added up, Dot is faster still.</summary>
    public static int Multiply(short a, short b) => cx16_multiply(a, b);

    /// <summary>The sum of a[i] * b[i] over two spans of the same length, as for a vector or a filter, wrapping round at 32 bits; the video chip multiplies and adds up. Returns 0 if the lengths differ.</summary>
    public static int Dot(ReadOnlySpan<short> a, ReadOnlySpan<short> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0;
        unsafe
        {
            // SAFETY: both pointers and the one length are the spans' own, and the lengths are equal.
            fixed (short* p = a)
            fixed (short* q = b)
                return cx16_dot(p, q, a.Length);
        }
    }

    // ---- the emulator ------------------------------------------------------------------------

    /// <summary>Whether the program is running in the emulator (x16emu), which has debug registers a real X16 doesn't.</summary>
    public static bool InEmulator => cx16_in_emulator() != 0;

    /// <summary>In the emulator started with -wav FILE: starts recording what it plays, from now on. This is how the sound is tested (cx16/test-audio.sh). Does nothing on a real X16.</summary>
    public static void StartRecording() => cx16_start_recording();

    // ---- the video chip's helpers ----------------------------------------------------------

    /// <summary>Whether the video chip has VERA FX (from VERA 0.3.1 on, and in the emulator): helpers that draw faster. Bitmap uses them when they are there, and does the same without them, slower.</summary>
    public static bool HasVeraFx => cx16_has_fx() != 0;

    /// <summary>Whether Bitmap and Tiles use the video chip's helpers: true if it has them, unless a program says false, to compare the two ways or to test the one that works without them. Setting it true on a chip without them has no effect.</summary>
    public static bool UseVeraFx
    {
        get => cx16_use_fx(-1) != 0;
        set => cx16_use_fx(value ? 1 : 0);
    }

    // ---- the palette ------------------------------------------------------------------

    /// <summary>Sets one of the 256 colors of the palette (all text, sprites and the bitmap take theirs from it) to an amount of red, green and blue, each 0-15. Colors 0-15 start as the C64's (Black ... LightGray), 16-31 as a ramp of grays, the rest as hues.</summary>
    public static void SetColor(int index, int red, int green, int blue)
    {
        if ((uint)index < 256)
            cx16_set_color(index, red, green, blue);
    }

    /// <summary>The amount of red, green and blue of a palette color, as 12 bits: red in bits 8-11, green 4-7, blue 0-3.</summary>
    public static int GetColor(int index) => (uint)index < 256 ? cx16_get_color(index) : 0;

    // ---- sprites -------------------------------------------------------------------

    /// <summary>Gives sprite 0-127 a shape and shows it in front of the text. The shape is 128 bytes: 16 rows of 16 pixels, 4 bits a pixel, two pixels to a byte, the left one in the high bits. A pixel's value picks one of 16 colors from the palette, from the 16 that start at 16 times the palette offset (0-15); 0 is see-through. There is room for 64 shapes (slot 0-63); sprites can share one. For other sizes and 256 colors, see the next one.</summary>
    public static void Sprite(int sprite, int slot, ReadOnlySpan<byte> shape, int paletteOffset = 0)
    {
        if ((uint)slot > 63)
            return;
        Sprite(sprite, SpriteShapes + slot * 128, shape, 16, 16, 4, paletteOffset);
    }

    /// <summary>
    /// Gives sprite 0-127 a shape of any size and shows it in front of the text.
    /// The shape goes to an address of the video chip's memory (a multiple of 32, below
    /// $1F9C0; the text screen and its font are above $1B000, and lib/CX16.cs's own
    /// shapes at $13000-$14FFF; the KERNAL keeps sprites in $13000-$1AFFF and a bitmap in $00000-$12BFF). Its width and height are each 8, 16, 32 or 64 pixels, with 4 or
    /// 8 bits a pixel, the leftmost pixel in the highest bits, row after row. With 8
    /// bits a pixel, a pixel's value is a color of the palette (0 is see-through); with
    /// 4, it is one of the 16 colors that start at 16 times the palette offset (0-15).
    /// Returns false, and changes nothing, if the shape's size doesn't fit these.
    /// </summary>
    public static bool Sprite(int sprite, int address, ReadOnlySpan<byte> shape, int width, int height, int bitsPerPixel = 4, int paletteOffset = 0)
    {
        int w = SizeCode(width), h = SizeCode(height);
        if ((uint)sprite > 127 || w < 0 || h < 0 || (bitsPerPixel != 4 && bitsPerPixel != 8)
            || (uint)paletteOffset > 15 || address < 0 || (address & 31) != 0
            || shape.Length != width * height * bitsPerPixel / 8 || address + shape.Length > 0x1F9C0)
            return false;
        unsafe
        {
            // SAFETY: the pointer and length come from the span, which fits
            // below the sound chip's registers in video memory (checked above).
            fixed (byte* p = shape)
                cx16_vcopy(address, p, shape.Length);
        }
        int attributes = SpriteAttributes + sprite * 8;
        cx16_vpoke(attributes, address >> 5);                                   // address bits 12-5
        cx16_vpoke(attributes + 1, (address >> 13) | (bitsPerPixel == 8 ? 0x80 : 0));   // bits 16-13, and the color depth
        cx16_vpoke(attributes + 6, 0x0C);                                       // in front of both layers
        cx16_vpoke(attributes + 7, h << 6 | w << 4 | paletteOffset);
        cx16_poke(VideoRegister, cx16_peek(VideoRegister) | 0x40);
        return true;
    }

    // A sprite's width or height as the video chip has it: 8, 16, 32 and 64 are 0-3.
    static int SizeCode(int pixels) => pixels == 8 ? 0 : pixels == 16 ? 1 : pixels == 32 ? 2 : pixels == 64 ? 3 : -1;

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

    // ---- sine and cosine, from a table --------------------------------------------------

    // A quarter of the sine wave: entry i is 32767 * sin(i / 256 of a turn), for i from 0 to 64.
    static ReadOnlySpan<short> QuarterWave => new short[]
    {
        0, 804, 1608, 2410, 3212, 4011, 4808, 5602, 6393, 7179, 7962, 8739, 9512, 10278, 11039, 11793,
        12539, 13279, 14010, 14732, 15446, 16151, 16846, 17530, 18204, 18868, 19519, 20159, 20787, 21403, 22005, 22594,
        23170, 23731, 24279, 24811, 25329, 25832, 26319, 26790, 27245, 27683, 28105, 28510, 28898, 29268, 29621, 29956,
        30273, 30571, 30852, 31113, 31356, 31580, 31785, 31971, 32137, 32285, 32412, 32521, 32609, 32678, 32728, 32757,
        32767,
    };

    /// <summary>The sine of an angle given in 256ths of a turn (any whole number: it goes round), as a number from -32767 to 32767, where 32767 is 1. A table of 65 numbers, good to 1.5e-5, and a hundred cycles or so: MathF.Sin takes 24,000, Math.Sin 400,000. For angles that are not a multiple of 1/256 of a turn, or for more digits, use those.</summary>
    public static int Sin(int angle)
    {
        int a = angle & 255;
        bool negative = a >= 128;
        a &= 127;
        if (a > 64)
            a = 128 - a;
        int value = QuarterWave[a];
        return negative ? -value : value;
    }

    /// <summary>The cosine of an angle in 256ths of a turn, as Sin has it.</summary>
    public static int Cos(int angle) => Sin(angle + 64);

    // ---- the mouse -----------------------------------------------------------------

    /// <summary>Switches the mouse on, with the pointer drawn by sprite 0, which the KERNAL then moves in every frame's interrupt. With defaultCursor the sprite is the KERNAL's arrow; without, it is whatever the program gave sprite 0 (see Sprite). It needs the machine's interrupts: it does nothing in the emulator's headless mode.</summary>
    public static void MouseOn(bool defaultCursor = true) => cx16_mouse_config(defaultCursor ? 1 : 0xFF);

    /// <summary>Switches the mouse, and its pointer, off.</summary>
    public static void MouseOff() => cx16_mouse_config(0);

    /// <summary>The pointer's column in pixels, 0 at the left edge of the screen: 0-639 with the 80x60 text screen.</summary>
    public static int MouseX => cx16_mouse(0);

    /// <summary>The pointer's row in pixels, 0 at the top.</summary>
    public static int MouseY => cx16_mouse(1);

    /// <summary>The buttons held down: LeftButton, RightButton and MiddleButton, added up.</summary>
    public static int MouseButtons => cx16_mouse(2);

    // ---- timing --------------------------------------------------------------------

    /// <summary>Waits for the start of the next frame: 60 times a second, and the moment to move things without flicker.</summary>
    public static void WaitFrame() => cx16_wait_frame();

    /// <summary>Sixtieths of a second since the machine was switched on.</summary>
    public static uint Ticks => cx16_ticks();

    /// <summary>Sets the real-time clock, which keeps the date and the time of day while the machine is off (if it has a battery). Clock.UtcNow reads it, as the time that was set: the X16 knows no time zone. The year is 1900 to 2155.</summary>
    public static void SetDateTime(int year, int month, int day, int hour, int minute, int second)
    {
        if (year < 1900 || year > 2155 || month < 1 || month > 12 || day < 1 || day > 31
            || (uint)hour > 23 || (uint)minute > 59 || (uint)second > 59)
            return;
        cx16_set_date_time(year, month, day, hour, minute, second);
    }

    /// <summary>24 random bits from the hardware (the KERNAL's entropy source): different every time, and fine as a seed. For many random numbers, give one to a Random: new Random((ulong)CX16.Entropy).</summary>
    public static int Entropy => cx16_entropy();

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
        int word = (int)(((uint)hertz * 2684 + 500) / 1000);      // rounded: 440 Hz is 1181, which plays 439.96
        cx16_voice(voice, word, volume, waveform);
    }

    /// <summary>Silences a voice.</summary>
    public static void Silence(int voice) => Tone(voice, 0, 0);
}
