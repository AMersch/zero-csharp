# Native libraries from zero-mode C#: design

Status: draft, 2026-11-22. Follows SDK 0.3.0 (macOS target and host).

## Intent

Today a zero-mode project builds a native executable, or a zero library: IL that
other zero projects compile in. This adds a third output: a native shared
library (`.so`, `.dylib`, `.dll`) whose `[UnmanagedCallersOnly(EntryPoint = "...")]`
methods other programs call.

The callers that matter are **.NET programs through P/Invoke** and **C programs**.
The point is to write a small, fast, dependency-free native component in C#,
one that has no .NET runtime inside it and starts instantly, and to use it from an
ordinary .NET application or from C, the way one would use a library written in C.

Success: a `dotnet build` of a project with one extra property produces a
library that a .NET console app loads with `[DllImport]`, and a C program loads
with `dlopen`/`LoadLibrary` or links against. This works on all four native targets
(linux-x64, linux-arm64, osx-arm64, win-x64), from every build host that can
build that target today.

## Goals

- `<NativeLib>Shared</NativeLib>` in a zero project builds a native shared
  library for the project's target (`ZeroTarget`), with the same targets and hosts
  as executables.
- Only the methods marked `[UnmanagedCallersOnly(EntryPoint = "name")]` are exported.
  zerocore's and lib/'s symbols stay private to the library.
- The library sets up its static fields when it loads, so static constructors,
  run-time-assigned statics and `new` behave as in an executable.
- Two zero libraries (or a zero library and a zero executable) in one process
  don't interfere: each has its own zerocore.
- Static constructors are thread-safe: several host threads may make their
  first calls at once. (The user's decision, 2026-11-22. It also fixes the same
  race in zero executables that use threads.)
- A how-to page shows both callers, P/Invoke from a .NET app and C, plus the
  limits that carry over.

## Non-goals

- **DOS and WebAssembly.** DOS has no shared libraries, and a wasm module works
  differently. Building a library for either target is an error that says so.
- **A generated C header.** C callers write the prototypes themselves; the docs
  show how the C# types map to C ones. This could come later. It's a separate feature
  because it needs a type mapping (structs, pointers, `bool`).
- **Packing native libraries into NuGet packages.** A project with `NativeLib`
  isn't packable as a zero library. Shipping the native file is the
  user's business, as with executables.
- **`[ModuleInitializer]`.** It doesn't run in zero-mode executables either
  (zerocore has no `RunModuleInitializers`). That's a separate, existing gap.
- **Unloading.** See Limits.
- **A zero executable linking a zero native library at build time.** Zero
  executables bind `[DllImport]` at link time, through `-i`, so this would need
  its own SDK plumbing (import library, rpath, ProjectReference). A zero
  executable can still `dlopen` one through libc. A zero project's
  ProjectReference to a `NativeLib` project is an error, rather than quietly
  compiling the library's IL in. (My call.)
- **Exports from referenced zero libraries.** As in NativeAOT by default, only
  the project's own `[UnmanagedCallersOnly(EntryPoint)]` methods are exported.
  Ones in referenced zero libraries are compiled in but not exported. The how-to
  page says so. (My call; opting other assemblies in can come later.)
- **A `dotnet new` template.** It's easy to add later, but not needed to prove the
  feature.

## Anti-goals

- A library that exports zerocore's runtime symbols (`RhpNewFast`, ...). Two
  zero libraries in one process would then bind to each other's copies.
- A library that silently skips its static setup and crashes on the
  first call that touches a static. The spike showed that this is what happens
  without the startup hook.
- Changing what `OutputType=Library` means today (a packable zero IL library).

## What the spike established (linux-arm64, 2026-11-22)

- `bflat build --target Shared` in zero mode (`--stdlib:none`, zerocore.o)
  already links a working `.so`. It exports exactly the `UnmanagedCallersOnly`
  entry points through a version script. Calls with numbers and pointers work.
  `RhpReversePInvoke` is a no-op, so any thread can call in.
- Without startup code, the first call that needs a GC static base (a static
  assigned at run time, or any static of a type with a non-preinitializable
  static constructor) crashes with SIGSEGV.
- Two small additions fix that:
  - zerocore exports `ZeroInitializeLibrary`, which runs the same module walk
    (`InitializeCommandLineArgs(0, null)`) that an executable's generated
    `main` runs;
  - a C constructor (`__attribute__((constructor))`) calls it.
- With those two additions, these all work:
  - static constructors, including ones that P/Invoke libc;
  - run-time statics that keep their values across calls;
  - `new` objects that persist across calls;
  - two copies of the library in one process with separate state.
- bflat's generated `__managed__Startup` runs only "library initializers", and
  zero mode has none, so it need not be called.
- lld exports `__start___modules`/`__stop___modules` from the `.so`. They're
  protected, so this was harmless in the spike, but they should be hidden.
- Spike sources:
  `/private/tmp/claude-501/-Users-onurgumus-Repos-bflat-test/d799d9d5-a70b-4122-afff-bd9c2c3035d5/scratchpad/shared`
  (throwaway).

## Approaches considered

1. **The SDK drives bflat's existing `--target Shared`, and a small
   per-OS startup object calls a zerocore export (chosen).** It reuses bflat's
   export collection, version script and `/def` file. The per-OS work stays in the
   payload objects we already ship (win/runtime.c, mac/runtime.c, and a new one
   for Linux).
2. **Run the static setup lazily from zerocore, on the first reverse P/Invoke,
   in `RhpReversePInvoke`.** It needs no constructor object, but it puts a check
   on every call into the library, adds a race when two threads make the first
   call at once, and does nothing for code that the library's own threads reach
   first. Rejected.
3. **Link the library ourselves on every OS, the way we link on macOS.**
   That gives full control, but it duplicates what bflat already does correctly
   on Linux and Windows. Rejected, except on macOS, where we already link.

## Design

### Project surface

```xml
<Project Sdk="ZeroCSharp.Sdk/0.4.0">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <NativeLib>Shared</NativeLib>
  </PropertyGroup>
</Project>
```

- `NativeLib` is NativeAOT's own property name, so .NET users already know it.
  Only `Shared` is accepted:
  - Any other value is an error.
  - `NativeLib` with `OutputType=Exe` is an error.
  - `NativeLib` with `ZeroTarget` dos or wasm is an error.
- A project with `NativeLib=Shared` gets `IsPackable=false` and no
  `ZeroPackDll`. Otherwise it goes through the native step as executables do:
  `ZeroNativeCommand`, incremental inputs and outputs, clean, publish.
- **Output:** `$(OutDir)$(ZeroTargetFolder)native/$(AssemblyName)` plus `.so`, `.dylib`
  or `.dll`, with no `lib` prefix.
  - That's NativeAOT's layout: a `native/` folder keeps the native `.dll` apart
    from the IL `.dll` of the same name on Windows.
  - .NET's `[DllImport("Name")]` probes both `Name.so` and `libName.so`.
- `dotnet publish` copies the library to the publish folder's root, as it does
  the executable.
- `dotnet run` on such a project is an error that says it's a library.

### zerocore

- Add `ZeroInitializeLibrary` to `StartupCodeHelpers` (zerocore/Runtime/Startup.cs):
  `[RuntimeExport("ZeroInitializeLibrary")]`, calling `InitializeCommandLineArgs(0, null)`.
  Leave it out under `WASM`.
- It's additive. Existing zero IL libraries still compile against it, so
  **`ZeroFramework` stays `zero03`**.

### Thread-safe static constructors (zerocore)

- **Today:** `ClassConstructorRunner.CheckStaticClassConstruction` clears
  `cctorMethodAddress` and then runs the constructor. A second thread arriving
  meanwhile sees 0 and reads statics that aren't set up yet.
- **New behaviour:**
  - The first thread claims the context atomically (compare-and-swap), runs the
    constructor, and then marks it done.
  - Other threads wait until it's done. Spinning with a yield is enough: static
    constructors are short.
  - The owning thread re-entering, when a constructor touches its own type's
    statics, proceeds without waiting, as today.
  - Two constructors that wait on each other from two threads (A's needs B,
    B's needs A) must not deadlock silently. Detect it and FailFast with a
    message, or let the re-entering thread proceed as .NET does. The builder
    chooses, and documents which.
- **Constraints:**
  - The context layout (`cctorMethodAddress`) is the compiler's. The state
    must fit there, or in data zerocore owns that's keyed by the context.
  - It must work on every target, including the single-threaded ones (DOS,
    wasm), where the atomics may compile to plain stores.
  - The fast path, a constructor that has already run, stays one load and
    compare. That keeps the cost off every static access.
- **Tests:**
  - Many threads call an export whose type has a slow static constructor, all
    at once. Every call sees initialized data, and the constructor ran exactly
    once.
  - The same through a zero executable using lib/'s threads.
  - The constructor that touches its own type still works.

### Per-OS startup objects (payload)

Each one calls `ZeroInitializeLibrary` once, at load, before any export can run.

- **Linux (x64, arm64):** a new object, linked only into libraries. It has one
  `.init_array` constructor that calls `ZeroInitializeLibrary`. It's built for
  both architectures alongside `zerocore.o`.
  - C or assembly is the builder's call. Whatever the payload build already
    has a compiler or assembler for.
- **macOS:** a new `mac/library.c` whose constructor does what `main` in
  mac/runtime.c does before calling `__managed__Main`:
  - copy `section$start/$end$__DATA$__modules` into the
    `__start___modules` table;
  - abort with the same message on overflow;
  - then call `ZeroInitializeLibrary`.
  - Factor the copy into a shared function or header, so the two can't drift.
  - Libraries link library.o instead of runtime.o's `main`. `get_nprocs` and
    `clock_gettime` are still needed, so they move to a part both can link, or
    runtime.c is split. Builder's call.
- **Windows:** add `zero_dll_start(void *instance, u32 reason, void *reserved)` to
  win/runtime.c.
  - On `DLL_PROCESS_ATTACH` it calls `_set_abort_behavior` as `zero_start`
    does, then `ZeroInitializeLibrary`. It returns TRUE.
  - The library links with `/entry:zero_dll_start` instead of `/entry:zero_start`.
  - It must not call `SetConsoleOutputCP`. That's the host process's choice.
  - Loader lock: the static setup only allocates (`HeapAlloc` through calloc).
    Static constructors run later, on first use, so it's safe under the lock.

### bflat arguments and linking

- **Linux and Windows:** add `--target Shared`, link the startup object with
  `--ldflags`, and write the output path into `native/`.
  - On Linux, also pass `--ldflags "-z start-stop-visibility=hidden"`, so
    `__start___modules`/`__stop___modules` aren't exported. The test checks the
    export list.
- **macOS:** bflat compiles with `-c`, so it doesn't link; the SDK links with
  `ld64.lld` as it does for executables, adding:
  - `-dylib -install_name @rpath/<Name>.dylib`;
  - the payload's library.o in place of runtime.o's `main`;
  - `-exported_symbols_list <file>` with the `UnmanagedCallersOnly` entry points
    (Mach-O names, leading underscore).
  - **Where the list comes from:** bflat writes an exports file for
    `--target Shared`. With `-c` it may not write or keep it, so the builder
    checks this first.
    - If it doesn't, change the fork so it does, writing the ld64 format
      (`_name` per line) for `--os macos`.
    - That's a fork commit and a new osx-arm64/linux/windows layout build, as in
      0.2/0.3.
    - The builder check found that with `-c` bflat writes `<output>.txt` and
      returns before the code that deletes it. If the fork's ILCompiler
      writes the ld64 format for Apple targets, as upstream does, no fork
      change is needed.
    - Don't scrape `nm`: it can't tell an export from zerocore's own global
      symbols.

### Testing (sdk/test/run.sh, in every CI job that runs native tests)

- **Fixture `sdk/test/fixtures/nativelib`:** a zero library whose exports cover:
  - numbers (int, long, double);
  - a pointer plus a length;
  - a blittable struct by value and by pointer;
  - a static assigned at run time that persists across calls;
  - a static constructor that can't be computed at build time (it calls libc);
  - `new` that persists across calls.
- **`native_library_dotnet`:** a plain .NET console app (`net10.0`, the
  Microsoft SDK, not ours) P/Invokes the built library and checks every result.
  This runs on every native-test host for its own target. It's the main test.
- **`native_library_c`:** a C program loads the library and checks the same
  results, using `dlopen`/`dlsym` on Linux and macOS, and `LoadLibrary` on Windows.
  - It runs where a C compiler is available. It must run in CI on the ubuntu x64
    job, the macOS job and the Windows job. Containers without `cc` skip it, and
    the skip is printed.
  - A second C test links the library at build time (`-l`/import library,
    `rpath`), on Linux and macOS at least.
- **`native_library_exports`:** the export list (`nm -D`, `nm -gU` and
  `dumpbin`/`llvm-objdump` on Windows) is exactly the entry points. No
  `Rhp*`, no `__start___modules`.
- **`native_library_two`:** two copies of the library loaded in one process keep
  separate state.
- **`native_library_cross`:** every host builds all four targets' libraries.
  Check the file format (ELF shared object, Mach-O dylib, PE DLL), as
  mac_host_builds_all_targets does for executables.
- **Built elsewhere, run on the target (my call, following the executables'
  CI):**
  - The jobs that today upload executables built on another host and run them
    on the target (`mac-programs`, `macos-built`, `windows-built-linux` and the
    Linux ones) do the same for the library: build the fixture library on every
    host, upload it, and on the target run the .NET P/Invoke caller against it.
  - A format check alone would pass a `.dylib` whose constructor never runs, or
    whose export list is empty.
- **`native_library_with_zero_exe`:** a zero executable `dlopen`s the library
  through libc and checks that both keep separate state.
- **Errors:** `NativeLib` with Exe, with dos/wasm, an unknown value, and a zero
  project's ProjectReference to a `NativeLib` project.

### Docs

- **New how-to, `docs/content/how-to/build-a-native-library.md`:**
  - the project file;
  - exports with `EntryPoint`, and why a method without one isn't exported;
  - the type mapping table for C (C# int to `int32_t`, and so on);
  - a .NET P/Invoke example and a C example;
  - the limits below.
  - Its code excerpts come from a sample, checked like the other pages
    (sample regions).
- **Update:** concepts/limits.md, install-with-dotnet.md (the output table), and
  the README feature list.

## Limits that carry over (documented, not fixed)

- **A panic calls `abort`:** it ends the host process, not just the call.
- **Memory from `new` is never freed:** a long-running host should do each
  call's work inside an arena.
- **Only C-compatible types cross the boundary:** numbers, pointers and
  blittable structs. No strings, arrays or objects, and each library has its
  own zerocore, so objects can't be shared between them.
- **Thread safety stops at static constructors:** the library's own static
  fields and arenas are as thread-safe as the code that uses them, as in an
  executable.
- **No unloading:** `dlclose`/`FreeLibrary` doesn't release memory the library
  allocated.

## Release

- **SDK 0.4.0.** It's a feature, so a minor bump. `ZeroFramework` stays `zero03`.
  - The 0.3 deferred minors can ride along if they're ready, but don't block on
    them.
- **Release order** is as before: the fork change (if macOS needs it) → new layouts →
  zero-csharp CI green → smoke → tag. Merges, tags and publishing happen only with
  the user's approval.

## Left to the builder

- Whether the Linux startup object is C or assembly.
- How to split mac/runtime.c between executables and libraries.
- The exact error message wording, following the existing style.
- The fixture's export names and the sample used by the how-to page.
