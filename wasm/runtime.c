// The native runtime for --stdlib:zero programs on WebAssembly (WASI).
//
// NativeAOT-LLVM normally links a full runtime: GC, exception handling, type
// loader. Zero mode needs none of that, only the handful of entry points the
// compiled code calls, so this file is all of it. It's linked into every
// module by ./build.sh --wasm, together with WASI's C library.
//
// Two NativeAOT-LLVM details shape it:
// - Managed code keeps some locals and arguments on a "shadow stack" in linear
//   memory, because WASM's own stack can't be addressed. Native code entering
//   managed code (main -> __managed__Main) hands it the shadow stack, and
//   runtime helpers called from managed code receive it as their first argument.
// - zerolib's [RuntimeExport] methods (RhpNewFast, RhpNewArray, ...) aren't
//   emitted on this target, so they're implemented here instead.
//
// All pointers are 32-bit. zerolib's object layout on wasm32:
//   [MethodTable* (4)] [array length (4), arrays only] [fields / elements]
// and a MethodTable starts with: ushort componentSize, ushort flags, uint baseSize.

#include <stdio.h>
#include <stdlib.h>

// ---- entry ----------------------------------------------------------------

int __managed__Main(int argc, char **argv);

int main(int argc, char **argv) { return __managed__Main(argc, argv); }

// zerolib's startup (Internal/Statics.Unix.cs, WASM path) needs the module
// header to allocate static fields of reference types. C# can't take a data
// symbol's address on WASM, so it asks for it here.
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

// Managed code calling native code, and polling for a GC: no GC, nothing to do.
int RhpTrapThreads; // 0: never stop for a GC
void RhpGcPoll(void *shadowStack) { (void)shadowStack; }
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

// ---- allocation (never freed, like every allocation in zero mode) ----------

static unsigned short component_size(void *methodTable) { return *(unsigned short *)methodTable; }
static unsigned base_size(void *methodTable) { return *(unsigned *)((char *)methodTable + 4); }

static void *allocate(size_t size)
{
    void *p = calloc(1, size); // C# requires zeroed memory
    if (!p)
    {
        fputs("zero mode: out of memory\n", stderr);
        abort();
    }
    return p;
}

void *RhpNewFast(void *shadowStack, void *methodTable)
{
    (void)shadowStack;
    void **obj = allocate(base_size(methodTable));
    obj[0] = methodTable;
    return obj;
}

void *RhpNewArray(void *shadowStack, void *methodTable, int count)
{
    (void)shadowStack;
    if (count < 0)
        abort();
    void **obj = allocate(base_size(methodTable) + (size_t)count * component_size(methodTable));
    obj[0] = methodTable;
    *(int *)(obj + 1) = count;
    return obj;
}

void *RhpNewArrayFast(void *shadowStack, void *methodTable, int count)
{
    return RhpNewArray(shadowStack, methodTable, count);
}

void *RhpNewPtrArrayFast(void *shadowStack, void *methodTable, int count)
{
    return RhpNewArray(shadowStack, methodTable, count);
}

// Storing an object reference: no GC, so no write barrier.
void RhpAssignRef(void **destination, void *reference) { *destination = reference; }
void RhpCheckedAssignRef(void **destination, void *reference) { *destination = reference; }
