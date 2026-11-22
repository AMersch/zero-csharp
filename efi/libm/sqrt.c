/* musl src/math/x86_64/sqrt.c and src/math/aarch64/sqrt.c (1.2.5): the
   processor's own square root instruction. */
#include <math.h>

double sqrt(double x)
{
#if defined(__x86_64__)
	__asm__ ("sqrtsd %1, %0" : "=x"(x) : "x"(x));
#else
	__asm__ ("fsqrt %d0, %d1" : "=w"(x) : "w"(x));
#endif
	return x;
}
