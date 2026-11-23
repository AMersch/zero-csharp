---
title: Task guides
index: 0
---

# Task guides

Each guide reaches one goal: the steps, the code, and what to watch out for. If you haven't built a
program yet, start with [Get started](../get-started/index.html); for a longer walk through one program,
the [tutorial](../tutorial/index.html) builds a quiz step by step.

## Text and input

- [Read input from the user](read-input.html): read a line into a buffer with `In.ReadLine`, and handle
  the end of input, lines that don't fit, and numbers; or read raw bytes with `In.Read`.
- [Format text](format-text.html): `$"..."` with alignment and number formats, written to stdout, into an
  arena string or into a `StrBuilder`.
- [Keep a string](keep-a-string.html): copy a line or a scratch result into an arena that lives long
  enough, and collect such strings in a list or map.
- [Read and write files](read-and-write-files.html): whole files in one call, an open file read and
  written piece by piece, and what differs on each target.
- [Use the clock and random numbers](use-the-clock-and-random-numbers.html): measure a duration, tell
  the date and time, and roll dice with numbers that are the same on every target.
- [Read environment variables and exit](use-the-environment.html): `Environment.GetEnvironmentVariable`,
  `Environment.Exit` from anywhere, the processor count and `NewLine`.

## Memory and collections

- [Choose a collection](choose-a-collection.html): `List`, `Dict`, `RefList`, `StrDict` or `RefDict`,
  keys for objects, and copying a collection into another arena.
- [Query data without allocating](query-data.html): `Where`, `Select`, `Sum` and query syntax over
  spans with `lib/Seq.cs`, tuples instead of anonymous types, and checking that a loop allocates
  nothing.
- [Pass structs by reference](pass-structs-by-ref.html): allocate a struct in an arena, pass it with
  `ref` and `in`, return a ref, and keep the ref from outliving a list's buffer or an arena scope.
- [Catch arena mistakes](catch-arena-mistakes.html): turn on `Arena.DebugChecks`, name your arenas, and
  see what the library stops the program for.

## Targets

- [Build for WebAssembly](build-for-webassembly.html): the toolchain, building and running a WASI
  module, and what differs from the native build.
- [Build for DOS](build-for-dos.html): the toolchain, the emulators, memory, input from a file, and
  running a program on a real machine.
- [Draw on the VGA screen](draw-on-vga.html): graphics mode, colors, drawing a frame without flicker,
  and reading the keyboard under DOS.
- [Build for UEFI](build-for-uefi.html): programs the firmware starts with no operating system, running
  them in QEMU, and booting a real PC from a USB stick.

## Speed

- [Use SIMD](use-simd.html): `Vector128` and `Vector256` without run-time CPU detection, the `-m` flags,
  and the ready-made kernels in `lib/Kernels.cs`.
- [Run a loop in parallel](run-in-parallel.html): `Parallel.For` over every processor, sharing a total
  with `Interlocked`, and one arena per thread.
- [Run a language model](run-a-language-model.html): a small Llama-style model, compiled into the
  program, telling stories on every target, DOS and UEFI included.

## Organizing code

- [Create a reusable library](create-a-library.html): a folder in `libs/` that every program can use,
  how it's built, and how to design its API for zero mode.
- [Build a native library](build-a-native-library.html): a `.so`, `.dylib` or `.dll` that .NET
  (`[DllImport]`) and C programs call, its exports, and the limits that carry over.

## Debugging

- [Debug a program](debug-a-program.html): breakpoints and variables in VS Code with CodeLLDB or on the
  command line, unoptimized Debug builds, and strings and spans shown as text.
