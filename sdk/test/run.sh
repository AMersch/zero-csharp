#!/usr/bin/env bash
# The tests of the Zero C# SDK packages (sdk/). Each test copies a project
# from sdk/test/fixtures into sdk/test/work, builds it with the dotnet CLI
# against the packages in sdk/feed, and checks the result.
#
# Usage:  sdk/test/run.sh                 pack (sdk/pack.sh), then run every test
#         sdk/test/run.sh name...         only these tests (names as in the list below)
# Env:    ZERO_NO_PACK=1                  use sdk/feed as it is (CI downloads it)
#         ZERO_HOSTS                      the host packages to expect (default: all,
#                                         "linux-x64 linux-arm64 win-x64"); also passed to pack.sh
#
# Exit code: the number of failed tests.
set -uo pipefail
cd "$(dirname "$0")/../.."
ROOT=$PWD
WORK=$ROOT/sdk/test/work
FEED=$ROOT/sdk/feed
VERSION=$(sed -n 's|.*<ZeroSdkVersion>\(.*\)</ZeroSdkVersion>.*|\1|p' sdk/version.props)
# The target framework: ZeroFramework (sdk/version.props), not made from the version.
TFM=$(sed -n 's|.*<ZeroFramework>\(.*\)</ZeroFramework>.*|\1|p' sdk/version.props)
case "$(uname -s)" in
    Linux)                    HOST_OS=Linux ;;
    Darwin)                   HOST_OS=Darwin ;;
    MINGW* | MSYS* | CYGWIN*) HOST_OS=Windows ;;   # Git Bash
    *)                        HOST_OS=$(uname -s) ;;
esac
export ZERO_HOSTS=${ZERO_HOSTS:-linux-x64 linux-arm64 win-x64 osx-arm64}
case "$(uname -m)" in
    x86_64 | amd64)  HOST_ARCH_NAME=x64;   DOCKER_ARCH=amd64; HOST_ELF_ARCH=x86-64 ;;
    *)               HOST_ARCH_NAME=arm64; DOCKER_ARCH=arm64; HOST_ELF_ARCH=aarch64 ;;
esac
case "$HOST_OS" in
    Windows) HOST_RID=win-x64 ;;
    Darwin)  HOST_RID=osx-$HOST_ARCH_NAME ;;     # osx-arm64; osx-x64 can't build
    *)       HOST_RID=linux-$HOST_ARCH_NAME ;;
esac

passed=0
failed=0
skipped=0
LOG=
pass() { echo "ok   $1"; passed=$((passed + 1)); }
fail() {
    echo "FAIL $1: $2"
    [ -n "$LOG" ] && [ -f "$LOG" ] && tail -20 "$LOG" | sed 's/^/     | /'
    failed=$((failed + 1))
}

skip() { echo "skip $1: $2"; skipped=$((skipped + 1)); }
# can_build <test name>: true on a host that builds native programs (Linux,
# Windows, an Apple Silicon Mac); on others, records a skip.
can_build() {
    [ "$HOST_OS" = Linux ] || [ "$HOST_OS" = Windows ] || [ "$HOST_RID" = osx-arm64 ] && return 0
    skip "$1" "builds native programs, which needs a Linux, Windows or Apple Silicon host"
    return 1
}
# linux_only <test name>: true on Linux; on other hosts, records a skip.
linux_only() {
    [ "$HOST_OS" = Linux ] && return 0
    skip "$1" "needs a Linux host"
    return 1
}
# exe <path>: the program for the host's own target (.exe on Windows).
exe() { if [ "$HOST_OS" = Windows ]; then echo "$1.exe"; else echo "$1"; fi; }
# is_elf <file> <x86-64|aarch64>, is_pe <file>, is_coff_x64 <file>: what a
# file is, from its header (Git Bash has no file command).
byte_at() { od -An -tu1 -j"$2" -N1 "$1" 2>/dev/null | tr -d ' '; }
is_elf() {
    [ "$(head -c 4 "$1" 2>/dev/null | tail -c 3)" = ELF ] || return 1
    case "$2" in x86-64) [ "$(byte_at "$1" 18)" = 62 ] ;; aarch64) [ "$(byte_at "$1" 18)" = 183 ] ;; *) return 1 ;; esac
}
is_pe() { [ "$(head -c 2 "$1" 2>/dev/null)" = MZ ]; }
is_coff_x64() { [ "$(byte_at "$1" 0)" = 100 ] && [ "$(byte_at "$1" 1)" = 134 ]; }
# is_macho_arm64 <file>: 64-bit Mach-O (cf fa ed fe) for arm64 (CPU type 0x0100000c).
is_macho_arm64() {
    [ "$(byte_at "$1" 0)" = 207 ] && [ "$(byte_at "$1" 1)" = 250 ] && [ "$(byte_at "$1" 2)" = 237 ] && [ "$(byte_at "$1" 3)" = 254 ] &&
        [ "$(byte_at "$1" 4)" = 12 ] && [ "$(byte_at "$1" 5)" = 0 ] && [ "$(byte_at "$1" 6)" = 0 ] && [ "$(byte_at "$1" 7)" = 1 ]
}
# The host's own target's native library suffix (NativeLib=Shared).
case "$HOST_OS" in Windows) HOST_LIB_EXT=.dll ;; Darwin) HOST_LIB_EXT=.dylib ;; *) HOST_LIB_EXT=.so ;; esac
# is_elf_shared <file>, is_macho_dylib <file>, is_pe_dll <file>: a native
# library for Linux (ELF type 3, ET_DYN, which a PIE program is too, but with
# no program interpreter: a program names /lib.../ld-linux...), macOS
# (Mach-O type 6, MH_DYLIB) or Windows (PE with IMAGE_FILE_DLL, 0x2000, in its
# characteristics).
is_elf_shared() {
    [ "$(head -c 4 "$1" 2>/dev/null | tail -c 3)" = ELF ] && [ "$(byte_at "$1" 16)" = 3 ] && ! LC_ALL=C grep -aq '/ld-linux' "$1"
}
is_macho_dylib() { is_macho_arm64 "$1" && [ "$(byte_at "$1" 12)" = 6 ]; }
is_pe_dll() {
    local pe
    is_pe "$1" || return 1
    pe=$(( $(byte_at "$1" 60) + 256 * $(byte_at "$1" 61) ))
    [ $(( $(byte_at "$1" $((pe + 23))) & 32 )) -ne 0 ]
}
# is_host_program <file>: a program for the host's own target.
is_host_program() {
    case "$HOST_OS" in Windows) is_pe "$1" ;; Darwin) is_macho_arm64 "$1" ;; *) is_elf "$1" "$HOST_ELF_ARCH" ;; esac
}
# native_path <path>: the path as the dotnet CLI on this host writes it.
native_path() { if [ "$HOST_OS" = Windows ]; then cygpath -w "$1"; else echo "$1"; fi; }
# zip_list <zip>: the entries' names; zip_dir <dir> <zip>: zips a folder's contents.
zip_list() {
    if command -v unzip > /dev/null; then unzip -Z1 "$1"
    else powershell.exe -NoProfile -Command "Add-Type -A System.IO.Compression.FileSystem; [IO.Compression.ZipFile]::OpenRead('$(native_path "$1")').Entries.FullName" | tr -d '\r'; fi
}
zip_dir() {
    if command -v zip > /dev/null; then (cd "$1" && zip -qr "$2" .)
    else powershell.exe -NoProfile -Command "Add-Type -A System.IO.Compression.FileSystem; [IO.Compression.ZipFile]::CreateFromDirectory('$(native_path "$1")', '$(native_path "$2")')"; fi
}

# fixture <fixture> [<dir under work>]: a fresh copy of a fixture, with the
# SDK version filled in and a nuget.config that finds the local feed.
fixture() {
    local from=$ROOT/sdk/test/fixtures/$1 to=$WORK/${2:-$1}
    rm -rf "$to"
    mkdir -p "$(dirname "$to")"
    cp -R "$from" "$to"
    find "$to" -name '*.csproj' -exec sed -i.bak "s|@ZERO_SDK_VERSION@|$VERSION|g" {} \; -exec rm -f {}.bak \;
    cat > "$to/nuget.config" <<EOF
<configuration>
  <packageSources>
    <clear />
    <add key="zero" value="$(native_path "$FEED")" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
    echo "$to"
}

# run_in <dir> <log name> dotnet args...: runs dotnet in <dir>, output to a log.
run_in() {
    local dir=$1
    LOG=$WORK/$2.log
    shift 2
    (cd "$dir" && dotnet "$@") > "$LOG" 2>&1
}

# run_limited <seconds> <output file> <program> [args]: runs it, output to the
# file, killed after <seconds> (exit 124): a deadlock fails a test instead of
# hanging it. macOS has no timeout command.
run_limited() {
    local secs=$1 out=$2 pid i=0
    shift 2
    "$@" > "$out" 2>&1 &
    pid=$!
    while kill -0 "$pid" 2> /dev/null; do
        if [ "$i" -ge $((secs * 10)) ]; then
            kill -9 "$pid" 2> /dev/null
            wait "$pid" 2> /dev/null
            return 124
        fi
        sleep 0.1
        i=$((i + 1))
    done
    wait "$pid"
}

# ---- Tests -------------------------------------------------------------------

test_hello_compiles() {
    local dir
    dir=$(fixture hello)
    if ! run_in "$dir" hello build; then fail "hello compiles" "dotnet build failed"; return; fi
    local dll=$dir/bin/Debug/$TFM/hello.dll
    if [ ! -f "$dll" ]; then fail "hello compiles" "no $dll"; return; fi
    # Metadata strings are NUL-terminated: an assembly reference is a whole one.
    if tr '\0' '\n' < "$dll" | grep -qx 'System.Runtime'; then
        fail "hello compiles" "the output references System.Runtime"; return
    fi
    pass "hello compiles"
}

test_console_is_an_error() {
    local dir
    dir=$(fixture console)
    if run_in "$dir" console build; then fail "console is an error" "the build succeeded"; return; fi
    if ! grep -qE 'CS0103|CS0234' "$LOG"; then fail "console is an error" "no CS0103/CS0234"; return; fi
    pass "console is an error"
}

test_unions_compile() {
    local dir
    dir=$(fixture union)
    if ! run_in "$dir" union build; then fail "unions compile" "dotnet build failed"; return; fi
    pass "unions compile"
}

test_plain_packages_refused() {
    local dir
    dir=$(fixture plainpkg)
    if run_in "$dir" plainpkg restore; then fail "plain packages refused" "restore succeeded"; return; fi
    if ! grep -q 'NU1202' "$LOG"; then fail "plain packages refused" "no NU1202"; return; fi
    pass "plain packages refused"
}

test_payload_complete() {
    local payload=$ROOT/sdk/ZeroCSharp.Sdk/payload f
    LOG=
    # start: a program's entry point; library: a native library's startup
    # (NativeLib=Shared); runtime: what both share.
    for f in ref/System.Private.CoreLib.dll ref/BflatLib.dll native/linux-x64/zerocore.o native/linux-x64/library.o \
             native/linux-arm64/zerocore.o native/linux-arm64/library.o \
             native/win-x64/start.obj native/win-x64/runtime.obj native/win-x64/library.obj native/win-x64/zerocore.obj \
             native/osx-arm64/start.o native/osx-arm64/runtime.o native/osx-arm64/library.o native/osx-arm64/zerocore.o \
             native/osx-arm64/libSystem.tbd VERSIONS.txt; do
        if [ ! -s "$payload/$f" ]; then fail "payload complete" "missing $f"; return; fi
    done
    if ! is_elf "$payload/native/linux-x64/zerocore.o" x86-64; then fail "payload complete" "linux-x64 object isn't x86-64"; return; fi
    if ! is_elf "$payload/native/linux-arm64/zerocore.o" aarch64; then fail "payload complete" "linux-arm64 object isn't aarch64"; return; fi
    if ! is_elf "$payload/native/linux-x64/library.o" x86-64; then fail "payload complete" "linux-x64 library.o isn't x86-64"; return; fi
    if ! is_elf "$payload/native/linux-arm64/library.o" aarch64; then fail "payload complete" "linux-arm64 library.o isn't aarch64"; return; fi
    for f in start.obj runtime.obj library.obj; do
        if ! is_coff_x64 "$payload/native/win-x64/$f"; then fail "payload complete" "win-x64/$f isn't COFF"; return; fi
    done
    for f in start.o runtime.o library.o zerocore.o; do
        if ! is_macho_arm64 "$payload/native/osx-arm64/$f"; then fail "payload complete" "osx-arm64/$f isn't arm64 Mach-O"; return; fi
    done
    if ! grep -q "install-name: *'/usr/lib/libSystem.B.dylib'" "$payload/native/osx-arm64/libSystem.tbd" ||
       ! grep -q "arm64-macos" "$payload/native/osx-arm64/libSystem.tbd"; then
        fail "payload complete" "libSystem.tbd doesn't name /usr/lib/libSystem.B.dylib for arm64-macos"; return
    fi
    pass "payload complete"
}

# Trims the host's layout (sdk/trim-layout.sh) and builds with the result in a
# clean Ubuntu 24.04: nothing installed, so it must need nothing the layout or
# a stock system lacks. Slow (it traces builds in a container): run it by name.
test_trimmed_layout_builds() {
    local layout=${BFLAT_DIR:-$ROOT/../bflat/layouts/linux-glibc-$HOST_ARCH_NAME} out=$WORK/trimmed size
    LOG=$WORK/trim.log
    if ! sdk/trim-layout.sh "$layout" "$out" > "$LOG" 2>&1; then fail "trimmed layout builds" "trim-layout.sh failed"; return; fi
    size=$(du -sm "$out" | cut -f1)
    if [ "$size" -ge 150 ]; then fail "trimmed layout builds" "$size MB, not under 150"; return; fi
    LOG=$WORK/trim-build.log
    if ! docker run --rm --platform "linux/$DOCKER_ARCH" -v "$ROOT:/w" -v "$out:/bflat:ro" -w /w ubuntu:24.04 bash -c '
            set -e
            P=sdk/ZeroCSharp.Sdk/payload
            F="--stdlib:none --langversion preview --compiler-feature updated-memory-safety-rules --no-reflection -i libc -i libm.so.6"
            B="/bflat/bflat build samples/Strings.cs -r $P/ref/BflatLib.dll -r $P/ref/System.Private.CoreLib.dll $F"
            $B --arch arm64 --ldflags $P/native/linux-arm64/zerocore.o --ldflags "-z nostart-stop-gc" -o /tmp/s-arm64
            $B --arch x64 --ldflags $P/native/linux-x64/zerocore.o --ldflags "-z nostart-stop-gc" -o /tmp/s-x64
            $B --os windows --arch x64 --ldflags "/entry:zero_start $P/native/win-x64/start.obj $P/native/win-x64/runtime.obj $P/native/win-x64/zerocore.obj" -o /tmp/s.exe
            /tmp/s-'"$HOST_ARCH_NAME"' > /dev/null' > "$LOG" 2>&1; then
        fail "trimmed layout builds" "building or running in ubuntu:24.04 failed"; return
    fi
    pass "trimmed layout builds ($size MB)"
}

test_host_packages() {
    local rid pkg size listing
    LOG=
    # Checked where the packages are made (Linux CI and the Mac); the Windows
    # job only builds with them.
    if [ "$HOST_OS" = Windows ]; then skip "host packages" "checked on Linux"; return; fi
    for rid in $ZERO_HOSTS; do
        pkg=$FEED/ZeroCSharp.Bflat.$rid.$VERSION.nupkg
        if [ ! -f "$pkg" ]; then fail "host packages" "no $(basename "$pkg")"; return; fi
        size=$(( $(wc -c < "$pkg") / 1000000 ))
        if [ "$size" -ge 100 ]; then fail "host packages" "$(basename "$pkg") is $size MB, not under 100"; return; fi
        # Listed once: grep -q stops reading early, which pipefail counts as a failure.
        listing=$(zip_list "$pkg" | sed 's/^/ /')
        local x=; [ "$rid" = win-x64 ] && x=.exe
        if ! grep -q " tools/bflat/bflat$x\$" <<< "$listing"; then fail "host packages" "$rid: no tools/bflat/bflat$x"; return; fi
        if ! grep -q " tools/bflat/bin/lld$x\$" <<< "$listing"; then fail "host packages" "$rid: no tools/bflat/bin/lld$x"; return; fi
        # The AGPL package must point to its exact source.
        if ! unzip -p "$pkg" "ZeroCSharp.Bflat.$rid.nuspec" | grep -qE 'commit="[0-9a-f]{40}"'; then
            fail "host packages" "$rid: the nuspec doesn't name the fork's commit"; return
        fi
    done
    pass "host packages ($ZERO_HOSTS)"
}

# ---- The native step (Linux hosts) -----------------------------------------

test_native_hello_runs() {
    local name="native hello runs" dir exe out
    can_build "$name" || return
    dir=$(fixture native)
    if ! run_in "$dir" native build; then fail "$name" "dotnet build failed"; return; fi
    exe=$(exe "$dir/bin/Debug/$TFM/native")
    if [ ! -x "$exe" ]; then fail "$name" "no $exe"; return; fi
    out=$("$exe"); if [ "$out" != hi ]; then fail "$name" "printed '$out'"; return; fi
    if ! is_host_program "$exe"; then fail "$name" "not a program for $HOST_RID"; return; fi
    pass "$name"
}

test_cross_targets() {
    local name="cross targets" dir other other_arch
    can_build "$name" || return
    dir=$(fixture native cross)
    if ! run_in "$dir" cross-win build -p:ZeroTarget=win-x64; then fail "$name" "win-x64 build failed"; return; fi
    if ! is_pe "$dir/bin/Debug/$TFM/win-x64/native.exe"; then fail "$name" "native.exe isn't a Windows program"; return; fi
    if [ "$HOST_ARCH_NAME" = x64 ]; then other=linux-arm64; other_arch=aarch64; else other=linux-x64; other_arch=x86-64; fi
    if ! run_in "$dir" cross-linux build -p:ZeroTarget=$other; then fail "$name" "$other build failed"; return; fi
    if ! is_elf "$dir/bin/Debug/$TFM/$other/native" "$other_arch"; then fail "$name" "not an ELF for $other_arch"; return; fi
    pass "$name"
}

test_bad_target() {
    local name="bad target" dir
    can_build "$name" || return
    dir=$(fixture native badtarget)
    if run_in "$dir" badtarget build -p:ZeroTarget=mac-arm64; then fail "$name" "the build succeeded"; return; fi
    if ! grep -q 'linux-x64, linux-arm64, win-x64' "$LOG"; then fail "$name" "the error doesn't list the targets"; return; fi
    pass "$name"
}

test_path_with_spaces() {
    local name="path with spaces" dir out
    can_build "$name" || return
    dir=$(fixture native "spaces dir/native")
    if ! run_in "$dir" spaces build; then fail "$name" "dotnet build failed"; return; fi
    out=$("$(exe "$dir/bin/Debug/$TFM/native")"); if [ "$out" != hi ]; then fail "$name" "printed '$out'"; return; fi
    pass "$name"
}

test_compile_items() {
    local name="compile items" dir out
    can_build "$name" || return
    dir=$(fixture subfolders)
    if ! run_in "$dir" subfolders build; then fail "$name" "dotnet build failed"; return; fi
    out=$("$(exe "$dir/bin/Debug/$TFM/subfolders")"); if [ "$out" != "from b" ]; then fail "$name" "printed '$out'"; return; fi
    pass "$name"
}

# Whether the last log shows bflat being run.
ran_bflat() { grep -qE 'tools/bflat/bflat(\.exe)?" build' "$LOG"; }

test_incremental() {
    local name="incremental" dir
    can_build "$name" || return
    dir=$(fixture native incremental)
    if ! run_in "$dir" incremental-1 build -v:n || ! ran_bflat; then fail "$name" "the first build didn't run bflat"; return; fi
    if ! run_in "$dir" incremental-2 build -v:n || ran_bflat; then fail "$name" "an unchanged project ran bflat again"; return; fi
    sleep 1; touch "$dir/Program.cs"
    if ! run_in "$dir" incremental-3 build -v:n || ! ran_bflat; then fail "$name" "a changed source didn't run bflat"; return; fi
    pass "$name"
}

test_bflat_failure_fails_the_build() {
    local name="bflat failure fails the build" dir
    can_build "$name" || return
    dir=$(fixture badimport)
    if run_in "$dir" badimport build; then fail "$name" "the build succeeded"; return; fi
    if ! ran_bflat; then fail "$name" "it failed before bflat ran"; return; fi
    # The build fails because bflat did (its command exited with an error), over
    # the import it can't bind.
    if ! grep -qE 'tools/bflat/bflat(\.exe)?" build @.*exited with code' "$LOG"; then fail "$name" "the failing command wasn't bflat"; return; fi
    if ! grep -q 'nosuchfunction' "$LOG"; then fail "$name" "the error doesn't name nosuchfunction"; return; fi
    pass "$name"
}

test_size() {
    local name="size" dir
    can_build "$name" || return
    dir=$(fixture native size)
    if ! run_in "$dir" size build -v:n -p:ZeroOptimizeFor=Size; then fail "$name" "dotnet build failed"; return; fi
    if ! grep -q 'bflat arguments:.* -Os' "$LOG"; then fail "$name" "bflat wasn't given -Os"; return; fi
    pass "$name"
}

test_macos_error() {
    local name="macos error" dir
    if [ "$HOST_RID" != osx-x64 ]; then skip "$name" "Intel Macs only"; return; fi
    dir=$(fixture native macos)
    if run_in "$dir" macos build; then fail "$name" "the build succeeded"; return; fi
    if ! grep -qF "Zero C# builds on Apple Silicon Macs, not Intel ones" "$LOG"; then
        fail "$name" "not the macOS message"; return
    fi
    dir=$(fixture hello macos-library)
    if ! run_in "$dir" macos-library build; then fail "$name" "a library didn't build on macOS"; return; fi
    pass "$name"
}

# ---- Libraries ----------------------------------------------------------------

# app_with_lib <work dir>: the program and the Geometry library (from libs/).
app_with_lib() {
    local dir
    dir=$(fixture app-with-lib "$1")
    cp "$ROOT"/libs/Geometry/*.cs "$dir/Geometry/"
    echo "$dir"
}

test_project_reference() {
    local name="project reference" dir out
    can_build "$name" || return
    dir=$(app_with_lib projref)
    if ! run_in "$dir/App" projref build; then fail "$name" "dotnet build failed"; return; fi
    out=$("$(exe "$dir/App/bin/Debug/$TFM/App")"); if [ "$out" != "Floor area: 37.00" ]; then fail "$name" "printed '$out'"; return; fi
    pass "$name"
}

test_library_change_rebuilds() {
    local name="library change rebuilds" dir out
    can_build "$name" || return
    dir=$(app_with_lib libchange)
    if ! run_in "$dir/App" libchange-1 build; then fail "$name" "the first build failed"; return; fi
    sleep 1
    sed -i.bak 's/double total = 0;/double total = 1;/' "$dir/Geometry/Measure.cs" && rm -f "$dir/Geometry/Measure.cs.bak"
    if ! run_in "$dir/App" libchange-2 build -v:n || ! ran_bflat; then fail "$name" "changing the library didn't run bflat"; return; fi
    out=$("$(exe "$dir/App/bin/Debug/$TFM/App")"); if [ "$out" != "Floor area: 38.00" ]; then fail "$name" "printed '$out'"; return; fi
    pass "$name"
}

# add_source <nuget.config> <name> <dir>: one more package source.
add_source() {
    local path; path=$(native_path "$3" | sed 's|\\|\\\\|g')
    sed -i.bak "s|<clear />|<clear />\n    <add key=\"$2\" value=\"$path\" />|" "$1" && rm -f "$1.bak"
}

test_pack_and_package_reference() {
    local name="pack and package reference" dir app out
    can_build "$name" || return
    dir=$(app_with_lib pack)
    rm -rf "$WORK/libfeed" "${NUGET_PACKAGES:-$HOME/.nuget/packages}/zerotest.geometry"
    if ! run_in "$dir/Geometry" pack-lib pack -o "$WORK/libfeed"; then fail "$name" "dotnet pack failed"; return; fi
    zip_list "$WORK/libfeed/ZeroTest.Geometry.1.0.0.nupkg" | sed 's/^/ /' > "$WORK/pack-list.txt" 2>&1
    if ! grep -q " lib/$TFM/Geometry.dll$" "$WORK/pack-list.txt"; then fail "$name" "the package has no lib/$TFM/Geometry.dll"; return; fi
    app=$(fixture app-with-pkg)/App
    add_source "$app/../nuget.config" libs "$WORK/libfeed"
    if ! run_in "$app" pkgref build; then fail "$name" "the program didn't build"; return; fi
    out=$("$(exe "$app/bin/Debug/$TFM/App")"); if [ "$out" != "Floor area: 37.00" ]; then fail "$name" "printed '$out'"; return; fi
    pass "$name"
}

# A package made for another SDK version (lib/zero01/, 0.1's framework) must be refused.
test_version_mismatch() {
    local name="version mismatch" app pkg=$WORK/oldfeed
    rm -rf "$pkg" "${NUGET_PACKAGES:-$HOME/.nuget/packages}/zerotest.geometry"
    mkdir -p "$pkg/src/lib/zero01"
    cp "$ROOT/sdk/ZeroCSharp.Sdk/payload/ref/BflatLib.dll" "$pkg/src/lib/zero01/Geometry.dll"
    printf '%s\n' '<?xml version="1.0"?>' \
        '<package><metadata><id>ZeroTest.Geometry</id><version>1.0.0</version><authors>test</authors><description>A zero library built for SDK 0.1.</description></metadata></package>' \
        > "$pkg/src/ZeroTest.Geometry.nuspec"
    zip_dir "$pkg/src" "$pkg/ZeroTest.Geometry.1.0.0.nupkg"
    app=$(fixture app-with-pkg oldpkg)/App
    add_source "$app/../nuget.config" old "$pkg"
    if run_in "$app" oldpkg restore; then fail "$name" "restore succeeded"; return; fi
    if ! grep -q "NU1202" "$LOG" || ! grep -q "zero01" "$LOG" || ! grep -q "$TFM" "$LOG"; then
        fail "$name" "no NU1202 naming zero01 and $TFM"; return
    fi
    pass "$name"
}

# ---- Templates ----------------------------------------------------------------

test_templates() {
    local name="templates" dir=$WORK/templates out
    can_build "$name" || return
    rm -rf "$dir" && mkdir -p "$dir"
    fixture hello templates-config > /dev/null    # just for its nuget.config
    cp "$WORK/templates-config/nuget.config" "$dir/"
    dotnet new uninstall ZeroCSharp.Templates > /dev/null 2>&1 || true
    if ! run_in "$dir" templates-install new install "$FEED/ZeroCSharp.Templates.$VERSION.nupkg"; then fail "$name" "install failed"; return; fi
    if ! run_in "$dir" templates-new new zero -o Hello; then fail "$name" "dotnet new zero failed"; return; fi
    if ! run_in "$dir/Hello" templates-build build; then fail "$name" "the program didn't build"; return; fi
    out=$(echo Ann | "$(exe "$dir/Hello/bin/Debug/$TFM/Hello")")
    if [[ "$out" != *"Hello, Ann!"* ]]; then fail "$name" "printed '$out'"; return; fi
    if ! run_in "$dir" templates-newlib new zerolib -o Lib; then fail "$name" "dotnet new zerolib failed"; return; fi
    if ! run_in "$dir/Lib" templates-pack pack -o "$dir/feed"; then fail "$name" "the library didn't pack"; return; fi
    pass "$name"
}

# ---- From the final review -----------------------------------------------------

test_custom_packages_folder() {
    local name="custom packages folder" dir out
    can_build "$name" || return
    dir=$(fixture native pkgfolder)
    rm -rf "$WORK/pkgfolder-packages"
    # No trailing slash, as CI setups often write it.
    if ! (export NUGET_PACKAGES=$(native_path "$WORK/pkgfolder-packages"); run_in "$dir" pkgfolder build); then fail "$name" "dotnet build failed"; return; fi
    out=$("$(exe "$dir/bin/Debug/$TFM/native")"); if [ "$out" != hi ]; then fail "$name" "printed '$out'"; return; fi
    pass "$name"
}

test_dotnet_run() {
    local name="dotnet run" dir
    can_build "$name" || return
    dir=$(fixture native run)
    if ! run_in "$dir" run run; then fail "$name" "dotnet run failed"; return; fi
    if [ "$(tail -1 "$LOG")" != hi ]; then fail "$name" "didn't print hi"; return; fi
    pass "$name"
}

test_publish() {
    local name="publish" dir out
    can_build "$name" || return
    dir=$(fixture native publish)
    if ! run_in "$dir" publish publish -c Release -o "$dir/out"; then fail "$name" "dotnet publish failed"; return; fi
    if [ ! -x "$(exe "$dir/out/native")" ]; then fail "$name" "no native program in the publish folder"; return; fi
    if [ -e "$dir/out/native.dll" ]; then fail "$name" "the IL native.dll was published"; return; fi
    out=$("$(exe "$dir/out/native")"); if [ "$out" != hi ]; then fail "$name" "printed '$out'"; return; fi
    pass "$name"
}

test_extra_dll_imports() {
    local name="extra dll imports" dir
    can_build "$name" || return
    dir=$(fixture native dllimports)
    # libdl.so.2 is a Linux library: build for Linux on every host.
    local target=$HOST_RID; [ "$HOST_OS" = Windows ] && target=linux-x64
    if ! run_in "$dir" dllimports build -v:n "-p:ZeroDllImports=libdl.so.2;" -p:ZeroTarget=$target; then fail "$name" "dotnet build failed"; return; fi
    if ! grep -q 'bflat arguments:.* -i libc .*-i libdl.so.2' "$LOG" && ! grep -q 'bflat arguments:.* -i libdl.so.2 .*-i libc ' "$LOG"; then
        fail "$name" "libc and libdl.so.2 weren't both passed"; return
    fi
    pass "$name"
}

test_inherited_settings() {
    local name="inherited settings" dir out
    can_build "$name" || return
    dir=$(fixture inherited)
    if ! run_in "$dir" inherited build; then fail "$name" "dotnet build failed"; return; fi
    out=$("$(exe "$dir/bin/Debug/$TFM/inherited")"); if [ "$out" != hi ]; then fail "$name" "printed '$out'"; return; fi
    pass "$name"
}

test_clean_and_rebuild() {
    local name="clean and rebuild" dir
    can_build "$name" || return
    dir=$(fixture native clean)
    if ! run_in "$dir" clean-1 build; then fail "$name" "dotnet build failed"; return; fi
    if ! run_in "$dir" clean-2 clean; then fail "$name" "dotnet clean failed"; return; fi
    if [ -e "$(exe "$dir/bin/Debug/$TFM/native")" ]; then fail "$name" "dotnet clean left the native program"; return; fi
    if ! run_in "$dir" clean-3 build -v:n || ! ran_bflat; then fail "$name" "building after clean didn't run bflat"; return; fi
    pass "$name"
}

# ---- 0.1.1 ----------------------------------------------------------------------

# Like dotnet build -r: the default build's program is in bin/Debug/<framework>/,
# a ZeroTarget's in a folder named after it. Building for one keeps the others,
# and dotnet clean removes them all.
test_targets_side_by_side() {
    # The host's own target, run; and another one, checked: Windows from Linux,
    # Linux x64 from Windows.
    local name="targets side by side" dir out host=$HOST_RID other=win-x64 other_file
    can_build "$name" || return
    [ "$HOST_OS" = Windows ] && other=linux-x64
    dir=$(fixture native sidebyside)
    other_file=$dir/bin/Debug/$TFM/$other/native
    [ "$other" = win-x64 ] && other_file=$other_file.exe
    if ! run_in "$dir" sidebyside-1 build; then fail "$name" "the default build failed"; return; fi
    if ! run_in "$dir" sidebyside-2 build -p:ZeroTarget=$other; then fail "$name" "the $other build failed"; return; fi
    if ! run_in "$dir" sidebyside-3 build -p:ZeroTarget=$host; then fail "$name" "the $host build failed"; return; fi
    out=$("$(exe "$dir/bin/Debug/$TFM/native")" 2>/dev/null)
    if [ "$out" != hi ]; then fail "$name" "the default program is gone after building for other targets"; return; fi
    out=$("$(exe "$dir/bin/Debug/$TFM/$host/native")" 2>/dev/null)
    if [ "$out" != hi ]; then fail "$name" "no $host/native"; return; fi
    if [ "$other" = win-x64 ]; then is_pe "$other_file"; else is_elf "$other_file" x86-64; fi \
        || { fail "$name" "no $other program"; return; }
    if ! run_in "$dir" sidebyside-mac build -p:ZeroTarget=osx-arm64; then fail "$name" "the osx-arm64 build failed"; return; fi
    if ! is_macho_arm64 "$dir/bin/Debug/$TFM/osx-arm64/native"; then fail "$name" "no osx-arm64 program"; return; fi
    out=$("$(exe "$dir/bin/Debug/$TFM/$host/native")" 2>/dev/null)
    if [ "$out" != hi ]; then fail "$name" "$host/native is gone after building for osx-arm64"; return; fi
    if ! run_in "$dir" sidebyside-4 clean; then fail "$name" "dotnet clean failed"; return; fi
    if [ -e "$(exe "$dir/bin/Debug/$TFM/native")" ] || [ -e "$(exe "$dir/bin/Debug/$TFM/$host/native")" ] || [ -e "$other_file" ] ||
       [ -e "$dir/bin/Debug/$TFM/osx-arm64/native" ]; then
        fail "$name" "dotnet clean left a native program"; return
    fi
    pass "$name"
}

# bflat compiles with the project's language version and compiler features,
# not fixed ones.
test_language_settings_forwarded() {
    local name="language settings forwarded" dir
    can_build "$name" || return
    dir=$(fixture native langver)
    if ! run_in "$dir" langver build -v:n -p:LangVersion=14 "-p:Features=strict"; then fail "$name" "dotnet build failed"; return; fi
    if ! grep -q 'bflat arguments:.* --langversion 14 ' "$LOG"; then fail "$name" "bflat wasn't given --langversion 14"; return; fi
    if ! grep -q 'bflat arguments:.* --compiler-feature strict ' "$LOG"; then fail "$name" "bflat wasn't given --compiler-feature strict"; return; fi
    if grep -q 'bflat arguments:.* --langversion preview' "$LOG"; then fail "$name" "bflat was still given --langversion preview"; return; fi
    pass "$name"
}

test_target_framework_is_an_error() {
    local name="target framework is an error" dir
    dir=$(fixture tfm)
    if run_in "$dir" tfm build; then fail "$name" "the build succeeded"; return; fi
    if ! grep -q "ZERO001" "$LOG" || ! grep -q "TargetFramework" "$LOG"; then fail "$name" "no ZERO001 error naming TargetFramework"; return; fi
    pass "$name"
}

# bflat already executable in a read-only packages folder (a shared or mounted
# cache): the build mustn't try to change it. As root, permissions don't stop
# chmod, so this can only fail when run as another user (as CI's runners are).
test_read_only_packages_folder() {
    local name="read-only packages folder" dir pkgs=$WORK/ro-packages out
    linux_only "$name" || return
    dir=$(fixture native readonly)
    chmod -R u+w "$pkgs" 2>/dev/null; rm -rf "$pkgs"
    if ! (export NUGET_PACKAGES=$pkgs; run_in "$dir" readonly-1 build); then fail "$name" "the first build failed"; return; fi
    chmod -R a-w "$pkgs"/zerocsharp.bflat.*
    sleep 1; touch "$dir/Program.cs"
    (export NUGET_PACKAGES=$pkgs; run_in "$dir" readonly-2 build -v:n)
    local rc=$?
    LOG=$WORK/readonly-2.log # run_in set it in the subshell
    chmod -R u+w "$pkgs"
    if [ $rc -ne 0 ]; then fail "$name" "building with a read-only packages folder failed"; return; fi
    if ! ran_bflat; then fail "$name" "the second build didn't run bflat"; return; fi
    out=$("$(exe "$dir/bin/Debug/$TFM/native")"); if [ "$out" != hi ]; then fail "$name" "printed '$out'"; return; fi
    pass "$name"
}

# The release workflow publishes only when the tag names the version packed.
test_release_tag_check() {
    local name="release tag check"
    LOG=$WORK/tagcheck.log
    # BFLAT_REF named: CI's environment sets it (sdk.yml).
    if ! BFLAT_REF=master sdk/check-tag.sh "sdk-v$VERSION" > "$LOG" 2>&1; then fail "$name" "sdk-v$VERSION was refused"; return; fi
    if BFLAT_REF=master sdk/check-tag.sh "sdk-v0.0.9" > "$LOG" 2>&1; then fail "$name" "sdk-v0.0.9 was accepted for $VERSION"; return; fi
    if ! grep -q "$VERSION" "$LOG"; then fail "$name" "the error doesn't name $VERSION"; return; fi
    # A release carries bflat from the fork's master, never a branch build.
    if BFLAT_REF=windows-lld sdk/check-tag.sh "sdk-v$VERSION" > "$LOG" 2>&1; then fail "$name" "a release from the fork branch windows-lld was accepted"; return; fi
    if ! grep -q "BFLAT_REF" "$LOG"; then fail "$name" "the error doesn't name BFLAT_REF"; return; fi
    if ! BFLAT_REF=master sdk/check-tag.sh "sdk-v$VERSION" > "$LOG" 2>&1; then fail "$name" "BFLAT_REF=master was refused"; return; fi
    pass "$name"
}

# ./build.sh --sdk-payload replaces its directory: only an empty one or an
# earlier payload, never a folder of something else.
test_payload_folder_guard() {
    local name="payload folder guard" dir=$WORK/notapayload
    rm -rf "$dir" && mkdir -p "$dir" && echo keep > "$dir/keep.txt"
    LOG=$WORK/payloadguard.log
    if ./build.sh --sdk-payload "${dir#$ROOT/}" > "$LOG" 2>&1; then fail "$name" "build.sh accepted the folder"; return; fi
    if [ ! -f "$dir/keep.txt" ]; then fail "$name" "the folder's files were removed"; return; fi
    if ! grep -q "isn't an SDK payload" "$LOG"; then fail "$name" "not the guard's message"; return; fi
    pass "$name"
}

# ZeroTarget from Directory.Build.targets, imported after the project: still a
# chosen target, with a folder of its own.
test_target_from_directory_targets() {
    # A target other than the host's: Windows from Linux, Linux x64 from Windows.
    local name="target from Directory.Build.targets" dir target=win-x64 program
    can_build "$name" || return
    [ "$HOST_OS" = Windows ] && target=linux-x64
    dir=$(fixture dirtargets)
    sed -i.bak "s|<ZeroTarget>win-x64</ZeroTarget>|<ZeroTarget>$target</ZeroTarget>|" "$dir/Directory.Build.targets" && rm -f "$dir/Directory.Build.targets.bak"
    if ! run_in "$dir" dirtargets build; then fail "$name" "dotnet build failed"; return; fi
    program=$dir/bin/Debug/$TFM/$target/dirtargets
    if [ "$target" = win-x64 ]; then is_pe "$program.exe"; else is_elf "$program" x86-64; fi \
        || { fail "$name" "no $target program in $target/"; return; }
    pass "$name"
}

# A project built with 0.1.0 (on nuget.org), then with this version: the
# program the new build makes must still be there afterwards.
test_upgrade_from_0_1_0() {
    local name="upgrade from 0.1.0" dir out
    linux_only "$name" || return
    dir=$(fixture native upgrade)
    sed -i.bak "s|ZeroCSharp.Sdk/$VERSION|ZeroCSharp.Sdk/0.1.0|" "$dir/native.csproj" && rm -f "$dir/native.csproj.bak"
    if ! run_in "$dir" upgrade-1 build; then fail "$name" "the 0.1.0 build failed"; return; fi
    sed -i.bak "s|ZeroCSharp.Sdk/0.1.0|ZeroCSharp.Sdk/$VERSION|" "$dir/native.csproj" && rm -f "$dir/native.csproj.bak"
    if ! run_in "$dir" upgrade-2 build; then fail "$name" "the $VERSION build failed"; return; fi
    out=$("$dir/bin/Debug/$TFM/native" 2>/dev/null); if [ "$out" != hi ]; then fail "$name" "the program is gone after the $VERSION build"; return; fi
    pass "$name"
}

# ---- 0.2 ------------------------------------------------------------------------

# 40 sources in a folder with a long name: their full paths alone pass cmd.exe's
# 8191-character limit, so bflat must get its arguments from a response file.
test_long_command_line() {
    local name="long command line" dir sub i main
    can_build "$name" || return
    dir=$(fixture native manyfiles)
    rm -f "$dir/Program.cs"
    sub="$dir/$(printf 'a%.0s' $(seq 1 120))"
    mkdir -p "$sub"
    main='public static class Program { public static int Main() { long n = 0;'
    # 80 files of ~160 characters each: past 8191 even on a short checkout path.
    for i in $(seq 1 80); do
        echo "public static class Part$i { public static long One() => 1; }" > "$sub/Part$i.cs"
        main="$main n += Part$i.One();"
    done
    echo "$main Out.Write(n); Out.Line(); return 0; } }" > "$dir/Program.cs"
    if ! run_in "$dir" manyfiles build -v:n; then fail "$name" "dotnet build failed"; return; fi
    if ! grep -qE 'tools/bflat/bflat(\.exe)?" build @' "$LOG"; then fail "$name" "bflat didn't get a response file"; return; fi
    # Past cmd.exe's 8191-character limit, or the test proves nothing there.
    local len; len=$(wc -c < "$dir/obj/Debug/$TFM/zero-native.args" | tr -d ' ')
    if [ "$len" -le 8191 ]; then fail "$name" "the arguments are only $len characters"; return; fi
    local out; out=$("$(exe "$dir/bin/Debug/$TFM/native")" | tr -d '\r')
    if [ "$out" != 80 ]; then fail "$name" "printed '$out'"; return; fi
    pass "$name"
}

# From Windows, both Linux targets: ELF programs for their architectures.
test_windows_builds_linux() {
    local name="windows builds linux" dir
    if [ "$HOST_OS" != Windows ]; then skip "$name" "Windows only"; return; fi
    dir=$(fixture native winlinux)
    if ! run_in "$dir" winlinux-x64 build -p:ZeroTarget=linux-x64; then fail "$name" "the linux-x64 build failed"; return; fi
    if ! run_in "$dir" winlinux-arm64 build -p:ZeroTarget=linux-arm64; then fail "$name" "the linux-arm64 build failed"; return; fi
    if ! is_elf "$dir/bin/Debug/$TFM/linux-x64/native" x86-64; then fail "$name" "linux-x64/native isn't an x86-64 ELF"; return; fi
    if ! is_elf "$dir/bin/Debug/$TFM/linux-arm64/native" aarch64; then fail "$name" "linux-arm64/native isn't an aarch64 ELF"; return; fi
    # Kept for CI, which runs them on Linux (sdk.yml, windows-built-linux).
    mkdir -p "$WORK/windows-built/linux-x64" "$WORK/windows-built/linux-arm64"
    cp "$dir/bin/Debug/$TFM/linux-x64/native" "$WORK/windows-built/linux-x64/"
    cp "$dir/bin/Debug/$TFM/linux-arm64/native" "$WORK/windows-built/linux-arm64/"
    pass "$name"
}

# A packages folder with a space and a non-ASCII letter, as a Windows user
# profile can have (C:\Users\Zoë Ann\.nuget\packages).
test_unusual_packages_folder() {
    local name="unusual packages folder" dir out pkgs="$WORK/pkgs Zoë Ann"
    can_build "$name" || return
    dir=$(fixture native unusualpkgs)
    rm -rf "$pkgs"
    if ! (export NUGET_PACKAGES=$(native_path "$pkgs"); run_in "$dir" unusualpkgs build); then
        LOG=$WORK/unusualpkgs.log; fail "$name" "dotnet build failed"; return
    fi
    out=$("$(exe "$dir/bin/Debug/$TFM/native")"); if [ "$out" != hi ]; then fail "$name" "printed '$out'"; return; fi
    pass "$name"
}

# Lines piped from PowerShell or cmd end in \r\n: the program reads the name
# without the \r (the template's program, built for the host).
test_crlf_input() {
    local name="crlf input" dir=$WORK/crlf out
    can_build "$name" || return
    rm -rf "$dir" && mkdir -p "$dir"
    fixture hello crlf-config > /dev/null
    cp "$WORK/crlf-config/nuget.config" "$dir/"
    dotnet new uninstall ZeroCSharp.Templates > /dev/null 2>&1 || true
    if ! run_in "$dir" crlf-install new install "$FEED/ZeroCSharp.Templates.$VERSION.nupkg"; then fail "$name" "install failed"; return; fi
    if ! run_in "$dir" crlf-new new zero -o Hello; then fail "$name" "dotnet new zero failed"; return; fi
    if ! run_in "$dir/Hello" crlf-build build; then fail "$name" "the program didn't build"; return; fi
    out=$(printf 'Ann\r\n' | "$(exe "$dir/Hello/bin/Debug/$TFM/Hello")" | od -An -c | tr -s ' ')
    if [[ "$out" != *"A n n !"* ]]; then fail "$name" "printed:$out"; return; fi
    pass "$name"
}

# Build output in a folder with a space between the project and it (artifacts
# at the repository's root, in "art out"): bflat must hand the linker the
# runtime objects' paths whole. Same for the packages folder (above).
test_artifacts_path_with_spaces() {
    local name="artifacts path with spaces" dir program out
    can_build "$name" || return
    dir=$(fixture native artspace/native)
    rm -rf "$WORK/artspace/art out"
    if ! run_in "$dir" artifacts build -p:UseArtifactsOutput=true "-p:ArtifactsPath=$(native_path "$WORK/artspace/art out")"; then fail "$name" "dotnet build failed"; return; fi
    program=$(find "$WORK/artspace/art out/bin" -type f -name "$(basename "$(exe native)")" 2>/dev/null | head -1)
    if [ -z "$program" ]; then fail "$name" "no program under art out/bin"; return; fi
    out=$("$program"); if [ "$out" != hi ]; then fail "$name" "printed '$out'"; return; fi
    pass "$name"
}

# Static constructors reached from eight threads at once: each thread sees the
# type set up, the constructor runs once, re-entry and a two-thread cycle work.
# 20 runs, since a race shows up only sometimes.
test_static_constructor_threads() {
    local name="static constructor threads" dir out i status expected
    can_build "$name" || return
    dir=$(fixture cctor-threads)
    if ! run_in "$dir" cctor-threads build; then fail "$name" "dotnet build failed"; return; fi
    expected=$'values 7 7 7 7 7 7 7 7\nruns 1\nself ok\ncycle ok'
    for i in $(seq 1 20); do
        run_limited 30 "$WORK/cctor-threads.out" "$(exe "$dir/bin/Debug/$TFM/cctor-threads")"
        status=$?
        out=$(tr -d '\r' < "$WORK/cctor-threads.out")
        if [ "$status" -ne 0 ]; then fail "$name" "run $i: exit $status: $out"; return; fi
        if [ "$out" != "$expected" ]; then fail "$name" "run $i: $out"; return; fi
    done
    pass "$name"
}

# ---- Native libraries (NativeLib=Shared) --------------------------------------

# call_native <call-native-lib.sh args>: the .NET caller (a fresh copy of its
# fixture, with the test feed's nuget.config) on a library; output to a log.
call_native() {
    local caller
    caller=$(fixture nativelib-caller)
    LOG=$WORK/call-native.log
    ZERO_CALLER_DIR=$caller "$ROOT/sdk/test/call-native-lib.sh" "$@" > "$LOG" 2>&1
}

# build_nativelib <dir> <log> [dotnet build args]: builds a copy of
# fixtures/nativelib; NATIVELIB is then the host target's library.
build_nativelib() {
    local dir=$1 log=$2
    shift 2
    NATIVELIB=$dir/bin/Debug/$TFM/native/nativelib$HOST_LIB_EXT
    run_in "$dir" "$log" build "$@"
}

test_native_library_dotnet() {
    local name="native library dotnet" dir
    can_build "$name" || return
    dir=$(fixture nativelib)
    if ! build_nativelib "$dir" nativelib; then fail "$name" "dotnet build failed"; return; fi
    if [ ! -f "$NATIVELIB" ]; then fail "$name" "no native/nativelib$HOST_LIB_EXT"; return; fi
    if ! call_native "$NATIVELIB"; then fail "$name" "the .NET caller didn't get what it should"; return; fi
    pass "$name"
}

# Exactly the [UnmanagedCallersOnly(EntryPoint)] methods are exported (on
# Windows the caller's "hidden ok" line checks zerocore's symbols instead).
test_native_library_exports() {
    local name="native library exports" dir exported expected
    can_build "$name" || return
    dir=$(fixture nativelib nativelib-exports)
    if ! build_nativelib "$dir" nativelib-exports; then fail "$name" "dotnet build failed"; return; fi
    expected=$(printf '%s\n' nl_add nl_cctor_value nl_mul_long nl_negate nl_offset nl_remember nl_scale nl_slow_runs nl_slow_value nl_sum)
    case "$HOST_OS" in
        Linux)  exported=$(nm -D --defined-only "$NATIVELIB" | awk '{print $3}' | sed 's/@.*//' | sort) ;;
        Darwin) exported=$(nm -gU "$NATIVELIB" | awk '{print $3}' | sed 's/^_//' | sort) ;;
        *)      skip "$name" "checked by the caller's hidden line on Windows"; return ;;
    esac
    if [ "$exported" != "$expected" ]; then fail "$name" "exports: $(echo $exported)"; return; fi
    pass "$name"
}

# Two copies of one library in a process keep separate state.
test_native_library_two() {
    local name="native library two" dir
    can_build "$name" || return
    dir=$(fixture nativelib nativelib-two)
    if ! build_nativelib "$dir" nativelib-two; then fail "$name" "dotnet build failed"; return; fi
    mkdir -p "$WORK/two"
    cp "$NATIVELIB" "$WORK/two/a$HOST_LIB_EXT"
    cp "$NATIVELIB" "$WORK/two/b$HOST_LIB_EXT"
    if ! call_native two "$WORK/two/a$HOST_LIB_EXT" "$WORK/two/b$HOST_LIB_EXT"; then fail "$name" "the copies share state"; return; fi
    pass "$name"
}

test_native_library_errors() {
    local name="native library errors" dir
    dir=$(fixture native nativelib-exe)
    if run_in "$dir" nativelib-exe build -p:NativeLib=Shared || ! grep -q "set OutputType to Library" "$LOG"; then
        fail "$name" "NativeLib with an Exe wasn't refused"; return
    fi
    dir=$(fixture nativelib nativelib-static)
    if run_in "$dir" nativelib-static build -p:NativeLib=Static || ! grep -q "NativeLib 'Static' isn't supported" "$LOG"; then
        fail "$name" "NativeLib=Static wasn't refused"; return
    fi
    dir=$(fixture nativelib nativelib-wasm)
    if run_in "$dir" nativelib-wasm build -p:ZeroTarget=wasm || ! grep -q "ZeroTarget 'wasm' has no shared libraries" "$LOG"; then
        fail "$name" "a wasm library wasn't refused"; return
    fi
    dir=$(fixture native nativelib-ref/App)
    fixture nativelib nativelib-ref/nativelib > /dev/null
    sed -i.bak 's|</Project>|  <ItemGroup><ProjectReference Include="../nativelib/nativelib.csproj" /></ItemGroup>\n</Project>|' "$dir/native.csproj" && rm -f "$dir/native.csproj.bak"
    if run_in "$dir" nativelib-ref build || ! grep -q "references a native library (NativeLib=Shared): nativelib" "$LOG"; then
        fail "$name" "a ProjectReference to a native library wasn't refused"; return
    fi
    dir=$(fixture nativelib nativelib-run)
    if run_in "$dir" nativelib-run run || ! grep -qi "library" "$LOG"; then
        fail "$name" "dotnet run of a native library didn't fail saying it's a library"; return
    fi
    pass "$name"
}

test_native_library_not_packable() {
    local name="native library not packable" dir
    dir=$(fixture nativelib nativelib-pack)
    run_in "$dir" nativelib-pack pack
    if [ -n "$(find "$dir" -name '*.nupkg')" ]; then fail "$name" "dotnet pack made a package"; return; fi
    pass "$name"
}

test_native_library_publish() {
    local name="native library publish" dir
    can_build "$name" || return
    dir=$(fixture nativelib nativelib-publish)
    if ! run_in "$dir" nativelib-publish publish -c Release -o "$dir/out"; then fail "$name" "dotnet publish failed"; return; fi
    if [ ! -f "$dir/out/nativelib$HOST_LIB_EXT" ]; then fail "$name" "no nativelib$HOST_LIB_EXT in the publish folder"; return; fi
    if ! call_native "$dir/out/nativelib$HOST_LIB_EXT"; then fail "$name" "the published library doesn't work"; return; fi
    pass "$name"
}

test_native_library_incremental() {
    local name="native library incremental" dir
    can_build "$name" || return
    dir=$(fixture nativelib nativelib-incremental)
    if ! run_in "$dir" nativelib-incremental-1 build -v:n || ! ran_bflat; then fail "$name" "the first build didn't run bflat"; return; fi
    if ! run_in "$dir" nativelib-incremental-2 build -v:n || ran_bflat; then fail "$name" "an unchanged library ran bflat again"; return; fi
    sleep 1; touch "$dir/Exports.cs"
    if ! run_in "$dir" nativelib-incremental-3 build -v:n || ! ran_bflat; then fail "$name" "a changed export didn't run bflat"; return; fi
    pass "$name"
}

test_native_library_clean() {
    local name="native library clean" dir
    can_build "$name" || return
    dir=$(fixture nativelib nativelib-clean)
    if ! build_nativelib "$dir" nativelib-clean || [ ! -f "$NATIVELIB" ]; then fail "$name" "dotnet build failed"; return; fi
    if ! run_in "$dir" nativelib-clean-2 clean || [ -e "$NATIVELIB" ]; then fail "$name" "dotnet clean left the library"; return; fi
    pass "$name"
}

test_native_library_path_with_spaces() {
    local name="native library path with spaces" dir
    can_build "$name" || return
    dir=$(fixture nativelib "with spaces/native lib")
    if ! build_nativelib "$dir" nativelib-spaces; then fail "$name" "dotnet build failed"; return; fi
    if ! call_native "$NATIVELIB"; then fail "$name" "the .NET caller didn't get what it should"; return; fi
    pass "$name"
}

# A native library that uses a zero library (a ProjectReference) compiles it in.
test_native_library_with_zero_reference() {
    local name="native library with zero reference" dir
    can_build "$name" || return
    app_with_lib nativelib-zeroref > /dev/null
    dir=$(fixture nativelib nativelib-zeroref/nativelib)
    sed -i.bak 's|</Project>|  <ItemGroup><ProjectReference Include="../Geometry/Geometry.csproj" /></ItemGroup>\n</Project>|' "$dir/nativelib.csproj" && rm -f "$dir/nativelib.csproj.bak"
    cat > "$dir/Area.cs" <<'CS'
using System.Runtime.InteropServices;

public static class AreaExport
{
    [UnmanagedCallersOnly(EntryPoint = "nl_rect_area")]
    public static double RectArea(double width, double height) => new Geometry.Rect(width, height).Area;
}
CS
    if ! build_nativelib "$dir" nativelib-zeroref; then fail "$name" "dotnet build failed"; return; fi
    if ! call_native area "$NATIVELIB"; then fail "$name" "nl_rect_area didn't return 6"; return; fi
    pass "$name"
}

test_native_library_dotted_name() {
    local name="native library dotted name" dir lib
    can_build "$name" || return
    dir=$(fixture nativelib nativelib-dotted)
    if ! run_in "$dir" nativelib-dotted build -p:AssemblyName=My.Native; then fail "$name" "dotnet build failed"; return; fi
    lib=$dir/bin/Debug/$TFM/native/My.Native$HOST_LIB_EXT
    if [ ! -f "$lib" ]; then fail "$name" "no native/My.Native$HOST_LIB_EXT"; return; fi
    if ! call_native "$lib"; then fail "$name" "the .NET caller didn't get what it should"; return; fi
    pass "$name"
}

# c_compiler: a C compiler for the host's own target, if there is one.
c_compiler() {
    if [ -n "${CC:-}" ]; then echo "$CC"
    elif command -v cc > /dev/null; then echo cc
    elif command -v clang > /dev/null; then echo clang
    elif command -v gcc > /dev/null; then echo gcc
    else return 1; fi
}

# A C program loads the library (dlopen, or LoadLibrary on Windows) and prints
# the first eight lines the .NET caller prints.
test_native_library_c() {
    local name="native library c" dir cc caller out ldl=
    can_build "$name" || return
    if ! cc=$(c_compiler); then skip "$name" "no C compiler"; return; fi
    dir=$(fixture nativelib nativelib-c)
    if ! build_nativelib "$dir" nativelib-c; then fail "$name" "dotnet build failed"; return; fi
    [ "$HOST_OS" = Linux ] && ldl=-ldl
    caller=$(exe "$WORK/nativelib-c-caller")
    LOG=$WORK/nativelib-c-cc.log
    if ! "$cc" "$(native_path "$ROOT/sdk/test/fixtures/nativelib-c/main.c")" -o "$(native_path "$caller")" $ldl > "$LOG" 2>&1; then
        fail "$name" "the C caller didn't compile"; return
    fi
    out=$("$caller" "$(native_path "$NATIVELIB")" 2>&1 | tr -d '\r')
    if [ "$out" != "$(head -8 "$ROOT/sdk/test/fixtures/nativelib-caller/expected.txt" | tr -d '\r')" ]; then fail "$name" "printed: $(echo $out)"; return; fi
    pass "$name"
}

# A C program linked against the library (the soname / install name, and an
# rpath to its folder).
test_native_library_c_linked() {
    local name="native library c linked" dir cc caller out
    can_build "$name" || return
    if [ "$HOST_OS" = Windows ]; then skip "$name" "Linux and macOS"; return; fi
    if ! cc=$(c_compiler); then skip "$name" "no C compiler"; return; fi
    dir=$(fixture nativelib nativelib-c-linked)
    if ! build_nativelib "$dir" nativelib-c-linked; then fail "$name" "dotnet build failed"; return; fi
    caller=$WORK/nativelib-c-linked-caller
    LOG=$WORK/nativelib-c-linked-cc.log
    if ! "$cc" -DLINKED "$ROOT/sdk/test/fixtures/nativelib-c/main.c" "$NATIVELIB" -Wl,-rpath,"$(dirname "$NATIVELIB")" -o "$caller" > "$LOG" 2>&1; then
        fail "$name" "the C caller didn't link"; return
    fi
    out=$("$caller" 2>&1)
    if [ "$out" != "$(head -8 "$ROOT/sdk/test/fixtures/nativelib-caller/expected.txt" | tr -d '\r')" ]; then fail "$name" "printed: $(echo $out)"; return; fi
    pass "$name"
}

# A zero program loads a zero native library with dlopen: each keeps its own
# zerocore and statics.
test_native_library_with_zero_exe() {
    local name="native library with zero exe" dir lib out
    can_build "$name" || return
    if [ "$HOST_OS" = Windows ]; then skip "$name" "zero programs import at link time; dlopen is Linux and macOS"; return; fi
    lib=$(fixture nativelib nativelib-zeroexe-lib)
    if ! build_nativelib "$lib" nativelib-zeroexe-lib; then fail "$name" "the library's dotnet build failed"; return; fi
    dir=$(fixture nativelib-zero-caller)
    if ! run_in "$dir" nativelib-zero-caller build; then fail "$name" "the program's dotnet build failed"; return; fi
    out=$("$dir/bin/Debug/$TFM/nativelib-zero-caller" "$NATIVELIB" 2>&1)
    if [ "$out" != $'add 5\nremember 3\nown 1' ]; then fail "$name" "printed: $(echo $out)"; return; fi
    pass "$name"
}

# Every host builds the library for all four targets. CI runs the results on
# their own systems with sdk/test/call-native-lib.sh (sdk.yml: mac-programs,
# windows-built-linux, macos-built), from the folders these are copied to.
test_native_library_cross() {
    local name="native library cross" dir t f
    can_build "$name" || return
    dir=$(fixture nativelib nativelib-cross)
    for t in linux-x64 linux-arm64 win-x64 osx-arm64; do
        if ! run_in "$dir" "nativelib-cross-$t" build -p:ZeroTarget=$t; then fail "$name" "the $t build failed"; return; fi
        case "$t" in
            linux-*)   f=$dir/bin/Debug/$TFM/$t/native/nativelib.so; is_elf_shared "$f" ;;
            win-x64)   f=$dir/bin/Debug/$TFM/$t/native/nativelib.dll; is_pe_dll "$f" ;;
            osx-arm64) f=$dir/bin/Debug/$TFM/$t/native/nativelib.dylib; is_macho_dylib "$f" ;;
        esac || { fail "$name" "$t's library isn't a $t shared library"; return; }
        case "$HOST_OS:$t" in
            Windows:linux-*)  mkdir -p "$WORK/windows-built/$t" && cp "$f" "$WORK/windows-built/$t/" ;;
            Darwin:linux-* | Darwin:win-x64) mkdir -p "$WORK/macos-built/$t" && cp "$f" "$WORK/macos-built/$t/" ;;
            *:osx-arm64)      [ "$HOST_OS" != Darwin ] && mkdir -p "$WORK/mac-built/$HOST_RID" && cp "$f" "$WORK/mac-built/$HOST_RID/" ;;
        esac
    done
    pass "$name"
}

# A name with a space: the output, the soname (Linux) and the install name
# (macOS) carry it whole.
test_native_library_name_with_spaces() {
    local name="native library name with spaces" dir lib
    can_build "$name" || return
    dir=$(fixture nativelib nativelib-spacename)
    if ! run_in "$dir" nativelib-spacename build "-p:AssemblyName=My Lib"; then fail "$name" "dotnet build failed"; return; fi
    lib="$dir/bin/Debug/$TFM/native/My Lib$HOST_LIB_EXT"
    if [ ! -f "$lib" ]; then fail "$name" "no native/My Lib$HOST_LIB_EXT"; return; fi
    if ! call_native "$lib"; then fail "$name" "the .NET caller didn't get what it should"; return; fi
    pass "$name"
}

# References the native-library check must let through: one only for build
# order (ReferenceOutputAssembly=false), and one to a project that doesn't
# exist (MSBuild warns about that one; the check mustn't make it an error).
test_native_library_reference_kinds() {
    local name="native library reference kinds" dir
    can_build "$name" || return
    dir=$(fixture native nativelib-order/App)
    fixture nativelib nativelib-order/nativelib > /dev/null
    sed -i.bak 's|</Project>|  <ItemGroup><ProjectReference Include="../nativelib/nativelib.csproj" ReferenceOutputAssembly="false" /></ItemGroup>\n</Project>|' "$dir/native.csproj" && rm -f "$dir/native.csproj.bak"
    if ! run_in "$dir" nativelib-order build; then fail "$name" "a build-order reference to a native library was refused"; return; fi
    if [ ! -f "$WORK/nativelib-order/nativelib/bin/Debug/$TFM/native/nativelib$HOST_LIB_EXT" ]; then fail "$name" "the build-order reference didn't build the library"; return; fi
    dir=$(fixture native nativelib-missing)
    sed -i.bak 's|</Project>|  <ItemGroup><ProjectReference Include="../nowhere/nowhere.csproj" /></ItemGroup>\n</Project>|' "$dir/native.csproj" && rm -f "$dir/native.csproj.bak"
    if run_in "$dir" nativelib-missing build; then :; fi
    if grep -q "MSB3202" "$LOG"; then fail "$name" "a missing project reference became an error (MSB3202)"; return; fi
    pass "$name"
}

# The docs' recipe: an ordinary .NET program that builds the library first
# (a build-order ProjectReference) and copies it next to itself, from a clean
# folder; .NET then finds it by name ([DllImport("MathLib")]).
test_native_library_app_recipe() {
    local name="native library app recipe" dir out
    can_build "$name" || return
    dir=$(fixture nativelib-app)
    fixture nativelib nativelib-app/MathLib > /dev/null
    mv "$dir/MathLib/nativelib.csproj" "$dir/MathLib/MathLib.csproj"
    if ! run_in "$dir/App" nativelib-app build; then fail "$name" "dotnet build failed"; return; fi
    out=$(dotnet "$dir/App/bin/Debug/net10.0/App.dll" 2>&1 | tr -d '\r')
    if [ "$out" != "add 5" ]; then fail "$name" "printed: $out"; return; fi
    pass "$name"
}

ALL_TESTS=(host_packages payload_complete hello_compiles console_is_an_error unions_compile plain_packages_refused
    native_hello_runs cross_targets bad_target path_with_spaces compile_items incremental
    bflat_failure_fails_the_build size macos_error
    project_reference library_change_rebuilds pack_and_package_reference version_mismatch templates
    custom_packages_folder dotnet_run publish extra_dll_imports inherited_settings clean_and_rebuild
    targets_side_by_side language_settings_forwarded target_framework_is_an_error read_only_packages_folder
    release_tag_check payload_folder_guard target_from_directory_targets upgrade_from_0_1_0
    long_command_line windows_builds_linux unusual_packages_folder crlf_input
    artifacts_path_with_spaces trim_windows_layout trim_stamp llama_template
    framework_from_property newer_sdk_library
    mac_target mac_target_links_libm_and_threads mac_target_path_with_spaces mac_target_undefined_import
    trim_macos_layout mac_host_builds_all_targets mac_target_common_libc mac_target_user_tbd
    tutorial_steps simd_instruction_set static_constructor_threads
    native_library_dotnet native_library_exports native_library_two native_library_errors
    native_library_not_packable native_library_publish native_library_incremental native_library_clean
    native_library_path_with_spaces native_library_with_zero_reference native_library_dotted_name
    native_library_c native_library_c_linked native_library_with_zero_exe native_library_cross
    native_library_name_with_spaces native_library_reference_kinds native_library_app_recipe)

# sdk/trim-windows-layout.sh with a stub objdump: a DLL that lld.exe imports
# is found whatever its capitals, and an objdump failure stops the trim
# instead of leaving the DLLs out.
test_trim_windows_layout() {
    local name="trim windows layout" d=$WORK/trimwin
    rm -rf "$d" && mkdir -p "$d/linux/bin" "$d/linux/lib" "$d/win/bin" "$d/win/lib" "$d/stub"
    touch "$d/linux/bflat" "$d/linux/bin/lld" "$d/linux/libfoo.so" "$d/linux/lib/zero.txt"
    touch "$d/win/bflat.exe" "$d/win/bin/lld.exe" "$d/win/foo.dll" "$d/win/lib/zero.txt" "$d/win/bin/Zlib1.dll"
    cat > "$d/stub/objdump" <<'STUB'
#!/usr/bin/env bash
[ -n "${STUB_FAIL:-}" ] && { echo "objdump: file format not recognized" >&2; exit 1; }
case "$2" in *lld.exe) printf '\tDLL Name: ZLIB1.DLL\r\n\tDLL Name: KERNEL32.dll\r\n' ;; esac
STUB
    chmod +x "$d/stub/objdump"
    LOG=$d/trim.log
    if ! PATH="$d/stub:$PATH" sdk/trim-windows-layout.sh "$d/win" "$d/linux" "$d/out" > "$LOG" 2>&1; then fail "$name" "the trim failed"; return; fi
    if [ -z "$(find "$d/out/bin" -name Zlib1.dll)" ]; then fail "$name" "bin/Zlib1.dll (imported as ZLIB1.DLL) is missing"; return; fi
    if [ ! -f "$d/out/foo.dll" ] || [ ! -f "$d/out/lib/zero.txt" ]; then fail "$name" "the mapped files are missing"; return; fi
    if STUB_FAIL=1 PATH="$d/stub:$PATH" sdk/trim-windows-layout.sh "$d/win" "$d/linux" "$d/out" > "$LOG" 2>&1; then fail "$name" "an objdump failure was ignored"; return; fi
    if ! grep -q "objdump" "$LOG"; then fail "$name" "the error doesn't name objdump"; return; fi
    pass "$name"
}

# sdk/trim-host.sh redoes a trimmed layout when any file it copied changes in
# the full layout, and names what's missing instead of failing on a cd.
test_trim_stamp() {
    local name="trim stamp" d=$WORK/trimstamp
    rm -rf "$d" && mkdir -p "$d/layouts/windows-x64/bin" "$d/layouts/windows-x64/lib" "$d/trimmed/linux-x64/bin" "$d/trimmed/linux-x64/lib" "$d/stub"
    touch "$d/trimmed/linux-x64/bflat" "$d/trimmed/linux-x64/bin/lld" "$d/trimmed/linux-x64/lib/zero.txt"
    touch "$d/layouts/windows-x64/bflat.exe" "$d/layouts/windows-x64/bin/lld.exe"
    echo one > "$d/layouts/windows-x64/lib/zero.txt"
    printf '#!/usr/bin/env bash\ntrue\n' > "$d/stub/objdump" && chmod +x "$d/stub/objdump"
    LOG=$d/trim.log
    if ! PATH="$d/stub:$PATH" sdk/trim-host.sh win-x64 "$d/layouts" "$d/trimmed" > "$LOG" 2>&1; then fail "$name" "the first trim failed"; return; fi
    echo two > "$d/layouts/windows-x64/lib/zero.txt"
    if ! PATH="$d/stub:$PATH" sdk/trim-host.sh win-x64 "$d/layouts" "$d/trimmed" > "$LOG" 2>&1; then fail "$name" "the second trim failed"; return; fi
    if [ "$(cat "$d/trimmed/win-x64/lib/zero.txt")" != two ]; then fail "$name" "a changed lib/ file didn't redo the trim"; return; fi
    rm -rf "$d/trimmed/linux-x64" "$d/trimmed/win-x64"
    if PATH="$d/stub:$PATH" sdk/trim-host.sh win-x64 "$d/layouts" "$d/trimmed" > "$LOG" 2>&1; then fail "$name" "win-x64 trimmed without a linux-x64 one"; return; fi
    if ! grep -q "linux-x64" "$LOG"; then fail "$name" "the error doesn't name linux-x64"; return; fi
    pass "$name"
}

# dotnet new zerollama: the Llama sample as a project, with the model from the
# ZeroCSharp.Stories260K package. It tells the repo sample's story, word for
# word, and builds for another target too.
test_llama_template() {
    local name="llama template" dir=$WORK/llamatemplate out other
    can_build "$name" || return
    rm -rf "$dir" && mkdir -p "$dir"
    fixture hello llamatemplate-config > /dev/null    # just for its nuget.config
    cp "$WORK/llamatemplate-config/nuget.config" "$dir/"
    dotnet new uninstall ZeroCSharp.Templates > /dev/null 2>&1 || true
    if ! run_in "$dir" llama-install new install "$FEED/ZeroCSharp.Templates.$VERSION.nupkg"; then fail "$name" "install failed"; return; fi
    if ! run_in "$dir" llama-new new zerollama -o Story; then fail "$name" "dotnet new zerollama failed"; return; fi
    if ! run_in "$dir/Story" llama-build build; then fail "$name" "the program didn't build"; return; fi
    out=$("$(exe "$dir/Story/bin/Debug/$TFM/Story")" | tr -d '\r')
    if [ "$out" != "$(tr -d '\r' < "$ROOT/tests/expected/Llama.txt")" ]; then fail "$name" "printed '$out'"; return; fi
    out=$(printf 'The dog ran.\n' | "$(exe "$dir/Story/bin/Debug/$TFM/Story")" -c -s 3 | tr -d '\r')
    if [[ "$out" != *"Tell a story together"* ]] || [ "$(wc -l <<< "$out")" -lt 3 ]; then fail "$name" "-c printed '$out'"; return; fi
    case "$HOST_RID" in win-x64) other=linux-x64 ;; *) other=win-x64 ;; esac
    if ! run_in "$dir/Story" llama-other build -p:ZeroTarget=$other; then fail "$name" "the $other build failed"; return; fi
    pass "$name"
}

# The framework is ZeroFramework's (sdk/version.props), not made from the
# version: builds go to bin/<config>/<it>/, packed libraries to lib/<it>/
# with build/<id>.props naming the SDK version that built them.
test_framework_from_property() {
    local name="framework from property" framework dir list
    framework=$(sed -n 's|.*<ZeroFramework>\(.*\)</ZeroFramework>.*|\1|p' sdk/version.props)
    if [ -z "$framework" ]; then fail "$name" "sdk/version.props has no ZeroFramework"; return; fi
    if [ "$TFM" != "$framework" ]; then fail "$name" "TFM is $TFM, ZeroFramework $framework"; return; fi
    dir=$(app_with_lib frameworkprop)
    if ! run_in "$dir/Geometry" frameworkprop-pack pack -o "$WORK/frameworkfeed"; then fail "$name" "dotnet pack failed"; return; fi
    if [ ! -f "$dir/Geometry/bin/Release/$framework/Geometry.dll" ]; then fail "$name" "no bin/Release/$framework/Geometry.dll"; return; fi
    list=$(zip_list "$WORK/frameworkfeed/ZeroTest.Geometry.1.0.0.nupkg")
    if ! grep -qx "lib/$framework/Geometry.dll" <<< "$list"; then fail "$name" "the package has no lib/$framework/Geometry.dll"; return; fi
    if ! grep -qx "build/ZeroTest.Geometry.props" <<< "$list"; then fail "$name" "the package has no build/ZeroTest.Geometry.props"; return; fi
    pass "$name"
}

# A library built by a newer SDK may use what this one lacks: ZERO002, naming
# both versions. One from the same SDK, or an older (prerelease) one, builds.
test_newer_sdk_library() {
    local name="newer sdk library" dir src v app
    can_build "$name" || return
    dir=$(app_with_lib newersdk)
    rm -rf "$WORK/newersdk-feed" "$WORK/newersdk-src"
    if ! run_in "$dir/Geometry" newersdk-pack pack -o "$WORK/newersdk-feed"; then fail "$name" "dotnet pack failed"; return; fi
    for v in 99.0.0 "$VERSION" 0.3.0-preview1; do
        src=$WORK/newersdk-src/$v
        mkdir -p "$src" "$WORK/newersdk-feeds/$v"
        (cd "$src" && unzip -qo "$WORK/newersdk-feed/ZeroTest.Geometry.1.0.0.nupkg")
        if [ ! -f "$src/build/ZeroTest.Geometry.props" ]; then fail "$name" "the package has no build/ZeroTest.Geometry.props"; return; fi
        # NuGet imports buildTransitive/'s copy when there is one: change both.
        for f in "$src/build/ZeroTest.Geometry.props" "$src/buildTransitive/ZeroTest.Geometry.props"; do
            sed -i.bak "s|SdkVersion=\"[^\"]*\"|SdkVersion=\"$v\"|" "$f" && rm -f "$f.bak"
        done
        rm -f "$WORK/newersdk-feeds/$v/ZeroTest.Geometry.1.0.0.nupkg"
        zip_dir "$src" "$WORK/newersdk-feeds/$v/ZeroTest.Geometry.1.0.0.nupkg"
        rm -rf "${NUGET_PACKAGES:-$HOME/.nuget/packages}/zerotest.geometry"
        app=$(fixture app-with-pkg "newersdk-app-$v")/App
        add_source "$app/../nuget.config" libs "$WORK/newersdk-feeds/$v"
        if [ "$v" = 99.0.0 ]; then
            if run_in "$app" "newersdk-$v" build; then fail "$name" "a library from SDK 99.0.0 was accepted"; return; fi
            if ! grep -q ZERO002 "$LOG" || ! grep -q "99.0.0" "$LOG" || ! grep -q "$VERSION" "$LOG"; then fail "$name" "no ZERO002 naming 99.0.0 and $VERSION"; return; fi
        elif ! run_in "$app" "newersdk-$v" build; then
            fail "$name" "a library from SDK $v was refused"; return
        fi
    done
    pass "$name"
}

# osx-arm64 from any build machine: bflat compiles, ld64.lld links against the
# payload's libSystem.tbd. The programs are kept in $WORK/mac-built/<host>/
# for CI, which runs them on a Mac (sdk.yml, *-built-macos).
test_mac_target() {
    local name="mac target" dir
    can_build "$name" || return
    dir=$(fixture native mactarget)
    if ! run_in "$dir" mactarget build -p:ZeroTarget=osx-arm64; then fail "$name" "dotnet build failed"; return; fi
    if ! is_macho_arm64 "$dir/bin/Debug/$TFM/osx-arm64/native"; then fail "$name" "osx-arm64/native isn't an arm64 Mach-O program"; return; fi
    mkdir -p "$WORK/mac-built/$HOST_RID"
    cp "$dir/bin/Debug/$TFM/osx-arm64/native" "$WORK/mac-built/$HOST_RID/native"
    pass "$name"
}

# Threads and libm on macOS come from libSystem: the link must find them all.
test_mac_target_links_libm_and_threads() {
    local name="mac target links libm and threads" dir
    can_build "$name" || return
    dir=$(fixture threads macthreads)
    if ! run_in "$dir" macthreads build -p:ZeroTarget=osx-arm64; then fail "$name" "dotnet build failed"; return; fi
    if ! is_macho_arm64 "$dir/bin/Debug/$TFM/osx-arm64/threads"; then fail "$name" "osx-arm64/threads isn't an arm64 Mach-O program"; return; fi
    mkdir -p "$WORK/mac-built/$HOST_RID"
    cp "$dir/bin/Debug/$TFM/osx-arm64/threads" "$WORK/mac-built/$HOST_RID/threads"
    pass "$name"
}

test_mac_target_path_with_spaces() {
    local name="mac target path with spaces" dir
    can_build "$name" || return
    dir=$(fixture native "mac space/Zoë Ann")
    if ! run_in "$dir" macspaces build -p:ZeroTarget=osx-arm64; then fail "$name" "dotnet build failed"; return; fi
    if ! is_macho_arm64 "$dir/bin/Debug/$TFM/osx-arm64/native"; then fail "$name" "no arm64 Mach-O program"; return; fi
    pass "$name"
}

# A C function libSystem.tbd doesn't list: the link fails, naming it.
test_mac_target_undefined_import() {
    local name="mac target undefined import" dir
    can_build "$name" || return
    dir=$(fixture native macundefined)
    cat > "$dir/Program.cs" <<'CS'
using System.Runtime.InteropServices;
public static class Program
{
    [DllImport("libc")] static safe extern int zero_no_such_function();
    public static int Main() => zero_no_such_function();
}
CS
    if run_in "$dir" macundefined build -p:ZeroTarget=osx-arm64; then fail "$name" "the build succeeded"; return; fi
    if ! grep -q "_zero_no_such_function" "$LOG"; then fail "$name" "the error doesn't name _zero_no_such_function"; return; fi
    pass "$name"
}

# sdk/trim-macos-layout.sh with a stub otool: the linux-arm64 list mapped to
# macOS names (.so -> .dylib), plus a dylib bflat loads, found whatever its
# capitals; an otool failure stops the trim instead of leaving dylibs out.
test_trim_macos_layout() {
    local name="trim macos layout" d=$WORK/trimmac
    rm -rf "$d" && mkdir -p "$d/linux/bin" "$d/linux/lib/linux" "$d/mac/bin" "$d/mac/lib/linux" "$d/stub"
    touch "$d/linux/bflat" "$d/linux/bin/lld" "$d/linux/libjitinterface_arm64.so" "$d/linux/lib/linux/zero.txt"
    touch "$d/mac/bflat" "$d/mac/bin/lld" "$d/mac/libjitinterface_arm64.dylib" "$d/mac/lib/linux/zero.txt" "$d/mac/libExtra.dylib" "$d/mac/unused.dylib"
    cat > "$d/stub/otool" <<'STUB'
#!/usr/bin/env bash
[ -n "${STUB_FAIL:-}" ] && { echo "otool: can't open file" >&2; exit 1; }
echo "$2:"
case "$2" in *bflat) printf '\t@rpath/LIBEXTRA.dylib (compatibility version 1.0.0)\n\t/usr/lib/libSystem.B.dylib (compatibility version 1.0.0)\n' ;; esac
STUB
    chmod +x "$d/stub/otool"
    LOG=$d/trim.log
    if ! PATH="$d/stub:$PATH" sdk/trim-macos-layout.sh "$d/mac" "$d/linux" "$d/out" > "$LOG" 2>&1; then fail "$name" "the trim failed"; return; fi
    if [ ! -f "$d/out/libjitinterface_arm64.dylib" ] || [ ! -f "$d/out/lib/linux/zero.txt" ] || [ ! -f "$d/out/bin/lld" ]; then fail "$name" "the mapped files are missing"; return; fi
    if [ -z "$(find "$d/out" -maxdepth 1 -name libExtra.dylib)" ]; then fail "$name" "libExtra.dylib (loaded as LIBEXTRA.dylib) is missing"; return; fi
    if [ -e "$d/out/unused.dylib" ]; then fail "$name" "unused.dylib was copied"; return; fi
    if STUB_FAIL=1 PATH="$d/stub:$PATH" sdk/trim-macos-layout.sh "$d/mac" "$d/linux" "$d/out" > "$LOG" 2>&1; then fail "$name" "an otool failure was ignored"; return; fi
    if ! grep -q "otool" "$LOG"; then fail "$name" "the error doesn't name otool"; return; fi
    pass "$name"
}

# On an Apple Silicon Mac: the Mac's own program, signed ad hoc and run, and
# the other three targets. They're kept in $WORK/macos-built/<rid>/ for CI,
# which runs them on their own systems (sdk.yml, macos-built-*).
test_mac_host_builds_all_targets() {
    local name="mac host builds all targets" dir out t f
    if [ "$HOST_RID" != osx-arm64 ]; then skip "$name" "Apple Silicon Macs only"; return; fi
    dir=$(fixture native machost)
    if ! run_in "$dir" machost build; then fail "$name" "dotnet build failed"; return; fi
    if ! codesign -v "$dir/bin/Debug/$TFM/native" 2> /dev/null; then fail "$name" "the program isn't signed"; return; fi
    out=$("$dir/bin/Debug/$TFM/native"); if [ "$out" != hi ]; then fail "$name" "printed '$out'"; return; fi
    for t in linux-x64 linux-arm64 win-x64; do
        if ! run_in "$dir" "machost-$t" build -p:ZeroTarget=$t; then fail "$name" "the $t build failed"; return; fi
        f=$dir/bin/Debug/$TFM/$t/native; [ "$t" = win-x64 ] && f=$f.exe
        case "$t" in
            linux-x64)   is_elf "$f" x86-64 ;;
            linux-arm64) is_elf "$f" aarch64 ;;
            win-x64)     is_pe "$f" ;;
        esac || { fail "$name" "$t/native isn't a $t program"; return; }
        mkdir -p "$WORK/macos-built/$t" && cp "$f" "$WORK/macos-built/$t/"
    done
    pass "$name"
}

# C functions programs commonly import link for macOS without extra steps:
# libSystem.tbd lists them. (Called only when there are 100 arguments, so the
# link needs them and running needs nothing.)
test_mac_target_common_libc() {
    local name="mac target common libc" dir
    can_build "$name" || return
    dir=$(fixture native maclibc)
    cat > "$dir/Program.cs" <<'CS'
using System.Runtime.InteropServices;
public static class Program
{
    [DllImport("libc")] static safe extern int snprintf(nint buffer, nint size, nint format);
    [DllImport("libc")] static safe extern nint fopen(nint path, nint mode);
    [DllImport("libc")] static safe extern nint fread(nint buffer, nint size, nint count, nint file);
    [DllImport("libc")] static safe extern int fclose(nint file);
    [DllImport("libc")] static safe extern void qsort(nint items, nint count, nint size, nint compare);
    [DllImport("libc")] static safe extern nint strerror(int error);
    [DllImport("libc")] static safe extern int getchar();
    [DllImport("libc")] static safe extern nint dlopen(nint path, int mode);
    [DllImport("libc")] static safe extern int atoi(nint text);
    [DllImport("libc")] static safe extern nint strtol(nint text, nint end, int radix);
    public static int Main(string[] args)
    {
        if (args.Length == 100)
        {
            snprintf(0, 0, 0); fclose(fopen(0, 0)); fread(0, 0, 0, 0); qsort(0, 0, 0, 0);
            strerror(0); getchar(); dlopen(0, 0); atoi(0); strtol(0, 0, 0);
        }
        Out.Line("hi");
        return 0;
    }
}
CS
    if ! run_in "$dir" maclibc build -p:ZeroTarget=osx-arm64; then fail "$name" "the link failed"; return; fi
    if ! is_macho_arm64 "$dir/bin/Debug/$TFM/osx-arm64/native"; then fail "$name" "no arm64 Mach-O program"; return; fi
    pass "$name"
}

# A C function libSystem.tbd doesn't list: the project's own .tbd, with
# libSystem's install name, in ZeroNativeInput (install-with-dotnet.md).
test_mac_target_user_tbd() {
    local name="mac target user tbd" dir
    can_build "$name" || return
    dir=$(fixture native macusertbd)
    cat > "$dir/Program.cs" <<'CS'
using System.Runtime.InteropServices;
public static class Program
{
    [DllImport("libc")] static safe extern int zero_only_in_my_tbd();
    public static int Main(string[] args) => args.Length == 100 ? zero_only_in_my_tbd() : 0;
}
CS
    printf '%s\n' '--- !tapi-tbd' 'tbd-version:     4' 'targets:         [ arm64-macos ]' \
        "install-name:    '/usr/lib/libSystem.B.dylib'" 'exports:' '  - targets:     [ arm64-macos ]' \
        '    symbols:     [ _zero_only_in_my_tbd ]' '...' > "$dir/more-libc.tbd"
    sed -i.bak 's|</Project>|  <ItemGroup><ZeroNativeInput Include="more-libc.tbd" /></ItemGroup>\n</Project>|' "$dir/native.csproj" && rm -f "$dir/native.csproj.bak"
    if ! run_in "$dir" macusertbd build -p:ZeroTarget=osx-arm64; then fail "$name" "the link failed"; return; fi
    if ! is_macho_arm64 "$dir/bin/Debug/$TFM/osx-arm64/native"; then fail "$name" "no arm64 Mach-O program"; return; fi
    pass "$name"
}

# The first program and the tutorial, as docs/ tells them: each step's samples/*.cs is the
# Program.cs of a zero project, built with dotnet build and run with its input, and prints
# what the repository's build does (tests/expected/).
test_tutorial_steps() {
    local name="tutorial steps" s dir input out
    can_build "$name" || return
    for s in Minimal QuizStep1 QuizStep2 QuizStep3 Quiz Refs; do
        dir=$(fixture native "tutorial-$s")
        rm -f "$dir/Program.cs"
        cp "$ROOT/samples/$s.cs" "$dir/Program.cs"
        input=/dev/null; [ -f "$ROOT/tests/input/$s.txt" ] && input=$ROOT/tests/input/$s.txt
        if ! run_in "$dir" "tutorial-$s" build; then fail "$name" "$s didn't build"; return; fi
        out=$("$(exe "$dir/bin/Debug/$TFM/native")" < "$input" | tr -d '\r')
        echo "size $s: $(wc -c < "$(exe "$dir/bin/Debug/$TFM/native")")" >> "$WORK/tutorial-sizes.txt"
        # Windows readers in PowerShell: cmd redirects the answers file unchanged.
        if [ "$HOST_OS" = Windows ] && [ "$input" != /dev/null ]; then
            out=$(cmd //c "$(native_path "$(exe "$dir/bin/Debug/$TFM/native")") < $(native_path "$input")" | tr -d '\r')
            if [ "$out" != "$(tr -d '\r' < "$ROOT/tests/expected/$s.txt")" ]; then fail "$name" "$s through cmd's < printed something else"; return; fi
        fi
        if [ "$out" != "$(tr -d '\r' < "$ROOT/tests/expected/$s.txt")" ]; then
            echo "$out" >> "$LOG"
            fail "$name" "$s printed something else"; return
        fi
    done
    pass "$name"
}

# docs/how-to/use-simd.md's ZeroInstructionSet, per target architecture: the
# Simd sample takes its SIMD path, and the result matches the scalar one.
test_simd_instruction_set() {
    local name="simd instruction set" dir out
    can_build "$name" || return
    dir=$(fixture native simd)
    rm -f "$dir/Program.cs" && cp "$ROOT/samples/Simd.cs" "$dir/Program.cs"
    sed -i.bak 's|</Project>|  <PropertyGroup>\n    <ZeroInstructionSet Condition="'"'"'$(ZeroTarget)'"'"' == '"'"'linux-x64'"'"' or '"'"'$(ZeroTarget)'"'"' == '"'"'win-x64'"'"'">x86-64-v3</ZeroInstructionSet>\n    <ZeroInstructionSet Condition="'"'"'$(ZeroTarget)'"'"' == '"'"'linux-arm64'"'"' or '"'"'$(ZeroTarget)'"'"' == '"'"'osx-arm64'"'"'">dotprod</ZeroInstructionSet>\n  </PropertyGroup>\n</Project>|' "$dir/native.csproj" && rm -f "$dir/native.csproj.bak"
    if ! run_in "$dir" simd build -v:n; then fail "$name" "dotnet build failed"; return; fi
    if ! grep -qE -- "-m (x86-64-v3|dotprod)" "$LOG"; then fail "$name" "bflat didn't get -m"; return; fi
    out=$("$(exe "$dir/bin/Debug/$TFM/native")" | tr -d '\r')
    if ! grep -qE "Path: (NEON|AVX)" <<< "$out"; then fail "$name" "not the SIMD path: $(grep Path: <<< "$out")"; return; fi
    if ! grep -q "Results match: true" <<< "$out"; then fail "$name" "the results don't match"; return; fi
    if ! run_in "$dir" simd-mac build -p:ZeroTarget=osx-arm64; then fail "$name" "the osx-arm64 build failed"; return; fi
    pass "$name"
}

# ---- Run ---------------------------------------------------------------------

if [ -z "${ZERO_NO_PACK:-}" ]; then
    if ! sdk/pack.sh > "$WORK.pack.log" 2>&1; then
        echo "sdk/pack.sh failed:"
        tail -30 "$WORK.pack.log"
        exit 1
    fi
fi
# The feed's packages keep their version between runs, so drop the cached
# copies: restore must take the ones just packed.
rm -rf "${NUGET_PACKAGES:-$HOME/.nuget/packages}"/zerocsharp.*
# MSBuild processes that stay alive between builds keep the SDK's files in
# memory; with the same version repacked, they'd build with the old ones.
export MSBUILDDISABLENODEREUSE=1
dotnet build-server shutdown > /dev/null 2>&1 || true
mkdir -p "$WORK"

tests=("$@")
[ ${#tests[@]} -eq 0 ] && tests=("${ALL_TESTS[@]}")
for name in "${tests[@]}"; do
    LOG=
    "test_${name//-/_}"
done
echo
echo "$passed passed, $failed failed, $skipped skipped"
exit "$failed"
