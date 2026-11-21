#!/usr/bin/env bash
# The tests of the Zero C# SDK packages (sdk/). Each test copies a project
# from sdk/test/fixtures into sdk/test/work, builds it with the dotnet CLI
# against the packages in sdk/feed, and checks the result.
#
# Usage:  sdk/test/run.sh                 pack (sdk/pack.sh), then run every test
#         sdk/test/run.sh name...         only these tests (names as in the list below)
# Env:    ZERO_NO_PACK=1                  use sdk/feed as it is (CI downloads it)
#         ZERO_HOSTS                      the host packages to expect (default: both,
#                                         "linux-x64 linux-arm64"); also passed to pack.sh
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
HOST_OS=$(uname -s)
export ZERO_HOSTS=${ZERO_HOSTS:-linux-x64 linux-arm64}
case "$(uname -m)" in
    x86_64 | amd64)  HOST_ARCH_NAME=x64;   DOCKER_ARCH=amd64; HOST_ELF_ARCH=x86-64 ;;
    *)               HOST_ARCH_NAME=arm64; DOCKER_ARCH=arm64; HOST_ELF_ARCH=aarch64 ;;
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
# linux_only <test name>: true on Linux; on other hosts, records a skip.
linux_only() {
    [ "$HOST_OS" = Linux ] && return 0
    skip "$1" "builds native programs, which needs a Linux host"
    return 1
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
    <add key="zero" value="$FEED" />
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
    if strings "$dll" | grep -qx 'System.Runtime'; then
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
    if ! file "$payload/native/linux-x64/zerocore.o" | grep -q 'x86-64'; then fail "payload complete" "linux-x64 object isn't x86-64"; return; fi
    if ! file "$payload/native/linux-arm64/zerocore.o" | grep -q 'aarch64'; then fail "payload complete" "linux-arm64 object isn't aarch64"; return; fi
    if ! file "$payload/native/win-x64/runtime.obj" | grep -qiE 'COFF|x86-64'; then fail "payload complete" "win-x64 object isn't COFF"; return; fi
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
    for rid in $ZERO_HOSTS; do
        pkg=$FEED/ZeroCSharp.Bflat.$rid.$VERSION.nupkg
        if [ ! -f "$pkg" ]; then fail "host packages" "no $(basename "$pkg")"; return; fi
        size=$(( $(wc -c < "$pkg") / 1000000 ))
        if [ "$size" -ge 100 ]; then fail "host packages" "$(basename "$pkg") is $size MB, not under 100"; return; fi
        # Listed once: grep -q stops reading early, which pipefail counts as a failure.
        listing=$(unzip -l "$pkg")
        if ! grep -q ' tools/bflat/bflat$' <<< "$listing"; then fail "host packages" "$rid: no tools/bflat/bflat"; return; fi
        if ! grep -q ' tools/bflat/bin/lld$' <<< "$listing"; then fail "host packages" "$rid: no tools/bflat/bin/lld"; return; fi
    done
    pass "host packages ($ZERO_HOSTS)"
}

# ---- The native step (Linux hosts) -----------------------------------------

test_native_hello_runs() {
    local name="native hello runs" dir exe out
    linux_only "$name" || return
    dir=$(fixture native)
    if ! run_in "$dir" native build; then fail "$name" "dotnet build failed"; return; fi
    exe=$dir/bin/Debug/$TFM/native
    if [ ! -x "$exe" ]; then fail "$name" "no $exe"; return; fi
    out=$("$exe"); if [ "$out" != hi ]; then fail "$name" "printed '$out'"; return; fi
    if ! file "$exe" | grep -q "ELF.*$HOST_ELF_ARCH"; then fail "$name" "not an ELF for $HOST_ELF_ARCH: $(file -b "$exe")"; return; fi
    pass "$name"
}

test_cross_targets() {
    local name="cross targets" dir other other_arch
    linux_only "$name" || return
    dir=$(fixture native cross)
    if ! run_in "$dir" cross-win build -p:ZeroTarget=win-x64; then fail "$name" "win-x64 build failed"; return; fi
    if [ "$(head -c 2 "$dir/bin/Debug/$TFM/native.exe" 2>/dev/null)" != MZ ]; then fail "$name" "native.exe isn't a Windows program"; return; fi
    if [ "$HOST_ARCH_NAME" = x64 ]; then other=linux-arm64; other_arch=aarch64; else other=linux-x64; other_arch=x86-64; fi
    if ! run_in "$dir" cross-linux build -p:ZeroTarget=$other; then fail "$name" "$other build failed"; return; fi
    if ! file "$dir/bin/Debug/$TFM/native" | grep -q "ELF.*$other_arch"; then fail "$name" "not an ELF for $other_arch"; return; fi
    pass "$name"
}

test_bad_target() {
    local name="bad target" dir
    linux_only "$name" || return
    dir=$(fixture native badtarget)
    if run_in "$dir" badtarget build -p:ZeroTarget=mac-arm64; then fail "$name" "the build succeeded"; return; fi
    if ! grep -q 'linux-x64, linux-arm64, win-x64' "$LOG"; then fail "$name" "the error doesn't list the targets"; return; fi
    pass "$name"
}

test_path_with_spaces() {
    local name="path with spaces" dir out
    linux_only "$name" || return
    dir=$(fixture native "spaces dir/native")
    if ! run_in "$dir" spaces build; then fail "$name" "dotnet build failed"; return; fi
    out=$("$dir/bin/Debug/$TFM/native"); if [ "$out" != hi ]; then fail "$name" "printed '$out'"; return; fi
    pass "$name"
}

test_compile_items() {
    local name="compile items" dir out
    linux_only "$name" || return
    dir=$(fixture subfolders)
    if ! run_in "$dir" subfolders build; then fail "$name" "dotnet build failed"; return; fi
    out=$("$dir/bin/Debug/$TFM/subfolders"); if [ "$out" != "from b" ]; then fail "$name" "printed '$out'"; return; fi
    pass "$name"
}

# Whether the last log shows bflat being run.
ran_bflat() { grep -q 'tools/bflat/bflat" build' "$LOG"; }

test_incremental() {
    local name="incremental" dir
    linux_only "$name" || return
    dir=$(fixture native incremental)
    if ! run_in "$dir" incremental-1 build -v:n || ! ran_bflat; then fail "$name" "the first build didn't run bflat"; return; fi
    if ! run_in "$dir" incremental-2 build -v:n || ran_bflat; then fail "$name" "an unchanged project ran bflat again"; return; fi
    sleep 1; touch "$dir/Program.cs"
    if ! run_in "$dir" incremental-3 build -v:n || ! ran_bflat; then fail "$name" "a changed source didn't run bflat"; return; fi
    pass "$name"
}

test_bflat_failure_fails_the_build() {
    local name="bflat failure fails the build" dir
    linux_only "$name" || return
    dir=$(fixture badimport)
    if run_in "$dir" badimport build; then fail "$name" "the build succeeded"; return; fi
    if ! ran_bflat; then fail "$name" "it failed before bflat ran"; return; fi
    if ! grep -qE 'error' "$LOG"; then fail "$name" "no error in the output"; return; fi
    pass "$name"
}

test_size() {
    local name="size" dir
    linux_only "$name" || return
    dir=$(fixture native size)
    if ! run_in "$dir" size build -v:n -p:ZeroOptimizeFor=Size; then fail "$name" "dotnet build failed"; return; fi
    if ! grep -q 'tools/bflat/bflat" build.* -Os' "$LOG"; then fail "$name" "bflat wasn't given -Os"; return; fi
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

ALL_TESTS=(host_packages payload_complete hello_compiles console_is_an_error unions_compile plain_packages_refused
    native_hello_runs cross_targets bad_target path_with_spaces compile_items incremental
    bflat_failure_fails_the_build size macos_error)

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
