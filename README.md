# bflat Zero Mode Examples

![bflat debugging in VS Code](bflat.png)

Exploring [bflat](https://flattened.net/)'s zero mode: C# compiled to tiny native binaries without .NET's runtime, written in **C# 15** under the **updated memory safety rules**.

**Zero mode is essentially "C with proper generics"**: C#'s syntax, type safety, and generics with C-like binary sizes (16-150 KB).

The samples don't use bflat's built-in zero-mode library (`--stdlib:zero`, zerolib). They use [`zerocore/`](zerocore/README.md), an MIT-licensed core library in this repo that `build.sh` compiles from source, so nothing AGPL-licensed is linked into the binaries (see [Core library](#core-library)).

## Quick start

```bash
./setup.sh            # checks prerequisites (+ builds the container image on macOS)
./build.sh            # builds every sample into ./bin
./build.sh Arena      # or just one
./run.sh Arena        # runs it (in the container on macOS)

./setup.sh --wasm     # WebAssembly: also downloads the toolchain (see below)
./build.sh --wasm     # builds every sample into ./bin/wasm/*.wasm
./run.sh --wasm Arena # runs it with wasmtime
```

Prerequisites:

- **The bflat fork** (see below). By default `../bflat/layouts/linux-glibc-<arch>`; override with `BFLAT_DIR`.
- **macOS:** Docker. bflat can't target macOS, so it runs in a Linux container of your Mac's own architecture (native on Apple Silicon).
- **Linux:** `libtinfo.so.5`, which bflat's bundled linker needs, and an assembler (`as`, from binutils) for zerocore's assembly routines. On Ubuntu 22.04: `sudo apt install libtinfo5 binutils`. Ubuntu 24.04 no longer ships libtinfo5. Cross-compiling needs the target's assembler too (`binutils-x86-64-linux-gnu` or `binutils-aarch64-linux-gnu`).

Windows: `.\setup.ps1` and `.\build.ps1` use the fork's `windows-x64` layout but are **untested**. The samples P/Invoke into `libc`, which may not resolve on Windows. zerocore has no Windows port, so `build.ps1` still builds against zerolib and skips the Casts sample.

## Getting bflat

The samples need a bflat fork, [OnurGumus/bflat](https://github.com/OnurGumus/bflat) (branch `master`). `build.sh` uses it as a compiler only: the first, fourth and fifth items below. The rest are changes to the fork's zerolib, which matter if you build with `--stdlib:zero` (as `build.ps1` still does). The fork differs from upstream bflat 10.0.0-rc.1 in these ways:

- **It compiles C# 15.** Roslyn is bumped to 5.12.0, the version shipped with the .NET 11 SDK. A new `--compiler-feature` option (the same as `csc -features:`) turns on the updated memory safety rules.
- **SIMD.** zerolib defines `Vector64/128/256<T>` (with `Create` and `As*`) and a subset of the hardware intrinsics: `AdvSimd` and `Dp` (SDOT) on arm64; `Sse`, `Avx`, `Avx2` and `Fma` on x64. That covers float arithmetic, FMA, min/max, rounding, conversions, divide and int8 dot products. The compiler treats them as real intrinsics.
- **Atomics and half floats.** `Interlocked` (compiled to `lock xadd`/`cmpxchg` and `ldaxr`/`stlxr`), `Volatile`, `Half`, `BFloat16` and the `BitConverter` bit conversions. Half/BFloat16 conversions are bit-for-bit identical to .NET 11's.
- **Instruction sets beyond the baseline come only from `-m`.** Zero mode has no run-time CPU detection, and upstream bflat failed to link on arm64 when code checked e.g. `Dp.IsSupported` without `-m`. Now such checks are simply `false` unless the instruction set is given with `-m`.
- **The .NET 10 GA JIT.** Upstream bflat 10.0.0-rc.1 ships the .NET 10 RC1 JIT, which miscompiles some static-field stores on x64. The fork's build swaps in Microsoft's GA JIT, which fixes them.
- **zerolib fixes.** `ThrowOverflowException` is added, so checked arithmetic and **integer division on arm64** compile. `Console.WriteLine(int)` is fixed.
- **Strings, arrays, statics and casts in zerolib.** Upstream zero mode can't compile or run a lot of ordinary C#; the fork makes these work:
  - `string` gets `==`, `!=`, `Equals`, `IsNullOrEmpty` and `Concat`, so `a == b`, `a + b` (string operands) and `switch` on strings compile. `+` allocates with malloc and is never freed, like every allocation in zero mode.
  - Static fields of reference types (`static string`, objects, arrays). Upstream segfaults on the first store: the compiler expects the runtime to allocate their storage at startup, and zerolib didn't. It now does, before `Main`.
  - `new T[n]` links (`RhpNewArrayFast`/`RhpNewPtrArrayFast`). Arrays of reference types (`string[]`, `object[]`) and class casts (`(T)x`, `is`, `as`) compile (`System.Runtime.TypeCast`). Casts to interface and array types still aren't supported.
  - Top-level statements, which need a `string[] args`.
  - Static constructors that build strings no longer crash the compiler: its static preinitializer looks up string's length field by its CoreLib name, `_stringLength`.
  - `new` zeroes memory (calloc instead of malloc), as C# requires.
- **A WASI build of zerolib** (`lib/wasi/wasm/zerolib.dll`), for [WebAssembly](#webassembly). It calls WASI's C library directly (no `libSystem.Native`) and uses 32-bit pointers.

  To update only zerolib in existing layouts, without the full build below, run `bflat build-il --deterministic --stdlib:none -d LINUX -d ARM64 -o:<layout>/lib/linux/arm64/zerolib.dll` (and so on per target) in `src/zerolib`, as `src/bflat/bflat.csproj` does; then rebuild `arenalib`, which references it.

The fork is AGPL-3.0, like upstream bflat's source. Binaries built with `--stdlib:zero` contain its zerolib; binaries built by `build.sh` don't (see [Core library](#core-library)).

Building the fork needs a GitHub token with **only** `read:packages` (bflat's packages live on GitHub Packages), and runs in an x64 .NET 10 container:

```bash
cd ../bflat
# src/bflat/NuGet.config (gitignored) reads the token from BFLAT_PACKAGES_TOKEN
# (the token file is mounted read-only, so it never appears on a command line)
docker run --rm --platform linux/amd64 -v "$PWD:/src" -w /src \
  -v ~/.config/bflat/packages-token:/run/token:ro \
  mcr.microsoft.com/dotnet/sdk:10.0 bash -c \
  'BFLAT_PACKAGES_TOKEN=$(cat /run/token) dotnet build src/bflat/bflat.csproj -t:BuildLayouts -c Release'
```

This produces `layouts/linux-glibc-{x64,arm64}` and `layouts/windows-{x64,arm64}`, and takes about 15 minutes under emulation.

## Core library

Every C# program needs a core library: the assembly that defines `object`, `int` and `string`, and holds the runtime code the compiler calls to allocate objects, check casts and set up static fields. .NET's is `System.Private.CoreLib` plus a native runtime, megabytes of it. bflat's zero mode replaces both with zerolib, about 4,000 lines, which is AGPL-3.0 and gets compiled into every binary.

[`zerocore/`](zerocore/README.md) is this repo's replacement for zerolib: about 2,100 lines of C# and three short assembly routines per architecture, under the MIT license. `build.sh` compiles it from source and passes it to bflat with `--stdlib:none`. Compared with zerolib it adds casts to interfaces, interface calls the compiler can't resolve at build time, boxing, and `Main(string[] args)`; it leaves out `Console`, `Math`, `Interlocked`, `Volatile` and `Half`, and it runs on Linux and WebAssembly only. Its README lists what works, what doesn't, and where the code comes from, including a caveat about the license that's worth reading before building a product on it.

Sample binaries come out about 10 KB smaller than with zerolib (Strings 17 KB smaller), except Tiny, which is 0.6 KB larger.

## Building

`zerocore/` and `lib/` are each compiled once into an IL assembly, and each sample references them with `-r`:

```bash
F="--langversion preview --compiler-feature updated-memory-safety-rules"
bflat build-il $(find zerocore -name '*.cs') -O --stdlib:none --langversion preview \
    -o bin/System.Private.CoreLib.dll
as zerocore/native/arm64.S -o bin/zerocore-arm64.o
bflat build-il lib/*.cs -O --stdlib:none -r bin/System.Private.CoreLib.dll $F \
    --target Shared -o bin/BflatLib.dll
bflat build samples/Arena.cs -r bin/BflatLib.dll -r bin/System.Private.CoreLib.dll \
    --stdlib:none --no-reflection -i libc $F --ldflags bin/zerocore-arm64.o -o bin/Arena
```

This is how you ship a bflat library without header files. The assembly's metadata describes the public API, so `-r` takes the place of `#include`. The assembly holds IL, not native code, so each sample's build compiles just the parts it uses into one static binary: generics are instantiated for the caller's types, calls are inlined across the library boundary, and code is generated for the caller's `-m` flags. One `BflatLib.dll` therefore gives NEON code on arm64 and AVX2 code on x64. Sizes match building from source to within about 1 KB.

- Pass `-O` to `build-il`. Unlike `bflat build`, it doesn't optimize unless you ask, and without `-O` the samples come out up to 1.4 KB bigger.
- The library and its consumers must be built against the same core library.
- The core library has to be named `System.Private.CoreLib`, and builds against it need `--no-reflection` and `-i` for each `[DllImport]` library (`build.sh` passes `-i libc -i libm.so.6`). [`zerocore/README.md`](zerocore/README.md) explains why.

`build.sh` adds:

- `--arch` for the target (default: this machine's architecture; set `BFLAT_ARCH=x64` or `arm64` to cross-compile).
- `-Os --no-pie` plus `strip` for Tiny.
- For Simd and Kernels: `-m x86-64-v3` on x64 (AVX2/FMA; Haswell or newer) and `-m dotprod` on arm64 (SDOT; ARMv8.2+, e.g. Apple M1).
- On arm64, `--ldflags "-z max-page-size=4096"`. Without it, the default 64 KB segment alignment pads even Tiny to ~200 KB. 4 KB-page binaries don't load on 16 KB/64 KB-page kernels (Asahi Linux, some servers); set `ARM64_MAX_PAGE_SIZE=65536` for those.

## WebAssembly

`./build.sh --wasm` compiles the same samples, `lib/` and `zerocore/` to WebAssembly modules for WASI, from 15 KB (Tiny) to 77 KB (Strings), stripped. All nine run under wasmtime: Strings passes its 140 checks and Casts its 41, both printing exactly what the native build prints, and the others match too, apart from what depends on the platform (below).

bflat can't target WebAssembly, so this uses a different compiler for the last step:

1. **bflat** compiles each sample to IL, exactly as for the native build, against `bin/BflatLib.dll`.
2. **[NativeAOT-LLVM](https://github.com/dotnet/runtimelab/tree/feature/NativeAOT-LLVM)**, an experimental NativeAOT compiler from dotnet/runtimelab, compiles the IL to LLVM bitcode. It uses zerocore (built with `-d WASM`) instead of .NET's runtime.
3. **The WASI SDK's clang** compiles the bitcode and links it with [`wasm/runtime.c`](wasm/runtime.c) and WASI's C library. `runtime.c` is zerocore's native half on WebAssembly, 40 lines of code: the entry point, the shadow stack, and the transitions NativeAOT-LLVM's code calls, all trivial without a GC. Allocation, casts and interface calls are zerocore's C#, the same code as on Linux.

`./setup.sh --wasm` downloads all three tools, pinned in `env.sh` (NativeAOT-LLVM 10.0.0-rc.1.26357.1, WASI SDK 34.0, wasmtime 50.0.1), into `tools/wasm`: about 900 MB, mostly the WASI SDK. They're Linux builds and run in the build container on macOS, like bflat.

Differences from the native build:

- **32-bit pointers.** Structs that hold pointers are smaller, so the arena byte counts printed by Arena and Collections are lower.
- **No SIMD.** Simd and Kernels take their scalar paths. WebAssembly has 128-bit SIMD, but `lib/Kernels.cs` has no path for it yet.
- **Clocks.** Simd and Kernels call `clock_gettime(1, ...)`, Linux's numeric `CLOCK_MONOTONIC`. In WASI's C library a clock ID is a pointer, so this reads the clock ID from address 1, which is zero: the realtime clock. That still measures durations.
- **Exit codes.** Modules are linked for WASI 0.1, which passes exit codes through. WASI 0.2 only reports success or failure.
- **One exact compiler version.** `wasm/runtime.c` and zerocore implement NativeAOT-LLVM's internal runtime functions (`RhpNewArray`, `RhpResolveInterfaceDispatch` and others). Their names and signatures aren't documented; they were read from the compiler's output. A newer NativeAOT-LLVM can change them. A changed signature fails the link, since `build.sh` turns `wasm-ld`'s signature warnings into errors, but a changed meaning only shows up when a program runs. So treat the versions in `env.sh` as a set, and after changing any of them, rerun the WASM samples and compare their output with the native build.
- **Not supported:** callbacks from native code into C# (`[UnmanagedCallersOnly]` reverse P/Invoke inside a running program), and `build.ps1` (Windows).

## Editor support

The `.csproj` files and `BflatTest.slnx` exist only so VS Code (C# Dev Kit) and other IDEs give IntelliSense that matches zero mode. bflat ignores them, so keep building with `build.sh`.

- `Directory.Build.props` compiles every project against zerocore instead of the .NET standard library, with the same C# 15 settings `build.sh` uses. Completion only offers what zero mode has, and the editor flags the same memory-safety errors bflat does.
- `zerocore/ZeroCore.csproj` is the core library itself, so nothing outside the repo is needed for IntelliSense, not even bflat, and F12 on `string` or `Span<T>` opens their source.
- `lib/BflatLib.csproj` covers `lib/*.cs`, and `samples/Samples.csproj` covers every sample. Each sample is a separate program with its own `Program` and `Main`, so each one has its own namespace (`ArenaSample`, `TinySample`, ...) to keep them apart. Avoid names like `Samples.Arena`: inside that namespace, `Arena` means the namespace, not the library's `Arena` class.
- `Samples.csproj` references the library project, so F12 opens the library's source.
- `dotnet build BflatTest.slnx` type-checks everything without running bflat. The editor uses the Roslyn from your .NET SDK and bflat uses the fork's, so a very new preview feature could pass one and fail the other.

## Updated memory safety rules

The samples compile with C# 15's [updated memory safety model](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/unsafe-code#the-updated-memory-safety-model-preview), which is in preview:

- **Pointers are safe to create, unsafe to use.** Pointer types, `&`, `stackalloc` and `fixed` work in safe code. Dereferencing (`*p`, `p->x`, `p[i]`) and calling function pointers need an `unsafe { }` block.
- **`unsafe` on a member is a contract.** Callers must be in an unsafe context too, and the body still needs inner `unsafe { }` blocks.
- **Every `extern` says `safe` or `unsafe`.** For example, `malloc` is `safe` and `free` is `unsafe`.

In `lib/Arena.cs`, allocating is safe: you get a pointer, and using it needs `unsafe` anyway. `Restore()`, `Reset()` and `Destroy()` are marked `unsafe`, because calling them is what turns other pointers into dangling ones:

```csharp
Arena arena = Arena.Create();
Point* p = arena.Alloc<Point>();         // safe
unsafe { p->X = 10; }                    // dereference: unsafe
unsafe { arena.Reset(); }                // SAFETY: p is not used after this
```

## Examples

| Sample | Shows | Size (Linux arm64) |
|---|---|---|
| `samples/Tiny.cs` | Smallest binary (`-Os --no-pie`, stripped) | ~16 KB |
| `samples/Minimal.cs` | Hello world via `puts` | ~38 KB |
| `samples/Arena.cs` | Growable arena: Fibonacci growth, dedicated blocks for large allocations, per-allocation alignment, `Reset` that keeps capacity, `Save`/`Restore` for temporary work, growing in place, a second independent arena | ~67 KB |
| `samples/Collections.cs` | `List<T>` and `Dict<K,V>` on top of an arena, including `Clone` into an arena that outlives the original | ~87 KB |
| `samples/VirtualTest.cs` | Polymorphism three ways: function-pointer vtables, static abstract interfaces, virtual methods | ~50 KB |
| `samples/Casts.cs` | Tests runtime type checks (41 checks): `is`, `as` and `(T)x` to interfaces and classes, generic interfaces, boxed structs, interface calls with many arguments and default methods, arrays of interfaces, delegates | ~93 KB |
| `samples/Simd.cs` | Float dot product with NEON (arm64) or AVX + FMA (x64), checked against scalar and timed (12x faster on Apple Silicon) | ~52 KB |
| `samples/Kernels.cs` | Tests and times `lib/Kernels.cs`: exp within 1 ulp of libm, softmax, SiLU, RMSNorm, matrix-vector product, exact int8 dot product (softmax 7x faster than scalar libm on Apple Silicon) | ~64 KB |
| `samples/Strings.cs` | Tests the string support (140 checks): span operations, parsing integers, hex and doubles (bit-exact), `$"..."` formatting with alignment and format strings, arena strings, `StrBuilder`, and the core library's `==`, `+`, string `switch` and static string fields; prints a table and UTF-8 text through `Out` | ~161 KB |

Sizes are for linux-arm64 with 4 KB pages and include debug info (only Tiny is stripped); x64 is within a few KB. Run `strip` to shrink the others.

### `lib/`

| File | What |
|---|---|
| `Arena.cs` | Growable bump allocator; see [Arenas](#arenas). Normal blocks grow 128, 128, 256, 384, 640, ...; an allocation too big for the next block gets its own block without changing that sequence. Alignment is per allocation, and a failed allocation returns null and leaves the arena untouched. |
| `List.cs` | Growable array, created in the arena you pass (`List<int>.Create(arena)`). It's a handle to an arena header, so copies share contents. `default(List<T>)` is safe to use: reads see an empty list and `Add` returns false. `Clone(arena)` makes an independent copy in any arena. |
| `Dict.cs` | Open-addressing hash map (FNV-1a, power-of-two capacity). Keys are compared by raw bytes: fine for integers and padding-free structs, not for floats. |
| `Out.cs` | Buffered stdout writer, UTF-8 encoded; zerocore has no `Console`. `Out.Line($"...")` formats straight into the buffer. |
| `Str.cs` | String support (see [Strings](#strings)): span operations as `System.MemoryExtensions`, `Str.TryParse`, `Str.Compare`, and new strings in an arena with `Str.Format` and `Str.Concat`. |
| `StrBuilder.cs` | Growable string buffer in an arena, with `Append($"...")`. A handle like `List<T>`. |
| `Kernels.cs` | SIMD math for LLM inference: `Dot`, `MatVec`, `DotI8`, `Exp`, `Softmax`, `Silu`, `RmsNorm`. Each has NEON, AVX2+FMA and scalar paths and handles any length. |

## What zero mode supports

Zero mode has no garbage collector, no exceptions (failures call `Environment.FailFast`), no async/await, no LINQ, and almost no base class library. On the other hand, these all work with zerocore:

- `Span<T>`, and arrays: `new T[n]`, including arrays of strings, objects and interfaces
- Static fields of every type, including strings, objects and arrays; static constructors
- Strings: `==`, `+`, `switch`, plus `lib/` for the rest (see [Strings](#strings))
- Casts: `(T)x`, `is`, `as` and type patterns, to classes and interfaces. Not through variance (`IEnumerable<string>` as `IEnumerable<object>`, `string[]` as `object[]`): those abort with a message.
- Boxing: a struct converted to `object` or to an interface it implements
- `Main(string[] args)` and top-level statements
- Generics, including constrained calls and **static abstract interface members**. These are resolved at compile time, which suits per-format compute kernels.
- Classes with virtual methods, and interfaces, including default interface methods. Every `new` calls calloc and is never freed. An interface call the compiler can't resolve at build time searches the class's method map on every call, so keep those out of hot loops.
- Delegates and lambdas (they allocate), `try`/`finally` and `using`
- Function pointers, `[UnmanagedCallersOnly]` callbacks, and P/Invoke. pthreads work, for example.

[`zerocore/README.md`](zerocore/README.md) has the full list, including what doesn't work.

## Arenas

`new` in zero mode never frees, so anything short-lived goes in an arena: memory handed out by moving a pointer forward and freed all at once. `lib/Arena.cs` is an instance type, so you create one per lifetime and pass it to whatever allocates:

```csharp
Arena app = Arena.Create();                  // lives as long as the program
Arena frame = Arena.Create(64 * 1024);       // reset after every frame or request

var names = List<int>.Create(app);           // collections live in the arena you give them
string line = Str.Format(frame, $"{x}, {y}");

unsafe { frame.Reset(); }                    // SAFETY: line is not used after this
```

- **An `Arena` is a handle** (one pointer), so passing or storing it is free and copies refer to the same arena. `default(Arena)` is "not created": every allocation from it returns null, and so does everything built on it.
- **`Reset()` keeps the memory.** It merges the blocks into one, so a loop that resets each round stops calling `malloc` after the first.
- **`Save()` and `Restore(mark)`** free only what was allocated since the mark, for temporary work in an arena that also holds data to keep. Restoring a mark from another arena, or from before a `Reset()`, ends the program with a message.
- **One hazard with `Restore`:** if a `List`, `Dict` or `StrBuilder` created before the mark grows after it, its new buffer is freed by `Restore` while the collection still points at it, and nothing reports that. Between `Save()` and `Restore()`, only add to collections created after the mark. When temporary work has to add to long-lived collections, give it a separate scratch arena instead and copy the result out (`sb.ToArenaString(target)`).
- **Copies are explicit.** `var b = a` on a `List`, `Dict` or `StrBuilder` shares contents. `list.Clone(arena)` and `dict.Clone(arena)` make an independent copy, in the same arena or another, for example to keep a result past a scratch arena's lifetime. Elements are copied as they are, so a pointer inside one still points where it did.
- **Growing in place.** `List` and `StrBuilder` extend their buffer without copying while it's still the arena's newest allocation (`TryExtend`). Otherwise the old buffer stays behind until the arena is reset.
- **Not thread-safe.** Use one arena per thread.
- **No stale-pointer detection.** After `Reset()`, an old pointer still reads valid-looking memory. That's why the freeing operations are `unsafe`.

## Strings

zerocore's `string` has `Length`, an indexer, `==`, `!=`, `+`, `Equals`, `IsNullOrEmpty` and string `switch`, and nothing else. `lib/Str.cs`, `lib/StrBuilder.cs` and `lib/Out.cs` add the rest, without a garbage collector:

```csharp
foreach (ReadOnlySpan<char> field in line.Split(','))       // no allocation
    if (field.Trim().StartsWith("#")) ...

Out.Line($"{name,-8}|{qty,5}|{price,9:F2}");                // straight to stdout

string label = Str.Format(arena, $"item {id:D4}");          // a real string, in the arena
```

| .NET | Zero mode with `lib/` | Allocates |
|---|---|---|
| `a == b`, `a != b`, `switch (s)` | the same | no |
| `a + b` | the same: calloc, never freed. In a loop use `Str.Concat(arena, a, b)` | calloc |
| `a == b` on a span | `span.SequenceEqual(other)` | no |
| `s.Substring(i, n)` | `s.AsSpan(i, n)` | no |
| `s.StartsWith`, `EndsWith`, `IndexOf`, `LastIndexOf`, `Contains`, `Trim` | same names, on strings and spans; `Trim` returns a span | no |
| `s.Split(',')` | `s.Split(',')`, used in `foreach`, yields spans | no |
| `int.Parse`, `long.Parse`, `ulong.Parse`, `double.Parse` | `Str.TryParse(s, out int v)` (or `long`, `ulong`, `double`) | no |
| `long.Parse(s, NumberStyles.HexNumber)` | `Str.TryParseHex(s, out ulong v)` (`0x` prefix optional) | no |
| `string.CompareOrdinal(a, b)` | `Str.Compare(a, b)` | no |
| `Console.WriteLine($"...")` | `Out.Line($"...")` | no |
| `$"..."` as a string | `Str.Format(arena, $"...")` (with only string holes, plain `$"..."` works too, through `+`) | arena |
| `a + b` without leaking | `Str.Concat(arena, a, b)` | arena |
| `StringBuilder` | `StrBuilder.Create(arena)` | arena |

- **Every string converts to `ReadOnlySpan<char>` implicitly.** C# 14 adds that conversion when `System.MemoryExtensions.AsSpan(string)` exists, which `lib/Str.cs` defines. So any span parameter takes a string. A null string acts as an empty one.
- **Arena strings are real strings.** A zero-mode string object is a method-table pointer, a length and the characters, allocated with plain `calloc`. `Str.Format` and `Str.Concat` lay one out in arena memory, so the result works anywhere a string does. It's freed with the rest of its arena (`Restore`, `Reset`, `Destroy`), so don't use it afterwards. `Str.Format` and `Str.Concat` return null if the arena isn't created or is full.
- **Holes in `$"..."`** take integers, `double` and `float`, `bool`, `char`, strings and spans, with an optional alignment (`{x,8}`, `{x,-8}`) and format: `D`/`Dn` and `X`/`x`/`Xn` for integers, `F`/`Fn` for doubles. Without a format, a double prints up to 6 decimals with trailing zeros removed. Values of 1.8e19 and up print as `1.00e+20`. Unlike .NET, `bool` prints as `true`/`false`, and `{-1:X}` prints 16 hex digits for any integer type, since every integer is formatted as a 64-bit `long` or `ulong`.
- **Parsing is strict and invariant:** no surrounding whitespace (call `Trim()` first), no thousands separators. `TryParse(s, out double)` is correctly rounded: 5,000 decimal strings across the whole range, denormals included, came out bit-identical to Python's `float()`. It accepts `.5`, `5.`, exponents, `NaN` and `Infinity`; too large gives Infinity and too small gives 0, like .NET.
- **Not supported:** culture-aware anything, `char`/number operands in `+` (`"n=" + 5`; use `Str.Format(arena, $"n={5}")`), and `string.Format`.

## Known issues

With zerocore, as `build.sh` builds:

| Issue | Status |
|---|---|
| `[DllImport]` of a library not passed with `-i` fails to compile ("Expected type 'Internal.Runtime.CompilerHelpers.InteropHelpers' not found") | Pass `-i <library>`; `build.sh` passes `-i libc -i libm.so.6` |
| Building without `--no-reflection` fails ("Failed to load type 'Internal.Metadata.NativeFormat.TypeDefinition'") | Pass `--no-reflection`; `build.sh` does |
| Non-baseline instruction sets (AVX2, FMA, dotprod) report `IsSupported == false` by default: there's no run-time CPU detection | Build with `-m x86-64-v3` / `-m dotprod`; `build.sh` does this for Simd and Kernels |
| No `Console`, `Math`, `Interlocked`, `Volatile`, `Half` | `lib/Out.cs` for output, libm via P/Invoke for math; the others aren't available |
| `"n=" + 5` doesn't compile (no `object.ToString`) | `Str.Format(arena, $"n={5}")` |
| Casts and interface calls that need variance abort at run time | Not supported |
| No Windows port | `build.ps1` builds against zerolib instead |

With bflat's zerolib (`--stdlib:zero`), for reference; the fork fixes most of these, and none of them affect `build.sh`:

| Issue | Status |
|---|---|
| Upstream zerolib lacks `ThrowOverflowException`, so checked arithmetic and integer `/` and `%` on arm64 don't compile | Fixed in the fork |
| Upstream zerolib's `Console.WriteLine(int)` prints a stray NUL before every number and garbage for negatives | Fixed in the fork |
| On x64, `staticStructField.FixedBuffer[variableIndex] = x` is miscompiled into a store to a bogus absolute address (segfault). This is a .NET 10 RC1 JIT bug, fixed in .NET 10 GA | Fixed in the fork (GA JIT) |
| No `System.Runtime.Intrinsics` (SIMD), `Interlocked`, `Volatile`, `Half` or `BFloat16` | Added in the fork |
| On arm64, checking a non-baseline instruction set (`Dp.IsSupported`, ...) without `-m` fails to link (`undefined symbol: g_cpuFeatures`) | Fixed in the fork: such checks are `false` unless the set is given with `-m` |
| Non-baseline instruction sets (AVX2, FMA, dotprod) report `IsSupported == false` by default: zero mode has no run-time CPU detection | Build with `-m x86-64-v3` / `-m dotprod`; `build.sh` does this for Simd and Kernels |
| `Environment.TickCount64` fails to link in zero mode (`undefined symbol: minipal_hires_ticks`) | Call libc `clock_gettime` instead (see `samples/Simd.cs`) |
| `[DllImport("libm")]` aborts at runtime | Use `[DllImport("libm.so.6")]` |
| `Math` has no `Sqrt`/`Exp`/... | libm via P/Invoke |
| Assigning a static field of a reference type (`static string`, objects, arrays) segfaults: zerolib never allocates their storage | Fixed in the fork |
| Upstream zerolib's `string` has no `==` or `+`, so those and string `switch` fail with `CS0656` | Fixed in the fork |
| `string` has no `$"..."` with non-string holes, `Substring` or parsing (`CS0117`/`CS1061`) | Use `lib/Str.cs` (see [Strings](#strings)) |
| `new T[n]` fails to link (`undefined symbol: RhpNewArrayFast` / `RhpNewPtrArrayFast`) | Fixed in the fork |
| Arrays of reference types, class casts and top-level statements fail with "Expected type 'System.Runtime.TypeCast' not found" | Fixed in the fork (class casts; casts to interface or array types aren't supported) |
| A static constructor that builds a string crashes the compiler (`NullReferenceException` in `TypePreinit`) | Fixed in the fork |
| Objects from `new` aren't zeroed (malloc) | Fixed in the fork (calloc) |

## Resources

- [bflat website](https://flattened.net/)
- [bflat GitHub](https://github.com/bflattened/bflat), [the fork](https://github.com/OnurGumus/bflat)
- [What's new in C# 15](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-15)

## License

MIT, including `zerocore/` (see [`zerocore/LICENSE`](zerocore/LICENSE)). zerocore's README explains where its code comes from and carries a caveat: it wasn't written in a clean room, so have it reviewed before shipping a commercial product on it.

bflat and the fork are AGPL-3.0. `build.sh` uses them as a compiler; their zerolib is not linked into the binaries it produces. Each binary does link the C library of the system it targets (glibc on Linux, WASI's libc on WebAssembly), under its own license.
