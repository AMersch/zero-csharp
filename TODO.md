# TODO

What is left to do for the Commander X16 target (`./build.sh --cx16`, `lib/CX16.cs`, `cx16/`), most
important first. (What is done is listed first.) Priority is value for programs written in C# divided by the work and the risk;
**Effort** is S (an hour or two: a wrapper and a test), M (a day) or L (several days).

The X16's own hardware is reached from `lib/CX16.cs` through functions in `cx16/runtime.c`
(`cs_cx16_*`), as the mouse and the sprites are. `cx16.h` of llvm-mos already declares most KERNAL
calls. `./test.sh --cx16` runs headless (`x16emu -testbench`), which has no frame interrupt, no sound
output and no mouse: what can only be heard or seen needs a check in a window, or a recording
(`-wav`, `-gif`).

## 1. Done

- **VERA FX, a first part:** the cache for `Bitmap.Clear`, `Fill` and `Tiles.Fill` (4 to 9 times faster), the line helper for `Bitmap.Line` (17 times faster), `CX16.HasVeraFx` and `UseVeraFx`. **Filled triangles** (`Bitmap.FillTriangle`, `Triangle`) by the polygon helper with cache writes. **The rest:** the multiplier (`CX16.Multiply`, `Dot`) and the affine helper (`Affine`, rotating and scaling into the bitmap; `samples/VeraFxDemo.cs`). Not yet: the helper for triangles that are cut by the picture's edge, 4-bit and 2-bit modes, affine drawing from a bitmap rather than tiles.
- **FM sound (YM2151):** `Fm` (patches in the chip's register order, notes by MIDI number, volume, pan, raw writes) and the PSG's pitch checked, both measured from recordings by `./test.sh --cx16-audio`. Not yet: the ROM's 162 instrument presets, the LFO (vibrato, tremolo) and noise as calls, a sequencer.
- **Folders:** `Disk` lists them and sends commands to the drive (`samples/DiskList.cs`). The files were moved off llvm-mos's stdio, which couldn't read from an SD card.
- **Mouse wheel:** `CX16.ReadMouseWheel` (the KERNAL's `mouse_get` returns it in X, which llvm-mos's wrapper drops). Turned by hand in the emulator; the headless tests have no mouse, and the read path was tested by writing the KERNAL's counter.
- **PCM audio:** `Pcm` plays 8 and 16 bit, mono and stereo samples from an array or the banked RAM, once or in a loop, refilled by an interrupt (`samples/PcmDemo.cs`, `./test.sh --cx16-audio`). Not yet: playing straight from a file, reading WAV files, telling the program when a sound ends other than by asking, more than one sound at a time.
- **Real-time clock:** `Clock.UtcNow` and `UnixMilliseconds` read it; `CX16.SetDateTime` sets it.
- **Bitmap:** `Bitmap` (clear, plot, get, fill, rectangle, line, circle, text) and the palette (`CX16.SetColor`) in 320x240 with 256 colors, directly on the VERA; sprite shapes moved to $13000 to keep clear of the bitmap. (`samples/BitmapDemo.cs`) Not yet: other depths (4, 2, 1 bit), 640x480, speed with VERA FX.
- **Tiles:** `Tiles` (map and tile pictures in 2, 4 and 8 bits, 8x8 and 16x16, flips and palette offsets, scrolling) on layer 0. (`samples/TileDemo.cs`) Not yet: the 1-bit text-like tile mode, layer 1 as tiles (it is the text), a second layer of tiles.
- **Screen modes:** `CX16.SetMode`, `CX16.Mode` (all of the KERNAL's modes, sprites and the mouse follow). (`samples/ScreenModes.cs`)
- **Hardware random seed:** `CX16.Entropy`, and `Clock.Monotonic` starts at a random moment, so
  `Random.FromClock()` differs from run to run. (`samples/ClockSeed.cs`)

## Also open

- **Real hardware:** nothing here has run on a real X16. Test at least `Mouse`, `DiskFiles` and a program with code in banks (two files on the SD card).
- **`cx16/math.c` for the C64:** the C64 runtime has no sin, cos, exp, log or pow either; the file stands alone and could be shared.
- **Heap with code in banks:** the build now moves the floating point library too and goes on until 4 KB are left, but it can only move what is code in a function of up to 7 KB: not the runtime (`cx16/runtime.c`, 5 KB for filled triangles), the data (3 KB), the 2 KB shadow stack, or one huge method. The functions are picked by size, not by how often they run.
- **Files:** four open at once; no `File.Move` or `Directory` as on the other targets (`Disk.Command` does it by hand). Reading is byte by byte through the KERNAL: its block read (`MACPTR`) would be much faster. A listing holds some 250 entries. Tested on a card image in the emulator, not on a real card or a serial drive.
- **Headless testing** has no frame interrupt: the clock, joysticks and mouse can't be tested there.
