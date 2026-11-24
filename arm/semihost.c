// A Cortex-M program in QEMU (arm/run.sh), or on a board with a debugger
// attached: everything outside the chip is done by the host, over
// semihosting. Linked with picolibc's crt0-semihost, which gets the command
// line from the host, and libsemihost, which gives read, write, open and the
// other file functions. See arm/runtime.c for the rest.

#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <unistd.h>

// A "bkpt 0xab" with an operation in r0 and its argument in r1: the debugger
// (or QEMU) does it and puts the result in r0.
static int semihost(int operation, void *argument)
{
    register int r0 __asm__("r0") = operation;
    register void *r1 __asm__("r1") = argument;
    __asm__ volatile("bkpt 0xab" : "+r"(r0) : "r"(r1) : "memory");
    return r0;
}

#define SYS_OPEN 0x01
#define SYS_READ 0x06
#define SYS_CLOCK 0x10
#define SYS_TIME 0x11
#define SYS_ELAPSED 0x30
#define SYS_TICKFREQ 0x31

// Linux's clock numbers (lib/Clock.cs). CLOCK_REALTIME (0) is the host's
// date and time, to the second (SYS_TIME). The others count from the start of
// the program: SYS_ELAPSED ticks of SYS_TICKFREQ per second, or without it
// SYS_CLOCK's hundredths of a second.
int clock_gettime(int clock, int64_t *timespec)
{
    if (clock == 0) {
        timespec[0] = (uint32_t)semihost(SYS_TIME, 0);
        timespec[1] = 0;
        return 0;
    }
    uint32_t ticks[2];
    int frequency = semihost(SYS_TICKFREQ, 0);
    if (frequency > 0 && semihost(SYS_ELAPSED, ticks) == 0) {
        uint64_t count = ((uint64_t)ticks[1] << 32) | ticks[0];
        timespec[0] = (int64_t)(count / (uint64_t)frequency);
        timespec[1] = (int64_t)((count % (uint64_t)frequency) * 1000000000u / (uint64_t)frequency);
        return 0;
    }
    int centiseconds = semihost(SYS_CLOCK, 0);
    timespec[0] = centiseconds / 100;
    timespec[1] = (int64_t)(centiseconds % 100) * 10000000;
    return 0;
}

// ---- standard input from a file -----------------------------------------------
//
// The host's console can't say "end of input", and C# programs reading a pipe
// wait for it. So arm/run.sh saves piped input to a file and names it with an
// argument (below), and fd 0 reads that file. Everything else, the console
// included, is picolibc's read: the link wraps it (-Wl,--wrap=read).

static int stdin_file = -1;

ssize_t __real_read(int fd, void *buffer, size_t count);

ssize_t __wrap_read(int fd, void *buffer, size_t count)
{
    if (fd != 0 || stdin_file < 0)
        return __real_read(fd, buffer, count);
    uintptr_t args[3] = {(uintptr_t)stdin_file, (uintptr_t)buffer, count};
    int left = semihost(SYS_READ, args); // the number of bytes NOT read
    return left < 0 ? -1 : (ssize_t)(count - (size_t)left);
}

// ---- the board ------------------------------------------------------------------
//
// QEMU's MPS2 boards have LEDs in their FPGA's registers (LED0, bit 0 is the
// first), and a fixed processor clock. env.sh says where and how fast.

#if !defined(ZERO_LED_REGISTER) || !defined(ZERO_CPU_HZ)
#error ZERO_LED_REGISTER and ZERO_CPU_HZ come from env.sh (arm_board)
#endif

void zero_led(int32_t on)
{
    volatile uint32_t *led = (volatile uint32_t *)ZERO_LED_REGISTER;
    *led = on ? (*led | 1u) : (*led & ~1u);
}

int32_t zero_led_is_on(void) { return (int32_t)(*(volatile uint32_t *)ZERO_LED_REGISTER & 1u); }

uint32_t zero_cpu_hz(void) { return ZERO_CPU_HZ; }

// ---- the arguments ------------------------------------------------------------

// The host passes the command line as one string, and picolibc's startup
// splits it at spaces. So arm/run.sh writes each argument with %XX for the
// bytes that would be lost (a space, a tab, '%'), decoded here.
static void decode(char *text)
{
    char *out = text;
    for (; *text; text++) {
        int high, low;
        if (text[0] == '%' && text[1] && text[2]) {
            high = text[1] <= '9' ? text[1] - '0' : (text[1] | 0x20) - 'a' + 10;
            low = text[2] <= '9' ? text[2] - '0' : (text[2] | 0x20) - 'a' + 10;
            if (high >= 0 && high < 16 && low >= 0 && low < 16) {
                *out++ = (char)(high * 16 + low);
                text += 2;
                continue;
            }
        }
        *out++ = *text;
    }
    *out = 0;
}

static const char stdin_option[] = "--zero-stdin=";

void zero_start(int *argc, char ***argv)
{
    for (int i = 0; i < *argc; i++)
        decode((*argv)[i]);
    // "--zero-stdin=FILE" right after the program's name: standard input.
    if (*argc > 1 && strncmp((*argv)[1], stdin_option, sizeof stdin_option - 1) == 0) {
        const char *path = (*argv)[1] + sizeof stdin_option - 1;
        uintptr_t args[3] = {(uintptr_t)path, 1 /* "rb" */, strlen(path)};
        stdin_file = semihost(SYS_OPEN, args);
        (*argv)[1] = (*argv)[0];
        (*argv)++;
        (*argc)--;
    }
}
