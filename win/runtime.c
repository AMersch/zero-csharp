// The C side of a 64-bit Windows program: the entry point, and the POSIX
// functions zerocore and lib/ call that Windows doesn't have under those names
// (write, read, clock_gettime). The rest of the C library (calloc, malloc,
// puts, abort, the math functions) comes from Windows' own, the Universal CRT;
// memset and memmove from ntdll.
//
// Compiled with clang for x86_64-pc-windows-msvc. There are no Windows
// headers here, so the few declarations needed are written out below.

typedef unsigned short wchar;
typedef unsigned int u32;
typedef long long i64;
typedef unsigned long long u64;
typedef void *Handle;

#define IMPORT __declspec(dllimport)

IMPORT wchar *GetCommandLineW(void);
IMPORT wchar **CommandLineToArgvW(const wchar *line, int *count);
IMPORT int WideCharToMultiByte(u32 page, u32 flags, const wchar *text, int length,
                               char *out, int size, const char *fallback, int *used);
IMPORT Handle GetProcessHeap(void);
IMPORT void *HeapAlloc(Handle heap, u32 flags, u64 size);
IMPORT Handle GetStdHandle(u32 which);
IMPORT int WriteFile(Handle file, const void *buffer, u32 count, u32 *written, void *overlapped);
IMPORT int ReadFile(Handle file, void *buffer, u32 count, u32 *read, void *overlapped);
IMPORT u32 GetLastError(void);
IMPORT int SetConsoleOutputCP(u32 page);
IMPORT int QueryPerformanceCounter(i64 *count);
IMPORT int QueryPerformanceFrequency(i64 *frequency);
IMPORT __declspec(noreturn) void ExitProcess(u32 code);
IMPORT unsigned int _set_abort_behavior(unsigned int flags, unsigned int mask);

#define CP_UTF8 65001
#define STD_INPUT_HANDLE ((u32)-10)
#define STD_OUTPUT_HANDLE ((u32)-11)
#define STD_ERROR_HANDLE ((u32)-12)
#define ERROR_BROKEN_PIPE 109
#define _WRITE_ABORT_MSG 1
#define _CALL_REPORTFAULT 2

// ---- where the compiler's module list is ------------------------------------
//
// The compiler puts a pointer to each module's header in section
// ".modules$I". The linker sorts sections by the part after "$", so these two
// land just before and just after it, which is what the ELF linker's
// __start___modules and __stop___modules give zerocore on Linux. Startup.cs
// skips the null entries (this first one, and any padding).

#pragma section(".modules$A", read)
#pragma section(".modules$Z", read)
__declspec(allocate(".modules$A")) void *__start___modules[1] = { 0 };
__declspec(allocate(".modules$Z")) void *__stop___modules[1] = { 0 };

// ---- entry ------------------------------------------------------------------

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

// ---- POSIX functions --------------------------------------------------------

static Handle handle_for(int fd)
{
    switch (fd) {
    case 0: return GetStdHandle(STD_INPUT_HANDLE);
    case 1: return GetStdHandle(STD_OUTPUT_HANDLE);
    case 2: return GetStdHandle(STD_ERROR_HANDLE);
    default: return 0;
    }
}

i64 write(int fd, const void *buffer, u64 count)
{
    Handle file = handle_for(fd);
    const char *next = buffer;
    u64 left = count;
    while (left > 0) {
        u32 chunk = left > 0x40000000 ? 0x40000000 : (u32)left;
        u32 written = 0;
        if (!WriteFile(file, next, chunk, &written, 0))
            return -1;
        next += written;
        left -= written;
    }
    return (i64)count;
}

// A console hands over lines ending in "\r\n"; lib/In expects "\n", as from a
// file or a pipe, so carriage returns are dropped.
i64 read(int fd, void *buffer, u64 count)
{
    Handle file = handle_for(fd);
    char *bytes = buffer;
    u32 chunk = count > 0x40000000 ? 0x40000000 : (u32)count;
    for (;;) {
        u32 got = 0;
        if (!ReadFile(file, bytes, chunk, &got, 0))
            return GetLastError() == ERROR_BROKEN_PIPE ? 0 : -1;
        if (got == 0)
            return 0;
        u32 kept = 0;
        for (u32 i = 0; i < got; i++)
            if (bytes[i] != '\r')
                bytes[kept++] = bytes[i];
        if (kept > 0)
            return kept;
    }
}

// CLOCK_MONOTONIC only, which is all the samples ask for.
int clock_gettime(int clock, i64 *timespec)
{
    (void)clock;
    i64 count, frequency;
    QueryPerformanceCounter(&count);
    QueryPerformanceFrequency(&frequency);
    timespec[0] = count / frequency;
    timespec[1] = (count % frequency) * 1000000000 / frequency;
    return 0;
}
