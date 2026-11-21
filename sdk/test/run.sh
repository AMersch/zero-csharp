#!/usr/bin/env bash
# The tests of the Zero C# SDK packages (sdk/). Each test copies a project
# from sdk/test/fixtures into sdk/test/work, builds it with the dotnet CLI
# against the packages in sdk/feed, and checks the result.
#
# Usage:  sdk/test/run.sh                 pack (sdk/pack.sh), then run every test
#         sdk/test/run.sh name...         only these tests (names as in the list below)
# Env:    ZERO_NO_PACK=1                  use sdk/feed as it is (CI downloads it)
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

passed=0
failed=0
LOG=
pass() { echo "ok   $1"; passed=$((passed + 1)); }
fail() {
    echo "FAIL $1: $2"
    [ -n "$LOG" ] && [ -f "$LOG" ] && tail -20 "$LOG" | sed 's/^/     | /'
    failed=$((failed + 1))
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

ALL_TESTS=(payload_complete hello_compiles console_is_an_error unions_compile plain_packages_refused)

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
mkdir -p "$WORK"

tests=("$@")
[ ${#tests[@]} -eq 0 ] && tests=("${ALL_TESTS[@]}")
for name in "${tests[@]}"; do
    LOG=
    "test_${name//-/_}"
done
echo
echo "$passed passed, $failed failed"
exit "$failed"
