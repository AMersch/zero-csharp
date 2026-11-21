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
  the end of input, lines that don't fit, and numbers.
- [Format text](format-text.html): `$"..."` with alignment and number formats, written to stdout, into an
  arena string or into a `StrBuilder`.
- [Keep a string](keep-a-string.html): copy a line or a scratch result into an arena that lives long
  enough, and collect such strings in a list or map.

## Memory and collections

- [Choose a collection](choose-a-collection.html): `List`, `Dict`, `RefList`, `StrDict` or `RefDict`,
  keys for objects, and copying a collection into another arena.
- [Query data without allocating](query-data.html): `Where`, `Select`, `Sum` and query syntax over
  spans with `lib/Seq.cs`, tuples instead of anonymous types, and checking that a loop allocates
  nothing.
- [Catch arena mistakes](catch-arena-mistakes.html): turn on `Arena.DebugChecks`, name your arenas, and
  see what the library stops the program for.

## Targets

- [Build for WebAssembly](build-for-webassembly.html): the toolchain, building and running a WASI
  module, and what differs from the native build.
- [Build for DOS](build-for-dos.html): the toolchain, the emulators, memory, input from a file, and
  running a program on a real machine.
- [Draw on the VGA screen](draw-on-vga.html): graphics mode, colors, drawing a frame without flicker,
  and reading the keyboard under DOS.

## Speed

- [Use SIMD](use-simd.html): `Vector128` and `Vector256` without run-time CPU detection, the `-m` flags,
  and the ready-made kernels in `lib/Kernels.cs`.
- [Run a loop in parallel](run-in-parallel.html): `Parallel.For` over every processor, sharing a total
  with `Interlocked`, and one arena per thread.
