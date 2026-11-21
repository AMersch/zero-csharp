# Zero C# SDK 0.2: Windows host Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `dotnet new zero` + `dotnet build` on Windows x64 make native programs for win-x64, linux-x64 and linux-arm64, published as ZeroCSharp 0.2.0 with framework `zero02`.

**Architecture:** The bflat fork's Windows layout gets LLVM 20.1.8's official `lld.exe` through the existing lld release overlay. A new `ZeroCSharp.Bflat.win-x64` package carries a trimmed copy whose file list is mapped from the Linux x64 trace. The SDK learns a Windows host (`.exe` names, no chmod, bflat's arguments as a response file), and CI runs `sdk/test/run.sh` in Git Bash on `windows-latest`.

**Tech Stack:** MSBuild project SDK, bash (Git Bash on Windows), GitHub Actions, bflat fork (C#), C (`win/runtime.c`).

**Spec:** `docs/superpowers/specs/2026-11-22-windows-host-design.md`

## Global Constraints

- Hosts: Linux x64, Linux arm64, Windows x64. macOS keeps its error: "Zero C# can't make native programs on macOS yet; build on Linux or Windows, or use the repository's build.sh."
- Targets from every host: linux-x64, linux-arm64, win-x64. The default target is the host's own (win-x64 on Windows).
- Version 0.2.0 (`sdk/version.props`); framework `zero02`, derived from the version as before.
- `lld.exe` is LLVM 20.1.8, from `clang+llvm-20.1.8-x86_64-pc-windows-msvc.tar.xz`. Its imports are Windows system DLLs only.
- Every bflat package names the fork's exact 40-hex commit (AGPL) and stays under 100 MB.
- Merging, tagging and publishing need the user's go-ahead. Pushes to the fork's master need it too. Stage files by name: `docs/llama-plan.md` is never committed.
- Windows behaviour is proven only on GitHub's `windows-latest`; RED/GREEN for Windows-only tests is read from CI logs.

## Review Focus

1. A Windows user profile or packages folder with spaces or non-ASCII characters (`C:\Users\Zoë Ann\.nuget\packages`): builds work. Test: `windows_unusual_packages_folder` (Task 4).
2. A project with many source files with long paths (cmd.exe's 8191-character limit): builds work. Test: `long_command_line` (Task 2).
3. Input piped from PowerShell or cmd ends lines in `\r\n`: `In.ReadLine` doesn't return the `\r`. Test: `crlf_input` (Task 6).
4. `dotnet clean` on Windows while nothing holds the program: every target's program is removed. Covered by `targets_side_by_side`, which runs on Windows (Task 5).
5. A Linux program built on Windows is a valid ELF for its architecture, and the same program built on Linux runs: covered by `cross_targets` and `windows_builds_linux` (Tasks 4 and 5).

---

### Task 1: Windows lld.exe in the fork's lld release and layout (repo: ../bflat)

**Files:**
- Modify: `../bflat/.github/workflows/lld.yml` (add a `win-x64` job, and upload `lld-win-x64.tar.gz` in `release`)
- Create: `../bflat/tools/check-lld-win.sh`
- Modify: `../bflat/src/bflat/bflat.csproj` (the `LldRid`/`LldFile` group and the overlay `Exec`, about lines 68-95 and 208-210)

**Interfaces:**
- Produces: the release asset `lld-win-x64.tar.gz`, holding `lld.exe` at the top. The `windows-x64` layout's `bin/lld.exe` is LLVM 20.1.8.

- [ ] **Step 1: Write `tools/check-lld-win.sh <lld.exe>`.** It fails (exit 1) unless `objdump -p` lists only DLLs matching `^(KERNEL32|ADVAPI32|ntdll|ole32|OLEAUT32|SHELL32|USER32|VERSION|WS2_32|bcrypt|api-ms-win-.*|ucrtbase)\.dll$` (case-insensitive), and the file's version strings contain `20.1.8`. It exits 2 when objdump is missing, like `check-lld.sh`.
- [ ] **Step 2: Run it against the blobs' MinGW `layouts/windows-x64/bin/lld.exe`.** Expected: FAIL. That lld needs `libwinpthread-1.dll` and isn't 20.1.8.
- [ ] **Step 3: Add the `win-x64` job to `lld.yml` on `ubuntu-latest`.** It downloads the LLVM archive, extracts `*/bin/lld.exe`, and runs `tools/check-lld-win.sh`. Then it packs `lld-win-x64.tar.gz` (`-C dir lld.exe`) and uploads it. `release` uploads `lld-*.tar.gz`.
- [ ] **Step 4: Add `win-x64` to the csproj.** Set `<LldRid Condition="'$(CompilerHost)' == 'windows-x64'">win-x64</LldRid>`. The overlay extracts `lld.exe` for that RID and `lld` for the others. Delete `bin/libwinpthread-1.dll` from the windows-x64 layout only if nothing else in `bin/` imports it (check `llvm-objcopy.exe` with objdump).
- [ ] **Step 5: Push to a fork branch (ask the user before pushing master).** The `lld` workflow runs on the path change. Expected: the release has `lld-win-x64.tar.gz`, and the check step's log shows only system DLLs.
- [ ] **Step 6: Build the layouts locally.** Run `dotnet build src/bflat/bflat.csproj -t:BuildLayouts -c:Release` (see the memory note on deleting `obj/Release` first). Then run `tools/check-lld-win.sh layouts/windows-x64/bin/lld.exe`. Expected: PASS.
- [ ] **Step 7: Commit in the fork.** Merge to master after the user's OK.

### Task 2: zero02 and bflat's arguments as a response file (Linux-observable)

**Files:**
- Modify: `sdk/version.props` (0.2.0), `sdk/ZeroCSharp.Sdk/Sdk/Sdk.targets` (ZeroNativeBuild's `Exec`), `sdk/ZeroCSharp.Sdk/Sdk/Sdk.props:4` (comment)
- Modify: `sdk/test/run.sh` (version-mismatch fixture name; new `long_command_line` test)
- Modify: `docs/get-started/install-with-dotnet.md`, `docs/how-to/create-a-library.md`: zero01 → zero02, and the NU1202 examples
- Create: `sdk/test/fixtures/manyfiles/` (a project whose `Program.cs` calls 40 classes, each in its own file under a 120-character directory name; written by the test, not checked in)

**Interfaces:**
- Produces: bflat runs as `"<bflat>" build @"<obj>/zero-native[.<target>].args"`. The args file is written one argument per line, quotes kept as they are today. The incremental inputs are unchanged.

- [ ] **Step 1: Write `test_long_command_line`.** It makes `manyfiles` under `$WORK/` with 40 sources, so the arguments run past 8191 characters, then builds it. Assertions: the build succeeds, the program prints `40`, and the log matches `tools/bflat/bflat(\.exe)?" build @`.
- [ ] **Step 2: Point the version-mismatch test at the other framework.** Change `test_version_mismatch` to make `lib/zero01/` (the other version's framework, now that ours is zero02) and grep for `zero01`.
- [ ] **Step 3: Bump `sdk/version.props` to 0.2.0 and run both tests on Linux.** Run: `ZERO_HOSTS=linux-arm64 sdk/test/linux.sh long_command_line version_mismatch`. Expected: `long_command_line` FAILs on the `@` assertion; `version_mismatch` passes.
- [ ] **Step 4: Run bflat with `@file`.** In `Sdk.targets`, write the args to the args file one per line and pass `@"$(ZeroNativeArgsFile)"`. Check how System.CommandLine beta4 splits response-file lines: if it splits on spaces inside a line, write each token on its own line and drop the quotes.
- [ ] **Step 5: Run the full Linux suite.** Run: `ZERO_HOSTS=linux-arm64 sdk/test/linux.sh`. Expected: all pass, with the macOS test skipped.
- [ ] **Step 6: Update the docs for zero02.** In the install page and the library guide, change `zero01` paths and text to `zero02`. Make the NU1202 example say "supports: zero01". Add to the install page: "Libraries packed with 0.1 (`zero01`) need re-packing with 0.2."
- [ ] **Step 7: Commit** `[sdk] 0.2: framework zero02; bflat gets its arguments from a response file`.

### Task 3: run.sh portable to Git Bash

**Files:**
- Modify: `sdk/test/run.sh` (host detection, the native-program helpers, the `nuget.config` feed path)

**Interfaces:**
- Produces, for later tasks:
  - `HOST_RID`: linux-x64, linux-arm64 or win-x64.
  - `can_build <name>`: replaces `linux_only`; true on Linux and Windows.
  - `exe <path-without-ext>`: the program path for the host's default target (adds `.exe` on Windows).
  - `is_elf <file> <aarch64|x86-64>` and `is_pe <file>`: header checks with `head -c` and `od`, since Git Bash has no `file`.
  - `native_path <path>`: `cygpath -w` on Windows, the path itself elsewhere. It's used for the feed in `nuget.config`.

- [ ] **Step 1: Rewrite host detection and add the helpers.**
  - Host detection: `uname -s` MINGW*/MSYS* means Windows, giving HOST_RID win-x64.
  - Helpers: the five listed in the Interfaces block.
  - Replace every `file ... | grep ELF` with `is_elf` and every `head -c 2 ... MZ` with `is_pe`.
  - Use `exe` wherever a test runs the default program.
  - `test_trimmed_layout_builds`, `test_read_only_packages_folder` and `test_host_packages`'s Linux checks skip on Windows, saying why.
- [ ] **Step 2: Run the full suite on Linux and on the Mac.** Expected: the same results as before the change (Linux 34+ pass; Mac 11 pass).
- [ ] **Step 3: Commit** `[sdk/test] run.sh runs under Git Bash`.

### Task 4: SDK on a Windows host

**Files:**
- Modify: `sdk/ZeroCSharp.Sdk/Sdk/Sdk.props` (ZeroHostRid), `sdk/ZeroCSharp.Sdk/Sdk/Sdk.targets` (ZeroBflatDir paths, chmod condition, ZeroCheckNativeHost message, RunCommand)
- Modify: `sdk/test/run.sh` (tests `windows_builds_linux`, `windows_unusual_packages_folder`)

**Interfaces:**
- Consumes: Task 3's helpers.
- Produces:
  - `ZeroHostRid` is `win-x64` on Windows x64.
  - `ZeroBflatExe` is `$(ZeroBflatDir)bflat.exe` on Windows and `$(ZeroBflatDir)bflat` elsewhere.
  - The chmod `Exec` runs only when `'$(OS)' != 'Windows_NT'`.
  - On another host (for example Windows arm64), the error reads: "Zero C# can't make native programs on this machine yet; build on Linux (x64 or arm64) or Windows x64."

- [ ] **Step 1: Write `test_windows_builds_linux`.** It runs only on Windows and skips elsewhere. It builds the `native` fixture with `-p:ZeroTarget=linux-x64` and then `linux-arm64`. Assertions: `is_elf bin/Debug/$TFM/linux-x64/native x86-64` and `is_elf .../linux-arm64/native aarch64`.
- [ ] **Step 2: Write `test_windows_unusual_packages_folder`.** It runs on Windows and Linux. It builds `native` with `NUGET_PACKAGES="$WORK/pkgs Zoë Ann"`. Assertion: the program prints `hi`.
- [ ] **Step 3: Implement the Sdk.props and Sdk.targets changes** listed in the Interfaces block.
- [ ] **Step 4: Run the Linux suite.** Run: `ZERO_HOSTS=linux-arm64 sdk/test/linux.sh`. Expected: all pass, with the Windows-only tests skipped.
- [ ] **Step 5: Commit.** Windows RED/GREEN comes in Task 5, once CI can run it.

### Task 5: The win-x64 bflat package and the Windows CI job

**Files:**
- Create: `sdk/trim-windows-layout.sh <windows-layout> <linux-x64-trimmed> <out>`
- Modify: `sdk/pack.sh` (the `win-x64` host: layout `windows-x64`, trimmed by the new script, a checksum stamp as for Linux; `ZERO_HOSTS` default "linux-x64 linux-arm64 win-x64")
- Modify: `sdk/test/run.sh` (`test_host_packages`: for win-x64, check for `tools/bflat/bflat.exe` and `tools/bflat/bin/lld.exe`)
- Modify: `.github/workflows/sdk.yml`:
  - `bflat` job: caches `layouts/windows-x64` too;
  - `trim` gains a `win-x64` entry that needs the linux-x64 trimmed artifact;
  - `pack`: adds win-x64;
  - a `test (windows-latest)` job runs in `shell: bash`, with setup-dotnet 10.0.x, the feed, and `ZERO_NO_PACK=1 sdk/test/run.sh`.

**Interfaces:**
- Consumes: Task 1's layout, with `bin/lld.exe` 20.1.8; Task 3's helpers; Task 4's SDK.
- Produces: `sdk/feed/ZeroCSharp.Bflat.win-x64.<version>.nupkg`.

- [ ] **Step 1: Write the mapping in `trim-windows-layout.sh`.**
  - Start from every file in the linux-x64 trimmed copy.
  - Top-level `lib*.so` becomes `<name without lib>.dll`; for example `libclrjit_unix_x64_x64.so` → `clrjit_unix_x64_x64.dll`.
  - `bflat` becomes `bflat.exe`, and `bin/lld` becomes `bin/lld.exe`.
  - Other paths (`lib/...`, notices) stay as they are.
  - Copy each mapped file from the Windows layout. The script fails, naming the file, if a mapped file is missing.
  - Then add every top-level `*.dll` that `bflat.exe` imports (`objdump -p`) and that exists in the layout.
- [ ] **Step 2: Run it locally on `../bflat/layouts/windows-x64` with the linux-x64 trimmed copy.** Expected: about 80 files, and `du` under 150 MB.
- [ ] **Step 3: Push the branch and open a draft PR.** The new Windows job runs Tasks 2–5. Expected on the first run: failures. Record them in the ledger; they're the Windows RED for Tasks 4 and 5 and the input for Task 6.
- [ ] **Step 4: Fix SDK and script problems until the Windows job's SDK tests pass.** `cross_targets`, `windows_builds_linux`, `path_with_spaces`, `dotnet_run`, `publish`, `templates` and `targets_side_by_side` must pass. Leave any failure caused by the program's own behaviour to Task 6.
- [ ] **Step 5: Commit** each fix with what it fixed.

### Task 6: Windows programs behave on real Windows

**Files:**
- Modify (as the runner shows): `lib/In.cs` (`ReadLine`), `win/runtime.c`, `zerocore/native/win-x64.S`, or the fork.
- Modify: `sdk/test/run.sh` (test `crlf_input`); `test.sh` / `tests/expected` if a sample's Windows output is checked.

**Interfaces:**
- Produces: `In.ReadLine(Span<char>)` returns the line's length without a trailing `\r`, on every target.

- [ ] **Step 1: Write `test_crlf_input`.** It runs on every host that builds. It builds the template program for the host and runs `printf 'Ann\r\n' | <program>`. Assertion: the output is exactly `What's your name? Hello, Ann!`, with a trailing newline allowed: `\r` is allowed only immediately before a newline, and there's no `\r` before the `!`.
- [ ] **Step 2: Run it on Linux.** Expected: FAIL (`Hello, Ann\r!`). It's also seen failing in the Windows job.
- [ ] **Step 3: Make `In.ReadLine` drop one `\r` before the line's `\n`.** This is in `lib/In.cs` and is the same on every target.
- [ ] **Step 4: Run `crlf_input` and the repo's own `./test.sh` on Linux.** Expected: PASS. Then push, and the Windows job passes `crlf_input`.
- [ ] **Step 5: Fix every other Windows-only failure from the Task 5 run the same way:** a test reproducing it, the fix, the run.
- [ ] **Step 6: Commit each fix.**

### Task 7: Docs, then the release

**Files:**
- Modify: `docs/get-started/install-with-dotnet.md` (Windows: the same commands, PowerShell examples `echo Ann | .\bin\Debug\zero02\Hello.exe`; the host list), `docs/concepts/limits.md`, `README.md`, `docs/get-started/index.md` (drop "Linux only" for the SDK)
- Modify: `sdk/ZeroCSharp.Sdk/Sdk/Sdk.props` comments (the host list)

- [ ] **Step 1: Edit the docs.** Run: `python3 scripts/build-docs.py`. Expected: it builds with no broken-link or excerpt errors.
- [ ] **Step 2: Wait for green CI on the PR, all jobs including Windows. Then ask the user to merge and tag `sdk-v0.2.0`.**
- [ ] **Step 3: Add `.github/workflows/sdk-smoke.yml`, checked in with this task.**
  - It's a `workflow_dispatch` workflow with input `version`.
  - Matrix: `ubuntu-24.04`, `ubuntu-24.04-arm`, `windows-latest`.
  - Each job runs `dotnet new install ZeroCSharp.Templates::<version>` from nuget.org, then `dotnet new zero -o Hello`, `dotnet build`, and checks that `echo Ann | <program>` prints `Hello, Ann!`.
  - It also builds `-p:ZeroTarget=` for the other two targets.
- [ ] **Step 4: Once nuget.org lists all five 0.2.0 packages, dispatch `sdk-smoke` with `0.2.0`.** Expected: green on all three runners.
