// The math library's functions that llvm-mos's C library doesn't have, for
// cx16/runtime.c (it includes this when the program calls any of them: the
// build defines CX16_MATH). They are the algorithms of FreeBSD's msun (fdlibm),
// which also lies under musl and every BSD: double precision, an error of
// about 1 unit in the last place or less. The float versions compute in double
// and round.
//
// Exp, log, log2, log10, sin, cos, tan, pow and atan2 are what lib's Math
// imports; the rest of Math (Asin, Sinh, Cbrt, ...) is built on those in C#.
// Sin, cos and tan reduce their argument exactly up to about 1.6 million
// radians; beyond that the result is good to a few digits, not to the last.
//
// This file stands alone: the C function names have the prefix cs_ that the
// build gives imports, and nothing here needs the runtime, so it can be tested
// on a computer against its own math library (see cx16/test-math.c).
#include <stdint.h>

double sqrt(double x);      // the runtime's, or the C library's when tested on a computer

// A double as two 32-bit words (little endian, as on the 6502 and on every
// computer this is tested on). Shifting a 64-bit integer instead costs the 6502
// hundreds of bytes at every use.
typedef union { double f; struct { uint32_t lo, hi; } w; } math_bits;

// The public functions are kept out of line (noinline): the float ones call the double ones
// for what they can't do, and inlined, the whole of pow would be in powf, a function too
// big to move into a bank.

static int32_t hi_word(double x) { math_bits u; u.f = x; return (int32_t)u.w.hi; }
static uint32_t lo_word(double x) { math_bits u; u.f = x; return u.w.lo; }
static double with_hi(double x, int32_t hi) { math_bits u; u.f = x; u.w.hi = (uint32_t)hi; return u.f; }
static double from_words(int32_t hi, uint32_t lo) { math_bits u; u.w.hi = (uint32_t)hi; u.w.lo = lo; return u.f; }

static double math_fabs(double x) { return x < 0 ? -x : x; }

// c[0] + t * (c[1] + t * (c[2] + ...)): the same operations, in the same order,
// as a polynomial written out, but in a loop. Each double operation is a call of
// some 100 bytes at its call site on this machine, so written out, a
// polynomial of a dozen terms is more code than the rest of its function.
__attribute__((noinline)) static double horner(double t, const double *c, int n)
{
    double sum = c[--n];
    while (n > 0)
        sum = c[--n] + t * sum;
    return sum;
}

// x * 2^n, for any n the result of which doesn't overflow in the middle.
static double scale_pow2(double x, int32_t n)
{
    while (n > 1000) { x *= from_words(0x7FE00000L, 0); n -= 1023; }       // 2^1023
    while (n < -1000) { x *= from_words(0x00100000L, 0); n += 1022; }      // 2^-1022
    return x * from_words((int32_t)(0x3FF + n) << 20, 0);
}

static const double huge = 1.0e300, tiny = 1.0e-300;

// ---- exp ----------------------------------------------------------------------

__attribute__((noinline)) double cs_exp(double x)
{
    static const double halF[2] = { 0.5, -0.5 };
    static const double ln2HI[2] = { 6.93147180369123816490e-01, -6.93147180369123816490e-01 };
    static const double ln2LO[2] = { 1.90821492927058770002e-10, -1.90821492927058770002e-10 };
    static const double invln2 = 1.44269504088896338700e+00;
    static const double P[] = { 1.66666666666666019037e-01, -2.77777777770155933842e-03,
        6.61375632143793436117e-05, -1.65339022054652515390e-06, 4.13813679705723846039e-08 };

    double y, hi = 0, lo = 0, c, t;
    int32_t k = 0, xsb;
    uint32_t hx = (uint32_t)hi_word(x);
    xsb = (int32_t)((hx >> 31) & 1);
    hx &= 0x7FFFFFFFul;

    if (hx >= 0x40862E42ul) {                       // |x| >= 709.78...
        if (hx >= 0x7FF00000ul) {
            if (((hx & 0xFFFFFul) | lo_word(x)) != 0)
                return x + x;                       // NaN
            return xsb == 0 ? x : 0.0;              // exp(+-inf)
        }
        if (x > 7.09782712893383973096e+02)
            return huge * huge;
        if (x < -7.45133219101941108420e+02)
            return tiny * tiny;
    }

    if (hx > 0x3FD62E42ul) {                        // |x| > 0.5 ln2
        if (hx < 0x3FF0A2B2ul) {                    // and |x| < 1.5 ln2
            hi = x - ln2HI[xsb];
            lo = ln2LO[xsb];
            k = 1 - xsb - xsb;
        } else {
            k = (int32_t)(invln2 * x + halF[xsb]);
            t = k;
            hi = x - t * ln2HI[0];
            lo = t * ln2LO[0];
        }
        x = hi - lo;
    } else if (hx < 0x3E300000ul) {                 // |x| < 2^-28
        return 1.0 + x;
    }

    t = x * x;
    c = x - t * horner(t, P, 5);
    if (k == 0)
        return 1.0 - ((x * c) / (c - 2.0) - x);
    y = 1.0 - ((lo - (x * c) / (2.0 - c)) - hi);
    return scale_pow2(y, k);
}


// ---- log ----------------------------------------------------------------------

static const double ln2_hi = 6.93147180369123816490e-01, ln2_lo = 1.90821492927058770002e-10;
// Lg1 ... Lg7, odd ones and even ones, for the two polynomials in w = z * z.
static const double Lodd[] = { 6.666666666666735130e-01, 2.857142874366239149e-01,
    1.818357216161805012e-01, 1.479819860511658591e-01 };
static const double Leven[] = { 3.999999999940941908e-01, 2.222219843214978396e-01, 1.531383769920937332e-01 };

// log(m) for m in about [sqrt(1/2), sqrt(2)), and the power of two that went
// out of x to get there (x = m * 2^k). NaN or an infinity for the special
// values, and then k is 0 and *special is 1.
static double log_mantissa(double x, int32_t *k, int *special)
{
    double f, hfsq, s, z, w, t1, t2, R;
    int32_t hx = hi_word(x), i, j;
    uint32_t lx = lo_word(x);
    *k = 0;
    *special = 0;
    if (hx < 0x00100000L) {                         // x < 2^-1022
        if ((((uint32_t)hx & 0x7FFFFFFFul) | lx) == 0) { *special = 1; return -1.0 / 0.0 * 1.0; }   // log(0)
        if (hx < 0) { *special = 1; return (x - x) / 0.0; }                                        // log(<0)
        *k -= 54;
        x *= 18014398509481984.0;                   // 2^54
        hx = hi_word(x);
    }
    if (hx >= 0x7FF00000L) { *special = 1; return x + x; }
    *k += (hx >> 20) - 1023;
    hx &= 0x000FFFFFL;
    i = (hx + 0x95F64L) & 0x100000L;
    x = with_hi(x, hx | (i ^ 0x3FF00000L));         // normalize x or x/2
    *k += (i >> 20);
    f = x - 1.0;
    if ((0x000FFFFFL & (2 + hx)) < 3) {             // -2^-20 <= f < 2^-20
        if (f == 0.0)
            return 0.0;
        R = f * f * (0.5 - 0.33333333333333333 * f);
        return f - R;
    }
    s = f / (2.0 + f);
    z = s * s;
    i = hx - 0x6147AL;
    w = z * z;
    j = 0x6B851L - hx;
    t1 = w * horner(w, Leven, 3);
    t2 = z * horner(w, Lodd, 4);
    i |= j;
    R = t2 + t1;
    if (i > 0) {
        hfsq = 0.5 * f * f;
        return f - (hfsq - s * (hfsq + R));
    }
    return f - s * (f - R);
}

__attribute__((noinline)) double cs_log(double x)
{
    double f, hfsq, s, z, w, t1, t2, R, dk;
    int32_t k, hx, i, j;
    uint32_t lx;

    hx = hi_word(x);
    lx = lo_word(x);
    k = 0;
    if (hx < 0x00100000L) {
        if ((((uint32_t)hx & 0x7FFFFFFFul) | lx) == 0)
            return -1.0 / 0.0;                      // log(+-0) = -inf
        if (hx < 0)
            return (x - x) / 0.0;                   // log(<0) = NaN
        k -= 54;
        x *= 18014398509481984.0;
        hx = hi_word(x);
    }
    if (hx >= 0x7FF00000L)
        return x + x;
    k += (hx >> 20) - 1023;
    hx &= 0x000FFFFFL;
    i = (hx + 0x95F64L) & 0x100000L;
    x = with_hi(x, hx | (i ^ 0x3FF00000L));
    k += (i >> 20);
    f = x - 1.0;
    if ((0x000FFFFFL & (2 + hx)) < 3) {
        if (f == 0.0) {
            if (k == 0)
                return 0.0;
            dk = (double)k;
            return dk * ln2_hi + dk * ln2_lo;
        }
        R = f * f * (0.5 - 0.33333333333333333 * f);
        if (k == 0)
            return f - R;
        dk = (double)k;
        return dk * ln2_hi - ((R - dk * ln2_lo) - f);
    }
    s = f / (2.0 + f);
    dk = (double)k;
    z = s * s;
    i = hx - 0x6147AL;
    w = z * z;
    j = 0x6B851L - hx;
    t1 = w * horner(w, Leven, 3);
    t2 = z * horner(w, Lodd, 4);
    i |= j;
    R = t2 + t1;
    if (i > 0) {
        hfsq = 0.5 * f * f;
        if (k == 0)
            return f - (hfsq - s * (hfsq + R));
        return dk * ln2_hi - ((hfsq - (s * (hfsq + R) + dk * ln2_lo)) - f);
    }
    if (k == 0)
        return f - s * (f - R);
    return dk * ln2_hi - ((s * (f - R) - dk * ln2_lo) - f);
}


double cs_log2(double x)
{
    static const double ivln2 = 1.44269504088896338700e+00;
    int32_t k;
    int special;
    double m = log_mantissa(x, &k, &special);
    if (special)
        return m;
    return (double)k + m * ivln2;       // k is exact; m * ivln2 is within an ulp
}

double cs_log10(double x)
{
    static const double ivln10 = 4.34294481903251816668e-01, log10_2hi = 3.01029995663611771306e-01,
        log10_2lo = 3.69423907715893078616e-13;
    int32_t k;
    int special;
    double m = log_mantissa(x, &k, &special);
    if (special)
        return m;
    double dk = (double)k;
    return dk * log10_2lo + ivln10 * m + dk * log10_2hi;
}

// ---- sin, cos, tan ---------------------------------------------------------------

static const double S1 = -1.66666666666666324348e-01;
static const double S2to6[] = { 8.33333333332248946124e-03, -1.98412698298579493134e-04,
    2.75573137070700676789e-06, -2.50507602534068634195e-08, 1.58969099521155010221e-10 };
static const double C1to3[] = { 4.16666666666666019037e-02, -1.38888888888741095749e-03, 2.48015872894767294178e-05 };
static const double C4to6[] = { -2.75573143513906633035e-07, 2.08757232129817482790e-09, -1.13596475577881948265e-11 };

static double kernel_sin(double x, double y, int iy)
{
    double z = x * x, v = z * x;
    double r = horner(z, S2to6, 5);
    if (iy == 0)
        return x + v * (S1 + z * r);
    return x - ((z * (0.5 * y - v * r) - y) - v * S1);
}

static double kernel_cos(double x, double y)
{
    double z = x * x, w = z * z;
    double r = z * horner(z, C1to3, 3) + w * w * horner(z, C4to6, 3);
    double hz = 0.5 * z;
    w = 1.0 - hz;
    return w + (((1.0 - w) - hz) + (z * r - x * y));
}

// The remainder of x / y, exactly (x >= 0, y > 0, both finite): long division on the bits.
static double math_fmod(double x, double y)
{
    int32_t ex = (hi_word(x) >> 20) & 0x7FF, ey = (hi_word(y) >> 20) & 0x7FF;
    uint64_t mx, my;
    if (ex < ey)
        return x;
    mx = ((uint64_t)((uint32_t)hi_word(x) & 0xFFFFFul) << 32 | lo_word(x)) | 0x10000000000000ull;
    my = ((uint64_t)((uint32_t)hi_word(y) & 0xFFFFFul) << 32 | lo_word(y)) | 0x10000000000000ull;
    for (; ex > ey; ex--) {
        if (mx >= my)
            mx -= my;
        mx <<= 1;
    }
    if (mx >= my)
        mx -= my;
    if (mx == 0)
        return 0.0;
    while (mx < 0x10000000000000ull) {      // normalize
        mx <<= 1;
        ey--;
    }
    return from_words((int32_t)(((uint32_t)ey << 20) | (uint32_t)((mx >> 32) & 0xFFFFFul)), (uint32_t)mx);
}

// x = n * pi/2 + (y[0] + y[1]); returns n (mod 8 is right).
static int32_t rem_pio2(double x, double *y)
{
    static const double pio2_1 = 1.57079632673412561417e+00, pio2_1t = 6.07710050650619224932e-11,
        pio2_2 = 6.07710050630396597660e-11, pio2_2t = 2.02226624879595063154e-21,
        pio2_3 = 2.02226624871116645580e-21, pio2_3t = 8.47842766036889956997e-32,
        invpio2 = 6.36619772367581382433e-01, toint = 6.755399441055744e15;
    double fn, r, w, t;
    int32_t n, ex, ey, ix = hi_word(x) & 0x7FFFFFFFL;

    // Beyond about 2^20 * pi/2 the three-part pi below isn't enough: take whole
    // turns off first (n mod 4 doesn't change). The remainder after the turns of the
    // double nearest 2 pi is exact, but that double isn't 2 pi: the result is good to
    // 16 - log10(x / 2 pi) digits or so, which is what such an argument can have.
    if (ix > 0x413921FBL) {                         // |x| > 1.6e6
        double r = math_fmod(math_fabs(x), 6.283185307179586);
        x = x < 0 ? -r : r;
        ix = hi_word(x) & 0x7FFFFFFFL;
    }

    fn = x * invpio2 + toint - toint;
    n = (int32_t)fn;
    r = x - fn * pio2_1;
    w = fn * pio2_1t;
    y[0] = r - w;
    ey = (int32_t)((hi_word(y[0]) >> 20) & 0x7FF);
    ex = ix >> 20;
    if (ex - ey > 16) {
        t = r;
        w = fn * pio2_2;
        r = t - w;
        w = fn * pio2_2t - ((t - r) - w);
        y[0] = r - w;
        ey = (int32_t)((hi_word(y[0]) >> 20) & 0x7FF);
        if (ex - ey > 49) {
            t = r;
            w = fn * pio2_3;
            r = t - w;
            w = fn * pio2_3t - ((t - r) - w);
            y[0] = r - w;
        }
    }
    y[1] = (r - y[0]) - w;
    return n;
}

__attribute__((noinline)) double cs_sin(double x)
{
    double y[2];
    int32_t ix = hi_word(x) & 0x7FFFFFFFL, n;
    if (ix <= 0x3FE921FBL) {
        if (ix < 0x3E500000L)
            return x;
        return kernel_sin(x, 0.0, 0);
    }
    if (ix >= 0x7FF00000L)
        return x - x;
    n = rem_pio2(x, y);
    switch (n & 3) {
    case 0: return kernel_sin(y[0], y[1], 1);
    case 1: return kernel_cos(y[0], y[1]);
    case 2: return -kernel_sin(y[0], y[1], 1);
    default: return -kernel_cos(y[0], y[1]);
    }
}

__attribute__((noinline)) double cs_cos(double x)
{
    double y[2];
    int32_t ix = hi_word(x) & 0x7FFFFFFFL, n;
    if (ix <= 0x3FE921FBL) {
        if (ix < 0x3E46A09EL)
            return 1.0;
        return kernel_cos(x, 0.0);
    }
    if (ix >= 0x7FF00000L)
        return x - x;
    n = rem_pio2(x, y);
    switch (n & 3) {
    case 0: return kernel_cos(y[0], y[1]);
    case 1: return -kernel_sin(y[0], y[1], 1);
    case 2: return -kernel_cos(y[0], y[1]);
    default: return kernel_sin(y[0], y[1], 1);
    }
}

double cs_tan(double x)
{
    double y[2];
    int32_t ix = hi_word(x) & 0x7FFFFFFFL, n;
    if (ix <= 0x3FE921FBL) {
        if (ix < 0x3E400000L)
            return x;
        return kernel_sin(x, 0.0, 0) / kernel_cos(x, 0.0);
    }
    if (ix >= 0x7FF00000L)
        return x - x;
    n = rem_pio2(x, y);
    if ((n & 1) == 0)
        return kernel_sin(y[0], y[1], 1) / kernel_cos(y[0], y[1]);
    return -kernel_cos(y[0], y[1]) / kernel_sin(y[0], y[1], 1);
}


// ---- atan2 -------------------------------------------------------------------------

static double math_atan(double x)
{
    static const double atanhi[] = { 4.63647609000806093515e-01, 7.85398163397448278999e-01,
        9.82793723247329054082e-01, 1.57079632679489655800e+00 };
    static const double atanlo[] = { 2.26987774529616870924e-17, 3.06161699786838301793e-17,
        1.39033110312309984516e-17, 6.12323399573676603587e-17 };
    static const double aTeven[] = { 3.33333333333329318027e-01, 1.42857142725034663711e-01,
        9.09088713343650656196e-02, 6.66107313738753120669e-02, 4.97687799461593236017e-02,
        1.62858201153657823623e-02 };           // aT[0], aT[2], ... aT[10]
    static const double aTodd[] = { -1.99999999998764832476e-01, -1.11111104054623557880e-01,
        -7.69187620504482999495e-02, -5.83357013379057348645e-02, -3.65315727442169155270e-02 };  // aT[1], aT[3], ...
    double w, s1, s2, z;
    int32_t ix, hx, id;

    hx = hi_word(x);
    ix = hx & 0x7FFFFFFFL;
    if (ix >= 0x44100000L) {                        // |x| >= 2^66
        if (ix > 0x7FF00000L || (ix == 0x7FF00000L && lo_word(x) != 0))
            return x + x;
        return hx > 0 ? atanhi[3] + atanlo[3] : -atanhi[3] - atanlo[3];
    }
    if (ix < 0x3FDC0000L) {                         // |x| < 0.4375
        if (ix < 0x3E200000L)
            return x;
        id = -1;
    } else {
        x = math_fabs(x);
        if (ix < 0x3FF30000L) {                     // |x| < 1.1875
            if (ix < 0x3FE60000L) { id = 0; x = (2.0 * x - 1.0) / (2.0 + x); }
            else { id = 1; x = (x - 1.0) / (x + 1.0); }
        } else {
            if (ix < 0x40038000L) { id = 2; x = (x - 1.5) / (1.0 + 1.5 * x); }
            else { id = 3; x = -1.0 / x; }
        }
    }
    z = x * x;
    w = z * z;
    s1 = z * horner(w, aTeven, 6);
    s2 = w * horner(w, aTodd, 5);
    if (id < 0)
        return x - x * (s1 + s2);
    z = atanhi[id] - ((x * (s1 + s2) - atanlo[id]) - x);
    return hx < 0 ? -z : z;
}

// The angle of the point (x, y); the argument order is C's atan2(y, x).
double cs_atan2(double y, double x)
{
    static const double pi_o_4 = 7.8539816339744827900E-01, pi_o_2 = 1.5707963267948965580E+00,
        pi = 3.1415926535897931160E+00, pi_lo = 1.2246467991473531772E-16;
    double z;
    int32_t k, m, hx, hy, ix, iy;
    uint32_t lx, ly;

    hx = hi_word(x); ix = hx & 0x7FFFFFFFL; lx = lo_word(x);
    hy = hi_word(y); iy = hy & 0x7FFFFFFFL; ly = lo_word(y);
    if (((uint32_t)ix | ((lx | -lx) >> 31)) > 0x7FF00000ul || ((uint32_t)iy | ((ly | -ly) >> 31)) > 0x7FF00000ul)
        return x + y;                               // NaN
    if (((hx - 0x3FF00000L) | (int32_t)lx) == 0)
        return math_atan(y);                        // x = 1.0
    m = ((hy >> 31) & 1) | ((hx >> 30) & 2);        // 2 * sign(x) + sign(y)

    if ((iy | (int32_t)ly) == 0) {                  // y = 0
        switch (m) {
        case 0:
        case 1: return y;
        case 2: return pi + tiny;
        default: return -pi - tiny;
        }
    }
    if ((ix | (int32_t)lx) == 0)
        return (hy < 0) ? -pi_o_2 - tiny : pi_o_2 + tiny;  // x = 0
    if (ix == 0x7FF00000L) {                        // x is inf
        if (iy == 0x7FF00000L) {
            switch (m) {
            case 0: return pi_o_4 + tiny;
            case 1: return -pi_o_4 - tiny;
            case 2: return 3.0 * pi_o_4 + tiny;
            default: return -3.0 * pi_o_4 - tiny;
            }
        }
        switch (m) {
        case 0: return 0.0;
        case 1: return -0.0;
        case 2: return pi + tiny;
        default: return -pi - tiny;
        }
    }
    if (iy == 0x7FF00000L)
        return (hy < 0) ? -pi_o_2 - tiny : pi_o_2 + tiny;   // y is inf

    k = (iy - ix) >> 20;
    if (k > 60) { z = pi_o_2 + 0.5 * pi_lo; m &= 1; }
    else if (hx < 0 && k < -60) z = 0.0;
    else z = math_atan(math_fabs(y / x));
    switch (m) {
    case 0: return z;
    case 1: return -z;
    case 2: return pi - (z - pi_lo);
    default: return (z - pi_lo) - pi;
    }
}

// ---- pow ---------------------------------------------------------------------------
//
// A whole-number exponent is done by squaring, which is exact where the result
// is (2^10, 10^3, ...) and good to a few units in the last place otherwise.
// Any other exponent goes through exp(y * log(x)), which is good to about
// |y * log(x)| units in the last place: 1e-14 up to results near e^100, a
// tenth of a percent of a digit for each doubling beyond. (fdlibm's own pow
// computes the logarithm with twice the bits, at 12 KB of code on this machine.)

__attribute__((noinline)) double cs_pow(double x, double y)
{
    int32_t hx = hi_word(x), hy = hi_word(y), ix = hx & 0x7FFFFFFFL, iy = hy & 0x7FFFFFFFL;
    uint32_t lx = lo_word(x), ly = lo_word(y);
    int whole, odd = 0;
    double result, magnitude;

    if ((iy | (int32_t)ly) == 0)
        return 1.0;                                 // x**0
    if (hx == 0x3FF00000L && lx == 0)
        return 1.0;                                 // 1**y
    if (x != x || y != y)
        return x + y;                               // NaN

    // Is y a whole number, and an odd one? (From its bits: above 2^53 all are even.)
    whole = 0;
    if (iy >= 0x43400000L) {
        whole = 1;
    } else if (iy >= 0x3FF00000L) {
        int32_t e = (iy >> 20) - 0x3FF;             // y = m * 2^e, the point after the first e bits
        if (e > 20) {
            uint32_t bit = 1ul << (52 - e);
            if ((ly & (bit - 1)) == 0) { whole = 1; odd = (ly & bit) != 0; }
        } else if (ly == 0) {
            int32_t bit = 1L << (20 - e);
            if ((iy & (bit - 1)) == 0) { whole = 1; odd = (iy & bit) != 0; }
        }
    }

    if (hy == 0x3FE00000L && ly == 0 && hx >= 0 && ix != 0x7FF00000L)
        return sqrt(x);                             // y is 0.5

    if (iy == 0x7FF00000L && ly == 0) {             // y is +-inf
        if (ix == 0x3FF00000L && lx == 0)
            return 1.0;                             // (-1)**inf
        if (ix >= 0x3FF00000L)
            return hy >= 0 ? y : 0.0;
        return hy < 0 ? -y : 0.0;
    }
    magnitude = math_fabs(x);
    if (ix == 0x7FF00000L || (ix | (int32_t)lx) == 0) {     // x is +-inf or +-0
        result = hy < 0 ? 1.0 / magnitude : magnitude;      // |x|**y
        return (hx < 0 && odd) ? -result : result;
    }
    if (hx < 0 && !whole)
        return (x - x) / (x - x);                   // (x<0)**(non-whole): NaN

    if (whole && iy < 0x40D00000L) {                // |y| < 16384: by squaring
        uint32_t n = (uint32_t)math_fabs(y);
        double base = magnitude;
        result = 1.0;
        while (n != 0) {
            if (n & 1)
                result *= base;
            n >>= 1;
            if (n != 0)
                base *= base;
        }
        if (hy < 0)
            result = 1.0 / result;
    } else {
        result = cs_exp(y * cs_log(magnitude));
    }
    return (hx < 0 && odd) ? -result : result;
}

// ---- single precision ----------------------------------------------------------------
//
// MathF's functions, computed in float: the 6502's float is 32 bits in software,
// a third of the code and about a quarter of the time of its double, and good to
// 24 bits, a few units in the last place of that (1e-7 relative or so). These are
// FreeBSD's e_expf and e_logf, and short polynomials for sin and cos. They use no
// double at all, so a program that has only floats doesn't carry the 18 KB of
// the double arithmetic. Past what a float can hold exactly, they give up some
// digits (sin and cos beyond 8192 radians, pow with a large exponent).

typedef union { float f; uint32_t i; } float_bits;

static uint32_t float_word(float x) { float_bits u; u.f = x; return u.i; }
static float float_from(uint32_t i) { float_bits u; u.i = i; return u.f; }

// x * 2^n for n in about -150 .. 130.
static float scale_pow2f(float x, int32_t n)
{
    if (n > 120) { x *= float_from(0x7E800000ul); n -= 126; }       // 2^126
    if (n < -120) { x *= float_from(0x0D800000ul); n += 100; }      // 2^-100
    return x * float_from((uint32_t)(127 + n) << 23);
}

__attribute__((noinline)) float cs_expf(float x)
{
    static const float ln2HI = 6.9313812256e-01f, ln2LO = 9.0580006145e-06f, invln2 = 1.4426950216e+00f,
        P1 = 1.6666667163e-01f, P2 = -2.7777778450e-03f;
    float y, hi = 0, lo = 0, c, t;
    int32_t k = 0;
    uint32_t hx = float_word(x), sign = hx >> 31;
    hx &= 0x7FFFFFFFul;

    if (hx >= 0x42B17218ul) {                       // |x| >= 88.72
        if (x != x)
            return x;
        if (sign == 0 && x > 88.7228f)
            return 1.0f / 0.0f;                     // overflow
        if (sign != 0 && hx >= 0x42CFF1B5ul)
            return 0.0f;                            // x <= -103.97
    }
    if (hx > 0x3EB17218ul) {                        // |x| > 0.5 ln2
        k = (int32_t)(invln2 * x + (sign ? -0.5f : 0.5f));
        t = (float)k;
        hi = x - t * ln2HI;
        lo = t * ln2LO;
        x = hi - lo;
    } else if (hx < 0x39000000ul) {                 // |x| < 2^-13
        return 1.0f + x;
    }
    t = x * x;
    c = x - t * (P1 + t * P2);
    if (k == 0)
        return 1.0f - ((x * c) / (c - 2.0f) - x);
    y = 1.0f - ((lo - (x * c) / (2.0f - c)) - hi);
    return scale_pow2f(y, k);
}

__attribute__((noinline)) float cs_logf(float x)
{
    static const float ln2_hi = 6.9313812256e-01f, ln2_lo = 9.0580006145e-06f, two25 = 3.355443200e+07f,
        Lg1 = 6.6666662693e-01f, Lg2 = 4.0000972152e-01f, Lg3 = 2.8498786688e-01f, Lg4 = 2.4279078841e-01f;
    float f, s, z, w, R, hfsq, dk, t1, t2;
    int32_t k = 0, i, j, ix;

    ix = (int32_t)float_word(x);
    if (ix < 0x00800000L) {                         // x < 2^-126, or negative
        if ((ix & 0x7FFFFFFFL) == 0)
            return -1.0f / 0.0f;                    // log(+-0)
        if (ix < 0)
            return (x - x) / 0.0f;                  // log(<0)
        k -= 25;
        x *= two25;
        ix = (int32_t)float_word(x);
    }
    if (ix >= 0x7F800000L)
        return x + x;
    k += (ix >> 23) - 127;
    ix &= 0x007FFFFFL;
    i = (ix + (0x95F64L << 3)) & 0x800000L;
    x = float_from((uint32_t)(ix | (i ^ 0x3F800000L)));     // normalize x or x/2
    k += (i >> 23);
    f = x - 1.0f;
    if ((0x007FFFFFL & (15 + ix)) < 16) {           // |f| < 2^-20
        if (f == 0.0f) {
            if (k == 0)
                return 0.0f;
            dk = (float)k;
            return dk * ln2_hi + dk * ln2_lo;
        }
        R = f * f * (0.5f - 0.33333333333333333f * f);
        if (k == 0)
            return f - R;
        dk = (float)k;
        return dk * ln2_hi - ((R - dk * ln2_lo) - f);
    }
    s = f / (2.0f + f);
    dk = (float)k;
    z = s * s;
    i = ix - (0x6147AL << 3);
    w = z * z;
    j = (0x6B851L << 3) - ix;
    t1 = w * (Lg2 + w * Lg4);
    t2 = z * (Lg1 + w * Lg3);
    i |= j;
    R = t2 + t1;
    if (i > 0) {
        hfsq = 0.5f * f * f;
        if (k == 0)
            return f - (hfsq - s * (hfsq + R));
        return dk * ln2_hi - ((hfsq - (s * (hfsq + R) + dk * ln2_lo)) - f);
    }
    if (k == 0)
        return f - s * (f - R);
    return dk * ln2_hi - ((s * (f - R) - dk * ln2_lo) - f);
}

// sin and cos of a float: the argument taken to within pi/4 of 0 with pi/2 in three
// pieces (each exact in a few bits, so n times it is exact), then a polynomial.
// Beyond 8192 radians, n would need more bits than the pieces have.
static float sinf_poly(float x)     // |x| <= pi/4
{
    float z = x * x;
    return x + x * z * (-1.6666667163e-01f + z * (8.3333337680e-03f + z * (-1.9841270114e-04f + z * 2.7557314297e-06f)));
}

static float cosf_poly(float x)     // |x| <= pi/4
{
    float z = x * x;
    return 1.0f - 0.5f * z + z * z * (4.1666667908e-02f + z * (-1.3888889225e-03f + z * (2.4801587642e-05f + z * -2.7557314297e-07f)));
}

// The remainder of x / y, exactly (x >= 0, y > 0, finite): long division on the bits.
static float math_fmodf(float x, float y)
{
    uint32_t ux = float_word(x), uy = float_word(y);
    int32_t ex = (int32_t)((ux >> 23) & 0xFF), ey = (int32_t)((uy >> 23) & 0xFF);
    uint32_t mx = (ux & 0x7FFFFFul) | 0x800000ul, my = (uy & 0x7FFFFFul) | 0x800000ul;
    if (ex < ey)
        return x;
    for (; ex > ey; ex--) {
        if (mx >= my)
            mx -= my;
        mx <<= 1;
    }
    if (mx >= my)
        mx -= my;
    if (mx == 0)
        return 0.0f;
    while (mx < 0x800000ul) {
        mx <<= 1;
        ey--;
    }
    return float_from(((uint32_t)ey << 23) | (mx & 0x7FFFFFul));
}

// A float beyond 8192 radians, taken within a few turns of 0: whole turns of the float
// nearest 2 pi come off exactly, but that isn't 2 pi, so every factor of ten in the size
// of x costs a digit of the result.
static float few_turns(float x)
{
    float r = math_fmodf(x < 0 ? -x : x, 6.2831855f);
    return x < 0 ? -r : r;
}

// Returns n, and the remainder in *r: x = n * pi/2 + *r.
static int32_t reduce_halfpi(float x, float *r)
{
    static const float P1 = 1.5703125f, P2 = 4.837512969970703125e-4f, P3 = 7.54978995489188216e-8f,
        twoOverPi = 6.3661977237e-01f;
    float n = (float)(int32_t)(x * twoOverPi + (x < 0 ? -0.5f : 0.5f));
    *r = ((x - n * P1) - n * P2) - n * P3;
    return (int32_t)n;
}

float cs_sinf(float x)
{
    float r;
    uint32_t hx = float_word(x) & 0x7FFFFFFFul;
    if (hx < 0x32000000ul)
        return x;                                   // |x| < 2^-27
    if (hx >= 0x7F800000ul)
        return x - x;                               // infinity or NaN
    if (hx > 0x46000000ul)
        x = few_turns(x);                           // |x| > 8192
    switch (reduce_halfpi(x, &r) & 3) {
    case 0: return sinf_poly(r);
    case 1: return cosf_poly(r);
    case 2: return -sinf_poly(r);
    default: return -cosf_poly(r);
    }
}

float cs_cosf(float x)
{
    float r;
    uint32_t hx = float_word(x) & 0x7FFFFFFFul;
    if (hx < 0x32000000ul)
        return 1.0f;
    if (hx >= 0x7F800000ul)
        return x - x;
    if (hx > 0x46000000ul)
        x = few_turns(x);
    switch (reduce_halfpi(x, &r) & 3) {
    case 0: return cosf_poly(r);
    case 1: return -sinf_poly(r);
    case 2: return -cosf_poly(r);
    default: return sinf_poly(r);
    }
}

// tanh: a series for small values, else through exp.
float cs_tanhf(float x)
{
    float a = x < 0 ? -x : x, t;
    if (x != x)
        return x;
    if (a > 9.0f)
        return x < 0 ? -1.0f : 1.0f;                // 1 - tanh(9) is below 2^-26
    if (a < 0.25f) {
        float z = x * x;
        return x * (1.0f + z * (-3.3333334327e-01f + z * (1.3333334029e-01f + z * (-5.3968254477e-02f + z * 2.1869488060e-02f))));
    }
    t = cs_expf(2.0f * a);
    t = 1.0f - 2.0f / (t + 1.0f);
    return x < 0 ? -t : t;
}

// x ** y: a whole number of up to 64 by squaring, which is exact where the result
// is, else exp(y * log(x)), good to about |y * log(x)| units in the last place.
float cs_powf(float x, float y)
{
    uint32_t hx = float_word(x), hy = float_word(y), ax = hx & 0x7FFFFFFFul, ay = hy & 0x7FFFFFFFul;
    float magnitude, result, base;
    int whole = 0, odd = 0;

    if (ay == 0 || hx == 0x3F800000ul)
        return 1.0f;                                // x ** 0 and 1 ** y
    if (ax > 0x7F800000ul || ay > 0x7F800000ul)
        return x + y;                               // NaN

    if (ay >= 0x4B800000ul) {                       // |y| >= 2^24: whole and even (or infinite)
        whole = 1;
    } else if (ay >= 0x3F800000ul) {                // whole if the bits after the point are 0
        int32_t e = (int32_t)(ay >> 23) - 127;
        uint32_t fraction = ay & (0x7FFFFFul >> e);
        if (fraction == 0) {
            whole = 1;
            odd = (ay >> (23 - e)) & 1;
        }
    }
    if (ay == 0x7F800000ul) {                       // y is +-inf
        if (ax == 0x3F800000ul)
            return 1.0f;
        return ((ax > 0x3F800000ul) == (hy >> 31 == 0)) ? 1.0f / 0.0f : 0.0f;
    }
    magnitude = float_from(ax);
    if (ax == 0 || ax == 0x7F800000ul) {            // x is +-0 or +-inf
        result = (hy >> 31) ? 1.0f / magnitude : magnitude;
        return (hx >> 31 && odd) ? -result : result;
    }
    if ((hx >> 31) && !whole)
        return (x - x) / (x - x);                   // (x<0)**(non-whole): NaN

    if (whole && ay < 0x42800000ul) {               // |y| < 64
        uint32_t m = (uint32_t)(y < 0 ? -y : y);
        result = 1.0f;
        base = magnitude;
        while (m != 0) {
            if (m & 1)
                result *= base;
            m >>= 1;
            if (m != 0)
                base *= base;
        }
        if (hy >> 31)
            result = 1.0f / result;
    } else {
        result = cs_expf(y * cs_logf(magnitude));
    }
    return ((hx >> 31) && odd) ? -result : result;
}
