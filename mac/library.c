// A macOS native library's startup (NativeLib=Shared): dyld runs this
// constructor when the library loads, before any of its exports can be
// called. It does what a program's main (start.c) does before Main: hands
// zerocore the module list, then sets up its static fields.
#include "modules.h"

void ZeroInitializeLibrary(void);

__attribute__((constructor)) static void zero_library_init(void)
{
    zero_copy_modules();
    ZeroInitializeLibrary();
}
