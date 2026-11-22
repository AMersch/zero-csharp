// The C side of a macOS program: the entry point (which also hands zerocore
// the compiler's module list), and two fix-ups. Everything
// else zerocore and lib/ call (calloc, write, read, abort, puts, the math
// functions) is in macOS's own C library under the same name as on Linux.

int __managed__Main(int argc, char **argv);

// The compiler's module list is the __DATA,__modules section. zerocore looks
// for it between __start___modules and __stop___modules (GNU ld's names);
// here those bound a table in zerocore/native/mac-arm64.S, filled from the
// section's bounds, which Mach-O linkers name section$start$/section$end$.
extern void *modules_first[] __asm("section$start$__DATA$__modules");
extern void *modules_last[] __asm("section$end$__DATA$__modules");
extern void *__start___modules[];
extern void *__stop___modules[];
long write(int fd, const void *buffer, unsigned long count);
void abort(void);

int main(int argc, char **argv)
{
    long count = modules_last - modules_first;
    if (count > __stop___modules - __start___modules)
    {
        static const char message[] = "zerocore: more compiled modules than mac-arm64.S has room for\n";
        write(2, message, sizeof message - 1);
        abort();
    }
    for (long i = 0; i < count; i++)
        __start___modules[i] = modules_first[i];
    return __managed__Main(argc, argv);
}

// lib/Threads.cs asks get_nprocs for the processor count, as on Linux;
// macOS's sysconf has it under another number than Linux's.
long sysconf(int name);
#define SC_NPROCESSORS_ONLN 58

int get_nprocs(void)
{
    return (int)sysconf(SC_NPROCESSORS_ONLN);
}

// The samples call clock_gettime(1, ...): 1 is CLOCK_MONOTONIC on Linux, but
// no clock at all on macOS. This definition comes before the C library's, so
// it answers for every clock with the monotonic one: all the samples use it
// for is measuring durations.
typedef unsigned long long u64;
u64 clock_gettime_nsec_np(int clock);
#define CLOCK_UPTIME_RAW 8

int clock_gettime(int clock, long long *timespec)
{
    (void)clock;
    u64 now = clock_gettime_nsec_np(CLOCK_UPTIME_RAW);
    timespec[0] = (long long)(now / 1000000000);
    timespec[1] = (long long)(now % 1000000000);
    return 0;
}
