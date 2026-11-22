---
title: Your first program
index: 3
---

# Your first program

The repository's programs live in `samples/`, one file each. `Minimal` is the smallest one that prints
something, and you don't need the repository to build it. This is all of it:

<!-- sample: samples/Minimal.cs file -->
```csharp
// Example: Minimal zero-mode "Hello World" (~39 KB, no .NET runtime).
// Build: ./build.sh Minimal
//
// Under the updated memory safety rules, creating a pointer (stackalloc) is
// safe; only the call that reads through it needs an unsafe block.

using System.Runtime.InteropServices;

namespace MinimalSample;

public static class Program
{
    [DllImport("libc")]
    static unsafe extern int puts(byte* s);

    public static int Main()
    {
        byte* msg = stackalloc byte[] {
            (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o',
            (byte)' ', (byte)'f', (byte)'r', (byte)'o', (byte)'m',
            (byte)' ', (byte)'z', (byte)'e', (byte)'r', (byte)'o',
            (byte)'-', (byte)'m', (byte)'o', (byte)'d', (byte)'e', (byte)'!', 0
        };
        unsafe { puts(msg); } // SAFETY: msg is NUL-terminated.
        return 0;
    }
}
```

It calls the C library's `puts` directly. That's possible because a zero-mode program is a native
program: `[DllImport("libc")]` binds to the C function when the program is linked, with no
marshalling layer in between. The message is a `stackalloc` byte array, which is safe to create; only
the call that reads through the pointer needs an `unsafe` block, with a comment saying why it's safe.
That is C# 15's [updated memory safety rules](../concepts/safety-rules.html) at work.

## Build and run it

With the .NET 10 SDK and the templates ([Install with dotnet](install-with-dotnet.html)), make a
project and put the program above in its `Program.cs`, in place of what's there:

```bash
dotnet new zero -o Minimal
cd Minimal
# Program.cs: the program above
dotnet build
bin/Debug/zero03/Minimal
```

```text
Hello from zero-mode!
```

`dotnet build` compiles the program for the machine you're on, on Linux, Windows (where it's
`bin\Debug\zero03\Minimal.exe`) or a Mac with Apple Silicon. `-p:ZeroTarget=` builds for another
one: `linux-x64`, `linux-arm64`, `win-x64` or `osx-arm64`.

With the [repository](install.html) instead, `./build.sh Minimal` and `./run.sh Minimal` do the same.
`build.sh` compiles the core library (`zerocore/`) and the library (`lib/`) from source first, and it
also builds for WebAssembly and DOS (next page).

## What you built

```bash
ls -l bin/Debug/zero03/Minimal
```

Under 40 KB, debug information included. It needs nothing installed to run: no .NET, no runtime,
only the C library every system has. In the repository, `./build.sh Tiny` builds the smallest sample,
stripped and optimized for size: 16 KB.

## Without puts

Most programs don't call `puts`. They use `lib/`, which gives zero mode the pieces it lacks: `Out` for
output, `In` for input, arenas, strings and collections. With it, the same program is one line:

```csharp no-sample
Out.Line("Hello from zero-mode!");
```

The [tutorial](../tutorial/index.html) starts from there.

## Adding a program of your own

`build.sh` builds the programs it knows by name. To add one:

1. Create `samples/Hello.cs`. Give it its own namespace (`namespace HelloSample;`): the editor
   project compiles all the samples together.
2. Add `Hello` to `ALL_SAMPLES` at the top of `build.sh`, and to `DOS_SAMPLES` if it should build
   for DOS.
3. `./build.sh Hello && ./run.sh Hello`.

Next: the same program [on WebAssembly](webassembly.html).
