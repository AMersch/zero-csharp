#!/usr/bin/env bash
# Builds every sample and checks that it still behaves: each must exit with 0
# (the test samples exit with their number of failed checks), and those whose
# output is the same on every run must print exactly what tests/expected holds.
#
# A sample with a tests/input/<Sample>.txt reads it as standard input; the
# others get an empty one.
#
# Guards is also run once per line of tests/expected/Guards.fail, with the
# line's first field as its argument. Each of those runs makes a mistake that
# lib/ must end the program for: the exit code must not be 0, and the output
# must hold every other field of the line (fields are separated by "|").
#
# Usage:  ./test.sh            native, for this machine (or BFLAT_ARCH)
#         ./test.sh --wasm     WebAssembly (needs ./setup.sh --wasm)
#         ./test.sh --dos      DOS, on an emulated 386 with 4 MB (needs ./setup.sh --dos)
#         ./test.sh --win      Windows x64, under Wine in a container
#         ./test.sh --mac      macOS, natively (on a Mac with Apple Silicon)
#         ./test.sh --all      native, the other architecture (macOS only, run
#                              under emulation), Windows, macOS (on Apple
#                              Silicon), and WebAssembly and DOS if set up
#         ./test.sh --update   native, and rewrite tests/expected from the output
# Env:    BFLAT_DIR, BFLAT_FORK, BFLAT_ARCH, WASM_TOOLS (see env.sh)
set -euo pipefail
cd "$(dirname "$0")"
source ./env.sh

SAMPLES=(Tiny Minimal Arena Collections VirtualTest Casts Core Simd Kernels Strings QuizStep1 QuizStep2 QuizStep3 Quiz Guards Queries Threads Records Refs Llama)

# Simd and Kernels print timings, so only their exit code is checked.
TIMED=" Simd Kernels "
# Arena and Collections print byte counts that depend on the pointer size, so
# on WebAssembly and DOS (32-bit) only their exit code is checked.
POINTER_SIZED=" Arena Collections "
# Simd and Kernels aren't built for DOS: they benchmark SIMD instructions,
# which a 386 doesn't have.
# Vga is a game for DOS only; "Vga test" plays by itself and reports.
DOS_SAMPLES=(Tiny Minimal Arena Collections VirtualTest Casts Core Strings QuizStep1 QuizStep2 QuizStep3 Quiz Guards Queries Threads Records Refs Llama Vga)

EXPECTED=tests/expected
failures=0

# $1: a label for the report; $2: "native", "wasm" or "dos"; $3: "check" or "update"
run_all() {
    local label=$1 target=$2 mode=$3 name out status expected argument input line missing i
    local fields=()
    local run=(./run.sh)
    local samples=("${SAMPLES[@]}")
    [ "$target" = wasm ] && run+=(--wasm)
    [ "$target" = win ] && run+=(--win)
    [ "$target" = mac ] && run+=(--mac)
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
        input=tests/input/$name.txt
        [ -f "$input" ] || input=/dev/null
        # shellcheck disable=SC2086
        out=$("${run[@]}" "$name" $argument < "$input" 2>&1) || status=$?
        # The C library's puts ends lines with "\r\n" on Windows, as it should.
        [ "$target" = win ] && out=${out//$'\r'/}
        expected=$EXPECTED/$name.txt

        if [ "$status" != 0 ]; then
            echo "FAIL $name: exit code $status"
            echo "$out" | tail -5 | sed 's/^/       /'
            failures=$((failures + 1))
        elif [[ "$TIMED" == *" $name "* ]] || { { [ "$target" = wasm ] || [ "$target" = dos ]; } && [[ "$POINTER_SIZED" == *" $name "* ]]; }; then
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

    while IFS= read -r line; do
        [ -n "$line" ] || continue
        IFS='|' read -r -a fields <<< "$line"
        name=${fields[0]}
        status=0
        out=$("${run[@]}" Guards "$name" < /dev/null 2>&1) || status=$?
        missing=
        for ((i = 1; i < ${#fields[@]}; i++)); do
            [[ "$out" == *"${fields[$i]}"* ]] || missing=${fields[$i]}
        done
        if [ "$status" = 0 ]; then
            echo "FAIL Guards $name: the program wasn't stopped"
            failures=$((failures + 1))
        elif [ -n "$missing" ]; then
            echo "FAIL Guards $name: stopped (exit code $status) without the message \"$missing\""
            echo "$out" | tail -3 | sed 's/^/       /'
            failures=$((failures + 1))
        else
            echo "ok   Guards $name (stopped, as it must be)"
        fi
    done < "$EXPECTED/Guards.fail"
}

test_native() {
    ./build.sh > /dev/null
    run_all "native $BFLAT_ARCH" native "${1:-check}"
}

test_wasm() {
    ./build.sh --wasm > /dev/null
    run_all "WebAssembly" wasm check
}

test_mac() {
    ./build.sh --mac > /dev/null
    run_all "macOS arm64" mac check
}

test_win() {
    ./build.sh --win > /dev/null
    # Build the Wine image now, if needed, so its build log doesn't end up in
    # the first program's output.
    ensure_win_image > /dev/null
    run_all "Windows x64 (Wine)" win check
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
    --win)    test_win ;;
    --mac)    test_mac ;;
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
        if [ "$(uname -s)" = Darwin ] && [ "$(uname -m)" = arm64 ]; then
            test_mac
        fi
        if command -v docker >/dev/null 2>&1 && find_dos_clang > /dev/null; then
            test_win
        else
            echo
            echo "(skipping Windows: needs Docker and a clang with the x86 target)"
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
    *) echo "Usage: ./test.sh [--wasm | --dos | --win | --mac | --all | --update]" >&2; exit 2 ;;
esac

echo
if [ "$failures" = 0 ]; then
    echo "All tests passed."
else
    echo "$failures test(s) FAILED."
    exit 1
fi
