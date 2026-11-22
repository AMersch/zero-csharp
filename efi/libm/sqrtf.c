/* musl src/math/x86_64/sqrtf.c and src/math/aarch64/sqrtf.c (1.2.5). */
#include <math.h>

float sqrtf(float x)
{
#if defined(__x86_64__)
	__asm__ ("sqrtss %1, %0" : "=x"(x) : "x"(x));
#else
	__asm__ ("fsqrt %s0, %s1" : "=w"(x) : "w"(x));
#endif
	return x;
}
