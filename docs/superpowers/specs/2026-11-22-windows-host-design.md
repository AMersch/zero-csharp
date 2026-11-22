# Zero C# SDK 0.2: Windows as a build machine: design

Date: 2026-11-22. Status: reviewed (builder check), for approval.

## Intent

Since 0.1.0, someone with only the .NET 10 SDK on Linux can `dotnet new zero` and `dotnet build` a
native program. 0.2 brings the same to Windows x64: the same commands, the same three targets
(linux-x64, linux-arm64, win-x64), no repository clone, nothing else installed. Windows is where most
.NET developers are, so it's what makes the packages usable by most of the audience.

## Goals

- On Windows x64 with the .NET 10 SDK: `dotnet new zero`, `dotnet build`, `dotnet run`,
  `dotnet publish`, `dotnet clean` behave as they do on Linux.
- From Windows, all three targets: win-x64 (the default there), linux-x64, linux-arm64. *(The user's
  decision.)*
- Everything 0.1.1 tests on Linux that applies to Windows is tested on a GitHub Windows runner.
- Linux hosts behave as in 0.1.1, except the framework name (zero02, below).
- win-x64 programs work on real Windows, not only under Wine: bugs the Windows runner finds in
  zerocore's Windows runtime (`win/runtime.c`, `zerocore/native/win-x64.S`), in zerocore itself or in
  the bflat fork are fixed in 0.2, which ships when Windows works end to end. *(The user's decision.)*

## Non-goals

- macOS as a host (still the clear error message), Windows arm64 as a host.
- New targets.
- Running Linux programs on Windows in tests (WSL); they're checked as ELF files.

## Constraints

- The target framework becomes `zero02`, as 0.1's rule says (each minor version is its own
  framework). NuGet then refuses library packages built for 0.1 (`zero01`) with NU1202, on every
  host, until they're re-packed with 0.2. *(The user's decision.)* Everything that names `zero01`
  moves: docs paths and text, the "Create a reusable library" guide, and tests that hard-code it
  (run.sh derives it from the version; the version-mismatch fixture's `zero02` becomes another
  version's name). The install page says that 0.1 libraries need re-packing.
- The bflat package for Windows is AGPL like the Linux ones: names the fork's exact commit.
- No local Windows machine: Windows behaviour is proven on GitHub's `windows-latest` runner only.

## Approaches (the linker for Linux targets on Windows)

The Windows layout's linker (`bin/lld.exe`, a MinGW build of LLVM 12 from bflat's blobs) links
Windows programs but rejects `-z nostart-stop-gc`, which Linux programs need with zerocore.

1. **LLVM's official 20.1.8 Windows `lld.exe`, added to the fork's `lld-20.1.8` release.** Quick; same
   version as the Linux hosts' linker. Risk: optional libraries as DLLs (the reason the official
   Linux build was unusable); a check of its imports guards that. **Chosen by the user.**
2. Build lld from source on a Windows runner. Full control, an hour-long build and a script debugged
   only through CI. Fallback if the import check fails.

## Design

**Fork (OnurGumus/bflat).**
- `.github/workflows/lld.yml` gets a `win-x64` job: download LLVM's
  `clang+llvm-20.1.8-x86_64-pc-windows-msvc.tar.xz`, extract `bin/lld.exe`, check its imports (with
  `llvm-objdump -p` or `objdump -p` on Linux) are only Windows system DLLs (KERNEL32, ADVAPI32,
  ntdll, api-ms-win-*, ucrtbase, ...: no libxml2, zlib, zstd, VC runtime outside the system), and add
  `lld-win-x64.tar.gz` to the release.
- `bflat.csproj`: `LldRid` win-x64 for the windows-x64 layout; the overlay extracts `lld.exe` there.
  The layout's `libwinpthread-1.dll` stays only if something else needs it.
- Push to master; the zero-csharp `bflat` CI job picks up the new commit.

**SDK (`sdk/ZeroCSharp.Sdk`).**
- `ZeroHostRid` win-x64 on Windows x64. Default `ZeroTarget` there is win-x64.
- Paths: `bflat.exe`, `bin/lld.exe` on Windows; no chmod step on Windows (`Exec` runs cmd.exe there,
  so the 0.1.1 `test -x ... || chmod` line must be Unix-only).
- bflat gets its command line from the args file the SDK already writes, as a response file
  (`bflat build @file`): MSBuild's `Exec` runs cmd.exe on Windows, whose command lines stop at 8191
  characters, and a project's full source paths can pass that. bflat's System.CommandLine (beta4)
  reads `@file` by default; a test with many long source paths proves it on both hosts. The same on
  Linux, so there's one way. *(Builder's call, from the builder check.)*
- `PackageDownload` of `ZeroCSharp.Bflat.win-x64` on Windows.
- `RunCommand` with `.exe` on Windows.
- The "can't make native programs on this machine" error stays for other hosts (Windows arm64, ...).

**Package `ZeroCSharp.Bflat.win-x64`.**
- Trimmed from the windows-x64 layout. The Windows layout can't be traced with strace, so the file
  list is derived: the target-side files (`lib/`, `ref/`, per-target libraries) are the same paths
  the Linux trace found, and the host-side files map from the Linux host's (`bflat` → `bflat.exe`,
  `libclrjit_*.so` → `clrjit_*.dll`, `libjitinterface_x64.so` → `jitinterface_x64.dll`, `bin/lld` →
  `bin/lld.exe`), plus license notices. A clean Windows runner building all three targets with the
  result is what proves the list. *(Builder's call.)*
- Under the same 100 MB package limit as the Linux ones.

**CI (`.github/workflows/sdk.yml`).**
- The `bflat` job caches `layouts/windows-x64` too; a `trim` step produces `sdk/trimmed/win-x64`.
- `pack` packs the win-x64 bflat package.
- A `test (windows-latest)` job runs `sdk/test/run.sh` in Git Bash with the packed feed. Tests that
  run native programs run Windows programs directly; Linux outputs are checked as ELF for the right
  architecture.

**Windows behaviour of programs.** The Windows runner runs the template program and the samples'
Windows builds for real. Expected trouble spots: input lines from PowerShell or cmd end in `\r\n`
(`In.ReadLine` must not return the `\r`), console output encoding, the exit code. What's found gets a
test and a fix in this release (see Goals).

**Tests (`sdk/test/run.sh`).** `linux_only` becomes "needs a host that builds" (Linux or Windows);
host-dependent paths and file checks come from the host (`.exe`, ELF vs PE). Windows-specific
additions: building linux-x64 and linux-arm64 from Windows gives ELF files; `dotnet run` and the
template program run on Windows.

**Docs.** Install page: Windows alongside Linux, PowerShell examples where they differ (`echo Ann |
.\bin\Debug\zero02\Hello.exe`); limits page and README drop "Linux only".

**Release.** Version 0.2.0, tag `sdk-v0.2.0`, trusted publishing as before.

## Left to the builder

- Exact cmd.exe quoting of the `Exec` lines and where the trimmed file list for Windows is written.
- Whether the Windows trim is a script in `sdk/` (bash, run on Linux in CI) or a CI step.
- Which tests need a Windows variant versus a skip on Windows.
