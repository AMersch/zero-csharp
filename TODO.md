# TODO

What is left to do for the Commander X16 target (`./build.sh --cx16`, `lib/CX16.cs`, `cx16/`), most
important first. Priority is value for programs written in C# divided by the work and the risk;
**Effort** is S (an hour or two: a wrapper and a test), M (a day) or L (several days).

The X16's own hardware is reached from `lib/CX16.cs` through functions in `cx16/runtime.c`
(`cs_cx16_*`), as the mouse and the sprites are. `cx16.h` of llvm-mos already declares most KERNAL
calls. `./test.sh --cx16` runs headless (`x16emu -testbench`), which has no frame interrupt, no sound
output and no mouse: what can only be heard or seen needs a check in a window, or a recording
(`-wav`, `-gif`).

## 1. Done

- **Real-time clock:** `Clock.UtcNow` and `UnixMilliseconds` read it; `CX16.SetDateTime` sets it.
- **Hardware random seed:** `CX16.Entropy`, and `Clock.Monotonic` starts at a random moment, so
  `Random.FromClock()` differs from run to run. (`samples/ClockSeed.cs`)

## 2. Next: the screen

| # | Task | Effort | Why |
|---|------|--------|-----|
| 3 | **Screen modes** (`cx16_k_screen_mode_set/get`: 40x30, 80x30, 80x60 text, and the 320x240 and 640x480 bitmap modes of the KERNAL) | S | `CX16.Columns` and `Rows` already follow the mode, and the mouse range comes from it (`cs_cx16_mouse_config`); what is missing is `CX16.Mode(n)`, and care that `Print`, `Clear` and sprites' limits use the new size. Prerequisite for 4. |
| 4 | **Bitmap layer**: pixels, lines, rectangles, fill, in 2, 4 and 8 bits a pixel (320x240x256 first) | M | The most visible gap: a program can write text and sprites but not draw. The KERNAL's graphics driver (`GRAPH_*`, `FB_*`) has lines, rectangles and text; or write the VERA bytes directly, which is faster. Decide first which VRAM addresses the program owns, so that a bitmap doesn't overwrite sprite shapes ($4000, `lib/CX16.cs`) or the text screen ($1B000). |
| 5 | **Tile layers**: map and tile set, scrolling, per-tile palette offset and flips | M | What games on the X16 are made of (a scrolling background in layer 0 behind the text in layer 1). The API is a layer's configuration (`CX16.Layer0.Tiles(...)`), a tile set copied to VRAM, a map, and `Scroll(x, y)`. Shares the VRAM planning of 4. |

Test: read the layer registers and the VRAM back (as `samples/CX16Demo.cs` does for text and sprites);
a picture check by a window or `-gif`.

## 3. Sound

| # | Task | Effort | Why |
|---|------|--------|-----|
| 6 | **YM2151 FM chip** ($9F40: register select, data, a busy flag): 8 voices of 4 operators | S for raw registers, L for instruments | `CX16.Tone` drives only the VERA's simple voices. Start with `Fm.Write(register, value)` that waits for the chip to be ready, then key on/off, volume and pitch for a voice, then a few preset instruments (the ROM's audio bank has ready ones, but that needs the bank switched in with interrupts off). Checkable in the emulator with `-wav` (the recording isn't silent). |
| 7 | **PCM audio** (VERA FIFO: format, rate, `audio.data`) | M | Samples, speech, and music played as wave data. The FIFO has to be fed faster than it is drained, from a frame interrupt or a tight loop; an interrupt handler written in C# is a new thing here, and the KERNAL's own handler must still run. Needs a plan for where the data is (a file read in chunks, or `Banks`). `-wav` to test. |

## 4. Later, and what needs finding out

| # | Task | Effort | Why it is last |
|---|------|--------|----------------|
| 8 | **VERA FX** (line drawing, polygon filling, affine transformation, a 32-bit multiplier and the cache, through `DCSEL` 2 to 6) | L | Fast, and the most interesting for demos, but niche, specific to VERA 0.3.1 and later (check the emulator and real boards), and useful only on top of 4 and 5. |
| 9 | **Mouse wheel** | S once known | `mouse_get` in this KERNAL's documentation returns position and buttons only; the wheel isn't in it. Find out first (a newer KERNAL's `mouse_get`, or the PS/2 packets of a 4-byte mouse, read through the I/O chip). Low value next to the rest. |

## Also open

- **Real hardware:** nothing here has run on a real X16. Test at least `Mouse`, `DiskFiles` and a program with code in banks (two files on the SD card).
- **`setup.sh`:** doesn't check for x16emu (only `run.sh` does).
- **`cx16/math.c` for the C64:** the C64 runtime has no sin, cos, exp, log or pow either; the file stands alone and could be shared.
- **Heap with code in banks:** a program that just fits (`SmallMath` has 310 bytes of heap) can run out of memory. The build warns below 3 KB; moving more code to get a minimum was tried and made some programs impossible. A smarter choice (the heap as a goal, with moving the largest functions first) is open.
- **Files:** four open at once, names up to 37 characters; no directory listing yet (`cbm_opendir` exists).
- **Headless testing** has no frame interrupt: the clock, joysticks and mouse can't be tested there.
