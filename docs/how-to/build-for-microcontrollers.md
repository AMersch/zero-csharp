---
title: Build for microcontrollers
index: 24
---

# Build for microcontrollers

`./build.sh --pico2` and `./build.sh --r4` compile a sample, `lib/` and zerocore for a Cortex-M
microcontroller: the one on a Raspberry Pi Pico 2 or on an Arduino Uno R4. It is the same C#, with
classes, interfaces, generics, lambdas, arenas, strings and collections, on a chip with no operating
system.

| Board | Chip | Processor | RAM | Flash |
|---|---|---|---|---|
| Raspberry Pi Pico 2 (`--pico2`) | RP2350 | Cortex-M33, 150 MHz, with a single-precision FPU | 520 KB | 4 MB |
| Arduino Uno R4 (`--r4`) | Renesas RA4M1 | Cortex-M4, 48 MHz, with a single-precision FPU | 32 KB | 256 KB |

The programs run in QEMU, on the board it emulates with the same processor. `--pico2` also makes each
program for the real Pico 2 ([below](#on-a-real-pico-2)); the Uno R4 is QEMU only so far.

## Get the tools

```bash
./setup.sh --arm
```

This downloads NativeAOT-LLVM (shared with `--wasm`, `--dos` and `--c64`) and
[Arm Toolchain for Embedded](https://github.com/arm/arm-toolchain) into `tools/`: Arm's own clang,
with [picolibc](https://github.com/picolibc/picolibc), a C library for microcontrollers, built for
every Cortex-M. Arm builds it for macOS, x64 and arm64 Linux, and Windows; `setup.sh` checks the file
against the hash Arm publishes. It also checks for QEMU: `brew install qemu`, or
`sudo apt install qemu-system-arm`.

## Build and run

```bash
./build.sh --pico2 Strings          # bin/pico2/Strings.elf
./run.sh --pico2 Strings            # runs it in QEMU, on a Cortex-M33
./test.sh --pico2                   # every sample, checked as on the other targets
./build.sh --r4                     # the samples that fit in the Uno R4
```

`build.sh` prints each program's size in flash (its code and data) and in RAM before the heap (its
writable data and its two stacks). What's left of the RAM is the heap.

A program behaves like a command: its output, standard input, arguments, files and exit code go to
your machine through *semihosting*, which QEMU provides. `./run.sh --pico2 Files` really writes files
in the current folder, and `./run.sh --pico2 Pipe < file` reads `file`.

## What fits

Code and read-only data stay in flash; RAM holds the writable data, the stacks and the heap.

- **Pico 2:** everything the other targets run except the SIMD benchmarks and the DOS game, Llama
  included. A program needs 35 KB of RAM before its heap, most of it the stacks: 16 KB each for the
  processor's stack and the stack managed code keeps its references on. Llama's 1.1 MB model stays in
  flash, and its key/value cache, sized for the 64 tokens it makes, is 82 KB of RAM.
- **Uno R4:** 29 of the samples, those that don't need a big heap. The stacks are 4 KB each, so a
  program needs about 10.5 KB of RAM before its heap and has some 20 KB left. Arena (which allocates a
  16 KB array), Collections, Files, Strings, Queries, Threads, Patterns, Scratch and Unicode don't fit.

String literals go to flash too, so the RAM they would cost is free. Flash is read-only: the memory
protection unit makes a write to it fault, in QEMU as on a chip, so a bug that writes to a string
literal or through a null pointer stops the program instead of going unnoticed.

## The LED, registers and interrupts

`Board` (`lib/Board.cs`) is the hardware: the board's LED, the chip's registers, and the SysTick timer,
which calls a C# method a number of times a second. `samples/Blink.cs` blinks the LED from that
method, an interrupt handler written in C#:

<!-- sample: samples/Blink.cs handler -->
```csharp
// 20 times a second; every fifth tick turns the LED over: two blinks
// a second.
[UnmanagedCallersOnly]
static void OnTick()
{
    s_ticks++;
    if (s_ticks % 5 == 0)
    {
        Board.Led(!Board.LedIsOn);
        s_toggles++;
    }
}
```

The handler is static, takes nothing, and is marked `[UnmanagedCallersOnly]`, because the processor
calls it, not C#. The main program starts the timer and sleeps between ticks:

<!-- sample: samples/Blink.cs blink -->
```csharp
unsafe { Check("the timer starts", Board.StartTicks(20, &OnTick)); }
while (s_ticks < 50)
    Board.WaitForInterrupt();
Board.StopTicks();
```

A handler runs between any two instructions of the rest of the program, so keep it short: count, set
a flag, change a pin. It must not allocate or print. It gets a stack of its own for the references in
its locals, so it can't disturb the code it interrupted; `Blink` checks that too, with a busier
handler at 1000 ticks a second while the main program works.

`Board.Read` and `Board.Write` reach any hardware register. Use them rather than a pointer: the
compiler may merge, move or drop reads and writes through a pointer (a polling loop that reads a
pointer can be turned into one that reads it once), and `Board`'s happen exactly where they are
written.

```csharp no-sample
Board.Write(0xd0000018, 1u << 15);   // Pico 2: GP15 high (SIO GPIO_OUT_SET)
```

## On a real Pico 2

`./build.sh --pico2` writes each program twice: `bin/pico2/<Sample>.elf` for QEMU, and
`bin/pico2/board/<Sample>.uf2` (with its `.elf`) for the board. The board's version starts the chip
itself, from its 12 MHz crystal, and talks over a serial console instead of semihosting:

- **Console:** UART0, 115200 baud, on GP0 (TX, pin 1) and GP1 (RX, pin 2), with GND on pin 3. The
  Raspberry Pi Debug Probe has a UART port for exactly this; any 3.3 V USB-serial adapter works too.
  What you type is echoed, backspace works, and Ctrl-D on an empty line ends the input.
- **No files and no arguments:** there is no host to keep them. `File.Open` fails, as it would on a
  full disk.
- **The end:** the program prints `--- exit code N ---` and the core stops.

To install a program, hold BOOTSEL while plugging the Pico 2 in, so that it shows up as a drive named
`RP2350`, and copy the `.uf2` file to it. Or let the script do it and print the console:

```bash
./run.sh --pico2 --board Strings    # copies to the RP2350 drive, or flashes with a Debug Probe
screen /dev/cu.usbmodem* 115200     # to type to a program, such as Quiz
```

With a Debug Probe wired to the Pico 2's debug pins, `run.sh` writes the program over it with OpenOCD
(`brew install open-ocd`), and no button is needed.

This part hasn't met a real board yet. It has run on [Wokwi](https://wokwi.com)'s simulated Pico 2:
Blink, Llama and most samples behave exactly as in QEMU. The exceptions are `double` division, which
the simulator gets wrong in the last bits, and one sort in Collections. The chip runs at 12 MHz until
the switch to 150 MHz, which needs the flash interface's timing changed too, is tried on a board.

## float, not double

Both processors do `float` arithmetic in hardware and `double` arithmetic in software, which is many
times slower. C# makes literals and `Math` double, so for loops that need speed (sensors, games,
audio), write `1.5f` and use `MathF`. The RP2350 also has a coprocessor that speeds up `double`; this
build doesn't use it yet.

## How it works

The same bitcode as for DOS and the Commodore 64 ([Targets](../concepts/targets.html)), compiled by
Arm's clang for the board's processor. Both sides are 32-bit and little-endian, so unlike on the C64
the bitcode needs no rewriting. `zero-bitcode wasm2arm` writes the program's data as assembly, putting
what nothing writes in `.rodata`; `arm/runtime.c` is zerocore's native half; picolibc's startup code
copies the writable data from flash to RAM and calls `main`. The QEMU boards are laid out like the
real chips: code at `0x10000000` and 520 KB of RAM for the Pico 2 (`arm/an505.ld.in`), 256 KB of flash
at 0 and 32 KB of RAM for the R4 (`arm/an386.ld.in`).

## Why not the Uno R3 or the original Pico

The classic Arduino Uno R3 has 2 KB of RAM, and an 8-bit processor that reads data from flash with
different instructions than from RAM. The smallest program here needs more RAM than that before it
starts. The original Pico, Pico W and Pico H have an RP2040, whose Cortex-M0+ has no divide
instruction and no floating-point unit at all; the RP2350 on the Pico 2 is the better target, and
costs about the same.
