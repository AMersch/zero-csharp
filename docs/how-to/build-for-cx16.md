---
title: Build for the Commander X16
index: 26
---

# Build for the Commander X16

`./build.sh --cx16` compiles a sample, `lib/` and zerocore to a `.prg` file for a
[Commander X16](https://www.commanderx16.com/): a 65C02 at 8 MHz with 512 KB of ROM, 2 MB of banked
RAM and the VERA video chip. It is the Commodore 64 pipeline with another memory map: the same C#,
compiled to LLVM bitcode, rewritten for 16-bit addresses and compiled by llvm-mos, which has a `cx16`
platform. [Build for the Commodore 64](build-for-c64.html) explains the pipeline and its limits.

The X16 has far more memory than a C64 (512 KB of banked RAM as the emulator starts, up to 2 MB), but a
program's pointers are 16 bits, so what it can address directly is the fixed RAM from $0801 to $9EFF:
38 KB, against about 50 KB on the C64. The rest is banked through an 8 KB window at $A000. Code that
doesn't fit goes there (see "Code in banks"), but the heap and the program's data can't span banks.
`lib/Banks.cs` is for explicit use of the rest, below.

## Get the tools

```bash
./setup.sh --c64
```

The tools are the C64's (NativeAOT-LLVM and llvm-mos 23.4.0 or newer, which has `mos-cx16-clang`),
and the emulator, [x16emu](https://github.com/X16Community/x16-emulator) (release 49 was used). `setup.sh`
doesn't download it. Put it on the `PATH`, or set `X16_EMULATOR` (`X16_HOME`, `/Applications/X16` and
`~/X16` are tried too), and `X16_ROM` if its `rom.bin` isn't next to it.

## The machine, from C#

`lib/CX16.cs` is the X16's own side, for programs built with `--cx16` (a program for another target that
calls it won't link):

```csharp
CX16.Clear(CX16.Black);
CX16.Print(4, 2, "Hello from C#", CX16.Yellow);   // column, row, on the 80x60 screen
CX16.Sprite(0, 0, shape);                         // 16x16 pixels, 4 bits each
while (CX16.ReadKey() == 0)
{
    CX16.WaitFrame();
    CX16.MoveSprite(0, x++, 100);
    CX16.Tone(0, 440, 40);                        // voice 0 of 16, 440 Hz
}
```

- **Text:** `Print` writes characters and colors (background too) into the video chip's text layer. The
  screen is in ISO text, so a character is its Latin-1 code; `Clear` switches to it, and leaving the
  program switches back for BASIC.
- **Video memory:** the VERA has 128 KB of its own, which `Peek` and `Poke` don't reach: `VPeek` and
  `VPoke` do.
- **Sprites:** 128 of them, in front of the text; `Sprite`, `MoveSprite`, `HideSprite`. The short form
  takes a 16x16 shape of 4 bits a pixel; the long one any size from 8 to 64 pixels, 4 or 8 bits a
  pixel (256 colors), at an address of the video memory you choose, and says false for a shape that
  doesn't fit.
- **Mouse:** `MouseOn` switches the KERNAL's mouse driver on, which then moves sprite 0 as the pointer
  in every frame's interrupt: `MouseOn()` shows its arrow, `MouseOn(defaultCursor: false)` keeps
  whatever picture the program gave sprite 0. `MouseX`, `MouseY` and `MouseButtons` read it.
- **Sound:** `Tone` plays a note on one of the 16 voices of the VERA's sound generator (pulse, sawtooth,
  triangle or noise).
- **Input:** `ReadKey`, and `Joystick(0)` for the keyboard's cursor keys, Ctrl and Alt, or 1-4 for
  controllers.
- **Time:** `WaitFrame` and `Ticks`, at 60 Hz.

`samples/CX16Demo.cs` uses most of it: `./run.sh --cx16 --window CX16Demo`. `samples/Mouse.cs` is a
pointer, an arrow as an 8-bit sprite that the mouse moves: `./run.sh --cx16 --window --capture Mouse`: `--capture` keeps the mouse in the window, whose
pointer covers 0-639 across and 0-479 down. The headless runs
(`./test.sh --cx16`) draw nothing and have no frame interrupt, so there `WaitFrame` gives up after about
two frames' time, and the clock, the joysticks and the mouse stand still; the demo's checks read the video memory
back, and leave those two to a run in a window. The YM2151 FM chip, the PCM audio, the bitmap layers
have no API yet.

## Files on the disk

`File` (`lib/File.cs`) reads and writes the SD card, or a drive on the serial bus, through the
KERNAL (llvm-mos's C library does the talking; `cx16/runtime.c` adapts it):

```csharp
File.WriteAllText("notes.txt", "hello\n");
File.AppendAllText("notes.txt", "more\n");
File.WriteAllBytes("#9:data.bin", bytes);       // on device 9
```

- **Names:** the SD card follows CBM DOS: short names, no folders to speak of. In the emulator, the
  files are in the folder `run.sh` gives it with `-fsroot` (`bin/cx16/`).
- **Device:** a path starting with `#` and a number and a colon is on that device (8 to 30):
  `"#9:data.bin"`. Without it, a file is on the device the program was loaded from, 8 for the SD
  card. A device that isn't there closes all the program's open files, because the KERNAL can't open
  anything else until it has.
- **Binary:** every file is opened as binary: no byte is changed on the way.
- **Moving about:** a file opened for reading can be moved anywhere with `Seek`, and `Length` and
  `Position` work, but only by reading (forward) or opening it again (backward): `Length` reads a file
  through twice. A file opened for writing, or for appending, can only go on from where it is.
  `File.OpenAppend` adds to a file; `File.Create` replaces one.
- **Limits:** four files open at once, and names of up to 37 characters. `File.Delete` says false
  for a file that isn't there.

`samples/DiskFiles.cs` tests all of it (the emulator's host folder stands in for the SD card);
`samples/Files.cs` is for the other targets: it wants 64 KB arenas and writes into the middle of files.

## Screen modes

`CX16.SetMode(mode)` switches the screen, `CX16.Mode` says which one it is in, and `Columns` and `Rows`
follow it. The modes (the KERNAL's, as BASIC's `SCREEN` has them):

| Constant | Mode | Columns x rows | Picture |
|----------|------|----------------|---------|
| `Text80x60` | $00 | 80x60 | 640x480, the start |
| `Text80x30` | $01 | 80x30 | 640x240 |
| `Text40x60` | $02 | 40x60 | 320x480 |
| `Text40x30` | $03 | 40x30 | 320x240 |
| `Text40x15` | $04 | 40x15 | 320x120 |
| `Text20x30` | $05 | 20x30 | 160x240 |
| `Text20x15` | $06 | 20x15 | 160x120 |
| `Graphics320x240` | $80 | 40x30 | 320x240, 256 colors under the text |

- **The picture's size** is the columns and rows times 8: a mode with fewer columns has fewer, bigger
  pixels, 2 or 4 times as wide, and the sprites, the mouse and `MoveSprite` use that picture's pixels.
  In `Text40x30` the mouse goes from 0 to 319 across and 0 to 239 down, and a sprite at (304, 224) is in
  the bottom right corner.
- **What a change does:** the KERNAL clears the screen and sets the video chip up again. The character set
  (ISO or PETSCII) stays, sprites stay on (shapes and positions aren't touched), and the mouse gets the
  new size (`MouseOn` need not be called again). A program that has set the video chip's own registers has
  to do that again.
- `SetMode` says false, and changes nothing, for a number that isn't a mode.
- `Graphics320x240` is the mode with the bitmap (see below).
- `samples/ScreenModes.cs` tests every mode.

## Bitmap and palette

`Bitmap.Enable()` switches to the mode with a bitmap (`Graphics320x240`): 320 by 240 pixels of 256 colors,
a byte a pixel, with the 40x30 text screen in front (text color 0 is see-through there). `lib/Bitmap.cs`:

```csharp
Bitmap.Enable();
Bitmap.Clear(0);
CX16.SetColor(40, 15, 8, 0);                  // palette color 40 is orange: red, green, blue, 0-15
Bitmap.Line(10, 10, 300, 200, 40);            // the colors of the palette
Bitmap.Fill(100, 50, 40, 30, 8);              // x, y, width, height
Bitmap.Rectangle(0, 0, Bitmap.Width, Bitmap.Height, 15);
Bitmap.Circle(160, 120, 50, 5);
Bitmap.Text(8, 8, "drawn", 1);                // or with a background color: Text(8, 8, "x", 1, 0)
```

`Plot`, `Get`, `Fill`, `Rectangle`, `Line`, `Circle` and `Text` cut off what is outside the picture, and do
nothing in another screen mode. The palette is `CX16.SetColor` and `GetColor`: 12 bits a color, and
colors 0 to 15 start as the C64's (`CX16.Black` ... `LightGray`).

**Where things are in the video memory** (the KERNAL's own layout, which the VERA leaves to the program:
anything from $00000 to $1F9BF is free):

| Address | What |
|---------|------|
| $00000 to $12BFF | the bitmap (and, in other layer setups, tile maps and tiles) |
| $13000 to $14FFF | the 64 shapes of `CX16.Sprite(sprite, slot, ...)`, 128 bytes each |
| $15000 to $1AFFF | free for sprite shapes at an address of your own (`Sprite(sprite, address, ...)`) |
| $1B000 to $1EBFF | the text screen |
| $1F000 to $1F7FF | the character set |
| $1F9C0 to $1FFFF | the video chip's own registers: sound, palette, sprite attributes |

Speed, measured at 8 MHz (a frame is 133,000 cycles): `Clear` 460,000; `Fill` of the whole picture 650,000, of a
row of 100 pixels 2,000; `Line` across the picture 116,000; `Circle` of radius 100 133,000; `Text` 12,000 a
character; `Plot` 1,500 (mostly the call). A picture is redrawn a few times a second, not 60. `samples/BitmapDemo.cs`
draws one and checks every function against the video memory.

## Date, time and random numbers

- **`Clock.UtcNow`, `Clock.UnixMilliseconds`:** the X16's real-time clock, read once when a program first
  asks, and from then on advanced by the jiffy clock (sixtieths of a second), so it has fractions of a
  second and costs no more reads. The clock keeps what was set, usually the local time: the X16 knows no
  time zone, so the "UTC" of `Clock.UtcNow` is whatever the clock says. A real X16 has to have the
  time set (`CX16.SetDateTime`, or BASIC's `SETTIME`); `run.sh --cx16` starts the emulator with `-rtc`,
  which gives it the computer's time.
- **`CX16.SetDateTime(year, month, day, hour, minute, second)`:** sets it (1900 to 2155; dates that
  don't exist are ignored).
- **`Clock.Monotonic`:** the jiffies since power-on, plus a start somewhere in the first second, taken
  from the hardware's random bits (a monotonic clock's origin is up to the machine). That is what makes
  `Random.FromClock()` give different numbers on every run, even on a machine whose clock isn't set.
- **`CX16.Entropy`:** 24 random bits from the KERNAL, for a seed: `new Random((ulong)CX16.Entropy)`.

`samples/ClockSeed.cs` tests these; it sets the clock and puts the time back (to the second).

## Math

llvm-mos's C library has no `sin`, `cos`, `tan`, `exp`, `log`, `log2`, `log10`, `pow` or `atan2`, which
`Math` (and `MathF`) need; `cx16/math.c` has them, from FreeBSD's msun (fdlibm). A program that uses
one gets all of them, and nothing is in a program that doesn't.

- **Accuracy:** double precision, a unit in the last place or two (`tan` three), checked against a computer's
  math library by `cx16/test-math.c` (`cc -O1 -o /tmp/test-math cx16/test-math.c -lm`) and in the
  emulator by `samples/SmallMath.cs`. Two exceptions, to keep `pow` from being 12 KB: `pow` with a
  whole-number exponent is exact where the result is, and for the others it is good to about
  `|y * log(x)|` units in the last place (1e-14 up to results near e^100). `sin`, `cos` and `tan` reduce
  their argument exactly up to 1.6 million radians; beyond that they lose a digit for every factor of ten.
- **Size and speed:** the functions are some 28 KB of code, on top of the 18 KB of the soft-float
  routines every `double` program has, so the build compiles them to IR like the program's own and moves
  the big ones into banks (see above). A call takes 0.4 to 0.9 million cycles: 50 to 110 milliseconds at 8 MHz
  (measured: `sin` 400,000, `exp` 380,000, `log` 450,000, `atan2` 620,000, `pow` 870,000, a `double`
  multiplication 37,000, `sqrt` 80,000). A game that needs many sines a frame wants `MathF`, or `CX16.Sin`.
- **`MathF`:** `Sin`, `Cos`, `Exp`, `Log`, `Pow` and `Tanh` of a `float` are computed in `float`: a
  polynomial for sine and cosine, FreeBSD's `expf` and `logf` for the others. They are 10 to 16 times
  faster (`sin` 24,000 cycles, `exp` 24,000, `log` 27,000, `tanh` 31,000, `pow` 53,000, or 9,000 for a
  whole exponent), good to a few units in the last place of a float (1e-7 relative: `Sin`, `Cos` an absolute
  1e-7, `Tanh` 3e-7), and they use no `double` at all, so a program that only has floats doesn't carry the soft-double
  routines (18 KB). `Pow` with another exponent than a whole one up to 64 loses `|y * log(x)|` units. Beyond 8192
  radians `Sin` and `Cos` lose a digit for every factor of ten.
- **A table:** one with linear interpolation reaches 1e-7 only with 2,048 entries of 4 bytes (8 KB), and
  then it is not much faster than the polynomial, so `MathF` has none. For games that turn things by whole steps
  there is `CX16.Sin(angle)` and `CX16.Cos(angle)`: the angle in 256ths of a turn, the result from -32767 to
  32767, from a table of 65 numbers: good to 1.5e-5, about a hundred cycles.

## Banked RAM

`lib/Banks.cs` copies bytes to and from the banked RAM, which is 63 banks of 8 KB in the emulator
(512 KB; the KERNAL keeps bank 0) and up to 255 on a machine with 2 MB:

```csharp
Banks.Write(100000, tile);         // an address in all the banks, one after another
Banks.Read(100000, tile);
Banks.Fill(0, Banks.Size, 0);
int b = Banks.Peek(5);             // -1 outside the banks
```

A program's pointers are 16 bits, so nothing it uses, not an object or an array, can be in a bank; the
processor sees one bank at a time in a window at $A000, and `Banks` switches the bank (the register at
$00), copies, and switches back, with interrupts off. Addresses run over the banks without a gap, so a
copy can cross from one into the next. A copy that doesn't fit in the banks returns false and does
nothing. The banks hold whatever was in them at startup. `samples/BanksDemo.cs` checks all of it.

### Arrays in the banks

`BankedArray<T>` (`lib/BankedArray.cs`) is an array whose elements are in the banks, for data that
doesn't fit in the 38 KB: `Banks.Alloc` reserves the room (a pointer that moves forward;
`Banks.Reset` or `Banks.Release(mark)` takes it back), and the array copies each element in and out.

```csharp
BankedArray<int> counts = BankedArray<int>.Create(100000);   // 400 KB, zeroed
counts[5]++;
counts.Read(0, span);                                        // many at once is faster
```

`T` is any unmanaged type, including structs that lie across the border between two banks. Every
access is a bank switch and a copy, a few hundred cycles, so loop over ranges with `Read` and `Write`
when there are many elements. An index out of range stops the program, as for `List<T>`; `Create`
returns a not-created array when the banks are full. `samples/BankedDemo.cs` has a 240 KB array of
structs and a sieve over flags in the banks.

`BankedList<T>` (`lib/BankedList.cs`) is the growable kind, with `Add`, `AddRange`, the indexer,
`Read`, `RemoveAt` and `Clear`. Like `List<T>` it is a handle, so copies share the elements; its count
and capacity are kept in the banks too. It doubles when full, in place while it is the last thing
`Banks.Alloc` handed out, and otherwise into a new region (the old one stays reserved until
`Banks.Reset` or `Banks.Release`). `samples/BankedListDemo.cs` grows one to 5,000 structs.

## Build and run

```bash
./build.sh --cx16                 # the samples that fit, into bin/cx16/*.prg
./run.sh --cx16 Refs              # prints the output, exits with the program's code
./run.sh --cx16 --window Minimal  # shows the X16's screen, at its real speed
./run.sh --cx16 --window --capture Mouse  # the window keeps the mouse and keyboard
./run.sh --cx16 QuizStep2 < answers.txt
./test.sh --cx16                  # every X16 sample, compared with tests/expected
```

## Code in banks

A program with more code than the 38 KB of fixed RAM (`SmallBuilders`, `SmallDoubles`, `SmallFloats`,
`SmallFormats` and `SmallDoubleFormats` are) keeps its largest functions in RAM banks. The build does
it by itself when the first link is too big:

1. It links once with room to spare, to see how big each function is.
2. It moves the largest functions (150 bytes or more, up to 8 KB) into banks, about as much as is
   over plus 4 KB for the heap and the stack, and links again, moving more if it was not enough.
3. A moved function `f` becomes `f__bank`, placed at $A000 in a bank of its own. A small function `f`
   stays in fixed RAM: it saves the bank register (the byte at $00), selects the bank, calls
   `f__bank`, and puts the old bank back. Calls, function pointers and virtual calls all go
   through it, so nothing else in the program changes. Each costs about 20 cycles and 40 bytes of
   fixed RAM.

The banks are written to a second file next to the program, `bin/cx16/NAME.BNK` (the program's name in
capitals, at most 12 characters). At startup the program loads it from the disk it came from, in one
KERNAL `LOAD` into $A000, which carries on into the next bank when one is full. So the two files have to
be together: on an SD card, and for `x16emu` in the folder `run.sh` points it at (`-fsroot`). `./run.sh
--cx16` and `./test.sh --cx16` do that.

A call through a thunk costs about 33 cycles more (measured in `x16emu`: 60,000 calls of a small
function took 183 jiffies in fixed RAM and 198 banked, 407 and 440 cycles a call, so a call that does
little gets 8% slower). The build moves the largest functions, which are rarely the ones that loop;
`ZERO_CX16_BANK=Name ./build.sh --cx16 Sample` moves the functions whose names contain `Name`, whether
the program needs it or not, for trying that.

Only code moves: the data, the heap and the stack stay in fixed RAM. The data banks of `lib/Banks.cs`
start after the code banks, so such a program has that many fewer. A program that fits has no `.BNK`
file, and nothing is different. The work is in `sdk/ZeroCSharp.Bitcode/X16.cs`.

An X16 has no standard output and no exit code, so `run.sh --cx16` uses the emulator's debug
registers, which `cx16/runtime.c` finds by reading $9FBE and $9FBF ("1" and "6" in the emulator):

- **Output:** every character a program prints is also written to $9FBB, which the emulator prints on its
  standard output as it is.
- **Exit code:** written to $9FB9, which the emulator reports as `User debug 1: $xx`. `cx16/watch.py`
  ends the emulator when it sees it, with the code as its own exit code. A run that hasn't finished
  after `X16_TIMEOUT` seconds (default 600) is stopped and exits with 124.
- **Input:** the emulator can't type into the keyboard, so the text goes into a cartridge image
  (`-cartbin`, ROM bank 32) that `cx16/runtime.c` reads instead of the keyboard, in the emulator only.

On a real X16 these registers aren't there: the runtime leaves them alone, and a program ends at BASIC's
`READY.`.
