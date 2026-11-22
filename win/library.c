// A 64-bit Windows native library's entry point (NativeLib=Shared; the
// linker's /entry:zero_dll_start). Windows calls it when the library loads
// (and when threads start and stop, and when it unloads, which need nothing).
// On load it sets up zerocore's static fields, as a program's entry point
// (start.c) does before Main. It doesn't touch the console's code page: that
// belongs to the program that loaded the library.
//
// It runs under Windows' loader lock: setting up static fields only allocates
// (HeapAlloc, through calloc), and static constructors run later, on first
// use.
//
// Compiled with clang for x86_64-pc-windows-msvc.

#include "declarations.h"

#define DLL_PROCESS_ATTACH 1

void ZeroInitializeLibrary(void);

int zero_dll_start(void *instance, u32 reason, void *reserved)
{
    (void)instance;
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        // As for a program: zerocore's abort exits with code 3 instead of
        // also offering to report the crash.
        _set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT);
        ZeroInitializeLibrary();
    }
    return 1;
}
