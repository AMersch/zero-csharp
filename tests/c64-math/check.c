// Checks c64/runtime.c's division and multiplication against the answers
// in cases.h (gen.py), in llvm-mos's 6502 simulator. Every way a C
// program reaches them: the operators, which the compiler turns into calls
// of __udivsi3 and the like (inline and from functions of their own), and
// the routines that give quotient and remainder at once.
#include <stdio.h>
#include "cases.h"

__attribute__((noinline)) static U udiv(U a, U b) { return a / b; }
__attribute__((noinline)) static U umod(U a, U b) { return a % b; }
__attribute__((noinline)) static S sdiv(S a, S b) { return a / b; }
__attribute__((noinline)) static S smod(S a, S b) { return a % b; }

#if BITS == 8
#define UDIVMOD __udivmodqi4
#define SDIVMOD __divmodqi4
#elif BITS == 16
#define UDIVMOD __udivmodhi4
#define SDIVMOD __divmodhi4
#elif BITS == 32
#define UDIVMOD __udivmodsi4
#define SDIVMOD __divmodsi4
#else
#define UDIVMOD __udivmoddi4
#define SDIVMOD __divmoddi4
#endif
U UDIVMOD(U, U, U *);
S SDIVMOD(S, S, S *);

int main(void)
{
    unsigned bad = 0;
    for (unsigned i = 0; i < sizeof t / sizeof t[0]; i++) {
        volatile U a = t[i][0], b = t[i][1];
        if ((U)(a * b) != t[i][2]) {
            printf("%d-bit %u: multiplication\n", BITS, i);
            bad++;
        }
        if (b == 0)
            continue;
        U q = a / b, r = a % b, r2;
        if (q != t[i][3] || r != t[i][4] || udiv(a, b) != q || umod(a, b) != r
                || UDIVMOD(a, b, &r2) != q || r2 != r) {
            printf("%d-bit %u: unsigned division\n", BITS, i);
            bad++;
        }
        if (a == ((U)1 << (BITS - 1)) && (S)b == -1)
            continue;                   // overflows; C# throws before it gets here
        S sq = (S)a / (S)b, sr = (S)a % (S)b, sr2;
        if ((U)sq != t[i][5] || (U)sr != t[i][6] || sdiv(a, b) != sq || smod(a, b) != sr
                || SDIVMOD(a, b, &sr2) != sq || sr2 != sr) {
            printf("%d-bit %u: signed division\n", BITS, i);
            bad++;
        }
    }
    printf("bad %u of %u\n", bad, (unsigned)(sizeof t / sizeof t[0]));
    return bad != 0;
}
