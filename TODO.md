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

- **VERA FX, a first part:** the cache for `Bitmap.Clear`, `Fill` and `Tiles.Fill` (4 to 9 times faster), the line helper for `Bitmap.Line` (17 times faster), `CX16.HasVeraFx` and `UseVeraFx`. **Filled triangles** (`Bitmap.FillTriangle`, `Triangle`): by scan lines with cache fills, not the chip's polygon helper (it writes a pixel at a time; see the how-to). Not yet: the affine helper (rotating and zooming tile maps and bitmaps), the multiplier.
- **FM sound (YM2151):** `Fm` (patches in the chip's register order, notes by MIDI number, volume, pan, raw writes) and the PSG's pitch checked, both measured from recordings by `./test.sh --cx16-audio`. Not yet: the ROM's 162 instrument presets, the LFO (vibrato, tremolo) and noise as calls, a sequencer.
- **Real-time clock:** `Clock.UtcNow` and `UnixMilliseconds` read it; `CX16.SetDateTime` sets it.
- **Bitmap:** `Bitmap` (clear, plot, get, fill, rectangle, line, circle, text) and the palette (`CX16.SetColor`) in 320x240 with 256 colors, directly on the VERA; sprite shapes moved to $13000 to keep clear of the bitmap. (`samples/BitmapDemo.cs`) Not yet: other depths (4, 2, 1 bit), 640x480, speed with VERA FX.
- **Tiles:** `Tiles` (map and tile pictures in 2, 4 and 8 bits, 8x8 and 16x16, flips and palette offsets, scrolling) on layer 0. (`samples/TileDemo.cs`) Not yet: the 1-bit text-like tile mode, layer 1 as tiles (it is the text), a second layer of tiles.
- **Screen modes:** `CX16.SetMode`, `CX16.Mode` (all of the KERNAL's modes, sprites and the mouse follow). (`samples/ScreenModes.cs`)
- **Hardware random seed:** `CX16.Entropy`, and `Clock.Monotonic` starts at a random moment, so
  `Random.FromClock()` differs from run to run. (`samples/ClockSeed.cs`)

## 2. Next: sound

| # | Task | Effort | Why |
|---|------|--------|-----|
| 1 | **PCM audio** (VERA FIFO: format, rate, `audio.data`) | M | Samples, speech, and music played as wave data. The FIFO has to be fed faster than it is drained, from a frame interrupt or a tight loop; an interrupt handler written in C# is a new thing here, and the KERNAL's own handler must still run. Needs a plan for where the data is (a file read in chunks, or `Banks`). `-wav` to test. |

## 3. Later, and what needs finding out

| # | Task | Effort | Why it is last |
|---|------|--------|----------------|
| 2 | **VERA FX, the rest** (the affine helper for rotating and zooming tile maps and bitmaps; the 32-bit multiplier; perhaps the polygon helper after all, for narrow triangles) | M each | The cache, the line helper and triangles are in (see Done). The affine helper gives mode-7 effects and is the one that makes the chip do what software can't do fast. The multiplier is a niche: a 16x16 signed multiply and accumulate through the cache. The polygon helper would help only for narrow shapes (see the how-to), so it is last. |
| 3 | **Mouse wheel** | S once known | `mouse_get` in this KERNAL's documentation returns position and buttons only; the wheel isn't in it. Find out first (a newer KERNAL's `mouse_get`, or the PS/2 packets of a 4-byte mouse, read through the I/O chip). Low value next to the rest. |

## Also open

- **Real hardware:** nothing here has run on a real X16. Test at least `Mouse`, `DiskFiles` and a program with code in banks (two files on the SD card).
- **`setup.sh`:** doesn't check for x16emu (only `run.sh` does).
- **`cx16/math.c` for the C64:** the C64 runtime has no sin, cos, exp, log or pow either; the file stands alone and could be shared.
- **Heap with code in banks:** a program that just fits (`SmallMath` has 310 bytes of heap) can run out of memory. The build warns below 3 KB; moving more code to get a minimum was tried and made some programs impossible. A smarter choice (the heap as a goal, with moving the largest functions first) is open.
- **Files:** four open at once, names up to 37 characters; no directory listing yet (`cbm_opendir` exists).
- **Headless testing** has no frame interrupt: the clock, joysticks and mouse can't be tested there.
