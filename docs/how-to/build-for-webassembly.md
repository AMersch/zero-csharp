---
title: Build for WebAssembly
index: 5
---

# Build for WebAssembly

`./build.sh --wasm` compiles the same C#, `lib/` and zerocore to WebAssembly modules for WASI. The
source doesn't change: the samples that run natively run under wasmtime and print the same output,
apart from what depends on the platform. This guide covers the setup, the build, running the modules,
and what differs. [Run it on WebAssembly](../get-started/webassembly.html) is the short version.

## Get the toolchain

bflat can't target WebAssembly, so the last steps use other tools. Download them once:

```bash
./setup.sh --wasm
```

This puts three tools into `tools/wasm`, pinned in `env.sh`:

| Tool | Version | Job |
|---|---|---|
| NativeAOT-LLVM | 10.0.0-rc.1.26357.1 | compiles IL to LLVM bitcode |
| WASI SDK | 34.0 | its clang compiles the bitcode and links the module |
| wasmtime | 50.0.1 | runs the module |

They take about 900 MB unpacked, mostly the WASI SDK. They are Linux builds; on macOS they run in the
build container, like bflat. To keep them elsewhere, set `WASM_TOOLS`; on macOS it has to be a
directory inside the repository, or the container can't see it.

## Build

```bash
./build.sh --wasm          # every sample, into bin/wasm/*.wasm
./build.sh --wasm Arena    # just one
```

Each module goes through three compilers:

1. **bflat** compiles the sample to IL against `bin/BflatLib.dll`, exactly as for the native build.
2. **NativeAOT-LLVM** compiles the IL to LLVM bitcode against zerocore's WebAssembly build (zerocore
   compiled with `-d WASM`). `[DllImport("libc")]` and `[DllImport("libm.so.6")]` are bound at link
   time to WASI's C library.
3. **The WASI SDK's clang** compiles the bitcode and links it with `wasm/runtime.c` and WASI's C
   library. `runtime.c` is zerocore's native half on WebAssembly: the entry point, the shadow stack and
   the transitions NativeAOT-LLVM's code calls. Allocation, casts and interface calls are zerocore's
   C#, the same code as on Linux.

The modules are stripped. They range from about 16 KB (Tiny) to about 80 KB (Collections and Strings); `build.sh` prints
each module's size when it finishes.

`build.sh` builds the programs listed in its `ALL_SAMPLES`. To build a new file in `samples/`, add its
name there.

## Run

```bash
./run.sh --wasm Arena
./run.sh --wasm Quiz < answers.txt
```

`run.sh --wasm` runs `bin/wasm/<Sample>.wasm` with the wasmtime from `tools/wasm`, passing any further
arguments to the program. Standard input reaches the program, so `In.ReadLine` works as it does
natively. The exit code is the program's.

To check every sample against the native output:

```bash
./test.sh --wasm
```

## What's different

- **32-bit pointers.** Structs that hold pointers are smaller, so code that prints sizes prints smaller
  numbers: the arena byte counts in Arena and Collections differ, and `test.sh` checks only their exit
  codes on WebAssembly.
- **No SIMD.** Instruction sets report `IsSupported == false`, so Simd and Kernels take their scalar
  paths. WebAssembly has 128-bit SIMD, but `lib/Kernels.cs` has no path for it yet.
- **One thread.** zerocore's WebAssembly build is its single-threaded 32-bit build.
- **Clocks.** Simd and Kernels call `clock_gettime(1, ...)`, Linux's numeric `CLOCK_MONOTONIC`. In
  WASI's C library a clock ID is a pointer, so this reads the clock ID from address 1, which is zero:
  the realtime clock. That still measures durations.
- **Exit codes.** Modules are linked for WASI 0.1 (`wasm32-wasip1`), which passes exit codes through.
  WASI 0.2 only reports success or failure, and the samples report failed checks in their exit code.
- **Stacks.** The link sets a 1 MB stack, and `wasm/runtime.c` reserves a 1 MB shadow stack, where
  managed code keeps the locals WebAssembly's own stack can't address.
- **Not supported:** callbacks from native code into C# (`[UnmanagedCallersOnly]` reverse P/Invoke
  inside a running program), and `build.ps1` on Windows.

## Running elsewhere

A module is linked for WASI 0.1 against WASI's C library, so it needs a runtime with WASI 0.1. The
repository runs modules only with wasmtime 50.0.1 (in `run.sh` and `test.sh`), so that is the one
that is tested. On Linux you can call it without `run.sh`; `<arch>` is `x86_64` or `aarch64`:

```bash
tools/wasm/wasmtime-v50.0.1-<arch>-linux/wasmtime run bin/wasm/Arena.wasm
```

## Watch out for

- **Treat the versions as a set.** `wasm/runtime.c` and zerocore implement NativeAOT-LLVM's internal
  runtime functions (`RhpNewArray`, `RhpResolveInterfaceDispatch` and others), whose names and
  signatures were read from the compiler's output, not from documentation. A changed signature fails
  the link, because `build.sh` turns `wasm-ld`'s signature warnings into errors. A changed meaning
  only shows when a program runs. After changing any version in `env.sh`, rerun the samples and
  compare their output with the native build.
- **It still needs bflat.** `build.sh --wasm` compiles zerocore, `lib/` and the sample to IL with the
  bflat fork first, so the fork has to be installed as for the native build.

See also: [How the targets work](../concepts/targets.html), [Build for DOS](build-for-dos.html),
which starts from the same bitcode, and [Limits](../concepts/limits.html).
