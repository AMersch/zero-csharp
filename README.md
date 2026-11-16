# bflat Zero Mode Examples

![bflat debugging in VS Code](bflat.png)

Exploring [bflat](https://flattened.net/) `--stdlib:zero` mode: C# compiled to tiny native binaries without any runtime, written in **C# 15** under the **updated memory safety rules**.

**Zero mode is essentially "C with proper generics"**: C#'s syntax, type safety, and generics with C-like binary sizes (16-80 KB).

## Quick start

```bash
./setup.sh            # checks prerequisites (+ builds the container image on macOS)
./build.sh            # builds every sample into ./bin
./build.sh Arena      # or just one
./run.sh Arena        # runs it (in the container on macOS)
```

Prerequisites:

- **The bflat fork** (see below). By default `../bflat/layouts/linux-glibc-<arch>`; override with `BFLAT_DIR`.
- **macOS:** Docker. bflat can't target macOS, so it runs in a Linux container of your Mac's own architecture (native on Apple Silicon).
- **Linux:** `libtinfo.so.5`, which bflat's bundled linker needs. On Ubuntu 22.04: `sudo apt install libtinfo5`. Ubuntu 24.04 no longer ships it.

Windows: `.\setup.ps1` and `.\build.ps1` use the fork's `windows-x64` layout but are **untested**. The samples P/Invoke into `libc`, which may not resolve on Windows.

## Getting bflat

The samples need a bflat fork, [OnurGumus/bflat](https://github.com/OnurGumus/bflat) (branch `csharp15`), which differs from upstream bflat 10.0.0-rc.1 in two ways:

- **It compiles C# 15.** Roslyn is bumped to 5.12.0, the version shipped with the .NET 11 SDK. A new `--compiler-feature` option (the same as `csc -features:`) turns on the updated memory safety rules.
- **SIMD.** zerolib defines `Vector64/128/256<T>` (with `Create` and `As*`) and a subset of the hardware intrinsics: `AdvSimd` and `Dp` (SDOT) on arm64; `Sse`, `Avx`, `Avx2` and `Fma` on x64. That covers float arithmetic, FMA, min/max, rounding, conversions, divide and int8 dot products. The compiler treats them as real intrinsics.
- **Atomics and half floats.** `Interlocked` (compiled to `lock xadd`/`cmpxchg` and `ldaxr`/`stlxr`), `Volatile`, `Half`, `BFloat16` and the `BitConverter` bit conversions. Half/BFloat16 conversions are bit-for-bit identical to .NET 11's.
- **Instruction sets beyond the baseline come only from `-m`.** Zero mode has no run-time CPU detection, and upstream bflat failed to link on arm64 when code checked e.g. `Dp.IsSupported` without `-m`. Now such checks are simply `false` unless the instruction set is given with `-m`.
- **The .NET 10 GA JIT.** Upstream bflat 10.0.0-rc.1 ships the .NET 10 RC1 JIT, which miscompiles some static-field stores on x64. The fork's build swaps in Microsoft's GA JIT, which fixes them.
- **zerolib fixes.** `ThrowOverflowException` is added, so checked arithmetic and **integer division on arm64** compile. `Console.WriteLine(int)` is fixed.

The fork is AGPL-3.0, like upstream bflat's source. Binaries built with it contain its zerolib.

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

## Building

`lib/` is compiled once into an IL assembly, and each sample references it with `-r`:

```bash
F="--langversion preview --compiler-feature updated-memory-safety-rules"
bflat build-il lib/*.cs -O --stdlib:zero $F --target Shared -o bin/BflatLib.dll
bflat build samples/Arena.cs -r bin/BflatLib.dll --stdlib:zero $F -o bin/Arena
```

This is how you ship a bflat library without header files. The assembly's metadata describes the public API, so `-r` takes the place of `#include`. The assembly holds IL, not native code, so each sample's build compiles just the parts it uses into one static binary: generics are instantiated for the caller's types, calls are inlined across the library boundary, and code is generated for the caller's `-m` flags. One `BflatLib.dll` therefore gives NEON code on arm64 and AVX2 code on x64. Sizes match building from source to within about 1 KB.

- Pass `-O` to `build-il`. Unlike `bflat build`, it doesn't optimize unless you ask, and without `-O` the samples come out up to 1.4 KB bigger.
- The library and its consumers must use the same `--stdlib`, here `--stdlib:zero`.
- Don't call the assembly `ZeroLib`. Zero mode's core library is an assembly named `zerolib`, assembly names are case-insensitive, and the collision fails the build with "Reference to type 'Object' claims it is defined in 'ZeroLib'".

`build.sh` adds:

- `--arch` for the target (default: this machine's architecture; set `BFLAT_ARCH=x64` or `arm64` to cross-compile).
- `-Os --no-pie` plus `strip` for Tiny.
- For Simd and Kernels: `-m x86-64-v3` on x64 (AVX2/FMA; Haswell or newer) and `-m dotprod` on arm64 (SDOT; ARMv8.2+, e.g. Apple M1).
- On arm64, `--ldflags "-z max-page-size=4096"`. Without it, the default 64 KB segment alignment pads even Tiny to ~200 KB. 4 KB-page binaries don't load on 16 KB/64 KB-page kernels (Asahi Linux, some servers); set `ARM64_MAX_PAGE_SIZE=65536` for those.

## Editor support

The `.csproj` files and `BflatTest.slnx` exist only so VS Code (C# Dev Kit) and other IDEs give IntelliSense that matches zero mode. bflat ignores them, so keep building with `build.sh`.

- `Directory.Build.props` compiles every project against zerolib instead of the .NET standard library, with the same C# 15 settings `build.sh` uses. Completion only offers what zero mode has, and the editor flags the same memory-safety errors bflat does.
- zerolib is found the way `env.sh` finds bflat: `BFLAT_DIR` if it's set, otherwise a layout of the fork in `../bflat` (or `BFLAT_FORK`).
- `lib/BflatLib.csproj` covers `lib/*.cs`, and `samples/Samples.csproj` covers every sample. Each sample is a separate program with its own `Program` and `Main`, so each one has its own namespace (`ArenaSample`, `TinySample`, ...) to keep them apart. Avoid names like `Samples.Arena`: inside that namespace, `Arena` means the namespace, not the library's `Arena` class.
- `Samples.csproj` references the library project, so F12 opens the library's source.
- `dotnet build BflatTest.slnx` type-checks everything without running bflat. The editor uses the Roslyn from your .NET SDK and bflat uses the fork's, so a very new preview feature could pass one and fail the other.

## Updated memory safety rules

The samples compile with C# 15's [updated memory safety model](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/unsafe-code#the-updated-memory-safety-model-preview), which is in preview:

- **Pointers are safe to create, unsafe to use.** Pointer types, `&`, `stackalloc` and `fixed` work in safe code. Dereferencing (`*p`, `p->x`, `p[i]`) and calling function pointers need an `unsafe { }` block.
- **`unsafe` on a member is a contract.** Callers must be in an unsafe context too, and the body still needs inner `unsafe { }` blocks.
- **Every `extern` says `safe` or `unsafe`.** For example, `malloc` is `safe` and `free` is `unsafe`.

In `lib/Arena.cs`, allocating is safe: you get a pointer, and using it needs `unsafe` anyway. `Reset()` and `Destroy()` are marked `unsafe`, because calling them is what turns other pointers into dangling ones:

```csharp
Point* p = Arena.Alloc<Point>();         // safe
unsafe { p->X = 10; }                    // dereference: unsafe
unsafe { Arena.Reset(); }                // SAFETY: p is not used after this
```

## Examples

| Sample | Shows | Size |
|---|---|---|
| `samples/Tiny.cs` | Smallest binary (`-Os --no-pie`, stripped) | ~16 KB |
| `samples/Minimal.cs` | Hello world via `puts` | ~46 KB |
| `samples/Arena.cs` | Growable arena: Fibonacci growth, dedicated blocks for large allocations, per-allocation alignment, `Reset` that keeps capacity | ~61 KB |
| `samples/Collections.cs` | `List<T>` and `Dict<K,V>` on top of the arena | ~81 KB |
| `samples/VirtualTest.cs` | Polymorphism three ways: function-pointer vtables, static abstract interfaces, virtual methods | ~52 KB |
| `samples/Simd.cs` | Float dot product with NEON (arm64) or AVX + FMA (x64), checked against scalar and timed (12x faster on Apple Silicon) | ~59 KB |
| `samples/Kernels.cs` | Tests and times `lib/Kernels.cs`: exp within 1 ulp of libm, softmax, SiLU, RMSNorm, matrix-vector product, exact int8 dot product (softmax 7x faster than scalar libm on Apple Silicon) | ~73 KB |

Sizes are for linux-arm64 with 4 KB pages and include debug info (only Tiny is stripped); x64 is within a few KB. Run `strip` to shrink the others.

### `lib/`

| File | What |
|---|---|
| `Arena.cs` | Growable bump allocator. Normal blocks grow 128, 128, 256, 384, 640, ...; an allocation too big for the next block gets its own block without changing that sequence. Alignment is per allocation, and a failed allocation returns null and leaves the arena untouched. |
| `List.cs` | Growable array. It's a handle to an arena header, so copies share contents. `default(List<T>)` is safe to use: reads see an empty list and `Add` returns false. |
| `Dict.cs` | Open-addressing hash map (FNV-1a, power-of-two capacity). Keys are compared by raw bytes: fine for integers and padding-free structs, not for floats. |
| `Out.cs` | Buffered stdout writer. zerolib's `Console` makes one syscall per character. |
| `Kernels.cs` | SIMD math for LLM inference: `Dot`, `MatVec`, `DotI8`, `Exp`, `Softmax`, `Silu`, `RmsNorm`. Each has NEON, AVX2+FMA and scalar paths and handles any length. |

## What zero mode supports

Zero mode has no garbage collector, no exceptions (failures call `Environment.FailFast`), no async/await, no LINQ, and almost no base class library. On the other hand, these all work:

- `Span<T>`
- Generics, including constrained calls and **static abstract interface members**. These are resolved at compile time, which suits per-format compute kernels.
- Classes with virtual methods and interface dispatch. Every `new` calls malloc and is never freed.
- Function pointers, `[UnmanagedCallersOnly]` callbacks, and P/Invoke. pthreads work, for example.

## Known issues

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

## Resources

- [bflat website](https://flattened.net/)
- [bflat GitHub](https://github.com/bflattened/bflat), [the fork](https://github.com/OnurGumus/bflat)
- [What's new in C# 15](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-15)

## License

MIT
