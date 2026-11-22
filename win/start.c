// A 64-bit Windows program's entry point. (runtime.c has what programs and
// native libraries share; library.c is a native library's entry point.)
//
// Compiled with clang for x86_64-pc-windows-msvc.

#include "declarations.h"

int __managed__Main(int argc, char **argv);

// The linker's entry point (/entry:zero_start): Windows calls it with no
// arguments and no C runtime set up, so this builds argc and argv itself, in
// UTF-8 as on Linux. The compiler's startup code receives them as
// InitializeCommandLineArgsW's arguments.
void zero_start(void)
{
    int argc = 0;
    wchar **wide = CommandLineToArgvW(GetCommandLineW(), &argc);
    char **argv = HeapAlloc(GetProcessHeap(), 0, (u64)(argc + 1) * sizeof(char *));
    for (int i = 0; i < argc; i++) {
        int size = WideCharToMultiByte(CP_UTF8, 0, wide[i], -1, 0, 0, 0, 0);
        argv[i] = HeapAlloc(GetProcessHeap(), 0, (u64)size);
        WideCharToMultiByte(CP_UTF8, 0, wide[i], -1, argv[i], size, 0, 0);
    }
    argv[argc] = 0;

    // zerocore calls the C library's abort after printing why. Without this,
    // Windows' abort also offers to report the crash; with it, abort just
    // exits with code 3.
    _set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT);

    // Out writes UTF-8; without this a console shows it in the OEM code page.
    SetConsoleOutputCP(CP_UTF8);
    ExitProcess((u32)__managed__Main(argc, argv));
}
