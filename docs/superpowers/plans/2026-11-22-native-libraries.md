# Native Libraries Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `<NativeLib>Shared</NativeLib>` in a zero project builds a native shared library (`.so`/`.dylib`/`.dll`) that .NET P/Invoke and C can call. Static constructors become thread-safe.

**Architecture:**
- zerocore gains thread-safe static constructors and a `ZeroInitializeLibrary` export.
- The payload gains one library startup object per OS:
  - a Linux `.init_array` entry;
  - a macOS constructor;
  - a Windows DLL entry point.
- The SDK drives bflat's `--target Shared` (Linux, Windows), or links with `ld64.lld -dylib` itself (macOS).
- A plain .NET console app (the caller fixture) checks every library, on its build host and, in CI, on its target OS.

**Tech Stack:**
- MSBuild (Sdk.targets/Sdk.props) and C# zerocore;
- C and GNU assembler for the payload objects;
- bash tests (sdk/test/run.sh) and GitHub Actions (sdk.yml).

**Spec:** docs/superpowers/specs/2026-11-22-native-libraries-design.md

## Global Constraints

- **Project surface:** `<OutputType>Library</OutputType>` + `<NativeLib>Shared</NativeLib>`. Other `NativeLib` values, `NativeLib` with `OutputType=Exe`, and `NativeLib` with `ZeroTarget` dos or wasm are build errors.
- **Output:** `$(OutDir)$(ZeroTargetFolder)native/$(AssemblyName)` + `.so` (Linux), `.dylib` (macOS) or `.dll` (Windows). No `lib` prefix.
- **Exports:** only the project's own `[UnmanagedCallersOnly(EntryPoint = "...")]` methods. No `Rhp*`, no `ZeroInitializeLibrary`, no `__start___modules`/`__stop___modules`.
- **Framework:** `ZeroFramework` stays `zero03`. `ZeroSdkVersion` becomes `0.4.0` (Task 9).
- **NuGet:** a `NativeLib` project gets `IsPackable=false` and never runs `ZeroPackDll`.
- **ProjectReference:** a zero project's ProjectReference to a `NativeLib` project is an error.
- **Out of scope:** DOS/wasm libraries, C header generation, `[ModuleInitializer]`, unloading, and build-time linking of a zero exe against a zero library.
- **Approvals:** merging to master, tagging and publishing happen only with the user's approval. Feature-branch pushes and PRs are fine.
- **Never commit `docs/llama-plan.md`.** Stage files by name.
- **Local Mac verification:**
  - Use the known recipe in memory dotnet-sdk-packages.md: pack with `ZERO_LAYOUTS=/nonexistent ZERO_PAYLOAD_PREBUILT=1`, or the trimmed layouts get wiped.
  - Docker may hang; restarting it is allowed.
  - CI (`sdk.yml` on the pushed branch) is the authority for Windows, cross-built and container results.

## Review Focus

1. **Paths with spaces:** a library project in a path with spaces must build on every target. The exports file, `-soname` and `-install_name` all carry the path or name. Pinned by `native_library_path_with_spaces` (Task 3, extended in Tasks 4 and 5).
2. **Incremental builds:** a no-change rebuild must not rerun bflat, and editing an export must rebuild the library. Pinned by `native_library_incremental` (Task 3).
3. **A library that references a zero library:** a library using lib/ (BflatLib) and a ProjectReference'd zero library must link both in. Pinned by `native_library_with_zero_reference` (Task 3).
4. **The first call from a thread other than the one that loaded the library:** the static setup must already be done. Pinned by the caller's `threads` line (Task 3).
5. **`AssemblyName` with dots** (`My.Native`): the output is `My.Native.so`, and `DllImport("My.Native")` finds it. Pinned by `native_library_dotted_name` (Task 3).

---

## The shared test pieces (made in Task 3, used by Tasks 4–7)

**Library fixture `sdk/test/fixtures/nativelib/`:** `nativelib.csproj` (Library + NativeLib=Shared) and `Exports.cs` with these exports.

| Export | C signature | What it returns |
|---|---|---|
| `nl_add` | `int32_t (int32_t a, int32_t b)` | `a + b` |
| `nl_mul_long` | `int64_t (int64_t a, int64_t b)` | `a * b` |
| `nl_scale` | `double (double x, double f)` | `x * f` |
| `nl_sum` | `int64_t (const int32_t* v, int32_t n)` | the sum of `v` |
| `nl_offset` | `Point (Point p, int32_t d)` with `Point {int32_t X, Y}` | `{X+d, Y+d}` |
| `nl_negate` | `void (Point* p)` | negates it in place |
| `nl_remember` | `int32_t (int32_t v)` | appends `v` to a static `int[]` assigned at run time; returns the new length |
| `nl_cctor_value` | `int32_t (void)` | a static set by a static constructor that calls libc `abs(-41)`, plus 1, so `42` |
| `nl_slow_value` | `int32_t (void)` | `SlowInit.Value` (see below) |
| `nl_slow_runs` | `int32_t (void)` | how many times `SlowInit`'s static constructor ran |

`SlowInit`'s static constructor:
- calls `abs` (so it can't be computed at build time);
- busy-loops about 50 ms;
- increments a counter with `Interlocked`;
- sets `Value = 7`.

**Caller fixture `sdk/test/fixtures/nativelib-caller/`:** a plain `Microsoft.NET.Sdk` `net10.0` console app.
- It finds the library through `NativeLibrary.SetDllImportResolver`, using the `ZERO_NATIVE_LIB` environment variable.
- `dotnet run` prints exactly (`expected.txt` beside it):

```
add 5
mul 6000000000
scale 7.5
sum 10
offset 11,22
negate -3,-4
remember 3
cctor 42
threads 8 7 1
hidden ok
```

- **Calls behind each line:**
  - `add(2,3)`, `mul_long(60000,100000)`, `scale(2.5,3.0)`, `sum({1,2,3,4})`;
  - `offset({1,12},10)` and `negate` on `{3,4}`;
  - `remember(5)`, `remember(6)`, `remember(7)`;
  - `threads`: 8 threads, released together by a barrier, each call `nl_slow_value` first thing. The line is "threads <count> <the value each saw, which must be the same for all> <nl_slow_runs()>". If the values differ it prints them all, so the line differs.
  - `hidden ok`: `NativeLibrary.TryGetExport` fails for `RhpNewFast`, `ZeroInitializeLibrary`, `__start___modules`, `___start___modules` and `RhpReversePInvoke`. Otherwise it prints `hidden <names found>`.
- **Mode `two <a> <b>`:** loads two copies, calls `nl_remember` 3 times on a and once on b, and prints `two 3 1`.

**`sdk/test/call-native-lib.sh <library> [two <copy-a> <copy-b>]`:**
- runs the caller with `dotnet run --project`;
- compares the output with `expected.txt` (or `two 3 1`), after `tr -d '\r'`;
- exits non-zero and prints a diff on a mismatch.
- run.sh tests and CI jobs both use it.

---

### Task 1: Thread-safe static constructors (zerocore)

**Files:**
- Modify: `zerocore/Runtime/Startup.cs` (`ClassConstructorRunner`, lines ~276–310)
- Modify: `zerocore/Runtime/ObjectAllocator.cs` (per-thread record and identity)
- Modify: `zerocore/README.md` (the "Static constructors are not" sentence)
- Create: `sdk/test/fixtures/cctor-threads/` (`cctor-threads.csproj` Exe, `Program.cs`)
- Test: `sdk/test/run.sh`, `test_static_constructor_threads`, added to `ALL_TESTS`

**Interfaces:**
- Produces: `internal static void* ObjectAllocator.CurrentThreadRecord()`, the calling thread's record. It's calloc'd and stored in the pthread key on first use, and doesn't count toward `s_threadsWithSetting`. Its address is the thread's identity. `Set` uses the same record.
- Produces: `ClassConstructorRunner.CheckStaticClassConstruction(ref StaticClassConstructionContext)` with the behaviour below. Its signature is unchanged (the compiler calls it).

- [ ] **Step 1: Write the fixture and the failing test.**
  - **`cctor-threads/Program.cs`:**
    - `Parallel.For(0, 8, ...)` (lib/Threads.cs), each iteration reading `SlowInit.Value` into a results array;
    - `SlowInit` as in the shared pieces: `abs`, about 50 ms busy loop, `Interlocked.Increment(ref Runs)`, `Value = 7`;
    - a second type `Self` whose static constructor reads its own static field (re-entry);
    - a pair `A`/`B` whose static constructors read each other's statics.
    - It prints `values 7 7 7 7 7 7 7 7`, `runs 1`, `self ok`, `cycle ok`, one per line.
  - **`test_static_constructor_threads`:**
    - `can_build`;
    - `dotnet build`, then run the program 20 times;
    - every run's output equals those four lines, or `fail "$name" "run $i: <output>"`.

- [ ] **Step 2: Run it and watch it fail.**
  - Run: `sdk/test/run.sh static_constructor_threads` (Linux, or a Mac with the pack recipe).
  - Expected: FAIL. Some run shows a `values` line with a 0, or a crash. If 20 runs never fail, raise the busy loop or the thread count until the old code fails at least once in 20. A test that can't fail proves nothing.

- [ ] **Step 3: Implement.**
  - **Fast path:** `context.cctorMethodAddress == 0` means done; return. This stays one load and compare.
  - **Slow path,** under a zerocore spin lock (`Interlocked.CompareExchange` on a static int):
    1. Re-check for 0.
    2. Look up the context in an in-progress list (calloc'd nodes `{context*, owner record*, next}`).
    3. If there's no node: add one owned by me, unlock, run the constructor, then lock, `Volatile.Write(ref context.cctorMethodAddress, 0)`, remove the node and unlock.
    4. If the owner is me: return (re-entry, as today).
    5. If the owner is another thread: record "waiting for <context>" in my thread record. If the owner's wait chain leads back to me, clear it and return without waiting (a cycle: proceed as .NET does). Otherwise unlock, spin and retry.
  - The waiting is a plain spin, like `ObjectAllocator.CreateKey`. zerocore has no yield.
  - Update the README sentence: static constructors are now safe to reach from several threads.

- [ ] **Step 4: Run it and watch it pass.**
  - Run: `sdk/test/run.sh static_constructor_threads threads tutorial_steps`
  - Expected: all `ok`.

- [ ] **Step 5: Run the repo's own build of the samples.**
  - Run: `./test.sh` (Linux samples).
  - Expected: it passes as on master. DOS and wasm use the same code with no second thread.

- [ ] **Step 6: Commit.**
  - Files: zerocore/Runtime/Startup.cs, zerocore/Runtime/ObjectAllocator.cs, zerocore/README.md, the fixture and run.sh.
  - Message: `[zerocore] Thread-safe static constructors`

### Task 2: ZeroInitializeLibrary and the startup objects (payload)

**Files:**
- Modify: `zerocore/Runtime/Startup.cs`: `[RuntimeExport("ZeroInitializeLibrary")] private static void InitializeLibrary()`, which calls `InitializeCommandLineArgs(0, null)`, under `#if !WASM`.
- Create: `zerocore/native/library-x64.S` and `zerocore/native/library-arm64.S`.
  - Each has one `.init_array` entry (`.quad`/`.xword zero_library_init`).
  - `zero_library_init` tail-calls `ZeroInitializeLibrary`.
  - Each ends with a `.note.GNU-stack` section.
- **macOS:** split `mac/runtime.c` into:
  - `mac/start.c`: `main`, as today;
  - `mac/runtime.c`: `get_nprocs`, `clock_gettime`;
  - `mac/library.c`: an `__attribute__((constructor))` that copies the module list, then calls `ZeroInitializeLibrary`;
  - `mac/modules.h`: `static inline void zero_copy_modules(void)`, the copy and overflow abort, used by start.c and library.c.
- **Windows:** split `win/runtime.c`:
  - `zero_start` moves to `win/start.c`;
  - new `win/library.c` with `int zero_dll_start(void *instance, u32 reason, void *reserved)`. On `reason == 1` (DLL_PROCESS_ATTACH) it calls `_set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT)`, then `ZeroInitializeLibrary`. It returns 1, and never calls `SetConsoleOutputCP`.
  - The shims stay in runtime.c.
- **Modify: `build.sh`:**
  - `build_sdk_payload` assembles `native/linux-{x64,arm64}/library.o`.
  - The win-x64 payload gains `start.obj` and `library.obj`; osx-arm64 gains `start.o` and `library.o`.
  - `build_win` and `build_mac_link` link start + runtime for the samples.
- Modify: `sdk/ZeroCSharp.Sdk/Sdk/Sdk.targets`: executables link `start.obj`/`start.o` too, alongside runtime.
- Test: `sdk/test/run.sh` `test_payload_complete`, which lists the new files.

**Interfaces:**
- Produces: the payload files `native/linux-x64/library.o`, `native/linux-arm64/library.o`, `native/win-x64/{start,runtime,library}.obj` and `native/osx-arm64/{start,runtime,library}.o`, plus the symbols `ZeroInitializeLibrary` and `zero_dll_start`.

- [ ] **Step 1:** Extend `test_payload_complete` to require the new files. Run `sdk/test/run.sh payload_complete`. Expected: FAIL, naming `library.o`.
- [ ] **Step 2:** Implement the files and the build.sh and Sdk.targets changes above.
- [ ] **Step 3:** Run `sdk/test/run.sh payload_complete native_hello_runs mac_target cross_targets windows_builds_linux`. Expected: `ok` for each, or `skip` on hosts they don't apply to. Executables are unchanged in behaviour.
- [ ] **Step 4:** Commit with the message `[payload] Library startup objects; zerocore's ZeroInitializeLibrary`.

### Task 3: The SDK builds Linux libraries; the caller fixture

**Files:**
- **Modify: `sdk/ZeroCSharp.Sdk/Sdk/Sdk.props` and `Sdk.targets`:**
  - Introduce `ZeroBuildsNative` (true when `OutputType != Library` or `NativeLib == Shared`), and use it everywhere `'$(OutputType)' != 'Library'` gates the native step, PackageDownload, clean and publish.
  - Keep `ZeroPackDll` and `RunCommand` gated on "Library without NativeLib".
  - `IsPackable=false` for NativeLib.
  - The errors from Global Constraints.
  - A `ZeroNativeLibKind` target that returns `$(NativeLib)`. Referencing zero projects call it with `<MSBuild Projects="@(ProjectReference)" Targets="ZeroNativeLibKind" SkipNonexistentTargets="true">` and error if one returns `Shared`.
  - On Linux targets, the library adds `--target Shared`, `--ldflags "<payload>/library.o"`, `--ldflags "-z start-stop-visibility=hidden"`, `--ldflags "-soname <AssemblyName>.so"`, and `-o` into `native/`.
  - Publish copies the library to the publish root.
- Create: the fixtures `nativelib/` and `nativelib-caller/` (with `expected.txt`) and `sdk/test/call-native-lib.sh`, as in "shared test pieces".
- **Tests in `sdk/test/run.sh`, all in `ALL_TESTS`:**
  - `native_library_dotnet`;
  - `native_library_exports` (Linux: `nm -D --defined-only` lists exactly the 10 `nl_*` names);
  - `native_library_two`;
  - `native_library_errors` (Exe+NativeLib, `NativeLib=Static`, `ZeroTarget=wasm`, a ProjectReference from the `native` fixture to `nativelib`, `dotnet run`: each fails with an error naming the cause);
  - `native_library_not_packable` (`dotnet pack` produces no .nupkg);
  - `native_library_publish`;
  - `native_library_incremental` (a no-change build doesn't run bflat, per `ran_bflat`; touching Exports.cs does);
  - `native_library_clean` (dotnet clean removes the library);
  - `native_library_path_with_spaces`;
  - `native_library_with_zero_reference` (the fixture plus a ProjectReference to `app-with-lib`'s zero library; an extra export returns a value computed by that library, and the test calls it through the caller);
  - `native_library_dotted_name` (`AssemblyName=My.Native`, output `native/My.Native.so`, the caller loads it).

**Interfaces:**
- Consumes: the Task 2 payload `library.o` and the Task 1 thread-safe constructors (the caller's `threads` line).
- Produces: the `ZeroBuildsNative` property, the `ZeroNativeLibKind` target, the output path rule, and `call-native-lib.sh`. Tasks 4–7 use these.

- [ ] **Step 1:** Write the fixtures, call-native-lib.sh and the tests. Run `sdk/test/run.sh native_library_dotnet native_library_errors` on Linux. Expected: FAIL (no library is produced; the errors aren't raised).
- [ ] **Step 2:** Implement the props/targets changes for Linux targets.
- [ ] **Step 3:** Run every `native_library_*` test on linux-arm64 (Docker on the Mac, or CI). Expected: all `ok`. Windows- and macOS-target cases print `skip`, which Tasks 4 and 5 fill in.
- [ ] **Step 4:** Run the whole `sdk/test/run.sh`. Expected: no new failures.
- [ ] **Step 5:** Commit with the message `[sdk] NativeLib=Shared: native libraries for Linux targets`.

### Task 4: Windows libraries

**Files:**
- Modify: `Sdk.targets`. For a win-x64 library: `--target Shared`, `--ldflags /entry:zero_dll_start`, `runtime.obj` + `library.obj` (not start.obj), and output `native/<Name>.dll`. bflat writes the `.def` from the exports file. Remove the `.pdb`, as executables do; keep `.lib`.
- Modify: `run.sh`, so `native_library_dotnet`, `_two`, `_path_with_spaces` and `_dotted_name` run their win-x64 case on Windows. `native_library_exports` on Windows checks only through the caller's `hidden ok` line.

- [ ] **Step 1:** Push the branch and let the Windows job run the Task 3 tests. Expected: FAIL. The win-x64 library case is missing or fails.
- [ ] **Step 2:** Implement.
- [ ] **Step 3:** Push and check that the CI `windows` job shows every `native_library_*` test `ok`. Report the run id.
- [ ] **Step 4:** Commit with the message `[sdk] Native libraries for Windows`.

### Task 5: macOS libraries

**Files:**
- **Check first:** does `bflat build --target Shared --os macos -c -o x.o` leave `x.txt`, with one `_name` line per export?
  - If it does: no fork change.
  - If it doesn't: change the fork's `src/bflat/BuildCommand.cs` so that with `-c` and `--target Shared` it writes and keeps the file in that format for `--os macos`. Then follow the 0.3 release path for a new layout (fork PR → layout workflows → `BFLAT_REF`).
  - Ledger which case it was.
- **Modify: `Sdk.targets`.** The osx-arm64 library link is:
  - `-dylib -install_name @rpath/<Name>.dylib -exported_symbols_list "<obj>/zero-native.osx-arm64.txt"`;
  - `runtime.o` + `library.o` (not start.o) + `zerocore.o` + `libSystem.tbd`;
  - output `native/<Name>.dylib`.
  - The exports file path gets forward slashes, like the other link arguments.
- Modify: `run.sh`, so the osx-arm64 cases run on a Mac. `native_library_exports` on a Mac checks that `nm -gU` lists exactly the 10 `_nl_*` names.

- [ ] **Step 1:** Run `sdk/test/run.sh native_library_dotnet native_library_exports` on the Mac. Expected: FAIL.
- [ ] **Step 2:** Implement, including the fork change if the check requires it.
- [ ] **Step 3:** Run every `native_library_*` test on the Mac. Expected: `ok`. Also check that `codesign -v native/nativelib.dylib` succeeds (lld signs ad hoc).
- [ ] **Step 4:** Commit with the message `[sdk] Native libraries for macOS`.

### Task 6: C callers and a zero executable caller

**Files:**
- Create: `sdk/test/fixtures/nativelib-c/main.c`.
  - It loads the library with `dlopen`/`dlsym` (`LoadLibraryA`/`GetProcAddress` under `_WIN32`), or, with `-DLINKED`, declares the exports `extern`.
  - It prints the first 8 lines of `expected.txt` (`add` … `cctor`).
- Create: `sdk/test/fixtures/nativelib-zero-caller/`, a zero Exe that imports `dlopen` and `dlsym` with `[DllImport("*")]`, calls `nl_add(2,3)` and `nl_remember` 3 times, and prints `add 5`, `remember 3`.
- **Tests:**
  - `native_library_c`: `${CC:-cc}`, or `clang` on Windows. If neither exists, `skip` with "no C compiler". Compare with the first 8 expected lines.
  - `native_library_c_linked`: Linux and macOS only. Link with `cc main.c -DLINKED <lib path> -Wl,-rpath,<dir>`, run, compare. This proves `-soname`/`-install_name`.
  - `native_library_with_zero_exe`: Linux and macOS host targets only; Windows skips with "zero executables import at link time".

- [ ] **Step 1:** Write the tests. Run `sdk/test/run.sh native_library_c native_library_c_linked native_library_with_zero_exe` on the Mac and on Linux. Expected: these pass straight away if Tasks 3–5 are right. A failure here is a finding against those tasks. Ledger it and fix it there.
- [ ] **Step 2:** Make sure the CI ubuntu x64, macos and windows jobs have a C compiler in PATH: `gcc` on ubuntu, Xcode `cc` on macOS, LLVM `clang` on windows-latest. Add a setup step only where it's missing. Push. Expected: `native_library_c` shows `ok`, not `skip`, in those three jobs.
- [ ] **Step 3:** Commit with the message `[sdk/test] C and zero-executable callers of a native library`.

### Task 7: Built elsewhere, run on the target (CI)

**Files:**
- Modify: `sdk/test/run.sh`. Add `native_library_cross`: every host builds `nativelib` for all four targets and checks each file's format.
  - Linux targets: ELF with `e_type` 3, a shared object.
  - osx-arm64: Mach-O with filetype 6, `MH_DYLIB`.
  - win-x64: PE with the DLL flag `0x2000` in its characteristics.
  - It copies the results into the existing artifact folders: `work/mac-built/$HOST_RID/nativelib.dylib`, `work/windows-built/<rid>/nativelib.so` and `work/macos-built/<rid>/nativelib.{so,dll}`.
- **Modify: `.github/workflows/sdk.yml`.** `mac-programs`, `windows-built-linux` and `macos-built` each get `actions/checkout`, `actions/setup-dotnet` (10.0.x) and a step that runs `sdk/test/call-native-lib.sh built/<...>/nativelib.<ext>`.

- [ ] **Step 1:** Add `native_library_cross`. Run it on the Mac. Expected: FAIL until the format helpers (`is_elf_shared`, `is_macho_dylib`, `is_pe_dll`) exist. Then add them. Expected: `ok`.
- [ ] **Step 2:** Add the CI steps. Push. Expected: every built-elsewhere job prints the 10 expected lines and passes. Record the run id in the ledger.
- [ ] **Step 3:** Commit with the message `[ci] Native libraries built on one host, called on their target`.

### Task 8: Docs and sample

**Files:**
- Create: a `samples/NativeLib/` zero library with `#region` excerpts, plus a C caller and a .NET caller snippet. The docs site checks excerpts against sample regions; see memory docs-site.md.
- **Create `docs/content/how-to/build-a-native-library.md`.** It covers:
  - the project file;
  - `EntryPoint`, and that a method without one, or one in a referenced zero library, isn't exported;
  - the C type table: `int`→`int32_t`, `long`→`int64_t`, `double`→`double`, `byte`→`uint8_t`, `nint`→`intptr_t`, `T*`→`T*`, blittable struct→struct with the same field order, `bool`→avoid, use `int`;
  - the .NET P/Invoke example (`DllImport` + the `native/` folder);
  - the C example (dlopen, and linking with rpath);
  - the five limits from the spec, worded as there.
- Modify: `docs/content/concepts/limits.md` (libraries; static constructors now thread-safe), `docs/content/how-to/install-with-dotnet.md` (an outputs table row for libraries), `README.md` (feature list), and the docs nav, if it lists pages explicitly.

- [ ] **Step 1:** Build the docs site as memory docs-site.md describes. Expected: the excerpt checks and link check pass, with the new page linked from the how-to index.
- [ ] **Step 2:** Commit with the message `[docs] Build a native library`.

### Task 9: Version 0.4.0 and the PR

**Files:**
- Modify: `sdk/version.props` (`ZeroSdkVersion` 0.4.0; `ZeroFramework` stays zero03).
- Modify: the templates and docs that name `ZeroCSharp.Sdk/0.3.0` (grep for it).

- [ ] **Step 1:** `grep -rn "0\.3\.0" --include=*.md --include=*.csproj --include=*.props --include=*.json . | grep -v docs/superpowers`. Update the version references. Then run `sdk/test/run.sh` on the Mac. Expected: no new failures.
- [ ] **Step 2:** Commit with the message `[sdk] 0.4.0`. Push the branch and open the PR (zero-csharp; the fork PR too, if Task 5 changed bflat). Wait for every sdk.yml job to be green.
- [ ] **Step 3:** Stop. Report the PR and the CI run to the user. Merging, tagging `sdk-v0.4.0` and publishing need their approval.
