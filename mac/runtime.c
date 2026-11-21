// The C side of a macOS program: the entry point, and one fix-up. Everything
// else zerocore and lib/ call (calloc, write, read, abort, puts, the math
// functions) is in macOS's own C library under the same name as on Linux.

int __managed__Main(int argc, char **argv);

int main(int argc, char **argv)
{
    return __managed__Main(argc, argv);
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
