#!/usr/bin/env bash
# Checks the X16's integer division and multiplication (cx16/runtime.c, the
# section "integer division") as tests/c64-math checks the C64's, with the
# same cases and the same check.c, but on an emulated X16: the compiler's
# registers aren't one block of the zero page there, as they are in the
# 6502 simulator. ./test.sh --cx16 runs it first. Needs ./setup.sh --cx16
# and x16emu.
set -euo pipefail
cd "$(dirname "$0")/.."
source ./env.sh

if ! emulator=$(find_x16_emulator); then
    echo "Error: no x16emu. Get it from https://github.com/X16Community/x16-emulator, or set X16_EMULATOR." >&2
    exit 1
fi
work=bin/cx16/math-check
mkdir -p "$work"

# The section of runtime.c, on its own.
python3 - "$work/arith.c" <<'PY'
import sys
s = open("cx16/runtime.c").read()
start = s.index("// ---- integer division")
end = s.index("// ---- end of integer division")
types = "typedef long i32;\ntypedef unsigned long u32;\ntypedef long long i64;\ntypedef unsigned long long u64;\n"
open(sys.argv[1], "w").write(types + s[start:end])
PY

# The output and the exit code go to the emulator's debug registers, as
# cx16/runtime.c sends them.
cat > "$work/harness.c" <<'C'
int check(void);
void __putchar(char c) { *(volatile unsigned char *)0x9FBB = c == 13 ? 10 : c; }
int main(void)
{
    *(volatile unsigned char *)0x9FB9 = (unsigned char)check();
    for (;;) {}
}
C

failed=0
total=0
for spec in "8 800" "16 800" "32 600" "64 300"; do
    set -- $spec
    bits=$1 size=$2 batch=0
    while python3 tests/c64-math/gen.py "$bits" "$batch" "$size" "$work/cases.h"; do
        "$LLVM_MOS/bin/mos-cx16-clang" -Os -I "$work" -Dmain=check -c tests/c64-math/check.c -o "$work/check.o"
        "$LLVM_MOS/bin/mos-cx16-clang" -Os "$work/check.o" "$work/harness.c" "$work/arith.c" -o "$work/check.prg"
        if ! out=$(python3 -I cx16/watch.py "${X16_TIMEOUT:-600}" "$emulator" -prg "$work/check.prg" -run -warp -testbench); then
            echo "$out" | head -20
            failed=1
        fi
        total=$((total + $(echo "$out" | awk '/^bad/ { n = $4 } END { print n + 0 }')))
        batch=$((batch + 1))
    done
done
if [ "$failed" -ne 0 ]; then
    echo "FAIL X16 division and multiplication (cx16/runtime.c)"
    exit 1
fi
echo "PASS X16 division and multiplication ($total pairs, 8 to 64 bits)"
