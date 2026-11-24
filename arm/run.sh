#!/usr/bin/env bash
# Runs bin/<board>/<Sample>.elf (./build.sh --pico2 or --r4) in QEMU, on the
# Cortex-M board that stands in for the real one (see env.sh). The program's
# output, standard input, arguments, files and exit code all go through
# semihosting: QEMU does them on this machine, so it runs like a command.
#
# Usage: arm/run.sh <board> <Sample> [args...]
# Env:   ARM_TIMEOUT  seconds before the run is stopped (default 60), so that
#                     a program stuck in a loop ends a test run with a failure
set -euo pipefail
cd "$(dirname "$0")/.."
source ./env.sh

if [ $# -lt 2 ]; then
    echo "Usage: ./run.sh --pico2|--r4 <Sample> [args...]" >&2
    exit 1
fi
board=$1
name=$2
shift 2
arm_board "$board"

program=bin/$board/$name.elf
if [ ! -f "$program" ]; then
    echo "No $program: run ./build.sh --$board $name" >&2
    exit 1
fi
if ! command -v qemu-system-arm >/dev/null 2>&1; then
    echo "Error: no qemu-system-arm. macOS: brew install qemu. Ubuntu: apt install qemu-system-arm" >&2
    exit 1
fi

# QEMU hands the program its command line as one string, which the program's
# startup splits at spaces. So each argument goes with %XX for '%', a space, a
# tab and a line break (arm/runtime.c decodes them), and with a comma written
# twice, as QEMU's option syntax wants.
encode() {
    local text=${1//%/%25}
    text=${text// /%20}
    text=${text//$'\t'/%09}
    text=${text//$'\n'/%0A}
    printf '%s' "${text//,/,,}"
}

config=enable=on,target=native,arg=$(encode "$name")
qemu=(qemu-system-arm -M "$ARM_MACHINE" -nographic -serial none -monitor none)

# Standard input. A terminal is the host's console, a character at a time.
# Anything else (a file, a pipe, /dev/null) is saved to a file that the
# program reads instead, because the console can't say the input ended.
input=
if [ -t 0 ]; then
    qemu+=(-chardev stdio,id=console)
    config+=,chardev=console
else
    input=$(mktemp)
    trap 'rm -f "$input"' EXIT
    cat > "$input"
    config+=,arg=$(encode "--zero-stdin=$input")
fi
for argument in "$@"; do
    config+=,arg=$(encode "$argument")
done

# -nographic -serial none -monitor none: no window, and QEMU's standard
# output is the program's alone. A program that crashes ends in picolibc's
# fault handler, which prints the registers and exits with 1.
timeout=${ARM_TIMEOUT:-60}
status=0
if command -v timeout >/dev/null 2>&1; then
    timeout "$timeout" "${qemu[@]}" -semihosting-config "$config" -kernel "$program" || status=$?
else
    "${qemu[@]}" -semihosting-config "$config" -kernel "$program" || status=$?
fi
if [ "$status" = 124 ]; then
    echo "arm/run.sh: stopped after $timeout seconds" >&2
fi
exit "$status"
