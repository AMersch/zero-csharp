---
title: Build for UEFI
index: 17
---

# Build for UEFI

`./build.sh --uefi` compiles the samples to UEFI applications: `.efi` files that a PC's firmware starts
directly, with no operating system underneath. There is no C library either. A program has the firmware,
zerocore, `lib/` and a few C files in `efi/`, and nothing else. Minimal is about 11 KB; Llama, with its
language model inside, is 1.1 MB and boots straight into telling a story.

UEFI programs are PE files using the Windows calling convention, so bflat compiles them the way it
compiles for Windows (`--os uefi`), for x64 or arm64.

## Get the tools

- **QEMU**, with the UEFI firmware OVMF. Homebrew's QEMU includes the firmware (`brew install qemu`).
  On Ubuntu: `sudo apt install qemu-system-x86 ovmf` for x64, `qemu-system-arm qemu-efi-aarch64`
  for arm64. Set `UEFI_FIRMWARE` to use another firmware file.
- **A clang with the Windows targets,** as for `--win`: Xcode's on macOS, the system's on Linux.
- **Python 3**, for `dos/fat16.py`, which makes the disk image.

## Build

```bash
./build.sh --uefi                    # every sample, for this machine's architecture
BFLAT_ARCH=x64 ./build.sh --uefi     # for x64
./build.sh --uefi Vga                # just one
```

Each program goes to `bin/uefi/<Sample>.efi`. `efi/` stands in for the C library:

| File | What |
|---|---|
| `efi/start.c` | `EfiMain`, the entry point: arguments from the UEFI Shell, an 8 MB stack, the exit code |
| `efi/runtime.c` | memory, `write` and `read`, the clock, threads, `abort`, on top of the firmware's boot services |
| `efi/machine.c` | what [`Uefi`](../reference/uefi.html) calls: graphics, keyboard, files, speaker, power |
| `efi/libm/` | the math functions, from [musl](https://musl.libc.org/) 1.2.5 (MIT) |
| `efi/x64.S`, `efi/arm64.S` | switching to the larger stack |

With the .NET SDK packages (see [Install with dotnet](../get-started/install-with-dotnet.html)), a
project builds for UEFI with `dotnet build -p:ZeroTarget=uefi-x64` (or `uefi-arm64`), into
`bin/Debug/zero03/uefi-x64/<Project>.efi`. Use `Uefi` from `lib/` as here.

## Run in QEMU

```bash
./run.sh --uefi Casts                # prints the output, exits with the program's code
./run.sh --uefi --window Vga         # shows the screen
./run.sh --uefi Quiz < answers.txt   # standard input from a file
./test.sh --uefi                     # every sample
```

`run.sh --uefi` reads the program's architecture from the file. It makes a disk holding the program
(as `PROGRAM.EFI`) and a `STARTUP.NSH`, which the UEFI Shell built into OVMF runs when it starts.
Without `--window` the program's input, output and errors are files on that disk, which `run.sh` fills
and prints, and it exits with the program's exit code. A run that hasn't finished after `UEFI_TIMEOUT`
seconds (default 300) is stopped and exits with 125. From a terminal, without input from a file, the
terminal is the machine's console, so you can type to the program.

A program for the Mac's own processor (arm64 on Apple Silicon) runs at full speed under
Hypervisor.framework. One for the other architecture is emulated, which is much slower.

## Draw, read keys, keep files

[`Uefi`](../reference/uefi.html) has the same members as [`Dos`](../reference/dos.html): a 320x200
picture with 256 colors, scaled up on the real screen; keys with PC scan codes; files on the volume
the program was loaded from; the PC speaker on x64. A program written for one runs on the other with
one alias. The Vga game does that:

```csharp no-sample
#if UEFI
using Machine = Uefi;
#else
using Machine = Dos;
#endif
```

bflat defines `UEFI` when it builds for UEFI. UEFI reports key presses but not releases, so `KeyDown`
counts a key as held until its repeats stop: about half a second after a single press. `Uefi` also has
`Shutdown` and `Reboot`.

## Boot a real PC

```bash
scripts/uefi-usb.sh /dev/diskN bin/uefi/Llama.efi
```

This erases the USB stick at `/dev/diskN`, makes it a FAT32 disk, and puts the program where the
firmware looks for one on removable media: `\EFI\BOOT\BOOTX64.EFI` (or `BOOTAA64.EFI` for arm64).
Choose the stick in the PC's boot menu. **Secure Boot has to be off**, because the program isn't
signed with a key the firmware trusts.

Started by the firmware this way, the program gets no arguments, its output goes to the screen, and
when it ends the firmware moves on to its next boot option. To run it with arguments, start it from a
UEFI Shell instead.

## What's different

- **One processor, one thread.** A thread runs to its end when it's created, as on DOS.
- **The clock** is the processor's counter: on x64 the time-stamp counter, measured against the
  firmware's `Stall` when first used; on arm64 the generic timer.
- **Math** comes from musl, so `Exp`, `Log`, `Pow`, `Sin` and the like can differ from Linux's in the
  last digit.
- **C# can call only the C functions `efi/` defines.** `[DllImport("libc")]` of anything else fails to
  link.
- **Memory** is the firmware's pool, as much as the machine has; QEMU gets 1 GB (`UEFI_MEMORY`).

See also: [Build for DOS](build-for-dos.html) and [How the targets work](../concepts/targets.html).
