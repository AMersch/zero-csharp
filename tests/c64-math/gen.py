# Writes one batch of division and multiplication cases, with .NET's
# answers worked out here, for check.c.
#
#   python3 gen.py BITS BATCH SIZE OUT.h    exits 1 when BATCH is past the end
#
# The cases: every pair of edge values (0, 1, powers of two and their
# neighbours, the extremes, their negations), then random pairs of random
# widths, and random dividends over divisors that fit in 8 and in 16 bits,
# which take their own paths in c64/runtime.c. Seeded, so every run checks
# the same numbers.
import random
import sys

bits, batch, size, out = int(sys.argv[1]), int(sys.argv[2]), int(sys.argv[3]), sys.argv[4]
random.seed(7)
mask = (1 << bits) - 1


def edges():
    half = 1 << (bits - 1)
    e = {0, 1, 2, 3, 7, 10, 37, 40, 127, 128, 255, 256, 257, 1000, 4095, 32767, 32768, 65535, 65536, 65537,
         half - 1, half, half + 1, mask, mask - 1, mask - 9, 0x12345678, 0xDEADBEEF}
    e |= {-x for x in e}
    return sorted({x & mask for x in e})


def randoms(width, n):
    return [random.getrandbits(random.randint(1, width)) for _ in range(n)]


def signed(x):
    return x - (1 << bits) if x >> (bits - 1) else x


def toward_zero(a, b):
    q = abs(a) // abs(b)
    return q if (a < 0) == (b < 0) else -q


pairs = [(a, b) for a in edges() for b in edges()]
pairs += list(zip(randoms(bits, 1000), randoms(bits, 1000)))
pairs += list(zip(randoms(bits, 500), randoms(min(bits, 8), 500)))
pairs += list(zip(randoms(bits, 500), randoms(min(bits, 16), 500)))
chunk = pairs[batch * size:(batch + 1) * size]
if not chunk:
    sys.exit(1)

ctype = {8: "unsigned char", 16: "unsigned short", 32: "unsigned long", 64: "unsigned long long"}[bits]
suffix = "ULL" if bits == 64 else "UL"
lines = [f"#define BITS {bits}", f"typedef {ctype} U;", f"typedef signed {ctype.split(' ', 1)[1]} S;",
         "// a, b, a * b, a / b, a % b unsigned, then signed", "static U t[][7] = {"]
for a, b in chunk:
    if b == 0:
        q = r = sq = sr = 0
    else:
        q, r = a // b, a % b
        sa, sb = signed(a), signed(b)
        tq = toward_zero(sa, sb)
        sq, sr = tq & mask, (sa - tq * sb) & mask
    lines.append("  {" + ",".join(f"{v}{suffix}" for v in (a, b, (a * b) & mask, q, r, sq, sr)) + "},")
lines.append("};")
open(out, "w").write("\n".join(lines) + "\n")
