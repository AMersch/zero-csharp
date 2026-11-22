---
title: Tutorial
index: 0
---

# Tutorial: a quiz

This tutorial builds a small interactive program: an arithmetic quiz that asks questions in a loop,
reads the answers, and lists the mistakes at the end. It is small, but it meets the questions every
zero-mode program meets: how to format output without building strings, how to read input without
allocating, what to do with text that has to outlive the buffer it was read into.

Each step is a complete program, and each adds one idea:

1. [Print the questions](print-questions.html): `Out` and `$"..."` formatting.
2. [Read the answers](read-answers.html): `In.ReadLine` and a buffer on the stack.
3. [Handle bad input](handle-bad-input.html): lines that are too long, not numbers, or empty.
4. [Keep the mistakes](keep-the-mistakes.html): an arena and a list of strings that last until the end.

## Before you start

You need the .NET 10 SDK and the Zero C# templates ([Install with dotnet](../get-started/install-with-dotnet.html)).
Make the project once:

```bash
dotnet new install ZeroCSharp.Templates
dotnet new zero -o Quiz
cd Quiz
```

Each step's page shows the parts that matter; the whole program is in the repository's `samples/`
folder, linked from each page. Put it in `Program.cs` in place of what's there, then build and run:

```bash
dotnet build
bin/Debug/zero03/Quiz
```

On Windows the program is `bin\Debug\zero03\Quiz.exe`. `dotnet run` works too, but the first
time it also prints what it restored, which gets in the way when you pipe answers in.

With the [repository](../get-started/install.html) instead, each step is a sample: `./build.sh
QuizStep1` and `./run.sh QuizStep1`. There every step also runs on WebAssembly and on DOS: add
`--wasm` or `--dos` to both.
