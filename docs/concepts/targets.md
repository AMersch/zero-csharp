---
title: Targets
index: 5
---

# Targets

The same C# source builds for six targets: a native Linux program, a macOS program for Apple
Silicon, a 64-bit Windows program, a WebAssembly module, a DOS program for a 386, and a UEFI program
that the firmware starts with no operating system. WebAssembly and DOS need a different compiler for
their last step, but all six share the same first step and the same core library. This page explains how each path works and what changes
along it.

## The shared first step

Before any target, `zerocore/` and `lib/` are each compiled into an IL assembly, and every sample is
compiled against them with `-r`. The assembly's metadata describes the public API, so `-r` takes the
place of a header file. Because it holds IL rather than native code, the last step for each program
compiles only what that program uses: generics are instantiated for its types, calls are inlined across
the library boundary, and code is generated for its target and its `-m` flags.

`lib/` is the same assembly for all six targets. zerocore is built a second time with `-d WASM` for
WebAssembly and DOS, because that build finds static fields differently.

## Native Linux

The native target is bflat's own: `bflat build` compiles a sample, with `zerocore/` as its core library
(`--stdlib:none`), into one executable for Linux on x64 or arm64 that links only the system's C library.

```bash
./build.sh Arena      # builds one sample into ./bin
./run.sh Arena        # runs it (in the container on macOS)
```

bflat is a Linux program, so on a Mac it runs in a Linux container of the Mac's own architecture, which
is native on Apple Silicon. `BFLAT_ARCH=x64` or `arm64` cross-compiles.

A few things `build.sh` adds matter for what you get:

- **Instruction sets come only from `-m`.** There is no run-time CPU detection, so Simd and Kernels are
  built with `-m x86-64-v3` on x64 and `-m dotprod` on arm64. Without those flags, their fast paths
  report `IsSupported == false`.
- **4 KB pages on arm64.** The default 64 KB segment alignment would pad even Tiny to about 200 KB, so
  `build.sh` links with a 4 KB maximum page size. Such binaries don't load on kernels with 16 KB or
  64 KB pages; set `ARM64_MAX_PAGE_SIZE=65536` for those.
- **Tiny** is built with `-Os --no-pie` and stripped.

[Your first program](../get-started/first-program.html) walks through a native build.

## macOS on Apple Silicon

`./build.sh --mac` builds programs that run on the Mac itself, without the container. bflat still runs
in the container, but with `--os macos -c` it only compiles: it writes a Mach-O object file, and the
Mac's own clang links it with a few small files:

- `mac/start.c` holds `main`, and `mac/runtime.c` a `clock_gettime` that takes Linux's clock numbers,
  as `lib/Clock.cs` passes them. The rest of the C library calls have the same names on macOS.
- `zerocore/native/mac-arm64.S` is `arm64.S` as a Mach-O object.

```bash
./build.sh --mac Core     # bin/mac/Core
./run.sh --mac Core       # or just bin/mac/Core
```

zerocore is unchanged. The link maps Apple's names for the module section's bounds onto the
`__start___modules` and `__stop___modules` symbols that GNU ld provides on Linux. Programs start at
63 KB, because macOS arm64 aligns each of a program's four segments to 16 KB pages.

## Windows x64

`./build.sh --win` uses the same bflat, with `--os windows --arch x64`, and the same zerocore and
`lib/`. A few small files supply what is Windows-specific:

- `win/start.c` is the entry point. Windows starts a program with no `argc` or `argv`, so it builds
  them from the command line, in UTF-8 as on Linux, and calls `Main`. `win/runtime.c` provides `write`,
  `read` and `clock_gettime` on top of kernel32, and `fseeko` and `ftello`, which the Universal CRT
  calls `_fseeki64` and `_ftelli64`. The rest of the C library comes from the Universal CRT.
- `zerocore/native/win-x64.S` is zerocore's assembly for the Windows calling convention, plus the stack
  probe that Windows needs for frames larger than a page.

```bash
./build.sh --win Core     # bin/win/Core.exe
./run.sh --win Core       # runs it under Wine, in an x64 container
```

The programs print what the native build prints. `puts` ends lines with `\r\n`, as Windows programs
do, and a program zerocore stops exits with code 3. Only x64 is supported.

## WebAssembly

bflat can't target WebAssembly, so `./build.sh --wasm` uses a different compiler for the last step:

1. **bflat** compiles each sample to IL, exactly as for the native build.
2. **[NativeAOT-LLVM](https://github.com/dotnet/runtimelab/tree/feature/NativeAOT-LLVM)**, an
   experimental NativeAOT compiler from dotnet/runtimelab, compiles the IL to LLVM bitcode. It uses
   zerocore, built with `-d WASM`, instead of .NET's runtime.
3. **The WASI SDK's clang** compiles the bitcode and links it with `wasm/runtime.c` and WASI's C
   library. The result runs under wasmtime.

NativeAOT-LLVM normally links a full runtime with a GC, exception handling and a type loader. Here
zerocore is the runtime: allocation, casts and interface calls are its C#, the same code as on Linux.
`wasm/runtime.c` holds only what can't be written in C#: the entry point, and a few functions whose
calling convention differs from a C# method's.

The detail that shapes it is the **shadow stack**. WebAssembly's own stack can't be addressed, so
managed code keeps some locals and arguments on a separate stack in linear memory. Native code entering
managed code hands it that stack, and every C# method takes it as a hidden first argument. Without a
GC, the transitions between native and managed code have nothing else to do.

What differs from the native build:

- **32-bit pointers**, so structs that hold pointers are smaller and arena byte counts are lower.
- **No SIMD.** Simd and Kernels take their scalar paths; `lib/Kernels.cs` has no WebAssembly path yet.
- **No callbacks from native code into C#** inside a running program.
- **One exact compiler version.** zerocore and `runtime.c` implement NativeAOT-LLVM's internal runtime
  functions, whose names and signatures aren't documented and were read from the compiler's output. A
  changed signature fails the link; a changed meaning only shows up when a program runs. Treat the
  versions pinned in `env.sh` as a set.

[Run on WebAssembly](../get-started/webassembly.html) and
[Build for WebAssembly](../how-to/build-for-webassembly.html) have the commands.

## DOS on a 386

bflat's own compiler can make 32-bit x86 programs, but only for a Pentium 4 or later, because its code
generator always uses SSE2 for floating point. The WebAssembly route doesn't have that problem: its
output isn't tied to a processor until the last step. So `./build.sh --dos` reuses it:

1. **bflat and NativeAOT-LLVM** compile each sample to LLVM bitcode, exactly as for WebAssembly, against
   zerocore's WebAssembly build, which is its single-threaded 32-bit build.
2. **`dos/wasm2i386.py`** rewrites the one WebAssembly-specific part. NativeAOT-LLVM writes the
   program's data (type descriptions, string literals, static fields) as a WebAssembly object file. The
   script turns it into assembly with the same bytes and translates its few tiny functions to C.
3. **clang with `-march=i386`** compiles the bitcode and those files to 386 code.
4. **bflat's linker** links it all with `dos/runtime.c` into a 32-bit Windows console program.

The [HX DOS extender](https://github.com/Baron-von-Riedesel/HX) runs such programs under DOS and supplies
the few Windows functions `runtime.c` calls. There is no C library, so `dos/runtime.c` is one: the entry
point, `calloc`, `write`, `clock_gettime`, `fopen` and the other file functions `lib/File.cs` calls,
the math functions and 64-bit division, plus what `wasm/runtime.c` holds for
WebAssembly. Managed code still passes its shadow stack as a hidden first argument, because it is the
same bitcode. `runtime.c` also gives C# the PC itself: the VGA's graphics mode, keyboard, mouse,
speaker, clock and files, which `lib/Dos.cs` wraps.

What the DOS target needs, and how it stays honest:

- **A 386 with a 387, or a 486DX or later.** All floating point uses 387 instructions, with no software
  fallback.
- **2 MB of memory or more.** Each program reserves 256 KB for its stack and 256 KB for the shadow stack.
- **A check that nothing newer than a 386 gets in.** After linking, `build.sh` disassembles each program
  and fails if it finds an instruction a 386 or 387 lacks. This is easy to break: clang's
  `-march=i386` still emits the 486's `cmpxchg` for an atomic operation. zerocore's `Interlocked`
  avoids that by being ordinary code in this build, and it was the first thing the check caught.
- **Bochs for testing.** Its `i386` processor model refuses instructions a 386 doesn't have. QEMU's
  `486` model runs the 486's own instructions, so it can't tell a 386 program from a 486 one.
- **Failure messages on standard output**, since DOS's command line can't redirect anything else. A
  failed cast or an index out of range ends the program with exit code 134.
- **One thread**, and the same exact-compiler-version caveat as WebAssembly.

```bash
./run.sh --dos Casts           # runs under FreeDOS in an emulator
./run.sh --dos --window Vga    # shows the DOS screen
```

[Run on DOS](../get-started/dos.html), [Build for DOS](../how-to/build-for-dos.html) and
[Draw on VGA](../how-to/draw-on-vga.html) go further.

## UEFI, with no operating system

A UEFI program is a PE file with the Windows calling convention, which bflat already makes for
Windows, so `./build.sh --uefi` is bflat again (`--os uefi`), for x64 or arm64. The firmware loads the
file and calls its entry point with a table of its services. There is no operating system and no C
library, so `efi/` supplies the few C functions zerocore and `lib/` call, on top of those services:

- **`efi/start.c`**: the entry point. It takes the arguments from the UEFI Shell as UTF-8, switches to
  an 8 MB stack (the firmware only promises 128 KB), turns off the firmware's 5-minute watchdog, turns
  on AVX on x64 (the firmware leaves it off, and `-m x86-64-v3` code needs it), and returns `Main`'s
  result as the program's status.
- **`efi/runtime.c`**: memory from the firmware's pool, `write` and `read` (files when the shell
  redirects them, the console otherwise), the monotonic clock from the processor's counter and the
  wall clock from the firmware's, and threads that run to their end when they're created.
- **`efi/machine.c`**: what [`Uefi`](../reference/uefi.html) wraps: graphics, keyboard, files, the
  PC speaker, power. It has the same members as `Dos`, so the Vga game runs on both. It also has
  `fopen` and the other file functions [`File`](../how-to/read-and-write-files.html) calls.
- **`efi/libm/`**: the math functions, from musl. On Windows they come from the system's C library;
  here nothing else would provide them.

```bash
./run.sh --uefi Casts          # in QEMU, on OVMF, the UEFI firmware
./run.sh --uefi --window Vga   # shows the screen
```

[Build for UEFI](../how-to/build-for-uefi.html) covers running it and booting a real PC.
