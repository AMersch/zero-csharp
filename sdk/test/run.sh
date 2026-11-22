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
# The target framework: zero + major and minor, without the dot (0.1 -> zero01).
TFM=zero$(echo "$VERSION" | cut -d. -f1,2 | tr -d .)
case "$(uname -s)" in
    Linux)                    HOST_OS=Linux ;;
    Darwin)                   HOST_OS=Darwin ;;
    MINGW* | MSYS* | CYGWIN*) HOST_OS=Windows ;;   # Git Bash
    *)                        HOST_OS=$(uname -s) ;;
esac
export ZERO_HOSTS=${ZERO_HOSTS:-linux-x64 linux-arm64 win-x64}
case "$(uname -m)" in
    x86_64 | amd64)  HOST_ARCH_NAME=x64;   DOCKER_ARCH=amd64; HOST_ELF_ARCH=x86-64 ;;
    *)               HOST_ARCH_NAME=arm64; DOCKER_ARCH=arm64; HOST_ELF_ARCH=aarch64 ;;
esac
case "$HOST_OS" in
    Windows) HOST_RID=win-x64 ;;
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
# Windows); on others, records a skip.
can_build() {
    [ "$HOST_OS" = Linux ] || [ "$HOST_OS" = Windows ] && return 0
    skip "$1" "builds native programs, which needs a Linux or Windows host"
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
# is_host_program <file>: a program for the host's own target.
is_host_program() { if [ "$HOST_OS" = Windows ]; then is_pe "$1"; else is_elf "$1" "$HOST_ELF_ARCH"; fi; }
# native_path <path>: the path as the dotnet CLI on this host writes it.
native_path() { if [ "$HOST_OS" = Windows ]; then cygpath -w "$1"; else echo "$1"; fi; }
# zip_list <zip>: the entries' names; zip_dir <dir> <zip>: zips a folder's contents.
zip_list() {
    if command -v unzip > /dev/null; then unzip -Z1 "$1"
    else powershell.exe -NoProfile -Command "Add-Type -A System.IO.Compression.FileSystem; [IO.Compression.ZipFile]::OpenRead('$(native_path "$1")').Entries.FullName"; fi
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
    for f in ref/System.Private.CoreLib.dll ref/BflatLib.dll native/linux-x64/zerocore.o \
             native/linux-arm64/zerocore.o native/win-x64/runtime.obj native/win-x64/zerocore.obj VERSIONS.txt; do
        if [ ! -s "$payload/$f" ]; then fail "payload complete" "missing $f"; return; fi
    done
    if ! is_elf "$payload/native/linux-x64/zerocore.o" x86-64; then fail "payload complete" "linux-x64 object isn't x86-64"; return; fi
    if ! is_elf "$payload/native/linux-arm64/zerocore.o" aarch64; then fail "payload complete" "linux-arm64 object isn't aarch64"; return; fi
    if ! is_coff_x64 "$payload/native/win-x64/runtime.obj"; then fail "payload complete" "win-x64 object isn't COFF"; return; fi
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
            $B --os windows --arch x64 --ldflags "/entry:zero_start $P/native/win-x64/runtime.obj $P/native/win-x64/zerocore.obj" -o /tmp/s.exe
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
    if [ "$HOST_OS" != Darwin ]; then skip "$name" "macOS only"; return; fi
    dir=$(fixture native macos)
    if run_in "$dir" macos build; then fail "$name" "the build succeeded"; return; fi
    if ! grep -qF "Zero C# can't make native programs on macOS yet; build on Linux or Windows, or use the repository's build.sh." "$LOG"; then
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
    if ! run_in "$dir" sidebyside-4 clean; then fail "$name" "dotnet clean failed"; return; fi
    if [ -e "$(exe "$dir/bin/Debug/$TFM/native")" ] || [ -e "$(exe "$dir/bin/Debug/$TFM/$host/native")" ] || [ -e "$other_file" ]; then
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
    if ! sdk/check-tag.sh "sdk-v$VERSION" > "$LOG" 2>&1; then fail "$name" "sdk-v$VERSION was refused"; return; fi
    if sdk/check-tag.sh "sdk-v0.0.9" > "$LOG" 2>&1; then fail "$name" "sdk-v0.0.9 was accepted for $VERSION"; return; fi
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
    for i in $(seq 1 40); do
        echo "public static class Part$i { public static long One() => 1; }" > "$sub/Part$i.cs"
        main="$main n += Part$i.One();"
    done
    echo "$main Out.Write(n); Out.Line(); return 0; } }" > "$dir/Program.cs"
    if ! run_in "$dir" manyfiles build -v:n; then fail "$name" "dotnet build failed"; return; fi
    if ! grep -qE 'tools/bflat/bflat(\.exe)?" build @' "$LOG"; then fail "$name" "bflat didn't get a response file"; return; fi
    local out; out=$("$(exe "$dir/bin/Debug/$TFM/native")" | tr -d '\r')
    if [ "$out" != 40 ]; then fail "$name" "printed '$out'"; return; fi
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

ALL_TESTS=(host_packages payload_complete hello_compiles console_is_an_error unions_compile plain_packages_refused
    native_hello_runs cross_targets bad_target path_with_spaces compile_items incremental
    bflat_failure_fails_the_build size macos_error
    project_reference library_change_rebuilds pack_and_package_reference version_mismatch templates
    custom_packages_folder dotnet_run publish extra_dll_imports inherited_settings clean_and_rebuild
    targets_side_by_side language_settings_forwarded target_framework_is_an_error read_only_packages_folder
    release_tag_check payload_folder_guard target_from_directory_targets upgrade_from_0_1_0
    long_command_line windows_builds_linux unusual_packages_folder crlf_input
    artifacts_path_with_spaces)

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
