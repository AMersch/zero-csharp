---
title: Debug a program
index: 16
---

# Debug a program

A zero-mode program is native code with debug information, so you debug it with a native debugger.
On macOS and Linux that's LLDB, from VS Code through the CodeLLDB extension or on the command line.
On Windows, where the linker writes a PDB next to the program, it's Microsoft's debugger, through
VS Code's C/C++ extension. The C# debugger that comes with VS Code's C# extension can't attach,
because there is no .NET runtime in the program for it to talk to.

This guide covers projects made with the .NET SDK
([Install with dotnet](../get-started/install-with-dotnet.html)).

## Debug builds aren't optimized

`dotnet build` and `dotnet run` build the Debug configuration, and the SDK compiles Debug without
optimizations (bflat's `-O0`). The debugger then stops on the line you put a breakpoint on, steps one
line at a time, and shows every local variable. `-c Release` builds an optimized program:

```bash
dotnet build                 # bin/Debug/zero03/: unoptimized, for debugging
dotnet build -c Release      # bin/Release/zero03/: optimized, for speed and size
```

`ZeroOptimizeFor` chooses for yourself, whatever the configuration: `None`, `Speed` or `Size`. In an
optimized program the debugger still works, but breakpoints can land on a nearby line, stepping jumps
around, and some variables show values that are out of date.

A Release build also leaves out the debug information and the symbol table, so a debugger sees no
names in it; `ZeroStripSymbols` set to `false` keeps them.

An unoptimized program is bigger and slower. `Minimal` is about 57 KB on Linux in Debug and about
15 KB in Release; the storyteller from `dotnet new zerollama` takes about twice as long. Time and
measure Release builds.

## In VS Code

Projects made by `dotnet new zero` and `dotnet new zerollama` come with a `.vscode` folder that sets
everything up:

| File | What it does |
|---|---|
| `launch.json` | builds the project, then starts `bin/Debug/zero03/<project>` in the integrated terminal, so the program can read what you type: under CodeLLDB, or under Microsoft's debugger in the configuration named `(Windows)` |
| `tasks.json` | the `build` task (`dotnet build`), also the default build task (⇧⌘B or Ctrl+Shift+B) |
| `settings.json` | `debug.allowBreakpointsEverywhere`, which lets you set breakpoints in `.cs` files without the C# debugger |
| `extensions.json` | recommends CodeLLDB, so VS Code offers to install it |
| `zero_lldb.py` | shows `string`, `Span<char>` and `ReadOnlySpan<char>` as text in LLDB (see below) |
| `zero.natvis` | the same for Microsoft's debugger, on Windows |

To debug on macOS or Linux:

1. Open the project's folder in VS Code (the folder with the `.csproj`, so VS Code finds `.vscode`).
2. Install CodeLLDB (`vadimcn.vscode-lldb`) when VS Code offers it. The first time it starts, it
   downloads its own LLDB.
3. Click in the margin next to a line of `Program.cs` to set a breakpoint, and press F5.

If VS Code says `Configured debug type 'lldb' is not supported`, CodeLLDB isn't installed yet. If it
says to restart before reinstalling CodeLLDB, quit VS Code completely and open it again.

On Windows, install the C/C++ extension (`ms-vscode.cpptools`) instead, choose `Debug <project>
(Windows)` in the Run and Debug view, and press F5. CodeLLDB on Windows stops on the right line but
shows no variables: the PDB's functions lack a type record that LLDB needs to read their locals, and
Microsoft's debugger doesn't.

For a project made before these files existed, copy the `.vscode` folder from a new
`dotnet new zero` project and change the program's name in `launch.json`. To pass arguments to the
program, add them to `"args"` in `launch.json`.

## On the command line

The same works with LLDB directly. A breakpoint by file and line, standard input from a file, and the
locals when it stops:

```bash
lldb bin/Debug/zero03/Hello
(lldb) command script import .vscode/zero_lldb.py
(lldb) b Program.cs:17
(lldb) process launch -i input.txt
(lldb) frame variable
```

On Windows, `cdb` from the Windows SDK's Debugging Tools reads the PDB. `.nvload` loads the natvis
file, `bp` with a file and line in backquotes sets the breakpoint, and `dv` lists the locals:

```text
cdb -lines bin\Debug\zero03\Hello.exe
0:000> .nvload .vscode\zero.natvis
0:000> bp `Hello!Program.cs:17`
0:000> g
0:000> dv
```

## Strings and spans

LLDB doesn't know C#'s types, so a span of characters would show as a pointer and a length.
`zero_lldb.py` teaches it to show the text instead:

```text
(S_P_CoreLib_System_ReadOnlySpan_1<Char>) name = "Ann" length=3
(S_P_CoreLib_System_Span_1<Char>) buffer = "Ann" + 61 \0 length=64
(String &) greeting = 0x000000010000c788 "Hello, world"
```

Characters past the end of the text in a buffer show as a count of `\0`s, quotes and control
characters are escaped, and text over 1024 characters is cut off with `...`. Expanding a variable
still shows its fields. Spans of other element types show as LLDB shows any struct: a pointer to
the first element and a length.

On Windows, `zero.natvis` does the same for Microsoft's debugger, showing a span as its text and
length (`"Ann" length=3`) and a string as its text.

## What to expect

- **Names are compiler names.** In the call stack, `Main` is `Hello_Program___Main__` on Linux and
  `_zero_native_osx_arm64_Program___Main__` on macOS, and types carry their namespace:
  `S_P_CoreLib_System_Span_1<Char>` is `Span<char>`. To break in a function, use its file and line
  rather than its C# name.
- **On macOS, the debug information stays in `obj/`.** The program refers to the object file the
  build left in `obj/Debug/zero03/`, and LLDB reads the information from there. Keep `obj/` while you
  debug, or run `dsymutil bin/Debug/zero03/<project>` to collect it into a `.dSYM` next to the
  program. (Apple's `dwarfdump` reports that object file as malformed; LLDB reads it correctly.)
- **A `[DllImport]` of a library that isn't linked fails differently.** Both builds fail, but in
  Debug bflat stops with `Expected type 'Internal.Runtime.CompilerHelpers.InteropHelpers' not found`
  instead of naming the function. Either way, add the library to `ZeroDllImports`.
- **A Debug build can allocate where a Release build doesn't.** Comparing two record structs with
  `==` boxes them in unoptimized code: an allocation per comparison, which in zero mode is never
  freed unless it's in an arena. Check allocation counts (`GC.GetTotalAllocatedBytes`) in Release.
