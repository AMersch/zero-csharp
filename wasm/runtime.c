// The native half of zerocore on WebAssembly (WASI).
//
// NativeAOT-LLVM normally links a full runtime: GC, exception handling, type
// loader. Here zerocore (C#) is the runtime: allocation, casts, interface
// calls and static fields all come from it, as on Linux. This file holds what
// can't be written in C#: the entry point, and a few functions whose calling
// convention differs from a C# method's. It's linked into every module by
// ./build.sh --wasm, together with WASI's C library.
//
// The detail that shapes it: managed code keeps some locals and arguments on a
// "shadow stack" in linear memory, because WASM's own stack can't be
// addressed. Native code entering managed code (main -> __managed__Main) hands
// it the shadow stack, and every C# method takes it as a hidden first argument.

#include <stdio.h>
#include <stdlib.h>

// ---- entry ----------------------------------------------------------------

int __managed__Main(int argc, char **argv);

int main(int argc, char **argv) { return __managed__Main(argc, argv); }

// zerocore's startup (Runtime/Startup.cs) needs the module header to set up
// static fields of reference types. C# can't take a data symbol's address on
// WASM, so it asks for it here.
extern char __ReadyToRunHeader[];

void *ZeroGetModuleHeader(void *shadowStack)
{
    (void)shadowStack;
    return __ReadyToRunHeader;
}

// ---- transitions between native and managed code --------------------------

static char shadow_stack[1 << 20] __attribute__((aligned(16)));

// Entering managed code from native code: hand out the shadow stack. Callbacks
// from native code back into managed code (nested reverse P/Invokes) aren't
// supported; zero-mode samples don't use them.
void *RhpReversePInvokeAndPushSparseVirtualUnwindFrame(void *frame, void *a, void *b)
{
    (void)frame;
    (void)a;
    (void)b;
    return shadow_stack;
}

void RhpReversePInvokeReturnAndPopSparseVirtualUnwindFrame(void *frame) { (void)frame; }

// A method with a catch or fault block registers itself here, so the
// exception dispatcher could find its handlers. `throw` never unwinds in zero
// mode (it prints and aborts, in zerocore's RhpThrowEx), so nothing reads it.
void RhpPushSparseVirtualUnwindFrame(void *frame, void *table, unsigned index)
{
    (void)frame;
    (void)table;
    (void)index;
}

void RhpPopSparseVirtualUnwindFrame(void) {}

// Managed code calling native code: nothing to do without a GC.
int RhpTrapThreads; // 0: never stop for a GC
void RhpPInvoke(void *frame) { (void)frame; }
void RhpPInvokeReturn(void *frame) { (void)frame; }

// ---- exceptions: zero mode has none, so these only report and stop ----------

int RhpExceptionThrown; // the flag NativeAOT-LLVM's emulated exceptions check after calls

int RhpHandleExceptionWasmCatch(void *a, void *b)
{
    (void)a;
    (void)b;
    fputs("zero mode: exception thrown\n", stderr);
    abort();
}

void RhpHandleUnhandledException(void *a, void *b)
{
    (void)a;
    (void)b;
    fputs("zero mode: unhandled exception\n", stderr);
    abort();
}

// ---- object references -------------------------------------------------------

// Storing an object reference: no GC, so a plain store. (On Linux these are
// zerocore/native/*.S.)
void RhpAssignRef(void **destination, void *reference) { *destination = reference; }
void RhpCheckedAssignRef(void **destination, void *reference) { *destination = reference; }

// Call sites of interface methods hold this function's address until their
// first call. Nothing calls it: compiled code asks zerocore for the method
// (RhpResolveInterfaceDispatch) instead.
void RhpInitialDynamicInterfaceDispatch(void) { abort(); }
