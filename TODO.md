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
- **Fibers and timers:** as on the C64, with the RAM bank switched too (`samples/SmallFibers.cs`, `SmallChannels.cs`).
- **The file APIs:** `File.GetInfo` and `Move`, `Directory`, read and write at once (`samples/DiskInfo.cs`, `DiskFolders.cs`, `DiskChange.cs`).
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

- **Real hardware:** tried on an X16 with ROM R47, VERA 47.0.2, SMC 47.0.0 and 2 MB of banked RAM. Passed there: `Tiny`, code and data in banks (`BanksWithCode`, `BanksDemo`), files on the SD card (`DiskFiles`, `DiskList`, `DiskInfo`, `DiskFolders`, `DiskChange`), `ClockSeed` (once the clock was set), the graphics (`ScreenModes`, `BitmapShapes`, `BitmapDemo`, `TileDemo`, `VeraFxDemo`, `Jameson`), sound (`SoundDemo`, `PcmDemo`, listened to), `CX16Demo` and `Mouse`. `VeraFxDemo` and `BitmapShapes` pass with or without the chip's FX; whether `CX16.HasVeraFx` is true there wasn't looked at. **Not yet tried there:** the other samples, `BankedDemo` and `BankedListDemo`, fibers and timers, ROM R49, and a drive on the serial bus.
- **`cx16/math.c` for the C64:** the C64 runtime has no sin, cos, exp, log or pow either; the file stands alone and could be shared.
- **Heap with code in banks:** the build now moves the floating point library too and goes on until 4 KB are left, but it can only move what is code in a function of up to 7 KB: not the runtime (`cx16/runtime.c`, 5 KB for filled triangles), the data (3 KB), the 2 KB shadow stack, or one huge method. The functions are picked by size, not by how often they run.
- **Files:** four open at once. Reading is byte by byte through the KERNAL: its block read (`MACPTR`) would be much faster. A listing for `Disk` holds some 250 entries. The code of `File`, `Directory` and `FileStream` is large, and `cx16/runtime.c`'s file functions can't go into banks. Tested on a card image in the emulator and on a real card, not on a drive on the serial bus.
- **Where a program stopped:** the C64 prints the calls a program was in when it stops, and `run.sh` names them (`trace` in `c64/runtime.c`). The X16 doesn't yet: with code in banks, an address from `$A000` to `$BFFF` on the stack doesn't say which bank's method it is.
- **async/await** (`lib/Tasks.cs`): small programs build and run, with most of their code in banks, but it leaves about 4 KB of heap, and a fiber takes 2 KB of it unless `Spawn` is given less. No sample covers it. `File`'s and `FileStream`'s async calls don't fit at all (and `cx16/runtime.c` lacks the `pipe` and `pthread_create` they import, which would answer that there are no threads).
- **Size:** a program that does nothing is 5.3 KB (startup, the allocator, the exception messages: all from `zerocore/`, the C64 carries the same) and one that opens and reads a file 27 KB. Of the latter, about 6 KB are `cx16/runtime.c`'s code for writing, appending and removing, which comes along because `fopen`'s mode is only known when it runs, and 4.5 KB the C# of `File.Open`, half of it turning the path into UTF-8. The linker already drops runtime functions nobody calls.
- **The build can loop:** when a code bank comes out too large, `X16MoveToBanks` (`sdk/ZeroCSharp.Bitcode/X16.cs`) tries again with more headroom and never gives up. Seen with `ZERO_CX16_BANK=Program` on `Refs`, `BankedDemo`, `BankedListDemo`, `BitmapDemo` and some Small samples.
- **Sockets** (`lib/Net.cs`): not there, as the X16 has no network.
- **Headless testing** has no frame interrupt: the clock, joysticks and mouse can't be tested there.
