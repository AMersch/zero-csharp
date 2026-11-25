// Tests cx16/math.c on a computer, against the system's math library: the
// largest error in units in the last place, over many values, for each
// function. Build and run:  cc -O1 -o /tmp/test-math cx16/test-math.c -lm && /tmp/test-math
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include "math.c"

static double ulp_error(double got, double want)
{
    if (isnan(want)) return isnan(got) ? 0 : 1e30;
    if (isinf(want)) return got == want ? 0 : 1e30;
    if (got == want) return 0;
    double u = nextafter(fabs(want), INFINITY) - fabs(want);
    if (u == 0) u = 4.9406564584124654e-324;
    return fabs(got - want) / u;
}

static double random_in(double lo, double hi)
{
    return lo + (hi - lo) * ((double)rand() / RAND_MAX);
}

typedef double (*fn1)(double);
typedef double (*fn2)(double, double);

static void test1(const char *name, fn1 f, fn1 ref, double lo, double hi, int count)
{
    double worst = 0, at = 0;
    for (int i = 0; i < count; i++) {
        double x = random_in(lo, hi);
        double e = ulp_error(f(x), ref(x));
        if (e > worst) { worst = e; at = x; }
    }
    printf("%-8s [%g, %g]: worst %.2f ulp at %.17g\n", name, lo, hi, worst, at);
}

static void test2(const char *name, fn2 f, fn2 ref, double lo, double hi, double lo2, double hi2, int count)
{
    double worst = 0, ax = 0, ay = 0;
    for (int i = 0; i < count; i++) {
        double x = random_in(lo, hi), y = random_in(lo2, hi2);
        double e = ulp_error(f(x, y), ref(x, y));
        if (e > worst) { worst = e; ax = x; ay = y; }
    }
    printf("%-8s x [%g, %g] y [%g, %g]: worst %.2f ulp at (%.17g, %.17g)\n", name, lo, hi, lo2, hi2, worst, ax, ay);
}

static double exp_ref(double x) { return exp(x); }
static double log_ref(double x) { return log(x); }
static double log2_ref(double x) { return log2(x); }
static double log10_ref(double x) { return log10(x); }
static double sin_ref(double x) { return sin(x); }
static double cos_ref(double x) { return cos(x); }
static double tan_ref(double x) { return tan(x); }
static double pow_ref(double x, double y) { return pow(x, y); }
static double atan2_ref(double y, double x) { return atan2(y, x); }

int main(void)
{
    srand(12345);
    int n = 200000;
    test1("exp", cs_exp, exp_ref, -700, 700, n);
    test1("exp", cs_exp, exp_ref, -1, 1, n);
    test1("log", cs_log, log_ref, 1e-300, 1e300, n);
    test1("log", cs_log, log_ref, 0.5, 2, n);
    test1("log2", cs_log2, log2_ref, 1e-300, 1e300, n);
    test1("log2", cs_log2, log2_ref, 0.5, 2, n);
    test1("log10", cs_log10, log10_ref, 1e-300, 1e300, n);
    test1("log10", cs_log10, log10_ref, 0.5, 2, n);
    test1("sin", cs_sin, sin_ref, -10, 10, n);
    test1("sin", cs_sin, sin_ref, -1e5, 1e5, n);
    test1("sin", cs_sin, sin_ref, -1.6e6, 1.6e6, n);
    test1("cos", cs_cos, cos_ref, -10, 10, n);
    test1("cos", cs_cos, cos_ref, -1e5, 1e5, n);
    test1("tan", cs_tan, tan_ref, -10, 10, n);
    test1("tan", cs_tan, tan_ref, -1e5, 1e5, n);
    test2("atan2", cs_atan2, atan2_ref, -10, 10, -10, 10, n);
    test2("atan2", cs_atan2, atan2_ref, -1e6, 1e6, -1, 1, n);
    test2("pow", cs_pow, pow_ref, 0.001, 1000, -20, 20, n);
    test2("pow", cs_pow, pow_ref, 0.5, 2, -1000, 1000, n);
    test2("pow", cs_pow, pow_ref, -100, 100, -9, 9, n);   // negative bases, mostly non-integer exponents: NaN
    test2("pow", cs_pow, pow_ref, 1, 10, 0, 300, n);

    // Beyond the exact range of the reduction: the error, in absolute terms.
    double worst = 0;
    for (int i = 0; i < n; i++) {
        double x = random_in(1.6e6, 1e10);
        double e = fabs(cs_sin(x) - sin(x));
        if (e > worst) worst = e;
    }
    printf("sin over 1.6e6..1e10: worst absolute error %.3g\n", worst);

    // Exact values and special cases.
    int bad = 0;
#define EXPECT(cond) do { if (!(cond)) { printf("FAILED: %s\n", #cond); bad++; } } while (0)
    EXPECT(cs_pow(2, 10) == 1024);
    EXPECT(cs_pow(10, 2) == 100);
    EXPECT(cs_pow(2, 0.5) == sqrt(2));
    EXPECT(cs_pow(-2, 3) == -8);
    EXPECT(isnan(cs_pow(-2, 0.5)));
    EXPECT(cs_pow(0, 0) == 1 && cs_pow(0, 3) == 0 && cs_pow(5, 0) == 1);
    EXPECT(isinf(cs_pow(10, 400)) && cs_pow(10, -400) == 0);
    EXPECT(cs_exp(0) == 1 && isinf(cs_exp(1000)) && cs_exp(-1000) == 0 && isnan(cs_exp(NAN)));
    EXPECT(cs_log(1) == 0 && isinf(cs_log(0)) && isnan(cs_log(-1)) && cs_log(M_E) == 1);
    EXPECT(cs_log2(8) == 3 && cs_log2(1) == 0 && cs_log2(0.5) == -1 && cs_log2(1024) == 10);
    EXPECT(cs_log10(1000) == 3 && cs_log10(1) == 0 && cs_log10(100) == 2 && cs_log10(0.001) == -3);
    EXPECT(cs_sin(0) == 0 && cs_cos(0) == 1 && cs_tan(0) == 0);
    EXPECT(isnan(cs_sin(INFINITY)) && isnan(cs_cos(NAN)));
    EXPECT(cs_atan2(0, 1) == 0 && cs_atan2(1, 0) == M_PI_2 && cs_atan2(0, -1) == M_PI && cs_atan2(-1, -1) == -3 * M_PI_4);
    EXPECT(cs_atan2(1, 1) == M_PI_4 && isnan(cs_atan2(NAN, 1)));
    EXPECT(cs_tanhf(0.5f) == tanhf(0.5f) && cs_tanhf(30) == 1 && cs_tanhf(-30) == -1);
    EXPECT(cs_sinf(1) == sinf(1) && cs_cosf(1) == cosf(1) && cs_expf(1) == expf(1) && cs_logf(2) == logf(2) && cs_powf(2, 8) == 256);
    printf(bad == 0 ? "special cases ok\n" : "%d special cases FAILED\n", bad);
    return bad != 0;
}
