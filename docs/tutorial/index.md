---
title: Tutorial
index: 0
---

# Tutorial: a quiz

This tutorial builds a small interactive program: an arithmetic quiz that asks questions in a loop,
reads the answers, and lists the mistakes at the end. It is small, but it meets the questions every
zero-mode program meets: how to format output without building strings, how to read input without
allocating, what to do with text that has to outlive the buffer it was read into.

Each step is a complete program in `samples/` that you can build and run, and each adds one idea:

1. [Print the questions](print-questions.html): `Out` and `$"..."` formatting.
2. [Read the answers](read-answers.html): `In.ReadLine` and a buffer on the stack.
3. [Handle bad input](handle-bad-input.html): lines that are too long, not numbers, or empty.
4. [Keep the mistakes](keep-the-mistakes.html): an arena and a list of strings that last until the end.

You need the [repository set up](../get-started/install.html) and a program built once, so you know
the tools work. Every step runs natively, on WebAssembly and on DOS; the commands below use the native
build. Add `--wasm` or `--dos` to `build.sh` and `run.sh` to try the others.
