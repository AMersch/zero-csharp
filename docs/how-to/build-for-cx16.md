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
./setup.sh --cx16
```

The tools are the C64's (NativeAOT-LLVM and llvm-mos 23.4.0 or newer, which has `mos-cx16-clang`),
and the emulator, [x16emu](https://github.com/X16Community/x16-emulator) (release 49 was used).
The samples also pass in the emulator with ROM R47, which an X16 may still have: there a call to a drive that
isn't there comes back with the wrong RAM bank, which `cx16/runtime.c` puts back. `setup.sh`
doesn't download it, but says whether it and its ROM are found. Put it on the `PATH`, or set `X16_EMULATOR` (`X16_HOME`, `/Applications/X16` and
`~/X16` are tried too), and `X16_ROM` if its `rom.bin` isn't next to it.

## From a project (`dotnet build`)

The SDK builds for the X16 as for the C64: `dotnet build -p:ZeroTarget=cx16` makes
`bin/Debug/<framework>/cx16/NAME.prg`, and, for a program whose code doesn't fit the fixed RAM, a `.BNK` file
next to it, which belongs with it on the disk. The published packages don't have this target yet; pack this
checkout's for your own machine:

```bash
sdk/pack-local.sh                  # ZeroCSharp.Sdk and .Templates VERSION-local (it says which), into sdk/feed/
dotnet new uninstall ZeroCSharp.Templates      # the published ones, if installed: both would clash
dotnet new install "$PWD/sdk/feed/ZeroCSharp.Templates.0.14.0-local.nupkg"
dotnet new zero -o Hello && cd Hello
dotnet new nugetconfig && dotnet nuget add source <repository>/sdk/feed -n zero-local --configfile nuget.config
dotnet build -p:ZeroTarget=cx16
x16emu -prg bin/Debug/zero03/cx16/Hello.prg -run -fsroot bin/Debug/zero03/cx16
```

`dotnet run` doesn't start an emulator. The first build downloads NativeAOT-LLVM and llvm-mos into the user's
cache. On a Mac, NativeAOT-LLVM runs in a container: the `docker` command must be there (Docker Desktop, Colima,
or a `docker` link to Podman), and the project and the package cache must be in folders it can mount. The
program's classes (`CX16`, `Bitmap`, `Pcm`, `Disk` ...) are in the package's library; calling them in a program
built for another target fails at the link. Linux arm64 can't build for the X16 (llvm-mos has no build for it).

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
  whatever picture the program gave sprite 0. `MouseX`, `MouseY` and `MouseButtons` read it, and
  `ReadMouseWheel` says how many notches the wheel has turned since it was last asked: negative away from you,
  positive towards you (the emulator passes on the host's direction, which "natural scrolling" turns round).
- **Sound:** `Tone` plays a note on one of the 16 voices of the VERA's sound generator (pulse, sawtooth,
  triangle or noise).
- **Input:** `ReadKey`, and `Joystick(0)` for the keyboard's cursor keys, Ctrl and Alt, or 1-4 for
  controllers.
- **Time:** `WaitFrame` and `Ticks`, at 60 Hz.

`samples/CX16Demo.cs` uses most of it: `./run.sh --cx16 --window CX16Demo`. `samples/Mouse.cs` is a
pointer, an arrow as an 8-bit sprite that the mouse moves, with the wheel counted: `./run.sh --cx16 --window --capture Mouse`: `--capture` keeps the mouse in the window, whose
pointer covers 0-639 across and 0-479 down. The headless runs
(`./test.sh --cx16`) draw nothing and have no frame interrupt, so there `WaitFrame` gives up after about
two frames' time, and the clock, the joysticks and the mouse stand still; the demo's checks read the video memory
back, and leave those two to a run in a window.

## Files on the disk

`File` (`lib/File.cs`) reads and writes the SD card, or a drive on the serial bus. `cx16/runtime.c` uses the
KERNAL's file calls for it, not llvm-mos's stdio, which is made for the old Commodore drives: it can't open a
file on the SD card for reading, and allows only 16 characters of a name.

```csharp
File.WriteAllText("notes.txt", "hello\n");
File.AppendAllText("notes.txt", "more\n");
File.WriteAllBytes("#9:data.bin", bytes);       // on device 9
File.TryReadAllText(arena, "//levels/:one.txt", out string text);    // in a folder
```

- **Names:** long names and spaces are fine, up to about 70 characters with the folder; a comma isn't (the drive
  takes what follows for the file's type). Upper and lower case find the same file.
- **Folders:** `"folder/name"` from the current folder, which `Directory.SetCurrentDirectory` (or
  `Disk.Command("cd:folder")`) changes, and `"/folder/name"` from the root. CBM DOS's own forms work too, where
  `//folder/:name` is from the root and `/folder/:name` from the current folder.
- **Device:** a path starting with `#` and a number and a colon is on that device (8 to 30):
  `"#9:data.bin"`. Without it, a file is on the device the program was loaded from, 8 for the SD
  card. A device that isn't there closes all the program's open files, because the KERNAL can't open
  anything else until it has.
- **Binary:** every file is read and written byte for byte.
- **Moving about:** a file opened for reading can be moved anywhere with `Seek`, and `Length` and
  `Position` work, but only by reading (forward) or opening it again (backward): `Length` reads a file
  through twice. A file opened for writing, or for appending, can only go on from where it is.
  `File.OpenAppend` adds to a file; `File.Create` replaces one. A `FileStream` opened to read and write moves
  anywhere at no cost (below).
- **Limits:** four files open at once. `File.Delete` says false for a file that isn't there.

### The other file APIs

The rest of `File`, `Directory`, `Path` and `FileStream` work as on the other systems, through
`cx16/runtime.c`:

```csharp
File.GetInfo("a.txt", out FileInfo info);            // length, date, folder or not
File.Move("a.txt", "levels/a.txt", overwrite: true);
Directory.Create("levels/deep");
foreach (DirectoryEntry entry in Directory.Enumerate("levels")) { }
using var save = new FileStream("save.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 64);
```

- **What it costs:** the drive has no "stat", so `File.Exists`, `GetInfo` and what builds on them read the
  folder's listing. These APIs are much code: expect banks, and leave out what isn't needed.
- **Read and write at once** is CMDR-DOS's `,M` mode with its position command; a drive on the serial bus
  hasn't got it.
- **Memory:** `FileStream` takes a 4 KB buffer from the heap unless given a smaller size, and
  `Directory.Enumerate` holds the folder's entries on the heap until the loop ends.
- **No `catch`:** on the 6502 an exception ends the program. Use the forms that return a `FileError`
  (`FileStream.TryOpen`).
- **Names** don't tell capitals apart (FAT32), and can't hold a comma, `*` or `?`.

### Folders

`Disk` (`lib/Disk.cs`) lists a folder and sends commands to the drive:

```csharp
char[] name = new char[80];
if (Disk.OpenDirectory())                       // the current folder; or Disk.OpenDirectory("levels")
{
    while (Disk.Next())
    {
        int length = Disk.CopyName(name);       // and Disk.Size in bytes, Disk.IsFolder
        Out.Line(new ReadOnlySpan<char>(name, 0, length));
    }
}
Disk.Command("md:levels");                      // make a folder; "cd:..", "rd:levels", "r:new=old"
```

- `OpenDirectory` reads the whole folder at once into the topmost bank of the banked RAM (a program that uses it
  has one bank less for `Banks`), because CMDR-DOS can't have a listing open while files are used: it then ends
  early, gives nonsense or hangs. So the loop may open, write and delete files, and goes through the folder as
  it was. The bank holds some 250 entries; `Disk.IsComplete` says false if there were more.
- It says false for a folder or a device that isn't there.
- A drive on the serial bus gives sizes in blocks of 254 bytes, not bytes. Nothing here was tried with one.
- `samples/DiskFiles.cs` and `DiskList.cs` test all of it; `samples/Files.cs` is for the other targets (it wants
  64 KB arenas and writes into the middle of files).

### On the emulator and on a card

Without a card image the emulator serves a folder of the computer as the disk (`-fsroot`, which `run.sh` points
at `bin/cx16/`). That is more forgiving than the SD card's own DOS, and different with folders: a listing of a
named folder shows the root, so `Directory.Enumerate` and `Disk` can't list one there. It also takes no folder in
CBM DOS's `folder/:name` form: the library sends plain paths, and so should `Disk.Command`
(`"s:folder/name"`), or a file of the same name in the root is removed, which is a file on your computer. `X16_SDCARD=sdcard.img.zip ./run.sh --cx16 Name` runs a program with a copy of a card
image instead (the emulator's release has an empty one), and `./test.sh --cx16` runs `samples/DiskFiles.cs` and
`DiskList.cs` both ways.

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
Bitmap.FillTriangle(20, 100, 120, 90, 70, 180, 12);        // a filled triangle, corners in any order
Bitmap.Text(8, 8, "drawn", 1);                // or with a background color: Text(8, 8, "x", 1, 0)
```

`Plot`, `Get`, `Fill`, `Rectangle`, `Line`, `Triangle`, `FillTriangle`, `Circle` and `Text` cut off what is outside the picture, and do
nothing in another screen mode. The palette is `CX16.SetColor` and `GetColor`: 12 bits a color, and
colors 0 to 15 start as the C64's (`CX16.Black` ... `LightGray`).

**Where things are in the video memory** (the KERNAL's own layout, which the VERA leaves to the program:
anything from $00000 to $1F9BF is free):

| Address | What |
|---------|------|
| $00000 to $12BFF | the bitmap (and, in other layer setups, tile maps and tiles) |
| $13000 to $14FFF | the 64 shapes of `CX16.Sprite(sprite, slot, ...)`, 128 bytes each |
| $15000 to $1AFFF | free for sprite shapes at an address of your own (`Sprite(sprite, address, ...)`); `Affine` puts its tiles at $15000 and its map at $19000 unless told otherwise |
| $1B000 to $1EBFF | the text screen |
| $1F000 to $1F7FF | the character set |
| $1F800 to $1F803 | where the multiplier leaves its result |
| $1F9C0 to $1FFFF | the video chip's own registers: sound, palette, sprite attributes |

Speed, measured at 8 MHz (a frame is 133,000 cycles), with the video chip's FX helpers (see below) and without
them:

| | with FX | without |
|-|---------|---------|
| `Clear` (76,800 pixels) | 127,000 | 460,000 |
| `Fill`, the whole picture | 133,000 | 650,000 |
| `Fill`, 100 by 50 pixels | 25,000 | 100,000 |
| `Line` across the picture | 6,700 | 116,000 |
| `Circle` of radius 100 | 133,000 | 133,000 |
| `FillTriangle`, 50 by 50 | 40,000 | 67,000 |
| `FillTriangle`, 100 by 100 | 93,000 | 140,000 |
| `FillTriangle`, 200 wide, 100 high | 140,000 | 180,000 |
| `FillTriangle`, 10 by 10 | 25,000 | 24,000 |
| `Affine.Draw`, 64 by 64 | 93,000 | 6,300,000 |
| `CX16.Multiply` (C#'s `a * b`: 2,200) | 400 | 1,300 |
| `CX16.Dot` of 100 pairs (a C# loop: 167,000) | 15,000 | 170,000 |
| `Text`, a character | 12,000 | 12,000 |
| `Plot` | 1,500 | 1,500 |

`Plot` is mostly the call from C#: a lot of pixels want `Fill`, `Line` or `Text`. With FX a frame holds a whole
picture's fill or a few dozen lines. `samples/BitmapDemo.cs` draws a picture and checks every function against the
video memory. `samples/BitmapShapes.cs` checks fills of every width and position, lines in all eight directions
and 15 triangles against an exact rule (a pixel is inside when its center is; the left edge and the top row belong
to a triangle), with the helpers and again without them. `samples/VeraFxDemo.cs` does the same for the multiplier
and `Affine`.

### VERA FX

From VERA 0.3.1 on, the video chip has helpers (FX) for drawing. `CX16.HasVeraFx` says whether this one has them (the
emulator does; the chip says so in its version registers, DCSEL 63, whose first byte is "V"). `Bitmap` and `Tiles` use
them where they pay, and do the same without them, slower; `CX16.UseVeraFx = false` switches them off, to compare the
two ways.

- **Cache fills:** `Bitmap.Clear`, `Fill` (from 16 pixels wide, or 32 tall) and `Tiles.Fill`. A write to the data port
  with the cache on writes four bytes, with a mask for the ends of a row (a bit for each nibble, both of a byte's bits
  set to leave it alone), so a row of any width and position is whole groups of four.
- **Lines:** `Bitmap.Line`, for a line that is wholly on the picture. The line helper steps one address along the longer
  axis and another along the shorter one when its slope accumulator carries. The slope has 9 fractional bits, so a pixel
  in the middle of a long line can be one off Bresenham's, and the two ends are always exact.
- FX is switched on only while a call needs it, with the interrupts off (the KERNAL's own writes to the video memory,
  those of the mouse pointer, must not become cache writes): so a long `Clear` delays the mouse by its 127,000 cycles.
- **Triangles** (`Bitmap.FillTriangle`): the polygon helper follows the two edges and says where each row starts and
  how long it is; the row is written four pixels at a time through the cache. It is about 1.5 times as fast as the
  software, and no faster for very small triangles.
  - The helper holds a slope to 1/256 of a pixel a row, so each edge starts again from the exact one every 16 rows.
    Its edges are within 1/32 of a pixel: a pixel whose center is that close to a side may differ from the software's.
    In a comparison of 38 triangles, 97 of 5,458 rows differed, none by more than one pixel.
  - Two triangles with a side in common still leave no gap and don't overlap.
  - A triangle that isn't wholly on the picture, or has an edge flatter than 60 pixels a row, is drawn in software.
    Next to one drawn by the helper, such a triangle may leave or double a pixel along the common side.

### The multiplier

`CX16.Multiply(a, b)` multiplies two `short`s to an `int`, and `CX16.Dot(a, b)` adds up the products of two spans
(vectors, filters), wrapping round at 32 bits. The 65C02 has no multiply instruction, so even one product is about five
times faster than C#'s `a * b`; the compiler doesn't use the chip for `*` by itself. Without FX both are worked out
in software.

### Rotating and scaling

`Affine` (`lib/Affine.cs`) draws a rectangle of the bitmap from a picture made of tiles, rotated, scaled, sheared or
mirrored:

```csharp
Affine.Source(8);                             // a map of 8 by 8 tiles
Affine.SetTile(1, pixels);                    // 64 bytes: 8x8, a color each
Affine.SetMap(0, 0, 1);                       // column, row, tile
Bitmap.Enable();
int cos = CX16.Cos(angle) >> 7, sin = CX16.Sin(angle) >> 7;     // 256 is 1
Affine.Draw(128, 88, 64, 64,                  // where, and how big
    32 * 256 - 32 * cos + 32 * sin, 32 * 256 - 32 * sin - 32 * cos,    // the source position of the top left corner
    cos, sin,                                 // its step for each pixel to the right
    -sin, cos);                               // and for each row down
```

- The map is 2, 8, 32 or 128 tiles each way, with up to 256 tiles of 8 bits a pixel. Outside it the picture repeats,
  or shows tile 0 (`clip: true`). `transparent: true` leaves the bitmap as it was where the source has color 0.
- Positions and steps are in 256ths of a source pixel.
- A map of 128 is 16 KB and doesn't fit at the usual place: `Affine.Source(128, tileAddress: 0x19000,
  mapAddress: 0x15000)`, which leaves room for 128 tiles.
- A large picture is slow even with the helper (a full screen is about 9 frames). The interrupts get in between
  rows, so the clock and the mouse go on.
- Without FX the same pixels are worked out in software, about 70 times slower.

### A wireframe ship

`samples/Jameson.cs` puts several of these together: a ship that grows out of a point and turns about three axes,
hidden lines left out, with `CX16.Multiply` for the rotation and `Bitmap.Line` for the picture, about 27 pictures a
second: `./run.sh --cx16 --window Jameson`. With no room for a second picture, it replaces one line at a time.

## Tiles

`Tiles` (`lib/Tiles.cs`) is layer 0 of the video chip as a map of tiles that scrolls smoothly: the background
of most games. It is behind the text and the sprites, and it is the layer the bitmap uses, so each takes it from
the other; a screen mode change switches it off (`Enable` again after one).

```csharp
Tiles.Enable(64, 64, 8, 4);          // 64 by 64 tiles of 8x8 pixels, 4 bits a pixel
Tiles.SetTile(1, brick);             // tile 1's picture: 32 bytes, two pixels a byte, the left one in the high bits
Tiles.Fill(1);
Tiles.Set(5, 3, 2, paletteOffset: 1, flipHorizontally: true);     // column, row, tile
Tiles.Scroll(frame, 0);              // the picture moves left
```

- **Map:** 32, 64, 128 or 256 tiles each way (two bytes a tile, from `MapAddress`), tiles of 8x8 or 16x16
  pixels, 2, 4 or 8 bits a pixel, a tile index of 0 to 1023. The 1-bit mode of the video chip, which works
  like text, isn't there.
- **Colors:** pixel color 0 is see-through (tile 0, empty at first, shows what is behind the layer, black).
  With 4 bits a pixel a map entry's palette offset (0-15) adds 16 times itself to the colors 1-15; colors
  above 15 are not changed.
- **Where in the video memory:** with `Enable`'s defaults the map starts at $00000 and the tile pictures
  follow it at the next multiple of 2048 (`TileAddress`): in the 75 KB below the sprites ($13000), which
  the bitmap uses too. `mapAddress` and `tileAddress` choose other places (multiples of 512 and 2048); the
  map and the tiles can go anywhere up to $1F9C0, so keep them clear of the sprite shapes, the text
  screen and the character set (see the table under "Bitmap and palette").
- **Scrolling:** `Scroll(x, y)` takes 0 to 4095 (more wraps round), the picture moves left and up as they
  grow, and the map repeats where it ends; `ScrollX` and `ScrollY` read it back.
- `Enable` clears the map, `Fill` and `Clear` set all of it, `Hide` and `Show` switch the layer, and what
  doesn't fit (a size, an index, an address, a picture of the wrong length) gives `false` and changes nothing.
- **Speed**, at 8 MHz (a frame is 133,000 cycles): `Fill` of a 64x64 map 120,000; `Set` and `Get` about 1,000
  a tile; `Scroll` 400; `SetTile` 2,700 for 32 bytes. A frame can change a hundred tiles and scroll.

`samples/TileDemo.cs` draws four tiles in code into a map that scrolls, and checks the registers and the
video memory for every function and for other sizes.

## Sound

The X16 makes sound in three ways, all reached from C#.

- **The video chip's sound generator (PSG)**, `CX16.Tone(voice, hertz, volume, waveform)`: 16 voices of pulse, sawtooth,
  triangle or noise waves, `CX16.Silence(voice)`. The frequency is rounded to the chip's steps of 0.37 Hz.
- **The FM chip (YM2151)**, `Fm` (`lib/Fm.cs`): 8 channels of 4 operators that modulate each other.

```csharp
Fm.Reset();                       // silences everything, sets every channel to a sine
Fm.SetPatch(0, Fm.Organ);         // an instrument on channel 0
Fm.NoteOn(0, 69);                 // a note: MIDI 69 is the A of 440 Hz (13 to 108 are in the chip's range)
Fm.SetVolume(0, 100);             // 0 to 127, 127 the instrument's own level
Fm.SetPan(0, left: true, right: false);
Fm.NoteOff(0);                    // it dies away as the instrument's release says
```

- **Instruments:** `Fm.Sine` (one pure tone, to hear a pitch with) and `Fm.Organ` (four operators an octave apart) are
  built in. Others are 26 bytes in the order of the chip's registers (`SetPatch` documents the layout): the
  algorithm, feedback and speakers; vibrato; and for each of the four operators the ratio (MUL, DT1), level
  (TL, 0 loudest to 127 silent) and envelope (KS, AR, D1R, D2R, D1L, RR). What the sound of such numbers is, the
  YM2151's manual says. The ROM's 162 presets (`ym_loadpatch`) are not reachable yet; and the ROM's audio routines
  keep a copy of the chip's registers of their own, so don't drive the chip with both.
- **`Fm.NoteOn(channel, note, fine)`:** `fine` is 0 to 63 sixty-fourths of a semitone above the note. The chip is
  tuned for the X16's 3.58 MHz clock.
- **`Fm.Write(register, value)`** is the chip's own interface for what has no call (the LFO, noise, the timers). It
  waits until the chip is ready: a write that comes while it is busy (150 cycles) is dropped. Its registers can't be
  read back, so `Fm` keeps what it needs (the algorithm of each channel, the levels, the volume) and `Write` should
  stay off the registers $20-$27 and $60-$7F.

### Sampled sound

`Pcm` (`lib/Pcm.cs`) plays samples on the video chip's PCM channel, in the background:

```csharp
Pcm.Play(samples, 8000);                          // a byte[] of signed 8-bit samples; returns at once
Pcm.Play(music, 22050, sixteenBit: true, stereo: true, loop: true);
Pcm.PlayBanked(address, count, 16000);            // from the banked RAM, as Banks counts it
Pcm.Volume = 10;                                  // 0 to 15
if (!Pcm.IsPlaying) Pcm.Stop();
```

- **The samples are signed**, 16-bit ones low byte first, stereo left before right. A WAV file's 8-bit samples are
  unsigned: flip the top bit of each. Rates are 1 to 48828 Hz, in the chip's steps of 381 Hz (8000 plays at 8011).
- **Nothing to call while it plays:** the chip interrupts when its 4 KB buffer is down to 1 KB, and a handler in the
  runtime fills it up. The handler is hooked into the KERNAL's interrupt vector ($0314) at the first `Play`, and
  taken out when the program ends.
- **Long sounds** don't fit in 38 KB: load them from a file a piece at a time into the banks (`Banks.Write`), then
  `PlayBanked`. The bytes must stay as they are while they play. Playing straight from a file is not there.
- **Gaps:** a refill that comes later than the last 1 KB lasts (an eighth of a second at 8000 Hz mono 8-bit, 5 ms at
  48828 Hz stereo 16-bit) is heard. Interrupts are off during disk loads and during some drawing with VERA FX.
- **Processor time**, measured: 2% at 8000 Hz mono 8-bit, 4% at 16000, 10% at 22050 Hz mono 16-bit, 46% at 48828 Hz
  stereo 16-bit.
- `samples/PcmDemo.cs` plays each kind.

### Testing sound

 The emulator makes no sound without a window, and cannot record without one either, so
`./test.sh --cx16` only runs `samples/SoundDemo.cs` and `PcmDemo.cs` through and checks what `Fm` and `Pcm` refuse and
what they tell the chips (its PCM buffer never empties there). `./test.sh --cx16-audio`
(`cx16/test-audio.sh`) opens the emulator for each of the two with `-wav`, which records what it plays, lets the
program start the recording itself (`CX16.StartRecording()`, the emulator's debug register $9FB6) and measures the
file (`cx16/audio-check.py`, with numpy): the pitch of the PSG tone and of the FM notes within 1.5 Hz, the partials of
the organ, a fifth as two peaks, a note in the left speaker only, and the volume ratio of two volumes to a few percent
of what 0.75 dB a step gives. That is how the pitch formulas were checked, and a regression in them shows there.
For `PcmDemo` it measures the pitch and length of each sound, the speaker and the volume.

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

### Loading a file into the banks

`Banks.Load` reads a whole file from the card straight into the banks and returns how many bytes it was,
or -1:

```csharp
int length = Banks.Load(0, "level1.bin");     // or "data/level1.bin"
```

It is one KERNAL `LOAD`, without the code behind `File`: a program that only loads a file came out at
13 KB where one that opens and reads it was 27 KB. The name is ASCII, in the current folder of the drive
the program came from. Leave room: the length isn't known before the file is read, and a file longer than
the banks from that address on overwrites what is there. `samples/BankLoad.cs` checks it.

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

## Fibers and timers

`Fiber`, channels and the timers work as on the C64 (`samples/SmallFibers.cs`, `SmallChannels.cs`): a switch also
keeps the RAM bank, so fibers may run code that is in banks. A fiber takes 2 KB of the heap for its stack unless
`Spawn` is given another size (512 bytes at least, which little code gets by with), so a program with fibers
usually has to ask for more heap (below). In the headless emulator, where the clock stands still, what a program
sleeps is added to the clock. Sockets aren't there: the X16 has no network.

## Large methods

Only a whole method moves into a bank, and one over 7 KB (7168 bytes) doesn't: it takes fixed RAM for good,
however many banks are free. `Main` gets there easily. 6502 code is long (50 lines can come to 13 KB), and
the compiler builds the methods a method calls into it, above all those called once.

A build that needs banks, or doesn't fit, names such methods:

```text
[cx16]   Game: Game_Program__Main is 13168 bytes, too large for a bank (7168 at most): it stays in fixed
RAM. Split it, with [MethodImpl(MethodImplOptions.NoInlining)] on the parts, and they can move.
```

The name is the program's, the type's and the method's; the size is what was measured before anything moved.
A program that fits as it is gets no such line.

Split the method, and **mark each part** so that it stays a method of its own:

```csharp no-sample
using System.Runtime.CompilerServices;

[MethodImpl(MethodImplOptions.NoInlining)]
static void LoadLevel() { /* ... */ }

[MethodImpl(MethodImplOptions.NoInlining)]
static void DrawLevel() { /* ... */ }

public static int Main()
{
    LoadLevel();
    DrawLevel();
    return 0;
}
```

**Without the attribute, splitting doesn't help:** the parts are put together again. Tried on
`samples/BankedListDemo.cs`, whose `Main` is 13 KB:

| `Main` | Largest method | Heap left |
|---|---|---|
| as it is | 13,168 bytes | 4,876 bytes |
| in three parts, not marked | 12,994 bytes | 548 bytes |
| in three parts, marked | 6,940 bytes, in a bank | 6,828 bytes |

- **Where to cut:** by what runs when (setting up, one level, the end), not through a loop that runs often: a
  call into a bank costs about 33 cycles more (see Code in banks, below), which a part called once a frame doesn't notice
  and one called for every pixel does.
- **Data between the parts** goes in parameters and return values, or in `static` fields.
- **A part that is still too large** is named again: split it further.
- Marking the library's own methods wouldn't do this for you: it made one sample smaller and another larger.

## More heap

A program with code in banks is given 4 KB of heap if there is enough code to move. One that allocates more asks
for it, in bytes: `ZERO_CX16_HEAP=9000 ./build.sh --cx16 Name`, or in a project
`<ZeroCx16Heap>9000</ZeroCx16Heap>`. More functions then go into banks, as far as there are any to move; the
build says how little it got if that is less than 3 KB or than asked for. `samples/SmallArena.cs` needs it
(`build.sh` sets it).

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

1. It links once with room to spare, to see how big each function is. The program's own functions
   count, `cx16/math.c`'s, and the floating point of the compiler's library (`libcrt.a`, whose
   members are LLVM IR): about 25 KB for a program that computes with doubles.
2. It moves the largest functions (150 bytes or more, up to 7 KB) into banks, about as much as is
   over plus 4 KB, and links again, moving more if it was not enough. Once the program links, it
   goes on moving while less than 4 KB are left for the heap, and keeps the link that left the
   most; so a banked program can take a few links more.
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
