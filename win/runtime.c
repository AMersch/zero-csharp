// The C side of a 64-bit Windows program or native library: the POSIX
// functions zerocore and lib/ call that Windows doesn't have under those names
// (write, read, clock_gettime, the pthreads lib/Threads.cs uses), and where
// the compiler's module list is. The rest of the C library (calloc, malloc,
// puts, abort, the math functions) comes from Windows' own, the Universal CRT;
// memset and memmove from ntdll. The entry point is start.c's (programs) or
// library.c's (native libraries).
//
// Compiled with clang for x86_64-pc-windows-msvc.

#include "declarations.h"

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

// ---- threads, as pthreads ---------------------------------------------------
//
// lib/Threads.cs and zerocore's ObjectAllocator use the POSIX names; these are
// the few they need, on Windows threads and thread-local storage.

typedef struct { void *(*start)(void *); void *argument; } ThreadStart;

static u32 run_thread(void *parameter)
{
    ThreadStart start = *(ThreadStart *)parameter;
    HeapFree(GetProcessHeap(), 0, parameter);
    start.start(start.argument);
    return 0;
}

int pthread_create(Handle *thread, void *attributes, void *(*start)(void *), void *argument)
{
    (void)attributes;
    ThreadStart *parameter = HeapAlloc(GetProcessHeap(), 0, sizeof(ThreadStart));
    if (!parameter)
        return 11; // EAGAIN
    parameter->start = start;
    parameter->argument = argument;
    *thread = CreateThread(0, 0, run_thread, parameter, 0, 0);
    if (!*thread) {
        HeapFree(GetProcessHeap(), 0, parameter);
        return 11;
    }
    return 0;
}

int pthread_join(Handle thread, void **result)
{
    WaitForSingleObject(thread, 0xFFFFFFFF);
    CloseHandle(thread);
    if (result)
        *result = 0;
    return 0;
}

int pthread_key_create(u64 *key, void (*destructor)(void *))
{
    (void)destructor;
    u32 index = TlsAlloc();
    if (index == 0xFFFFFFFF)
        return 11;
    *key = index;
    return 0;
}

void *pthread_getspecific(u64 key) { return TlsGetValue((u32)key); }

int pthread_setspecific(u64 key, void *value) { return TlsSetValue((u32)key, value) ? 0 : 22; }

int get_nprocs(void) { return (int)GetActiveProcessorCount(0xFFFF); }

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
