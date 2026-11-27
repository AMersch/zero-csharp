// WebAssembly SIMD (simd128) kernels for zerocore's TensorPrimitives and
// lib/Kernels.cs, compiled with -msimd128 and linked into every module by
// ./build.sh --wasm. Each works on a whole span.
//
// Why C and not C#: NativeAOT-LLVM has no WebAssembly intrinsics (no
// PackedSimd; Vector128 operations throw PlatformNotSupportedException), and
// LLVM can't vectorize C# loops either: the compiler checks every memory
// access for null (WebAssembly has no page protection), and each check is an
// exit from the loop, which the loop vectorizer refuses.
//
// The TensorPrimitives kernels do what TensorPrimitives' SSE2 path does,
// operation for operation (same lanes, same order of additions, a * b + c
// rounded twice, the same libm functions for values out of the vector
// range), so they give the same bits as an x64 build. simd128 has no fused
// multiply-add (relaxed-simd's isn't deterministic) and no fast-math is
// used, so the compiler can't change any of that.
//
// zerocore calls these with [DllImport("*")] only when
// System.Runtime.Intrinsics.Wasm.PackedSimd.IsSupported, which is true only
// in zerocore's WebAssembly build.

#include <math.h>
#include <wasm_simd128.h>

typedef v128_t V;

static inline V splat(float x) { return wasm_f32x4_splat(x); }
static inline V load(const float *p) { return wasm_v128_load(p); }
static inline void store(float *p, V v) { wasm_v128_store(p, v); }
// a * b + c, two roundings (SSE2's MultiplyAdd).
static inline V mul_add(V a, V b, V c) { return wasm_f32x4_add(wasm_f32x4_mul(a, b), c); }

// SSE2's horizontal sum: (lane 0 + lane 2) + (lane 1 + lane 3).
static inline float sum4(V v)
{
    return (wasm_f32x4_extract_lane(v, 0) + wasm_f32x4_extract_lane(v, 2))
         + (wasm_f32x4_extract_lane(v, 1) + wasm_f32x4_extract_lane(v, 3));
}

// ---- TensorPrimitives: element-wise ---------------------------------------

enum { OP_ADD, OP_SUBTRACT, OP_MULTIPLY, OP_DIVIDE };

static inline V vop(int op, V x, V y)
{
    switch (op)
    {
        case OP_ADD: return wasm_f32x4_add(x, y);
        case OP_SUBTRACT: return wasm_f32x4_sub(x, y);
        case OP_MULTIPLY: return wasm_f32x4_mul(x, y);
        default: return wasm_f32x4_div(x, y);
    }
}

static inline float sop(int op, float x, float y)
{
    switch (op)
    {
        case OP_ADD: return x + y;
        case OP_SUBTRACT: return x - y;
        case OP_MULTIPLY: return x * y;
        default: return x / y;
    }
}

// The operator is a constant in each copy, so the switch leaves the loop.
#define BINARY_LOOP(OP)                                                          \
    for (; i + 4 <= n; i += 4)                                                   \
        store(d + i, vop(OP, xStep ? load(x + i) : splat(*x), yStep ? load(y + i) : splat(*y)));

// d[i] = x[i] op y[i]; a step of 0 makes that argument one value for all.
void ZeroSimdBinary(int op, const float *x, int xStep, const float *y, int yStep, float *d, int n)
{
    int i = 0;
    switch (op)
    {
        case OP_ADD: BINARY_LOOP(OP_ADD) break;
        case OP_SUBTRACT: BINARY_LOOP(OP_SUBTRACT) break;
        case OP_MULTIPLY: BINARY_LOOP(OP_MULTIPLY) break;
        default: BINARY_LOOP(OP_DIVIDE) break;
    }
    for (; i < n; i++)
        d[i] = sop(op, x[i * xStep], y[i * yStep]);
}

// d[i] = (x[i] first y[i]) second z[i]: MultiplyAdd, AddMultiply.
void ZeroSimdTernary(int first, int second, const float *x, int xStep, const float *y, int yStep,
                     const float *z, int zStep, float *d, int n)
{
    int i = 0;
    for (; i + 4 <= n; i += 4)
    {
        V vx = xStep ? load(x + i) : splat(*x);
        V vy = yStep ? load(y + i) : splat(*y);
        V vz = zStep ? load(z + i) : splat(*z);
        store(d + i, vop(second, vop(first, vx, vy), vz));
    }
    for (; i < n; i++)
        d[i] = sop(second, sop(first, x[i * xStep], y[i * yStep]), z[i * zStep]);
}

// ---- TensorPrimitives: reductions -------------------------------------------

// Four running sums of 4 lanes each, added at the end.
float ZeroSimdSum(const float *x, int n)
{
    int i = 0;
    V s0 = wasm_f32x4_const_splat(0), s1 = s0, s2 = s0, s3 = s0;
    for (; i + 16 <= n; i += 16)
    {
        s0 = wasm_f32x4_add(s0, load(x + i));
        s1 = wasm_f32x4_add(s1, load(x + i + 4));
        s2 = wasm_f32x4_add(s2, load(x + i + 8));
        s3 = wasm_f32x4_add(s3, load(x + i + 12));
    }
    float sum = sum4(wasm_f32x4_add(wasm_f32x4_add(s0, s1), wasm_f32x4_add(s2, s3)));
    for (; i < n; i++)
        sum += x[i];
    return sum;
}

float ZeroSimdDot(const float *x, const float *y, int n)
{
    int i = 0;
    V s0 = wasm_f32x4_const_splat(0), s1 = s0, s2 = s0, s3 = s0;
    for (; i + 16 <= n; i += 16)
    {
        s0 = wasm_f32x4_add(s0, wasm_f32x4_mul(load(x + i), load(y + i)));
        s1 = wasm_f32x4_add(s1, wasm_f32x4_mul(load(x + i + 4), load(y + i + 4)));
        s2 = wasm_f32x4_add(s2, wasm_f32x4_mul(load(x + i + 8), load(y + i + 8)));
        s3 = wasm_f32x4_add(s3, wasm_f32x4_mul(load(x + i + 12), load(y + i + 12)));
    }
    float sum = sum4(wasm_f32x4_add(wasm_f32x4_add(s0, s1), wasm_f32x4_add(s2, s3)));
    for (; i < n; i++)
        sum += x[i] * y[i];
    return sum;
}

// ---- TensorPrimitives: Exp, Sigmoid, Tanh, SoftMax ----------------------------
//
// Cephes' expf, as TensorPrimitives' V.Exp on SSE2: fx = round(x log2 e)
// (to nearest, ties to even), r = x - fx ln 2 in two parts, a degree-6
// polynomial, times 2^fx through the exponent bits.

#define EXP_LO (-87.0f)
#define EXP_HI 88.0f
#define LOG2E 1.44269504088896341f
#define NEG_LN2_HI (-0.693359375f)
#define NEG_LN2_LO 2.12194440e-4f
#define P0 1.9875691500e-4f
#define P1 1.3981999507e-3f
#define P2 8.3334519073e-3f
#define P3 4.1665795894e-2f
#define P4 1.6666665459e-1f
#define P5 5.0000001201e-1f

static inline V exp4(V x)
{
    V fx = wasm_f32x4_nearest(wasm_f32x4_mul(x, splat(LOG2E)));
    V r = mul_add(fx, splat(NEG_LN2_HI), x);
    r = mul_add(fx, splat(NEG_LN2_LO), r);
    V p = splat(P0);
    p = mul_add(p, r, splat(P1));
    p = mul_add(p, r, splat(P2));
    p = mul_add(p, r, splat(P3));
    p = mul_add(p, r, splat(P4));
    p = mul_add(p, r, splat(P5));
    V y = mul_add(p, wasm_f32x4_mul(r, r), wasm_f32x4_add(r, splat(1.0f)));
    // fx is a whole number, so fx + 127 is the exponent field.
    V biased = wasm_i32x4_trunc_sat_f32x4(wasm_f32x4_add(fx, splat(127.0f)));
    return wasm_f32x4_mul(y, wasm_i32x4_shl(biased, 23));
}

static inline V sigmoid4(V x)
{
    V one = splat(1.0f);
    return wasm_f32x4_div(one, wasm_f32x4_add(one, exp4(wasm_f32x4_sub(wasm_f32x4_const_splat(0), x))));
}

#define T0 (-5.70498872745e-3f)
#define T1 2.06390887954e-2f
#define T2 (-5.37397155531e-2f)
#define T3 1.33314422036e-1f
#define T4 (-3.33332819422e-1f)

static inline V tanh4(V x)
{
    V a = wasm_f32x4_abs(x);
    V z = wasm_f32x4_mul(a, a);
    V p = splat(T0);
    p = mul_add(p, z, splat(T1));
    p = mul_add(p, z, splat(T2));
    p = mul_add(p, z, splat(T3));
    p = mul_add(p, z, splat(T4));
    V small = mul_add(wasm_f32x4_mul(p, z), a, a);

    V one = splat(1.0f);
    V capped = wasm_f32x4_pmin(splat(40.0f), a); // SSE2's min(a, 40): a < 40 ? a : 40
    V e = exp4(wasm_f32x4_add(capped, capped));
    V large = wasm_f32x4_sub(one, wasm_f32x4_div(splat(2.0f), wasm_f32x4_add(e, one)));

    V t = wasm_v128_bitselect(small, large, wasm_f32x4_lt(a, splat(0.625f)));
    return wasm_v128_bitselect(x, t, splat(-0.0f)); // x's sign bit, t's others
}

enum { FN_EXP, FN_SIGMOID, FN_TANH };

static inline int in_range(int fn, float x)
{
    switch (fn)
    {
        case FN_EXP: return x >= EXP_LO && x <= EXP_HI;
        case FN_SIGMOID: return x >= -EXP_HI && x <= -EXP_LO;
        default: return x == x;
    }
}

// The scalar forms TensorPrimitives uses: MathF.Exp, Sigmoid in double,
// MathF.Tanh (zerocore calls the same libm functions).
static inline float scalar_fn(int fn, float x)
{
    switch (fn)
    {
        case FN_EXP: return expf(x);
        case FN_SIGMOID: return (float)(1 / (1 + exp(-x)));
        default: return tanhf(x);
    }
}

static inline V vector_fn(int fn, V x)
{
    switch (fn)
    {
        case FN_EXP: return exp4(x);
        case FN_SIGMOID: return sigmoid4(x);
        default: return tanh4(x);
    }
}

// A block of 4 with a value outside the vector range is done by the scalar
// function, so special values come out exactly as MathF's.
#define UNARY_LOOP(FN)                                                           \
    for (; i + 4 <= n; i += 4)                                                   \
    {                                                                            \
        if (in_range(FN, x[i]) & in_range(FN, x[i + 1]) & in_range(FN, x[i + 2]) & in_range(FN, x[i + 3])) \
            store(d + i, vector_fn(FN, load(x + i)));                            \
        else                                                                     \
            for (int j = i; j < i + 4; j++)                                      \
                d[j] = scalar_fn(FN, x[j]);                                      \
    }

void ZeroSimdUnary(int fn, const float *x, float *d, int n)
{
    int i = 0;
    switch (fn)
    {
        case FN_EXP: UNARY_LOOP(FN_EXP) break;
        case FN_SIGMOID: UNARY_LOOP(FN_SIGMOID) break;
        default: UNARY_LOOP(FN_TANH) break;
    }
    for (; i < n; i++)
        d[i] = scalar_fn(fn, x[i]);
}

// The largest value, when every value is finite: returns 0 for an infinity
// or a NaN (x * 0 is NaN for those, and the check sum stays 0 otherwise).
int ZeroSimdMaxIfFinite(const float *x, int n, float *maxOut)
{
    int i = 0;
    float finite = 0, max = x[0];
    if (n >= 4)
    {
        V vmax = splat(x[0]), check = wasm_f32x4_const_splat(0), zero = check;
        for (; i + 4 <= n; i += 4)
        {
            V v = load(x + i);
            vmax = wasm_f32x4_pmax(v, vmax); // SSE2's max(vmax, v): vmax > v ? vmax : v
            check = wasm_f32x4_add(check, wasm_f32x4_mul(v, zero));
        }
        float l0 = wasm_f32x4_extract_lane(vmax, 0), l1 = wasm_f32x4_extract_lane(vmax, 1);
        float l2 = wasm_f32x4_extract_lane(vmax, 2), l3 = wasm_f32x4_extract_lane(vmax, 3);
        float a = l0 > l1 ? l0 : l1, b = l2 > l3 ? l2 : l3;
        max = a > b ? a : b;
        finite = sum4(check);
    }
    for (; i < n; i++)
    {
        finite += x[i] * 0;
        max = x[i] > max ? x[i] : max;
    }
    *maxOut = max;
    return finite == 0;
}

// SoftMax's first pass: d = e^(x - max), returning their sum. x - max is
// rounded in float; its error, recovered exactly (TwoSum), corrects e^.
float ZeroSimdSoftMaxExp(const float *x, float *d, int n, float max)
{
    float sum = 0, low = max + EXP_LO;
    int i = 0;
    V vmax = splat(max), negMax = splat(-max), vsum = wasm_f32x4_const_splat(0);
    for (; i + 4 <= n; i += 4)
    {
        if (!((x[i] >= low) & (x[i + 1] >= low) & (x[i + 2] >= low) & (x[i + 3] >= low)))
        {
            for (int j = i; j < i + 4; j++)
                sum += d[j] = (float)exp((double)x[j] - max);
            continue;
        }
        V v = load(x + i);
        V dv = wasm_f32x4_sub(v, vmax);
        V bb = wasm_f32x4_sub(dv, v);
        V error = wasm_f32x4_add(wasm_f32x4_sub(v, wasm_f32x4_sub(dv, bb)), wasm_f32x4_sub(negMax, bb));
        V e = exp4(dv);
        e = mul_add(e, error, e);
        store(d + i, e);
        vsum = wasm_f32x4_add(vsum, e);
    }
    sum += sum4(vsum);
    for (; i < n; i++)
        sum += d[i] = (float)exp((double)x[i] - max);
    return sum;
}

// ---- lib/Kernels.cs ----------------------------------------------------------
//
// Its exp clamps the input to [-87.33, 88.72] instead of falling back to
// libm, so softmax and SiLU stay in vectors. No fused multiply-add here, so
// the last bits differ from the NEON and AVX2 paths (within the few ulps
// samples/Kernels.cs allows).

#define K_EXP_HI 88.72283935546875f
#define K_EXP_LO (-87.33654022216797f)

static inline V kexp4(V x)
{
    x = wasm_f32x4_pmin(splat(K_EXP_HI), wasm_f32x4_pmax(splat(K_EXP_LO), x));
    return exp4(x);
}

float ZeroSimdKernelDot(const float *a, const float *b, int n) { return ZeroSimdDot(a, b, n); }

void ZeroSimdKernelMatVec(float *y, const float *w, const float *x, int rows, int cols)
{
    for (int r = 0; r < rows; r++)
        y[r] = ZeroSimdDot(w + (long)r * cols, x, cols);
}

// Exact for values in [-128, 127]: pairs of int8 products fit in int32.
int ZeroSimdKernelDotI8(const signed char *a, const signed char *b, int n)
{
    int i = 0;
    V acc = wasm_i32x4_const_splat(0);
    for (; i + 16 <= n; i += 16)
    {
        V va = wasm_v128_load(a + i), vb = wasm_v128_load(b + i);
        acc = wasm_i32x4_add(acc, wasm_i32x4_dot_i16x8(wasm_i16x8_extend_low_i8x16(va), wasm_i16x8_extend_low_i8x16(vb)));
        acc = wasm_i32x4_add(acc, wasm_i32x4_dot_i16x8(wasm_i16x8_extend_high_i8x16(va), wasm_i16x8_extend_high_i8x16(vb)));
    }
    int sum = wasm_i32x4_extract_lane(acc, 0) + wasm_i32x4_extract_lane(acc, 1)
            + wasm_i32x4_extract_lane(acc, 2) + wasm_i32x4_extract_lane(acc, 3);
    for (; i < n; i++)
        sum += a[i] * b[i];
    return sum;
}

void ZeroSimdKernelExp(const float *x, float *y, int n)
{
    int i = 0;
    for (; i + 4 <= n; i += 4)
        store(y + i, kexp4(load(x + i)));
    for (; i < n; i++)
        y[i] = expf(x[i]);
}

// In place: x = exp(x - max) / sum.
void ZeroSimdKernelSoftmax(float *x, int n)
{
    int i = 0;
    float max = x[0];
    if (n >= 4)
    {
        V m = load(x);
        for (i = 4; i + 4 <= n; i += 4)
            m = wasm_f32x4_pmax(m, load(x + i));
        float l0 = wasm_f32x4_extract_lane(m, 0), l1 = wasm_f32x4_extract_lane(m, 1);
        float l2 = wasm_f32x4_extract_lane(m, 2), l3 = wasm_f32x4_extract_lane(m, 3);
        max = l0 > max ? l0 : max;
        max = l1 > max ? l1 : max;
        max = l2 > max ? l2 : max;
        max = l3 > max ? l3 : max;
    }
    for (; i < n; i++)
        max = x[i] > max ? x[i] : max;

    i = 0;
    V vmax = splat(max), vsum = wasm_f32x4_const_splat(0);
    for (; i + 4 <= n; i += 4)
    {
        V e = kexp4(wasm_f32x4_sub(load(x + i), vmax));
        store(x + i, e);
        vsum = wasm_f32x4_add(vsum, e);
    }
    float sum = sum4(vsum);
    for (; i < n; i++)
    {
        x[i] = expf(x[i] - max);
        sum += x[i];
    }
    float factor = 1.0f / sum;
    V f = splat(factor);
    for (i = 0; i + 4 <= n; i += 4)
        store(x + i, wasm_f32x4_mul(load(x + i), f));
    for (; i < n; i++)
        x[i] *= factor;
}

// In place: x = x / (1 + exp(-x)).
void ZeroSimdKernelSilu(float *x, int n)
{
    int i = 0;
    V one = splat(1.0f), zero = wasm_f32x4_const_splat(0);
    for (; i + 4 <= n; i += 4)
    {
        V v = load(x + i);
        store(x + i, wasm_f32x4_div(v, wasm_f32x4_add(one, kexp4(wasm_f32x4_sub(zero, v)))));
    }
    for (; i < n; i++)
        x[i] = x[i] / (1.0f + expf(-x[i]));
}

// y = x * scale * weight (RmsNorm's second pass).
void ZeroSimdKernelScaleMultiply(float *y, const float *x, const float *weight, int n, float scale)
{
    int i = 0;
    V s = splat(scale);
    for (; i + 4 <= n; i += 4)
        store(y + i, wasm_f32x4_mul(wasm_f32x4_mul(load(x + i), s), load(weight + i)));
    for (; i < n; i++)
        y[i] = x[i] * scale * weight[i];
}
