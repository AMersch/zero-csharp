#!/usr/bin/env bash
# Builds every sample and checks that it still behaves: each must exit with 0
# (the test samples exit with their number of failed checks), and those whose
# output is the same on every run must print exactly what tests/expected holds.
#
# Usage:  ./test.sh            native, for this machine (or BFLAT_ARCH)
#         ./test.sh --wasm     WebAssembly (needs ./setup.sh --wasm)
#         ./test.sh --dos      DOS, on an emulated 386 with 4 MB (needs ./setup.sh --dos)
#         ./test.sh --all      native, the other architecture (macOS only, run
#                              under emulation), and WebAssembly and DOS if set up
#         ./test.sh --update   native, and rewrite tests/expected from the output
# Env:    BFLAT_DIR, BFLAT_FORK, BFLAT_ARCH, WASM_TOOLS (see env.sh)
set -euo pipefail
cd "$(dirname "$0")"
source ./env.sh

SAMPLES=(Tiny Minimal Arena Collections VirtualTest Casts Core Simd Kernels Strings)

# Simd and Kernels print timings, so only their exit code is checked.
TIMED=" Simd Kernels "
# Arena and Collections print byte counts that depend on the pointer size, so
# on WebAssembly and DOS (32-bit) only their exit code is checked.
POINTER_SIZED=" Arena Collections "
# Simd and Kernels aren't built for DOS: they benchmark SIMD instructions,
# which a 386 doesn't have.
# Vga is a game for DOS only; "Vga test" plays by itself and reports.
DOS_SAMPLES=(Tiny Minimal Arena Collections VirtualTest Casts Core Strings Vga)

EXPECTED=tests/expected
failures=0

# $1: a label for the report; $2: "native", "wasm" or "dos"; $3: "check" or "update"
run_all() {
    local label=$1 target=$2 mode=$3 name out status expected argument
    local run=(./run.sh)
    local samples=("${SAMPLES[@]}")
    [ "$target" = wasm ] && run+=(--wasm)
    if [ "$target" = dos ]; then
        run+=(--dos)
        samples=("${DOS_SAMPLES[@]}")
    fi

    echo
    echo "== $label"
    for name in "${samples[@]}"; do
        status=0
        argument=
        [ "$name" = Vga ] && argument=test
        # shellcheck disable=SC2086
        out=$("${run[@]}" "$name" $argument 2>&1) || status=$?
        expected=$EXPECTED/$name.txt

        if [ "$status" != 0 ]; then
            echo "FAIL $name: exit code $status"
            echo "$out" | tail -5 | sed 's/^/       /'
            failures=$((failures + 1))
        elif [[ "$TIMED" == *" $name "* ]] || { [ "$target" != native ] && [[ "$POINTER_SIZED" == *" $name "* ]]; }; then
            echo "ok   $name (exit code only)"
        elif [ "$mode" = update ]; then
            mkdir -p "$EXPECTED"
            echo "$out" > "$expected"
            echo "ok   $name (expected output updated)"
        elif [ ! -f "$expected" ]; then
            echo "FAIL $name: no $expected (run ./test.sh --update)"
            failures=$((failures + 1))
        elif [ "$out" != "$(cat "$expected")" ]; then
            echo "FAIL $name: output differs from $expected"
            diff <(echo "$out") "$expected" | head -10 | sed 's/^/       /' || true
            failures=$((failures + 1))
        else
            echo "ok   $name"
        fi
    done
}

test_native() {
    ./build.sh > /dev/null
    run_all "native $BFLAT_ARCH" native "${1:-check}"
}

test_wasm() {
    ./build.sh --wasm > /dev/null
    run_all "WebAssembly" wasm check
}

# build.sh --dos also checks every program for instructions newer than a 386.
test_dos() {
    ./build.sh --dos > /dev/null
    local emulator cpu=486
    emulator=$(find_dos_emulator)
    [ "$emulator" = bochs ] && cpu=i386
    run_all "DOS ($emulator, ${DOS_CPU:-$cpu}, ${DOS_MEMORY:-4} MB)" dos check
}

case "${1:-}" in
    "")       test_native ;;
    --update) test_native update ;;
    --wasm)   test_wasm ;;
    --dos)    test_dos ;;
    --all)
        test_native
        if [ "$(uname -s)" = Darwin ]; then
            host=$BFLAT_ARCH
            if [ "$host" = arm64 ]; then other=x64; else other=arm64; fi
            export BFLAT_ARCH=$other
            test_native
            # Leave bin/ holding binaries for this machine again.
            export BFLAT_ARCH=$host
            ./build.sh > /dev/null
        else
            echo
            echo "(skipping the other architecture: running it needs emulation, which this script only sets up on macOS)"
        fi
        if [ -x "$WASM_ILC" ] && [ -x "$WASI_SDK/bin/clang" ]; then
            test_wasm
        else
            echo
            echo "(skipping WebAssembly: run ./setup.sh --wasm first)"
        fi
        if [ -x "$WASM_ILC" ] && [ -f "$DOS_BOOT" ] && find_dos_clang > /dev/null && find_dos_emulator > /dev/null; then
            test_dos
        else
            echo
            echo "(skipping DOS: run ./setup.sh --dos first)"
        fi
        ;;
    *) echo "Usage: ./test.sh [--wasm | --dos | --all | --update]" >&2; exit 2 ;;
esac

echo
if [ "$failures" = 0 ]; then
    echo "All tests passed."
else
    echo "$failures test(s) FAILED."
    exit 1
fi
