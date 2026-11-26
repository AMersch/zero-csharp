// WebAssembly SIMD, under the name .NET gives it.
//
// Only IsSupported: NativeAOT-LLVM, which compiles zerocore to WebAssembly,
// can't compile the operations themselves (it turns any Vector128 operation
// into a PlatformNotSupportedException). Vector code for WebAssembly is in C
// instead, in wasm/simd.c, built with -msimd128 and linked into every module
// by ./build.sh --wasm; IsSupported says whether those kernels are there.
// It's a constant, so the compiler drops the branches that don't apply.
namespace System.Runtime.Intrinsics.Wasm
{
    public abstract class PackedSimd
    {
#if WASM_SIMD
        public static bool IsSupported => true;
#else
        public static bool IsSupported => false;
#endif
    }
}
