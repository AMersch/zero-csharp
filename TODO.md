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

- **VERA FX, a first part:** the cache for `Bitmap.Clear`, `Fill` and `Tiles.Fill` (4 to 9 times faster), the line helper for `Bitmap.Line` (17 times faster), `CX16.HasVeraFx` and `UseVeraFx`. Not yet: the polygon filler (filled triangles), the affine helper (rotating and zooming tile maps and bitmaps), the multiplier.
- **Real-time clock:** `Clock.UtcNow` and `UnixMilliseconds` read it; `CX16.SetDateTime` sets it.
- **Bitmap:** `Bitmap` (clear, plot, get, fill, rectangle, line, circle, text) and the palette (`CX16.SetColor`) in 320x240 with 256 colors, directly on the VERA; sprite shapes moved to $13000 to keep clear of the bitmap. (`samples/BitmapDemo.cs`) Not yet: other depths (4, 2, 1 bit), 640x480, speed with VERA FX.
- **Tiles:** `Tiles` (map and tile pictures in 2, 4 and 8 bits, 8x8 and 16x16, flips and palette offsets, scrolling) on layer 0. (`samples/TileDemo.cs`) Not yet: the 1-bit text-like tile mode, layer 1 as tiles (it is the text), a second layer of tiles.
- **Screen modes:** `CX16.SetMode`, `CX16.Mode` (all of the KERNAL's modes, sprites and the mouse follow). (`samples/ScreenModes.cs`)
- **Hardware random seed:** `CX16.Entropy`, and `Clock.Monotonic` starts at a random moment, so
  `Random.FromClock()` differs from run to run. (`samples/ClockSeed.cs`)

## 2. Next: sound

| # | Task | Effort | Why |
|---|------|--------|-----|
| 1 | **YM2151 FM chip** ($9F40: register select, data, a busy flag): 8 voices of 4 operators | S for raw registers, L for instruments | `CX16.Tone` drives only the VERA's simple voices. Start with `Fm.Write(register, value)` that waits for the chip to be ready, then key on/off, volume and pitch for a voice, then a few preset instruments (the ROM's audio bank has ready ones, but that needs the bank switched in with interrupts off). Checkable in the emulator with `-wav` (the recording isn't silent). |
| 2 | **PCM audio** (VERA FIFO: format, rate, `audio.data`) | M | Samples, speech, and music played as wave data. The FIFO has to be fed faster than it is drained, from a frame interrupt or a tight loop; an interrupt handler written in C# is a new thing here, and the KERNAL's own handler must still run. Needs a plan for where the data is (a file read in chunks, or `Banks`). `-wav` to test. |

## 3. Later, and what needs finding out

| # | Task | Effort | Why it is last |
|---|------|--------|----------------|
| 3 | **VERA FX, the rest** (polygon filler for filled triangles; affine helper for rotating and zooming tile maps and bitmaps; the 32-bit multiplier) | M each | The cache and the line helper are in (see Done). The polygon filler is the next most useful (`Bitmap.FillTriangle`): the doc gives the steps (row by row: read DATA1, read the fill length from FX_POLY_FILL, write DATA1 that many pixels) but no example, so it would be worked out in the emulator like the line helper. The affine helper gives mode-7 effects. The multiplier is a niche: a 16x16 signed multiply and accumulate through the cache. |
| 4 | **Mouse wheel** | S once known | `mouse_get` in this KERNAL's documentation returns position and buttons only; the wheel isn't in it. Find out first (a newer KERNAL's `mouse_get`, or the PS/2 packets of a 4-byte mouse, read through the I/O chip). Low value next to the rest. |

## Also open

- **Real hardware:** nothing here has run on a real X16. Test at least `Mouse`, `DiskFiles` and a program with code in banks (two files on the SD card).
- **`setup.sh`:** doesn't check for x16emu (only `run.sh` does).
- **`cx16/math.c` for the C64:** the C64 runtime has no sin, cos, exp, log or pow either; the file stands alone and could be shared.
- **Heap with code in banks:** a program that just fits (`SmallMath` has 310 bytes of heap) can run out of memory. The build warns below 3 KB; moving more code to get a minimum was tried and made some programs impossible. A smarter choice (the heap as a goal, with moving the largest functions first) is open.
- **Files:** four open at once, names up to 37 characters; no directory listing yet (`cbm_opendir` exists).
- **Headless testing** has no frame interrupt: the clock, joysticks and mouse can't be tested there.
