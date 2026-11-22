#!/usr/bin/env bash
# Calls a native library built from sdk/test/fixtures/nativelib with an
# ordinary .NET program (fixtures/nativelib-caller) and checks what it prints
# against fixtures/nativelib-caller/expected.txt. The tests run it on the
# machine that built the library; CI also runs it on the library's own
# system when another one built it.
#
# Usage:  sdk/test/call-native-lib.sh <library>
#         sdk/test/call-native-lib.sh two <copy a> <copy b>   (two copies, separate state)
#         sdk/test/call-native-lib.sh area <library>          (nl_rect_area, with a zero library referenced)
# Env:    ZERO_CALLER_DIR   the caller project (default: the fixture)
# Exit:   0 when the output is as expected.
set -uo pipefail
here=$(cd "$(dirname "$0")" && pwd)
caller=${ZERO_CALLER_DIR:-$here/fixtures/nativelib-caller}
# Absolute, and as the dotnet CLI on this machine writes paths.
full() { (cd "$(dirname "$1")" && echo "$PWD/$(basename "$1")"); }
native() { if command -v cygpath > /dev/null; then cygpath -w "$1"; else echo "$1"; fi; }

out_dir=$caller/bin/caller
if ! build=$(dotnet build "$(native "$caller")" -c Release -o "$(native "$out_dir")" -nologo -v q 2>&1); then
    echo "call-native-lib: building the caller failed:"
    echo "$build"
    exit 1
fi
if [ "${1:-}" = area ]; then
    expected="area 6"
    out=$(dotnet "$(native "$out_dir/nativelib-caller.dll")" area "$(native "$(full "$2")")" 2>&1 | tr -d '\r')
elif [ "${1:-}" = two ]; then
    expected="two 3 1"
    out=$(dotnet "$(native "$out_dir/nativelib-caller.dll")" two "$(native "$(full "$2")")" "$(native "$(full "$3")")" 2>&1 | tr -d '\r')
else
    expected=$(tr -d '\r' < "$caller/expected.txt")
    out=$(ZERO_NATIVE_LIB="$(native "$(full "${1:?library}")")" dotnet "$(native "$out_dir/nativelib-caller.dll")" 2>&1 | tr -d '\r')
fi
if [ "$out" != "$expected" ]; then
    echo "call-native-lib: unexpected output from $*:"
    diff <(echo "$expected") <(echo "$out")
    exit 1
fi
echo "$out"
