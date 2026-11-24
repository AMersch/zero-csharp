---
title: Get started
index: 0
---

# Get started

This site documents zero-mode C#: C# compiled by a fork of [bflat](https://flattened.net/) into small
native programs that carry no .NET runtime and no garbage collector. A program here is C#'s syntax,
type checking and generics, compiled to something the size of a C program: the hello-world sample is
about 12 KB.

The same source builds for six targets:

- **Native Linux**, x64 or arm64. On macOS the compiler runs in a Linux container.
- **macOS on Apple Silicon**, programs that run on the Mac directly.
- **Windows x64**, built from Linux or macOS and tested under Wine.
- **WebAssembly** (WASI), run with wasmtime.
- **DOS on a 386**, run under FreeDOS in an emulator, or on old hardware.
- **UEFI**, x64 or arm64: a program the PC's firmware starts with no operating system, run in QEMU
  or booted from a USB stick ([Build for UEFI](../how-to/build-for-uefi.html)).

Programs that are small enough also build for a **Commodore 64**, an 8-bit machine with 50 KB for a
program ([Build for the Commodore 64](../how-to/build-for-c64.html)).

Without a runtime, some of C# is missing, and memory works differently: `new` allocates and nothing
ever frees it, so programs allocate from arenas that they free all at once. The repository's core
library (`zerocore/`) and library (`lib/`) give you strings, collections, input and output on those
terms.

## Where to go

1. [Install with dotnet](install-with-dotnet.html): on Linux, Windows or an Apple Silicon Mac,
   `dotnet new zero` and `dotnet build` make a native program, with nothing to clone, for Linux,
   macOS, Windows or UEFI. Or
   [install from the repository](install.html), the bflat fork and its prerequisites, which also
   builds for WebAssembly and DOS.
2. [Build your first program](first-program.html) and run it.
3. Run the same program [on WebAssembly](webassembly.html) and [on DOS](dos.html).

Or skip the installing: [run the samples in your browser](browser.html), compiled to WebAssembly from
this site's version of the code.

If you know C# from .NET, [Coming from .NET](coming-from-dotnet.html) maps what you use there to what
you use here.

Then the [tutorial](../tutorial/index.html) builds a small interactive program step by step, the
[concepts](../concepts/index.html) explain how zero mode works and why, and the
[task guides](../how-to/index.html) answer specific questions.
