// The native half of zerocore on the Commander X16.
//
// c64/runtime.c with the X16's differences: a 65C02 at 8 MHz, 38 KB of fixed
// RAM from $0801 to $9EFF (llvm-mos's cx16 platform), the KERNAL's X16 jump
// table, a 60 Hz jiffy clock from RDTIM, and, when the program runs in the
// emulator (x16emu), the emulator's debug registers at $9FB0-$9FBF for what
// the C64 gets from VICE: a port that is the program's standard output and a
// register that reports its exit code. Everything else, the heap, the
// shadow stack, the Rhp* functions, is the same C, for the same bitcode.
//
// C's int is 16 bits here and C#'s is 32: see c64/runtime.c, whose notes on
// the cs_ prefix and the 4-byte object references hold here too.

#include <cbm.h>
#include <cx16.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef long i32;
typedef unsigned long u32;
typedef long long i64;
typedef unsigned long long u64;

// ---- entry and exit ---------------------------------------------------------

i32 __managed__Main(i32 argc, char **argv);

// The emulator's debug registers (x16emu README, "Emulator I/O registers").
// $9FBE and $9FBF read "1" and "6" in the emulator, and nothing on a real
// machine. $9FBB is a port: a byte written to it is printed on the host's
// standard output, as it is (newlines and all, unlike BSOUT's echo). Writing
// a byte to $9FB9 prints "User debug 1: $xx" there.
#define EMU_ID_1 (*(volatile unsigned char *)0x9FBE)
#define EMU_ID_2 (*(volatile unsigned char *)0x9FBF)
#define EMU_STDOUT (*(volatile unsigned char *)0x9FBB)
#define EMU_DEBUG1 (*(volatile unsigned char *)0x9FB9)

static char in_emulator;

// Leaves the program: back to BASIC's READY (the build links llvm-mos's
// save-basic.o, which puts back the memory BASIC needs). The exit code has
// nowhere to go on an X16, so in the emulator it is written to $9FB9, where
// cx16/run.sh finds it ("User debug 1: $xx" on the emulator's standard output)
// and ends the emulator.
static char iso_mode;       // cs_cx16_clear switched the screen to ISO text

static void leave(int code)
{
    if (iso_mode)
        cbm_k_chrout(0x8F); // back to PETSCII, for BASIC
    if (in_emulator)
        EMU_DEBUG1 = (unsigned char)code;
    exit(code);
}

#ifdef CX16_CODE_BANKS
// A program with more code than fixed RAM holds has some of its functions in
// RAM banks 1 to CX16_CODE_BANKS (see sdk/ZeroCSharp.Bitcode/Banking.cs). They
// are in a file next to the program, which one KERNAL LOAD into $A000 reads:
// it carries on into the next bank when one is full, and leaves the last one
// selected.
static void load_code_banks(void)
{
    unsigned char device = *(volatile unsigned char *)0x00BA;   // the one the program came from
    RAM_BANK = 1;
    cbm_k_setnam(CX16_BANK_FILE);
    cbm_k_setlfs(0, device ? device : 8, 0);
    void *end = cbm_k_load(0, (void *)0xA000);
    RAM_BANK = 1;
    if (end == 0 || cbm_k_readst() & 0xBF) {
        puts("can't load " CX16_BANK_FILE);
        leave(1);
    }
}
#define CODE_BANKS CX16_CODE_BANKS
#else
#define CODE_BANKS 0
#endif

int main(void)
{
    in_emulator = EMU_ID_1 == 0x31 && EMU_ID_2 == 0x36;
#ifdef CX16_CODE_BANKS
    load_code_banks();
#endif
    leave((int)__managed__Main(0, 0));
    return 0;
}

void cs_exit(i32 code) { leave((int)code); }
void cs_abort(void) { leave(134); } // what a Unix shell reports for abort()

// zerocore's startup (Runtime/Startup.cs) needs the module header to set up
// static fields of reference types; C# can't take a data symbol's address.
extern char __ReadyToRunHeader[];
void *ZeroGetModuleHeader(void *shadowStack) { (void)shadowStack; return __ReadyToRunHeader; }

// ---- transitions between native and managed code ---------------------------

// Compiled C# keeps the locals that hold object references on a stack of its
// own, the "shadow stack", which every method gets as a hidden first
// argument. It isn't checked for overflow; deep recursion needs more.
static char shadow_stack[2048];

void *RhpReversePInvokeAndPushSparseVirtualUnwindFrame(i32 frame, void *a, i32 b)
{
    (void)frame; (void)a; (void)b;
    return shadow_stack;
}
void RhpReversePInvokeReturnAndPopSparseVirtualUnwindFrame(void *frame) { (void)frame; }
void RhpPushSparseVirtualUnwindFrame(void *frame, void *table, i32 index) { (void)frame; (void)table; (void)index; }
void RhpPopSparseVirtualUnwindFrame(void) {}

// Managed code calling native code: nothing to do without a GC.
i32 RhpTrapThreads; // 0: never stop for a GC
void RhpPInvoke(void *frame) { (void)frame; }
void RhpPInvokeReturn(void *frame) { (void)frame; }

// ---- exceptions: zero mode has none ------------------------------------------

// RhpExceptionThrown, the flag compiled code checks after calls, is missing
// on purpose: ll2mos replaces every read of it with zero, and if a write
// survived the optimizer the program wouldn't link. These two are only called
// from the code those checks guard, so they normally aren't linked in either.
void *RhpHandleExceptionWasmCatch(void *a, i32 b)
{
    (void)a; (void)b;
    fputs("zero mode: exception thrown\n", stdout);
    leave(134);
    return 0;
}

void RhpHandleUnhandledException(void *a, void *b)
{
    (void)a; (void)b;
    fputs("zero mode: unhandled exception\n", stdout);
    leave(134);
}

// ---- object references ---------------------------------------------------------

// Storing an object reference: no GC, so a plain store, of all four bytes.
void RhpAssignRef(u32 *destination, void *reference) { *destination = (unsigned)reference; }
void RhpCheckedAssignRef(u32 *destination, void *reference) { *destination = (unsigned)reference; }

// Call sites of interface methods hold this function's address until their
// first call. Nothing calls it: compiled code asks zerocore for the method.
void RhpInitialDynamicInterfaceDispatch(void) { leave(134); }

// ---- memory --------------------------------------------------------------------

// The heap: the memory between the program and the stack, handed out in
// 4-byte units. zerocore keeps flags in the two low bits of pointers to what
// it allocates (an interface call's cache, for one), as every 32-bit system
// lets it, so every block starts on a multiple of 4.
//
// llvm-mos's own malloc, with the wrapper it needed (it aligns to nothing),
// came to 2.1 to 2.7 KB; this one is about half that. Each block has a 4-byte
// header: its size in units, header included, and, while it is free, the
// next free block. Free blocks are kept in address order, and one freed next
// to another becomes part of it, so an arena that grows, is freed and grows
// again reuses the same memory. A request is served by the first free block
// big enough, else from memory never used yet.
typedef struct chunk {
    struct chunk *next;
    unsigned units;
} chunk;

// The heap runs from the end of the program to 1 KB below where the stack
// is when the first block is asked for, as llvm-mos's own malloc does.
// __rc0 and __rc1 are llvm-mos's stack pointer, in the zero page.
extern char __heap_start[], __stack_reserve_size[];
extern volatile unsigned char __rc0, __rc1;
static chunk *free_list;
static chunk *unused, *heap_end;

// The C library's names: anything in it that allocates gets this heap too.
// llvm-mos's malloc then never comes into the program, which is the point:
// two allocators would hand out the same memory. (If something ever needs
// another function from its malloc.cc, such as aligned_alloc, the link
// fails on a second definition of malloc instead.)
void *malloc(size_t size)
{
    unsigned units;
    chunk **link, *block;
    if (size > 0xFFF0)
        return 0;
    units = (size + 3) / 4 + 1;
    for (link = &free_list; (block = *link) != 0; link = &block->next) {
        if (block->units >= units) {
            if (block->units == units) {
                *link = block->next;
            } else { // the block's end is the new block
                block->units -= units;
                block += block->units;
                block->units = units;
            }
            return block + 1;
        }
    }
    if (!unused) { // the first request
        unsigned stack = __rc0 | (unsigned)__rc1 << 8;
        unused = (chunk *)(((unsigned)__heap_start + 3) & ~3u);
        heap_end = (chunk *)((stack - (unsigned)__stack_reserve_size) & ~3u);
        if (heap_end < unused)
            heap_end = unused;
    }
    if ((unsigned)(heap_end - unused) < units)
        return 0;
    block = unused;
    unused += units;
    block->units = units;
    return block + 1;
}

void free(void *pointer)
{
    chunk *block, *before = 0, *after = free_list;
    if (!pointer)
        return;
    block = (chunk *)pointer - 1;
    while (after && after < block) {
        before = after;
        after = after->next;
    }
    if (after && block + block->units == after) { // joins the one after it
        block->units += after->units;
        block->next = after->next;
    } else {
        block->next = after;
    }
    if (!before)
        free_list = block;
    else if (before + before->units == block) { // and the one before it
        before->units += block->units;
        before->next = block->next;
    } else {
        before->next = block;
    }
}

void *calloc(size_t count, size_t size)
{
    unsigned long total = (unsigned long)count * size;
    void *block = total > 0xFFF0 ? 0 : malloc((size_t)total);
    if (block)
        memset(block, 0, (size_t)total);
    return block;
}

void *realloc(void *pointer, size_t size)
{
    void *block;
    size_t had;
    if (!pointer)
        return malloc(size);
    block = malloc(size);
    if (block) {
        had = (((chunk *)pointer - 1)->units - 1) * 4;
        memcpy(block, pointer, had < size ? had : size);
        free(pointer);
    }
    return block;
}

// What C# imports, with its 32-bit sizes.
void *cs_calloc(u32 count, u32 size) { return (count | size) > 0xFFF0 ? 0 : calloc((size_t)count, (size_t)size); }
void *cs_malloc(u32 size) { return size > 0xFFF0 ? 0 : malloc((size_t)size); }
void cs_free(void *pointer) { free(pointer); }

void *cs_memset(void *destination, i32 value, u32 count) { return memset(destination, (int)value, (size_t)count); }
void *cs_memmove(void *destination, const void *source, u32 count) { return memmove(destination, source, (size_t)count); }

// ---- the screen and the keyboard as standard output and input -----------------

// Every character the program prints goes through here, as ASCII: putchar
// turns it into the X16's character set and calls the KERNAL. In the emulator
// it also goes, untouched, to the emulator's standard output port, which is
// how cx16/run.sh gets a program's output to the terminal.
__attribute__((noinline)) void zero_out(char character)
{
    if (in_emulator)
        EMU_STDOUT = (unsigned char)character;
    putchar(character);
}

i32 cs_write(i32 file, const char *buffer, u32 count)
{
    (void)file; // standard output and standard error are both the screen
    for (u32 i = 0; i < count; i++)
        zero_out(buffer[i]);
    return (i32)count;
}

i32 cs_puts(const char *text)
{
    while (*text)
        zero_out(*text++);
    zero_out('\n');
    return 0;
}

// Reads from the keyboard, a line at a time: the KERNAL's own line editor,
// with a cursor, hands a line over when RETURN is pressed (getchar turns it
// into ASCII). Like a terminal, a read returns what is left of the current
// line, the newline included, or as much of it as fits.
//
// A keyboard has no end of input, and programs wait for one ("until read
// returns 0"). So, as Ctrl-Z does on DOS, a line that holds only the X16's
// left-arrow key (top left; '_' in ASCII) ends the input for good.
static char line[82]; // the editor's longest line is 80 characters
static unsigned char line_length, line_at;
static char input_ended;

// ---- input for tests, from a cartridge -----------------------------------------
//
// The emulator can't type into the keyboard from outside, so cx16/run.sh puts
// the text a test reads into a cartridge image (-cartbin), which the emulator
// maps from ROM bank 32 on: "ZIN1", the length in two bytes, the text. In the
// emulator, and only there, a read takes its bytes from there when that is
// present, and the keyboard is left alone. The bank is switched with
// interrupts off: their handler's vector is in the ROM bank that is switched in.
static unsigned char cart_state; // 0: not looked yet, 1: input, 2: none
static unsigned cart_length, cart_at;

static unsigned char cart_byte(unsigned offset)
{
    volatile unsigned char *bank = (volatile unsigned char *)0x0001;
    unsigned char saved = *bank, value;
    __asm__ volatile("sei" ::: "memory");
    *bank = 32;
    value = *(volatile unsigned char *)(0xC000u + offset);
    *bank = saved;
    __asm__ volatile("cli" ::: "memory");
    return value;
}

static char cart_input(void)
{
    if (cart_state == 0) {
        cart_state = 2;
        if (in_emulator && cart_byte(0) == 'Z' && cart_byte(1) == 'I' && cart_byte(2) == 'N' && cart_byte(3) == '1') {
            cart_length = cart_byte(4) | (unsigned)cart_byte(5) << 8;
            cart_state = 1;
        }
    }
    return cart_state == 1;
}

// The next byte of the test's input, or -1 at its end.
static int cart_getc(void)
{
    return cart_at < cart_length ? cart_byte(6 + cart_at++) : -1;
}

// ---- keys one at a time, for Console.ReadKey (lib/InKeys.cs) -----------------
//
// The POSIX calls ReadKey and KeyAvailable make. A "termios" is one byte, 1
// for raw; tcsetattr remembers it, and in raw mode cs_read returns the next
// key press, from the KERNAL's keyboard buffer (GETIN), as a Unix terminal
// would send it: letters, digits and signs in ASCII, RETURN as '\r', DEL as
// backspace, RUN/STOP as Escape, and the cursor and function keys as escape
// sequences (ESC [ A for cursor up, ESC O P for F1).

struct pollfd {
    i32 fd;
    short events, revents;
};

static char keys_raw;

i32 cs_isatty(i32 fd) { return fd == 0; }

i32 cs_tcgetattr(i32 fd, unsigned char *termios)
{
    if (fd != 0)
        return -1;
    termios[0] = 0;
    return 0;
}

void cs_cfmakeraw(unsigned char *termios) { termios[0] = 1; }

i32 cs_tcsetattr(i32 fd, i32 when, const unsigned char *termios)
{
    (void)when;
    if (fd != 0)
        return -1;
    keys_raw = (char)termios[0];
    return 0;
}

// A key from GETIN (PETSCII) as a terminal's bytes, or 0 bytes for one that
// has no meaning here (colors, CLR, the graphics signs).
static unsigned char keys_bytes(unsigned char key, char *out)
{
    static const char *const sequences[] = {
        "\x1b[B", "\x1b[A", "\x1b[C", "\x1b[D", "\x1b[H", "\x1b[2~",
        "\x1bOP", "\x1bOR", "\x1b[15~", "\x1b[18~", "\x1bOQ", "\x1bOS", "\x1b[17~", "\x1b[19~",
    };
    static const unsigned char codes[] = { 17, 145, 29, 157, 19, 148, 133, 134, 135, 136, 137, 138, 139, 140 };
    for (unsigned char i = 0; i < sizeof codes; i++) {
        if (codes[i] == key) {
            const char *sequence = sequences[i];
            unsigned char n = 0;
            while (sequence[n]) {
                out[n] = sequence[n];
                n++;
            }
            return n;
        }
    }
    char c;
    if (key == 13)
        c = '\r';
    else if (key == 20)
        c = 8; // DEL: backspace
    else if (key == 3)
        c = 0x1B; // RUN/STOP: Escape
    else if (key >= 0x41 && key <= 0x5A)
        c = (char)(key + 0x20); // unshifted letters are lower case here
    else if (key >= 0xC1 && key <= 0xDA)
        c = (char)(key - 0x80); // shifted ones upper case
    else if (key >= 0x20 && key <= 0x3F)
        c = (char)key;
    else if (key == 0x5F)
        c = '_'; // the left-arrow key
    else
        return 0;
    out[0] = c;
    return 1;
}

static i32 keys_read(char *buffer, u32 count)
{
    if (cart_input()) { // the bytes as they are, to the end
        u32 n = 0;
        while (n < count && cart_at < cart_length)
            buffer[n++] = (char)cart_getc();
        return (i32)n;
    }
    for (;;) {
        unsigned char key = cbm_k_getin();
        if (key == 0)
            continue;
        char bytes[8];
        unsigned char n = keys_bytes(key, bytes);
        if (n == 0)
            continue;
        if (n > count)
            n = (unsigned char)count;
        memcpy(buffer, bytes, n);
        return n;
    }
}

// Only standard input is asked about: whether the keyboard buffer has a key.
i32 cs_poll(struct pollfd *fds, u32 count, i32 timeout)
{
    i32 ready = 0;
    (void)timeout;
    for (u32 i = 0; i < count; i++) {
        fds[i].revents = 0;
        if (fds[i].fd == 0 && (fds[i].events & 1)
            && (cart_input() ? cart_at < cart_length : cx16_k_kbdbuf_peek(0) >= 0)) {
            fds[i].revents = 1;
            ready++;
        }
    }
    return ready;
}

i32 cs_read(i32 file, char *buffer, u32 count)
{
    unsigned char available;
    (void)file;
    if (keys_raw)
        return keys_read(buffer, count);
    if (line_at == line_length) {
        if (input_ended)
            return 0;
        line_length = line_at = 0;
        for (;;) {
            int character = cart_input() ? cart_getc() : getchar();
            if (character < 0)
                break;
            if (line_length < sizeof line)
                line[line_length++] = (char)character;
            if (character == '\n')
                break;
        }
        if (line_length == 0 || (line_length == 2 && line[0] == '_' && line[1] == '\n')) {
            input_ended = 1;
            line_length = 0;
            return 0;
        }
    }
    available = line_length - line_at;
    if (count < available)
        available = (unsigned char)count;
    memcpy(buffer, line + line_at, available);
    line_at += available;
    return available;
}

// ---- the clock --------------------------------------------------------------------

// The KERNAL counts sixtieths of a second since the machine was switched on
// (RDTIM, from the vertical blank interrupt), and the real-time clock keeps the
// date and the time of day, to the second (it keeps what was set, usually the
// local time: no time zone is known). Of clock_gettime's two clocks:
//
//   0, the wall clock: the real-time clock, read once and then advanced by the
//     jiffies, so that it has sixtieths of a second and costs no more reads.
//   1, the monotonic clock: the jiffies since power-on, plus an arbitrary start
//     (a monotonic clock's origin is not defined) taken from the hardware's
//     random bits, which makes Random.FromClock() differ from run to run even
//     when the real-time clock isn't set.
static u32 jiffies(void) { return cx16_k_rdtim(); }

static char wall_known;                 // the real-time clock has been read
static i64 wall_seconds;                // Unix time then
static u32 wall_jiffies;                // and the jiffies then
static char origin_known;
static i64 origin_ns;                   // the monotonic clock's start, in nanoseconds

// Seconds since 1970-01-01 of a date (the proleptic Gregorian calendar's days from the
// civil date: Howard Hinnant's algorithm), and a time of day.
static i64 unix_seconds(int year, int month, int day, int hour, int minute, int second)
{
    long y = year - (month <= 2), era = (y >= 0 ? y : y - 399) / 400;
    long yoe = y - era * 400, doy = (153L * (month > 2 ? month - 3 : month + 9) + 2) / 5 + day - 1;
    long doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    i64 days = (i64)era * 146097 + doe - 719468;
    return days * 86400 + (i64)hour * 3600 + minute * 60 + second;
}

static void read_wall_clock(void)
{
    cx16_date_time_t now;
    cx16_k_clock_get_date_time(&now);
    wall_seconds = unix_seconds(1900 + now.year, now.mon, now.day, now.hour, now.min, now.sec);
    wall_jiffies = jiffies();
    wall_known = 1;
}

i32 cs_clock_gettime(i32 clock, i64 *timespec)
{
    u32 ticks = jiffies();
    if (clock == 0) {
        if (!wall_known)
            read_wall_clock();
        u32 since = ticks - wall_jiffies;
        timespec[0] = wall_seconds + since / 60;
        timespec[1] = (i64)(since % 60) * 16666667;
    } else {
        if (!origin_known) {
            origin_ns = (i64)cx16_k_entropy_get() * 61;     // up to a second
            origin_known = 1;
        }
        i64 ns = (i64)ticks * 16666667 + origin_ns;
        timespec[0] = ns / 1000000000;
        timespec[1] = ns % 1000000000;
    }
    return 0;
}

// Sets the real-time clock (and the wall clock with it).
void cs_cx16_set_date_time(i32 year, i32 month, i32 day, i32 hour, i32 minute, i32 second)
{
    cx16_k_clock_set_date_time((unsigned char)(year - 1900), (unsigned char)month, (unsigned char)day,
        (unsigned char)hour, (unsigned char)minute, (unsigned char)second, 0);
    wall_known = 0;
}

// 24 random bits from the hardware.
i32 cs_cx16_entropy(void) { return (i32)cx16_k_entropy_get(); }

// ---- the rest of what zerocore and lib/ import -------------------------------------

// One thread: thread-local keys are plain slots.
#define KEYS 4
static void *key_values[KEYS];
static unsigned char key_count;

i32 cs_pthread_key_create(u32 *key, void *destructor)
{
    (void)destructor;
    if (key_count == KEYS)
        return 11; // EAGAIN
    *key = key_count++;
    return 0;
}

void *cs_pthread_getspecific(u32 key) { return key < key_count ? key_values[key] : 0; }

i32 cs_pthread_setspecific(u32 key, void *value)
{
    if (key >= key_count)
        return 22; // EINVAL
    key_values[key] = value;
    return 0;
}

char *cs_getenv(const char *name) { (void)name; return 0; } // no environment
i32 cs_sched_yield(void) { return 0; }                      // no other thread to yield to
i32 cs_get_nprocs(void) { return 1; }

// ---- floating point ------------------------------------------------------------------
//
// The 6502 has none; llvm-mos computes float and double in software, slowly
// and at a few KB per program that uses them. Its C library has no math
// functions, so the ones compiled code calls by itself are here: rounding,
// remainder and square root. Sin, Pow, Log and the like (Math.*, MathF.*)
// are not: a program that uses them doesn't link (undefined symbol cs_pow).

// C#'s conversions to integers: NaN is 0, out of range is the nearest limit.
#define SATURATING(name, T, F, LOW, HIGH) \
    T name(F x) { if (x != x) return 0; if (x <= (F)(LOW)) return (LOW); if (x >= (F)(HIGH)) return (HIGH); return (T)x; }
SATURATING(zero_fptosi_sat_i32_f64, i32, double, (-2147483647L - 1), 2147483647L)
SATURATING(zero_fptosi_sat_i32_f32, i32, float, (-2147483647L - 1), 2147483647L)
SATURATING(zero_fptosi_sat_i64_f64, i64, double, (-9223372036854775807LL - 1), 9223372036854775807LL)
SATURATING(zero_fptosi_sat_i64_f32, i64, float, (-9223372036854775807LL - 1), 9223372036854775807LL)
SATURATING(zero_fptoui_sat_i32_f64, u32, double, 0UL, 4294967295UL)
SATURATING(zero_fptoui_sat_i32_f32, u32, float, 0UL, 4294967295UL)
SATURATING(zero_fptoui_sat_i64_f64, u64, double, 0ULL, 18446744073709551615ULL)
SATURATING(zero_fptoui_sat_i64_f32, u64, float, 0ULL, 18446744073709551615ULL)

double trunc(double x)
{
    union { double value; u64 bits; } u = { x };
    int exponent = (int)((u.bits >> 52) & 0x7FF) - 1023;
    if (exponent < 0)
        return x < 0 ? -0.0 : 0.0;
    if (exponent >= 52)
        return x; // no fraction bits left (or infinity, or NaN)
    u.bits &= ~((1ULL << (52 - exponent)) - 1);
    return u.value;
}

double floor(double x) { double whole = trunc(x); return whole > x ? whole - 1 : whole; }
double ceil(double x) { double whole = trunc(x); return whole < x ? whole + 1 : whole; }

// To the nearest whole number, halves to the even one.
double roundeven(double x)
{
    double below = floor(x), fraction = x - below;
    if (fraction < 0.5)
        return below;
    if (fraction > 0.5)
        return below + 1;
    return trunc(below / 2) * 2 == below ? below : below + 1;
}

double fabs(double x) { return x < 0 ? -x : x; }

// Exact while x / y fits in 53 bits, which covers the % of ordinary numbers.
double fmod(double x, double y) { return x - trunc(x / y) * y; }

// The square root, correctly rounded: the root of the 53-bit mantissa, found
// a bit at a time the way long division finds a quotient, to 54 bits (one
// more than fits, to round by).
double sqrt(double x)
{
    union { double value; u64 bits; } u = { x };
    int exponent = (int)((u.bits >> 52) & 0x7FF);
    u64 mantissa = u.bits & 0x000FFFFFFFFFFFFFULL, root = 0, rest = 0, result;
    if (x != x || x == 0 || u.bits == 0x7FF0000000000000ULL)
        return x; // NaN, either zero, infinity
    if (x < 0) {
        u.bits = 0x7FF8000000000000ULL; // NaN
        return u.value;
    }
    if (exponent == 0) { // a number too small for the usual form
        exponent = 1;
        while (!(mantissa & (1ULL << 52))) {
            mantissa <<= 1;
            exponent--;
        }
    } else {
        mantissa |= 1ULL << 52;
    }
    exponent -= 1023;
    if (exponent & 1) { // an even exponent halves exactly
        mantissa <<= 1;
        exponent--;
    }
    // The root of mantissa * 2^54, two bits of it per step from the top.
    for (int i = 0; i < 54; i++) {
        u64 trial = (root << 2) | 1;
        rest <<= 2;
        if (i <= 26)
            rest |= (mantissa >> (52 - 2 * i)) & 3;
        root <<= 1;
        if (rest >= trial) {
            rest -= trial;
            root |= 1;
        }
    }
    result = root >> 1;
    if ((root & 1) && (rest != 0 || (result & 1)))
        result++;
    u.bits = ((u64)(exponent / 2 + 1023) << 52) + (result - (1ULL << 52));
    return u.value;
}

float truncf(float x) { return (float)trunc(x); }
float sqrtf(float x) { return (float)sqrt(x); }
float floorf(float x) { return (float)floor(x); }
float ceilf(float x) { return (float)ceil(x); }
float roundevenf(float x) { return (float)roundeven(x); }
float fabsf(float x) { return x < 0 ? -x : x; }
float fmodf(float x, float y) { return (float)fmod(x, y); }

// ---- math, for lib/Math: sin, cos, exp, log, pow, ... -----------------------------------
//
// Only in a program that calls them (the build defines CX16_MATH): they are
// some 28 KB of soft floating point. cx16/test-math.c tests them on a computer.

#ifdef CX16_MATH
// math.c is compiled apart, to IR, by the build (X16Prepare in X16.cs), so that the
// big functions can be moved into banks; the rest is here.

// The imports of lib's Math that the runtime has under C's own names.
double cs_sqrt(double x) { return sqrt(x); }
double cs_fabs(double x) { return fabs(x); }
double cs_floor(double x) { return floor(x); }
double cs_ceil(double x) { return ceil(x); }
double cs_trunc(double x) { return trunc(x); }
double cs_rint(double x) { return roundeven(x); }
float cs_sqrtf(float x) { return sqrtf(x); }
float cs_fabsf(float x) { return fabsf(x); }
float cs_floorf(float x) { return floorf(x); }
float cs_ceilf(float x) { return ceilf(x); }
float cs_truncf(float x) { return truncf(x); }
float cs_rintf(float x) { return roundevenf(x); }
#endif

#ifdef CX16_FILES
// ---- files, for lib/File.cs ----------------------------------------------------------
//
// Only in a program that uses File (the build defines CX16_FILES then): the C
// library's stdio is some 3 KB, which no other program should carry.
//
// The C library's own stdio, which llvm-mos built on the KERNAL's file calls:
// a file is on the disk device the program came from (the SD card, or an IEC
// drive), by the name CBM DOS gives it.
//
// What the KERNAL can't do is move around in a file: llvm-mos's fseek wants an
// lseek that isn't there, and a sequential file has no positions. So a file
// is a handle (1 to MAX_FILES, in the table below) and this keeps the
// position itself. A file opened for reading can be moved anywhere: forward
// by reading and throwing the bytes away, backward by opening it again, and
// the end by reading to it. That is slow (File.Length reads a file through
// twice) but it works, and it is all File and TryReadAllText need. A file
// opened for writing or appending can only "move" to where it is. (A device
// that isn't there, named in a path, closes all open files: see below.)
//
// Files are always opened in binary mode ("rb"): the text mode swaps newline
// and carriage return, which would change bytes. CBM DOS appends to a file
// when its name ends in ",a" ("name,a" with mode "ab"); for the rest, a file
// that is written is removed first, since CBM DOS won't write over one.
//
// A path may start with "#" and a device number and a colon ("#9:notes.txt"):
// the file is then on that device, the way a Commodore disk drive is
// addressed (8 and up: the SD card is 8, drives on the serial bus 8 to 11).
// Without it the device is the one the program came from, which llvm-mos
// keeps in curunit.

#define MAX_FILES 4
#define MAX_NAME 40

extern unsigned char curunit;       // the device a file is on when its name doesn't say

static struct {
    FILE *file;
    unsigned long position;
    char path[MAX_NAME];
    unsigned char device;
    char mode;          // 'r', 'w' or 'a'
} files[MAX_FILES];

// The path without a "#9:" in front of it, and the device that said: the
// default one if nothing did. 0 if the device is not a number from 8 to 30.
static const char *take_device(const char *path, unsigned char *device)
{
    *device = curunit;
    if (path[0] != '#')
        return path;
    unsigned number = 0;
    const char *p = path + 1;
    while (*p >= '0' && *p <= '9' && number < 100)
        number = number * 10 + (unsigned)(*p++ - '0');
    if (p == path + 1 || *p != ':' || number < 8 || number > 30)
        return 0;
    *device = (unsigned char)number;
    return p + 1;
}

static int file_slot(i32 handle)
{
    return handle >= 1 && handle <= MAX_FILES && files[handle - 1].file ? (int)handle - 1 : -1;
}

// Opening a file on a device that isn't there leaves the KERNAL unable to open
// anything, on any device, until it has closed everything. So that is what
// happens: every open file of the program is closed too.
static void forget_files_if_device_missing(void)
{
    if (!(cbm_k_readst() & 0x80))
        return;
    cbm_k_clall();
    for (int i = 0; i < MAX_FILES; i++)
        files[i].file = 0;
}

i32 cs_fopen(const char *given, const char *mode)
{
    unsigned char device;
    const char *path = take_device(given, &device);
    if (path == 0)
        return 0;
    int slot = 0;
    while (slot < MAX_FILES && files[slot].file)
        slot++;
    if (slot == MAX_FILES || strlen(path) >= MAX_NAME - 2 || (mode[0] != 'r' && mode[0] != 'w' && mode[0] != 'a'))
        return 0;
    char name[MAX_NAME];
    char binary[3] = { mode[0], 'b', 0 };
    strcpy(name, path);
    unsigned char standard = curunit;
    curunit = device;
    if (mode[0] == 'w')
        remove(path);
    else if (mode[0] == 'a')
        strcat(name, ",a");
    FILE *file = fopen(name, binary);
    curunit = standard;
    if (file == 0) {
        forget_files_if_device_missing();
        return 0;
    }
    files[slot].device = device;
    files[slot].file = file;
    files[slot].position = 0;
    strcpy(files[slot].path, path);
    files[slot].mode = mode[0];
    return slot + 1;
}

u32 cs_fread(void *buffer, u32 size, u32 count, i32 handle)
{
    int slot = file_slot(handle);
    if (slot < 0 || size == 0 || count == 0 || size > 0xFFFF || count > 0xFFFF)
        return 0;
    u32 items = (u32)fread(buffer, (size_t)size, (size_t)count, files[slot].file);
    files[slot].position += items * size;
    return items;
}

u32 cs_fwrite(const void *buffer, u32 size, u32 count, i32 handle)
{
    int slot = file_slot(handle);
    if (slot < 0 || size == 0 || count == 0 || size > 0xFFFF || count > 0xFFFF)
        return 0;
    u32 items = (u32)fwrite(buffer, (size_t)size, (size_t)count, files[slot].file);
    files[slot].position += items * size;
    return items;
}

i32 cs_fclose(i32 handle)
{
    int slot = file_slot(handle);
    if (slot < 0)
        return -1;
    int result = fclose(files[slot].file);
    files[slot].file = 0;
    return (i32)result;
}

// Reads and drops bytes until the position is `target`, or the file ends.
static void skip_to(int slot, unsigned long target)
{
    unsigned char scratch[64];
    while (files[slot].position < target) {
        unsigned long left = target - files[slot].position;
        size_t n = fread(scratch, 1, left < sizeof scratch ? (size_t)left : sizeof scratch, files[slot].file);
        if (n == 0)
            return;
        files[slot].position += n;
    }
}

i32 cs_fseeko(i32 handle, long long offset, i32 origin)
{
    int slot = file_slot(handle);
    if (slot < 0)
        return -1;
    if (files[slot].mode != 'r')
        return origin == 0 && offset == (long long)files[slot].position ? 0 : -1;

    long long target;
    if (origin == 2) {      // SEEK_END: the end is where reading stops
        skip_to(slot, 0xFFFFFFFFul);
        target = (long long)files[slot].position + offset;
    } else {
        target = origin == 1 ? (long long)files[slot].position + offset : offset;
    }
    if (target < 0 || target > 0x7FFFFFFFLL)
        return -1;
    if ((unsigned long)target < files[slot].position) {     // back: from the start again
        fclose(files[slot].file);
        unsigned char standard = curunit;
        curunit = files[slot].device;
        files[slot].file = fopen(files[slot].path, "rb");
        curunit = standard;
        files[slot].position = 0;
        if (files[slot].file == 0)
            return -1;
    }
    skip_to(slot, (unsigned long)target);
    return files[slot].position == (unsigned long)target ? 0 : -1;
}

long long cs_ftello(i32 handle)
{
    int slot = file_slot(handle);
    return slot < 0 ? -1 : (long long)files[slot].position;
}

i32 cs_remove(const char *given)
{
    unsigned char device;
    const char *path = take_device(given, &device);
    if (path == 0)
        return -1;
    unsigned char standard = curunit;
    curunit = device;
    // The drive's scratch command says nothing about a file that isn't there,
    // so look first.
    FILE *file = fopen(path, "rb");
    i32 result = -1;
    if (file != 0) {
        fclose(file);
        result = (i32)remove(path);
    } else {
        forget_files_if_device_missing();
    }
    curunit = standard;
    return result;
}
#endif // CX16_FILES

// ---- the machine, for lib/CX16.cs ----------------------------------------------------

i32 cs_cx16_peek(i32 address) { return *(volatile unsigned char *)(unsigned)address; }
void cs_cx16_poke(i32 address, i32 value) { *(volatile unsigned char *)(unsigned)address = (unsigned char)value; }

// The video chip's memory: 128 KB, reached through an address and a data
// register. address is 0-131071 (17 bits), passed as a 32-bit number.
static void vera_at(u32 address, unsigned char step)
{
    VERA.control &= ~1;                     // data port 0
    VERA.address = (unsigned short)address;
    VERA.address_hi = (unsigned char)((address >> 16) & 1) | (unsigned char)(step << 4);
}

i32 cs_cx16_vpeek(u32 address)
{
    vera_at(address, 0);
    return VERA.data0;
}

void cs_cx16_vpoke(u32 address, i32 value)
{
    vera_at(address, 0);
    VERA.data0 = (unsigned char)value;
}

// Copies bytes to the video memory, an address that goes up by one a byte.
void cs_cx16_vcopy(u32 address, const unsigned char *source, i32 count)
{
    vera_at(address, 1);
    for (unsigned i = 0; i < (unsigned)count; i++)
        VERA.data0 = source[i];
}

// Two bytes at an address of the video memory: a tile map's entry is two bytes.
void cs_cx16_vpoke2(u32 address, i32 first, i32 second)
{
    vera_at(address, 1);
    VERA.data0 = (unsigned char)first;
    VERA.data0 = (unsigned char)second;
}

static char has_fx(void);
static void fx_begin4(unsigned char a, unsigned char b, unsigned char c, unsigned char d);
static void fx_end(void);

// `count` pairs of bytes, from an address: a whole tile map of one tile. With the video chip's
// cache (see fx_begin) four bytes, two pairs, go in a write; the address must then be a
// multiple of 4.
void cs_cx16_vfill2(u32 address, i32 count, i32 first, i32 second)
{
    unsigned char a = (unsigned char)first, b = (unsigned char)second;
    unsigned n = (unsigned)count;
    if (n >= 16 && (address & 3) == 0 && has_fx()) {
        unsigned groups = n / 2;
        fx_begin4(a, b, a, b);
        vera_at(address, 3);                        // up by 4
        unsigned g = groups;
        for (; g >= 8; g -= 8) {
            VERA.data0 = 0; VERA.data0 = 0; VERA.data0 = 0; VERA.data0 = 0;
            VERA.data0 = 0; VERA.data0 = 0; VERA.data0 = 0; VERA.data0 = 0;
        }
        while (g-- != 0)
            VERA.data0 = 0;
        fx_end();
        address += (u32)groups * 4;
        n -= groups * 2;
        if (n == 0)
            return;
    }
    vera_at(address, 1);
    for (; n != 0; n--) {
        VERA.data0 = a;
        VERA.data0 = b;
    }
}

// The text screen is ISO text: a character's code in screen memory is its
// Latin-1 code, and the KERNAL's own "clear screen" character empties it and
// puts the cursor that Out writes at in the top left corner.
void cs_cx16_clear(i32 background)
{
    // The KERNAL's color characters, by palette index.
    static const unsigned char color_char[16] = {
        0x90, 0x05, 0x1C, 0x9F, 0x9C, 0x1E, 0x1F, 0x9E,
        0x81, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0x9B };
    background &= 15;
    if (!iso_mode) {
        cbm_k_chrout(0x0F);
        iso_mode = 1;
    }
    cbm_k_chrout(0x01);                         // swap: the background is now the text color
    cbm_k_chrout(color_char[background]);
    cbm_k_chrout(0x01);                         // and back: that is the background
    cbm_k_chrout(background == 1 ? 0x90 : 0x05); // text: white, or black on white
    cbm_k_chrout(147);
}

static unsigned char mouse_show;        // what the program last asked mouse_config for
static char mouse_on;
void cs_cx16_mouse_config(i32 show);

// Switches the screen mode (0-6: text, $80: 320x240 with 256 colors under 40x30 text). The
// KERNAL clears the screen and starts the video chip over (the character set stays, ISO
// or PETSCII, as it was); what a program would not expect to lose is put back: the
// sprites stay on, and the mouse gets the new screen's size. Returns 1, or 0 for a mode
// that isn't one.
static signed char current_mode = -1;   // the mode as last set here; -1 until asked

i32 cs_cx16_mode(i32 mode)
{
    VERA.control &= ~2;                             // DCSEL 0: the register with the sprite bit
    unsigned char sprites = VERA.display.video & 0x40;
    if (cx16_k_screen_mode_set((unsigned char)mode) != 0)
        return 0;
    VERA.control &= ~2;
    VERA.display.video |= sprites;
    current_mode = (signed char)mode;
    if (mouse_on)
        cs_cx16_mouse_config(mouse_show);
    return 1;
}

i32 cs_cx16_mode_number(void)
{
    screen_mode_info_t info;
    cx16_k_screen_mode_get(&info);
    current_mode = (signed char)info.mode;
    return info.mode;
}

// ---- the palette and the bitmap -----------------------------------------------------
//
// Colors: 256 entries of 2 bytes at $1FA00 in the video memory, a green and a blue
// nibble, then a red one. The bitmap of mode $80: 320 by 240 pixels, a byte each,
// row after row from address 0 (the KERNAL's own place for it). Everything that
// draws does nothing in another mode.

void cs_cx16_set_color(i32 index, i32 red, i32 green, i32 blue)
{
    unsigned char bytes[2];
    bytes[0] = (unsigned char)((green & 15) << 4 | (blue & 15));
    bytes[1] = (unsigned char)(red & 15);
    cs_cx16_vcopy(0x1FA00ul + (unsigned)(index & 255) * 2, bytes, 2);
}

i32 cs_cx16_get_color(i32 index)        // 12 bits: red, green, blue, 4 each
{
    unsigned lo = (unsigned)cs_cx16_vpeek(0x1FA00ul + (unsigned)(index & 255) * 2);
    unsigned hi = (unsigned)cs_cx16_vpeek(0x1FA00ul + (unsigned)(index & 255) * 2 + 1);
    return (i32)(((hi & 15) << 8) | lo);
}

#define BITMAP_WIDTH 320
#define BITMAP_HEIGHT 240

// ---- VERA FX: the video chip's helpers (from VERA 0.3.1 on) -------------------------------
//
// A video chip that has them says so in its version registers (DCSEL 63): the first byte
// is "V". The helper used here is the cache: a write to the data port with "cache write"
// on writes four bytes, the ones in the cache, to the address (rounded down to 4) in place
// of the one byte written, and the address goes on by 4 (increment code 3): a fill four
// times faster, 2 cycles a pixel. FX is switched off again after every use, and the
// interrupts are off meanwhile: the KERNAL's own writes to the video memory (the mouse
// pointer) must not be turned into cache writes.

static signed char fx_known = -1;

static char has_fx_chip(void)
{
    if (fx_known < 0) {
        VERA.control = 63 << 1;
        fx_known = VERA.display.dcver0 == 'V';
        VERA.control = 0;
    }
    return fx_known;
}

i32 cs_cx16_has_fx(void) { return has_fx_chip(); }

static signed char fx_switched_off;                 // the program asked for no FX

static char has_fx(void) { return has_fx_chip() && !fx_switched_off; }

// Whether the helpers are used: they are, if the chip has them, until a program says no (to compare, or to
// test the way that works without them). on: 1 or 0 to say, -1 to ask. Returns 1 if they are in use.
i32 cs_cx16_use_fx(i32 on)
{
    if (on >= 0)
        fx_switched_off = on == 0;
    return has_fx_chip() && !fx_switched_off;
}

// Switches the cache on, holding four bytes of one value. Until fx_end, the interrupts are
// off and every write to the data port is a cache write: its value says which of the four
// bytes are left alone. It has a bit for each nibble, 8 in all, bit 0 for the low nibble of
// the byte at the address: a byte is left alone entirely when both of its bits are set (3 for
// the first byte, 12 for the second, 48, 192), and half of it when one is. 0 writes all four.
static void fx_begin4(unsigned char a, unsigned char b, unsigned char c, unsigned char d)
{
    __asm__ volatile("sei" ::: "memory");
    VERA.control = 6 << 1;                          // DCSEL 6: the cache
    VERA.display.fxcachel = a;
    VERA.display.fxcachem = b;
    VERA.display.fxcacheh = c;
    VERA.display.fxcacheu = d;
    VERA.control = 2 << 1;                          // DCSEL 2: FX_CTRL
    VERA.display.fxctrl = 0x40;                     // cache write enable
    VERA.control = 0;
}

static void fx_begin(unsigned char value) { fx_begin4(value, value, value, value); }

static void fx_end(void)
{
    VERA.control = 2 << 1;
    VERA.display.fxctrl = 0;
    VERA.control = 0;
    __asm__ volatile("cli" ::: "memory");
}

// Fills `groups` times 4 bytes of the video memory from `address` (a multiple of 4) with a value.
static void fx_fill_groups(u32 address, unsigned groups, unsigned char value)
{
    fx_begin(value);
    vera_at(address, 3);                            // up by 4
    for (; groups >= 8; groups -= 8) {
        VERA.data0 = 0; VERA.data0 = 0; VERA.data0 = 0; VERA.data0 = 0;
        VERA.data0 = 0; VERA.data0 = 0; VERA.data0 = 0; VERA.data0 = 0;
    }
    while (groups-- != 0)
        VERA.data0 = 0;
    fx_end();
}

static char in_bitmap(void)
{
    if (current_mode < 0)
        cs_cx16_mode_number();
    return (unsigned char)current_mode == 0x80;
}

// Points the data port at pixel (x, y): y * 320 + x is up to 76,799, a bit more than 16 bits,
// so it is added in 16 and the carry becomes the 17th bit. (A 32-bit sum is slow here.)
static void vera_at_pixel(unsigned x, unsigned y, unsigned char step)
{
    unsigned above = y << 8, below = (y << 6) + x, low = above + below;
    VERA.control &= ~1;
    VERA.address = low;
    VERA.address_hi = (unsigned char)((low < above) | (unsigned char)(step << 4));
}

void cs_cx16_bitmap_clear(i32 color)
{
    if (!in_bitmap())
        return;
    unsigned char value = (unsigned char)color;
    if (has_fx()) {
        fx_fill_groups(0, 19200u, value);           // 76,800 pixels, 4 at a time
        return;
    }
    vera_at(0, 1);
    for (unsigned i = 0; i < 9600u; i++) {          // 76,800 pixels, 8 at a time (an int has 16 bits: no 320 * 240)
        VERA.data0 = value; VERA.data0 = value; VERA.data0 = value; VERA.data0 = value;
        VERA.data0 = value; VERA.data0 = value; VERA.data0 = value; VERA.data0 = value;
    }
}

void cs_cx16_bitmap_plot(i32 x, i32 y, i32 color)
{
    if (x < 0 || x >= BITMAP_WIDTH || y < 0 || y >= BITMAP_HEIGHT || !in_bitmap())
        return;
    vera_at_pixel((unsigned)x, (unsigned)y, 0);
    VERA.data0 = (unsigned char)color;
}

i32 cs_cx16_bitmap_get(i32 x, i32 y)
{
    if (x < 0 || x >= BITMAP_WIDTH || y < 0 || y >= BITMAP_HEIGHT || !in_bitmap())
        return -1;
    vera_at_pixel((unsigned)x, (unsigned)y, 0);
    return VERA.data0;
}

// Writes `count` bytes of one value to the video memory from the address the data port is
// at (it goes up by one each time): eight at a time, then the rest.
static void run_of(unsigned char value, unsigned count)
{
    for (; count >= 8; count -= 8) {
        VERA.data0 = value; VERA.data0 = value; VERA.data0 = value; VERA.data0 = value;
        VERA.data0 = value; VERA.data0 = value; VERA.data0 = value; VERA.data0 = value;
    }
    while (count-- != 0)
        VERA.data0 = value;
}

// One row of a fill, with the cache on (fx_begin has been called): the pixels from `first` up to but
// not including `end` of a row, whole groups of 4 with the first and the last cut by the mask. The
// cache's write value says which of the four bytes to leave alone.
static void fx_row(unsigned first, unsigned end, unsigned row)
{
    static const unsigned char before[4] = { 0x00, 0x03, 0x0F, 0x3F };   // none | byte 0 | 0 and 1 | 0 to 2
    static const unsigned char after[4] = { 0xFC, 0xF0, 0xC0, 0x00 };    // bytes 1 to 3 | 2 and 3 | 3 | none
    unsigned firstGroup = first & ~3u, lastGroup = (end - 1) & ~3u;
    unsigned char headMask = before[first & 3], tailMask = after[(end - 1) & 3];
    unsigned middle = (lastGroup - firstGroup) / 4;      // groups after the first
    vera_at_pixel(firstGroup, row, 3);                   // up by 4 a write
    if (middle == 0) {
        VERA.data0 = headMask | tailMask;
    } else {
        VERA.data0 = headMask;
        for (unsigned n = middle - 1; n != 0; n--)
            VERA.data0 = 0;
        VERA.data0 = tailMask;
    }
}

// A filled rectangle, cut to the screen.
void cs_cx16_bitmap_fill(i32 x, i32 y, i32 width, i32 height, i32 color)
{
    if (width <= 0 || height <= 0 || !in_bitmap())
        return;
    i32 x1 = x + width, y1 = y + height;
    if (x < 0) x = 0;
    if (y < 0) y = 0;
    if (x1 > BITMAP_WIDTH) x1 = BITMAP_WIDTH;
    if (y1 > BITMAP_HEIGHT) y1 = BITMAP_HEIGHT;
    if (x >= x1 || y >= y1)
        return;
    unsigned count = (unsigned)(x1 - x);
    unsigned char value = (unsigned char)color;
    if (has_fx() && (count >= 16 || (y1 - y) >= 32)) {
        // A small one isn't faster this way, only about as fast: the call from C# costs more than it.
        fx_begin(value);
        for (i32 row = y; row < y1; row++)
            fx_row((unsigned)x, (unsigned)x1, (unsigned)row);
        fx_end();
        return;
    }
    for (i32 row = y; row < y1; row++) {
        vera_at_pixel((unsigned)x, (unsigned)row, 1);
        run_of(value, count);
    }
}

// A pixel, cut to the screen, with the mode already known to be right.
static void plot_checked(int x, int y, unsigned char color)
{
    if ((unsigned)x >= BITMAP_WIDTH || (unsigned)y >= BITMAP_HEIGHT)
        return;
    vera_at_pixel((unsigned)x, (unsigned)y, 0);
    VERA.data0 = color;
}

// A coordinate as a 16-bit int, cut to a range that keeps the arithmetic below inside 16
// bits: a point that far off the picture is as good as any other for what is drawn.
static int near_screen(i32 v) { return v < -8000 ? -8000 : v > 8000 ? 8000 : (int)v; }

// A line by the video chip's line helper (FX): it steps ADDR1 along the longer axis of the
// line (its increment) and, when its slope accumulator carries, also by ADDR0's increment, the
// step along the shorter one. So there is one write to DATA1 for each pixel, about 10 cycles,
// where Bresenham's plot takes 360. The slope is the shorter side over the longer, 0 to 1,
// in 9 fractional bits, and the accumulator starts at a half: pixel k is k * slope + 1/2 down.
// The slope is rounded, which can move a pixel in the middle by one, but the end is exact:
// 0.5 + N * slope stays within 0.31 of the true value for N up to 319. Only for lines
// that are wholly on the screen (the helper doesn't cut), and 3 or more pixels long.
static void fx_line(int ax, int ay, int bx, int by, unsigned char color)
{
    int dx = bx - ax, dy = by - ay;
    unsigned adx = dx < 0 ? -dx : dx, ady = dy < 0 ? -dy : dy;
    char alongX = adx >= ady;
    unsigned major = alongX ? adx : ady, minor = alongX ? ady : adx;
    // Steps, as the increment byte of ADDRx_H wants them: code in bits 7:4 (1 is 1, 14 is 320),
    // 8 for "minus". The major step goes along the longer axis, the minor one along the other.
    unsigned char one = (unsigned char)((1 << 4) | (dx < 0 ? 8 : 0)), row = (unsigned char)((14 << 4) | (dy < 0 ? 8 : 0));
    unsigned char rowByX = (unsigned char)((14 << 4) | (dy < 0 ? 8 : 0)), colByY = (unsigned char)((1 << 4) | (dx < 0 ? 8 : 0));
    unsigned char majorStep = alongX ? one : row, minorStep = alongX ? rowByX : colByY;
    unsigned slope = (unsigned)(((u32)minor * 512ul + major / 2) / major);      // 0 to 512
    u32 start = ((u32)(unsigned)ay << 8) + ((u32)(unsigned)ay << 6) + (unsigned)ax;
    unsigned count = major + 1;

    __asm__ volatile("sei" ::: "memory");
    VERA.control = 2 << 1;                          // DCSEL 2
    VERA.display.fxctrl = 1;                        // address 1 works as the line helper
    VERA.control = 3 << 1;                          // DCSEL 3: the slope (writing its high byte also starts the accumulator at a half)
    VERA.display.fxxincrl = (unsigned char)slope;
    VERA.display.fxxincrh = (unsigned char)(slope >> 8);
    VERA.control = 0;                               // ADDR0: its step is the one along the shorter axis
    VERA.address = 0;
    VERA.address_hi = minorStep;
    VERA.control = 1;                               // ADDR1: the start, and the step along the longer axis
    VERA.address = (unsigned)start;
    VERA.address_hi = (unsigned char)(majorStep | (unsigned char)(start >> 16));
    for (; count >= 8; count -= 8) {
        VERA.data1 = color; VERA.data1 = color; VERA.data1 = color; VERA.data1 = color;
        VERA.data1 = color; VERA.data1 = color; VERA.data1 = color; VERA.data1 = color;
    }
    while (count-- != 0)
        VERA.data1 = color;
    VERA.control = 2 << 1;
    VERA.display.fxctrl = 0;
    VERA.control = 0;
    __asm__ volatile("cli" ::: "memory");
}

// A line from one point to another, cut to the screen pixel by pixel.
void cs_cx16_bitmap_line(i32 x0, i32 y0, i32 x1, i32 y1, i32 color)
{
    if (!in_bitmap())
        return;
    if (y0 == y1) {                                 // across: one run
        i32 left = x0 < x1 ? x0 : x1, right = x0 < x1 ? x1 : x0;
        cs_cx16_bitmap_fill(left, y0, right - left + 1, 1, color);
        return;
    }
    if (has_fx() && (unsigned long)x0 < BITMAP_WIDTH && (unsigned long)x1 < BITMAP_WIDTH
        && (unsigned long)y0 < BITMAP_HEIGHT && (unsigned long)y1 < BITMAP_HEIGHT) {
        i32 w = x1 > x0 ? x1 - x0 : x0 - x1, h = y1 > y0 ? y1 - y0 : y0 - y1;
        if ((w > h ? w : h) >= 3) {
            fx_line((int)x0, (int)y0, (int)x1, (int)y1, (unsigned char)color);
            return;
        }
    }
    int ax = near_screen(x0), ay = near_screen(y0), bx = near_screen(x1), by = near_screen(y1);
    int dx = bx > ax ? bx - ax : ax - bx, dy = by > ay ? ay - by : by - ay;     // dy is minus the height
    int sx = ax < bx ? 1 : -1, sy = ay < by ? 1 : -1, error = dx + dy;
    for (;;) {
        plot_checked(ax, ay, (unsigned char)color);
        if (ax == bx && ay == by)
            return;
        int twice = 2 * error;
        if (twice >= dy) { error += dy; ax += sx; }
        if (twice <= dx) { error += dx; ay += sy; }
    }
}

// The outline of a circle (the midpoint algorithm, an eighth of it at a time).
void cs_cx16_bitmap_circle(i32 cx, i32 cy, i32 radius, i32 color)
{
    if (radius < 0 || !in_bitmap())
        return;
    unsigned char c = (unsigned char)color;
    int px = near_screen(cx), py = near_screen(cy);
    int x = radius > 8000 ? 8000 : (int)radius, y = 0, error = 1 - x;
    while (x >= y) {
        plot_checked(px + x, py + y, c); plot_checked(px - x, py + y, c);
        plot_checked(px + x, py - y, c); plot_checked(px - x, py - y, c);
        plot_checked(px + y, py + x, c); plot_checked(px - y, py + x, c);
        plot_checked(px + y, py - x, c); plot_checked(px - y, py - x, c);
        y++;
        if (error < 0) {
            error += 2 * y + 1;
        } else {
            x--;
            error += 2 * (y - x) + 1;
        }
    }
}

// A filled triangle on a chip with VERA FX, by its polygon helper: the chip follows the two
// edges (a position and an increment each) and says, for each row, where it starts and how
// long it is; the pixels go four at a time through the cache. Found out in the emulator:
// each read of data port 1 steps both positions and points port 1 at the row's start (port 0,
// the row's address, plus the left position); FX_POLY_FILL gives the length and the start's
// place in its group of four; a read of data port 0 goes to the next row. A row covers the
// pixels from floor(left) up to floor(right), and a row's step is twice the register's value,
// in 1/512 of a pixel, so a slope is held to 1/256 of a pixel per row. So that this doesn't
// add up, an edge starts again from the exact one at each picture row that is a multiple of
// 16, and in between depends on nothing but the edge and the row, so that two triangles with
// a common side compute the same pixels on it. The helper's edge is within 1/32 of a pixel of
// the exact one. A position is set 1/256 low, so that an edge exactly through a pixel's middle
// falls as in the software.
typedef struct {
    int x, y;           // the top of the edge
    i32 slope;          // x per row, 16.16
} edge;

static edge edge_of(int xa, int ya, int xb, int yb)
{
    edge e;
    e.x = xa;
    e.y = ya;
    e.slope = ((i32)(xb - xa) << 16) / (yb - ya);
    return e;
}

// An edge's exact x at the middle of a row `rows` rows further on than `value` is at: for a
// whole block a shift, else by adding (fewer than 16 times), never a 32-bit multiplication.
static i32 edge_on(i32 value, i32 slope, int rows)
{
    if (rows == 16)
        return value + (slope << 4);
    while (rows-- > 0)
        value += slope;
    return value;
}

// Gives the helper one edge: its step per row, and the position that makes the row at
// hand start at `value` (the exact x at the middle of the row).
static void fx_edge(char isLeft, i32 value, i32 slope)
{
    int step = (int)((slope + 128) >> 8);           // twice this, in 1/512, is a row
    i32 position = value + 0x8000L - ((i32)step << 7) - 0x100;
    if (position < 0)
        position = 0;
    unsigned char low = (unsigned char)step, high = (unsigned char)(((unsigned)step >> 8) & 0x7F);
    VERA.control = 3 << 1;                          // the step (this also sets the position's fraction)
    if (isLeft) {
        VERA.display.fxxincrl = low;
        VERA.display.fxxincrh = high;
        VERA.control = 4 << 1;
        VERA.display.fxxposl = (unsigned char)(position >> 16);
        VERA.display.fxxposh = (unsigned char)((position >> 24) & 7);
        VERA.control = 5 << 1;
        VERA.display.fxxposs = (unsigned char)(position >> 8);
    } else {
        VERA.display.fxyincrl = low;
        VERA.display.fxyincrh = high;
        VERA.control = 4 << 1;
        VERA.display.fxyposl = (unsigned char)(position >> 16);
        VERA.display.fxyposh = (unsigned char)((position >> 24) & 7);
        VERA.control = 5 << 1;
        VERA.display.fxyposs = (unsigned char)(position >> 8);
    }
}

static void fx_triangle(int top, int middle, int bottom, const edge *longer, const edge *upper, const edge *lower,
    char shortIsLeft, unsigned char color)
{
    static const unsigned char before[4] = { 0x00, 0x03, 0x0F, 0x3F };
    static const unsigned char after[4] = { 0xFC, 0xF0, 0xC0, 0x00 };
    u32 start = ((u32)(unsigned)top << 8) + ((u32)(unsigned)top << 6);

    __asm__ volatile("sei" ::: "memory");
    VERA.control = 6 << 1;
    VERA.display.fxcachel = color;
    VERA.display.fxcachem = color;
    VERA.display.fxcacheh = color;
    VERA.display.fxcacheu = color;
    VERA.control = 2 << 1;
    VERA.display.fxctrl = 2 | 0x40;                 // port 1: polygon helper; cache writes
    VERA.control = 0;                               // ADDR0: the row's first pixel, 320 on
    VERA.address = (unsigned)start;
    VERA.address_hi = (unsigned char)((14 << 4) | (unsigned char)(start >> 16));
    VERA.control = 1;                               // ADDR1: 4 on after each cache write
    VERA.address = 0;
    VERA.address_hi = 3 << 4;

    // The two edges, each with its exact x at its last anchor row.
    const edge *shorter = middle > top ? upper : lower;
    i32 longValue = ((i32)longer->x << 16) + longer->slope / 2, shortValue = ((i32)shorter->x << 16) + shorter->slope / 2;
    int longRow = top, shortRow = top;
    int row = top;
    while (row < bottom) {
        char atMiddle = row == middle && middle > top;
        char newBlock = (row & 15) == 0 && row != top;
        if (atMiddle) {                             // the other short edge starts here
            shorter = lower;
            shortValue = ((i32)lower->x << 16) + lower->slope / 2;
            shortRow = row;
        }
        if (newBlock) {
            longValue = edge_on(longValue, longer->slope, row - longRow);
            longRow = row;
            if (!atMiddle) {
                shortValue = edge_on(shortValue, shorter->slope, row - shortRow);
                shortRow = row;
            }
        }
        if (row == top || newBlock)
            fx_edge(!shortIsLeft, longValue, longer->slope);
        if (row == top || newBlock || atMiddle)
            fx_edge(shortIsLeft, shortValue, shorter->slope);
        VERA.control = 5 << 1;                      // the lengths are read here

        int end = (row | 15) + 1;                   // on to the next anchor row,
        if (row < middle && end > middle)           // the middle vertex
            end = middle;
        if (end > bottom)                           // or the bottom
            end = bottom;
        for (; row < end; row++) {
            (void)VERA.data1;                       // step, and point port 1 at the row's start
            unsigned char low = VERA.display.fxpolyfilll, high = VERA.display.fxpolyfillh;
            if ((high & 0xC0) != 0xC0) {            // not a negative length
                unsigned length = ((unsigned)(high >> 1) << 3) | ((low >> 1) & 7);
                if (length != 0) {
                    unsigned total = ((low >> 5) & 3) + length;
                    unsigned char head = before[(low >> 5) & 3], tail = after[(total - 1) & 3];
                    unsigned groups = (total + 3) >> 2;
                    if (groups == 1) {
                        VERA.data1 = head | tail;
                    } else {
                        VERA.data1 = head;
                        for (groups -= 2; groups != 0; groups--)
                            VERA.data1 = 0;
                        VERA.data1 = tail;
                    }
                }
            }
            (void)VERA.data0;                       // the next row
        }
    }
    VERA.control = 2 << 1;
    VERA.display.fxctrl = 0;
    VERA.control = 0;
    __asm__ volatile("cli" ::: "memory");
}

// A filled triangle, by scan lines. A pixel is inside when its center is: a row of pixels runs from the
// first whose center is not left of the triangle's left edge up to the first whose center is not left of
// the right edge, so that triangles which share an edge leave no gap and don't overlap (the left edge
// and the top row belong to a triangle, the right edge and the bottom row to the next). The edges are
// followed in 16.16 fixed point: the slope of an edge (the x it moves per row) is added once a row.
// On a chip with VERA FX a triangle wholly on the picture is drawn by the chip's polygon helper (above),
// which follows the edges and writes four pixels at a time through the cache. What is left, a triangle
// that is cut by the picture's edge or has an edge flatter than 60 pixels a row, or a chip without FX, is
// drawn here: each row a run of writes, the edges added up in 16.16 (exact to 1/65536 a row).
void cs_cx16_bitmap_triangle(i32 ax, i32 ay, i32 bx, i32 by, i32 cx, i32 cy, i32 color)
{
    if (!in_bitmap())
        return;
    int x[3] = { near_screen(ax), near_screen(bx), near_screen(cx) };
    int y[3] = { near_screen(ay), near_screen(by), near_screen(cy) };
    for (int i = 0; i < 2; i++)                     // sorted by y: 0 is the top, 2 the bottom
        for (int j = 0; j < 2 - i; j++)
            if (y[j] > y[j + 1]) {
                int t = x[j]; x[j] = x[j + 1]; x[j + 1] = t;
                t = y[j]; y[j] = y[j + 1]; y[j + 1] = t;
            }
    if (y[0] == y[2])
        return;                                     // no height: no row has its center inside
    unsigned char value = (unsigned char)color;

    // With the polygon helper: a triangle wholly on the picture (the helper doesn't cut) whose
    // edges aren't flatter than 60 pixels a row (a step has 6 bits of whole pixels).
    if (has_fx() && x[0] >= 0 && x[0] < BITMAP_WIDTH && x[1] >= 0 && x[1] < BITMAP_WIDTH && x[2] >= 0 && x[2] < BITMAP_WIDTH
        && y[0] >= 0 && y[2] < BITMAP_HEIGHT) {
        edge longer = edge_of(x[0], y[0], x[2], y[2]), upper = longer, lower = longer;
        if (y[1] > y[0])
            upper = edge_of(x[0], y[0], x[1], y[1]);
        if (y[2] > y[1])
            lower = edge_of(x[1], y[1], x[2], y[2]);
        const i32 limit = 60L << 16;
        long side = (long)(x[1] - x[0]) * (y[2] - y[0]) - (long)(x[2] - x[0]) * (y[1] - y[0]);
        if (longer.slope < limit && longer.slope > -limit && upper.slope < limit && upper.slope > -limit
            && lower.slope < limit && lower.slope > -limit) {
            if (side != 0)          // (three corners in a line cover no pixel's middle)
                fx_triangle(y[0], y[1], y[2], &longer, &upper, &lower, side < 0, value);
            return;
        }
    }

    i32 longSlope = ((i32)(x[2] - x[0]) << 16) / (y[2] - y[0]);
    i32 longX = ((i32)x[0] << 16) + longSlope / 2;                  // at the center of the top row
    i32 shortSlope, shortX;
    if (y[1] > y[0]) {                              // the upper short edge, from the top to the middle
        shortSlope = ((i32)(x[1] - x[0]) << 16) / (y[1] - y[0]);
        shortX = ((i32)x[0] << 16) + shortSlope / 2;
    } else {                                        // a flat top: the lower one from the start
        shortSlope = ((i32)(x[2] - x[1]) << 16) / (y[2] - y[1]);
        shortX = ((i32)x[1] << 16) + shortSlope / 2;
    }
    char cache = has_fx() && (y[2] - y[0]) >= 6;
    if (cache)
        fx_begin(value);
    for (int row = y[0]; row < y[2] && row < BITMAP_HEIGHT; row++) {
        if (row == y[1] && y[1] > y[0]) {           // the middle: the short edge goes on from the middle vertex
            shortSlope = ((i32)(x[2] - x[1]) << 16) / (y[2] - y[1]);
            shortX = ((i32)x[1] << 16) + shortSlope / 2;
        }
        if (row >= 0) {
            i32 low = shortX < longX ? shortX : longX, high = shortX < longX ? longX : shortX;
            i32 first = (low + 0x7FFF) >> 16, end = (high + 0x7FFF) >> 16;      // ceil(edge - 1/2) in pixels
            if (first < 0) first = 0;
            if (end > BITMAP_WIDTH) end = BITMAP_WIDTH;
            if (first < end) {
                if (cache) {
                    fx_row((unsigned)first, (unsigned)end, (unsigned)row);
                } else {
                    vera_at_pixel((unsigned)first, (unsigned)row, 1);
                    run_of(value, (unsigned)(end - first));
                }
            }
        }
        shortX += shortSlope;
        longX += longSlope;
    }
    if (cache)
        fx_end();
}

// ---- the multiplier (VERA FX) --------------------------------------------------------------
//
// The chip multiplies the two 16-bit numbers held in the first and second half of its cache,
// signed, to 32 bits, and has a 32-bit accumulator: reading FX_ACCUM adds the product to it,
// and a cache write puts product + accumulator into the video memory, from where it is read
// back (4 bytes at $1F800, which neither the KERNAL nor anything here uses). Found out in the
// emulator. Without the chip, C does the same.

#define PRODUCT_ADDRESS 0x1F800ul

static void fx_operands(int a, int b)
{
    VERA.control = 6 << 1;
    VERA.display.fxcachel = (unsigned char)a;
    VERA.display.fxcachem = (unsigned char)((unsigned)a >> 8);
    VERA.display.fxcacheh = (unsigned char)b;
    VERA.display.fxcacheu = (unsigned char)((unsigned)b >> 8);
}

static void fx_multiply_begin(void)
{
    __asm__ volatile("sei" ::: "memory");
    VERA.control = 2 << 1;
    VERA.display.fxmult = 0x90;             // reset the accumulator, multiplier on
    VERA.display.fxmult = 0x10;
    VERA.display.fxctrl = 0x40;             // cache write: it writes the result
}

// Writes product + accumulator to the video memory, reads it back, and switches FX off.
static i32 fx_multiply_end(void)
{
    VERA.control = 0;
    vera_at(PRODUCT_ADDRESS, 0);
    VERA.data0 = 0;
    VERA.control = 2 << 1;
    VERA.display.fxctrl = 0;
    VERA.display.fxmult = 0;
    VERA.control = 0;
    vera_at(PRODUCT_ADDRESS, 1);
    u32 result = VERA.data0;
    result |= (u32)VERA.data0 << 8;
    result |= (u32)VERA.data0 << 16;
    result |= (u32)VERA.data0 << 24;
    __asm__ volatile("cli" ::: "memory");
    return (i32)result;
}

i32 cs_cx16_multiply(i32 a, i32 b)
{
    int x = (int)a, y = (int)b;             // 16 bits, signed
    if (!has_fx())
        return (long)x * y;
    fx_multiply_begin();
    fx_operands(x, y);
    return fx_multiply_end();
}

// The sum of a[i] * b[i], wrapping round at 32 bits: the chip adds up, one product a read.
i32 cs_cx16_dot(const short *a, const short *b, i32 count)
{
    unsigned n = count < 0 ? 0 : (unsigned)count;
    if (!has_fx()) {
        u32 sum = 0;
        for (unsigned i = 0; i < n; i++)
            sum += (u32)((long)a[i] * b[i]);
        return (i32)sum;
    }
    fx_multiply_begin();
    for (unsigned i = 0; i < n; i++) {
        fx_operands(a[i], b[i]);
        (void)VERA.display.fxcachem;        // FX_ACCUM (DCSEL 6): adds the product
    }
    fx_operands(0, 0);
    return fx_multiply_end();
}

// ---- textures drawn rotated and scaled (VERA FX's affine helper) -----------------------------
//
// The source is a map of 2, 8, 32 or 128 tiles each way (a byte a tile, the tile's number)
// of 8x8 tiles of 8 bits a pixel (64 bytes each), in the video memory; both addresses
// multiples of 2048. The position (u, v) in it, in 256ths of a pixel, moves by (du, dv) for
// each pixel drawn along a row, and by (u_row, v_row) from one row to the next. With the
// helper a read of data port 1 gives the pixel at the position and moves on; the chip keeps
// 9 bits after the point, so the 8 given here fit. Past the map's edge it wraps round, or
// shows tile 0 ("clip"). Without FX the same is worked out here, with the same bits, so
// that both draw the same pixels.

static u32 texture_tiles, texture_map;
static unsigned texture_pixels;             // the map's width and height in pixels; 0: no source yet
static unsigned char texture_size_code;
static char texture_clip;

i32 cs_cx16_texture(u32 tiles, u32 map, i32 size, i32 clip)
{
    unsigned code = size == 2 ? 0 : size == 8 ? 1 : size == 32 ? 2 : size == 128 ? 3 : 4;
    if (code == 4 || (tiles & 2047) || (map & 2047) || tiles > 0x1F800ul || map + (u32)size * size > 0x1F800ul)
        return 0;
    texture_tiles = tiles;
    texture_map = map;
    texture_pixels = (unsigned)size * 8;
    texture_size_code = (unsigned char)code;
    texture_clip = clip != 0;
    return 1;
}

static unsigned char texel(long u, long v)
{
    unsigned x = (unsigned)(u >> 8) & 2047, y = (unsigned)(v >> 8) & 2047;
    unsigned tile;
    if (texture_clip && (x >= texture_pixels || y >= texture_pixels))
        tile = 0;
    else {
        x &= texture_pixels - 1;
        y &= texture_pixels - 1;
        vera_at(texture_map + (u32)(y >> 3) * (texture_pixels >> 3) + (x >> 3), 0);
        tile = VERA.data0;
    }
    vera_at(texture_tiles + (u32)tile * 64 + (y & 7) * 8 + (x & 7), 0);
    return VERA.data0;
}

// A width by height rectangle of the bitmap with its top left corner at x, y. u and v are the
// source position of that corner. transparent: source pixels of color 0 leave the picture as it is.
void cs_cx16_affine(i32 x, i32 y, i32 width, i32 height, i32 u, i32 v, i32 du, i32 dv, i32 u_row, i32 v_row, i32 transparent)
{
    if (!in_bitmap() || texture_pixels == 0 || width <= 0 || height <= 0)
        return;
    if (x < 0) { u -= du * x; v -= dv * x; width += x; x = 0; }
    if (y < 0) { u -= u_row * y; v -= v_row * y; height += y; y = 0; }
    if (width > BITMAP_WIDTH - x) width = BITMAP_WIDTH - x;
    if (height > BITMAP_HEIGHT - y) height = BITMAP_HEIGHT - y;
    if (width <= 0 || height <= 0)
        return;

    if (!has_fx()) {
        for (unsigned row = 0; row < (unsigned)height; row++, u += u_row, v += v_row) {
            long pu = u, pv = v;
            for (unsigned i = 0; i < (unsigned)width; i++, pu += du, pv += dv) {
                unsigned char color = texel(pu, pv);
                if (color != 0 || !transparent)
                    plot_checked((int)x + (int)i, (int)y + (int)row, color);
            }
        }
        return;
    }

    unsigned xstep = (unsigned)(du * 2) & 0x7FFF, ystep = (unsigned)(dv * 2) & 0x7FFF;   // 9 bits after the point
    __asm__ volatile("sei" ::: "memory");
    VERA.control = 2 << 1;
    unsigned char mode = (unsigned char)(3 | (transparent ? 0x80 : 0));      // address 1: affine
    VERA.display.fxctrl = mode;
    VERA.display.fxtilebase = (unsigned char)(((unsigned)(texture_tiles >> 11) << 2) | (texture_clip ? 2 : 0));
    VERA.display.fxmapbase = (unsigned char)(((unsigned)(texture_map >> 11) << 2) | texture_size_code);
    VERA.control = 3 << 1;
    VERA.display.fxxincrl = (unsigned char)xstep;
    VERA.display.fxxincrh = (unsigned char)(xstep >> 8);
    VERA.display.fxyincrl = (unsigned char)ystep;
    VERA.display.fxyincrh = (unsigned char)(ystep >> 8);
    for (unsigned row = 0; row < (unsigned)height; row++, u += u_row, v += v_row) {
        VERA.control = 4 << 1;                      // the position's whole part ...
        VERA.display.fxxposl = (unsigned char)(u >> 8);
        VERA.display.fxxposh = (unsigned char)((u >> 16) & 7);
        VERA.display.fxyposl = (unsigned char)(v >> 8);
        VERA.display.fxyposh = (unsigned char)((v >> 16) & 7);
        VERA.control = 5 << 1;                      // ... and what is after the point
        VERA.display.fxxposs = (unsigned char)u;
        VERA.display.fxyposs = (unsigned char)v;
        VERA.control = 0;
        vera_at_pixel((unsigned)x, (unsigned)y + row, 1);
        unsigned n = (unsigned)width;
        for (; n >= 4; n -= 4) {
            VERA.data0 = VERA.data1; VERA.data0 = VERA.data1;
            VERA.data0 = VERA.data1; VERA.data0 = VERA.data1;
        }
        while (n-- != 0)
            VERA.data0 = VERA.data1;

        // A large picture takes many frames: let the interrupts in between rows, with the
        // helper off (the KERNAL's handler saves the addresses, but knows nothing of FX).
        VERA.control = 2 << 1;
        VERA.display.fxctrl = 0;
        VERA.control = 0;
        __asm__ volatile("cli\n\tnop\n\tsei" ::: "memory");
        VERA.control = 2 << 1;
        VERA.display.fxctrl = mode;
    }
    VERA.control = 2 << 1;
    VERA.display.fxctrl = 0;
    VERA.control = 0;
    __asm__ volatile("cli" ::: "memory");
}

// Text: the glyphs of the character set in the video memory (8 bytes a character, from
// $1F000, the screen codes of ISO text), in a color, on a background color or, if that
// is below 0, on what is there. Cut to the screen.
void cs_cx16_bitmap_text(i32 x, i32 y, const unsigned short *text, i32 count, i32 foreground, i32 background)
{
    unsigned char glyph[8], line[8];
    if (!in_bitmap())
        return;
    if (!iso_mode) {
        cbm_k_chrout(0x0F);
        iso_mode = 1;
    }
    for (i32 i = 0; i < count; i++, x += 8) {
        if (x <= -8 || x >= BITMAP_WIDTH || y <= -8 || y >= BITMAP_HEIGHT)
            continue;
        unsigned short c = text[i] < 256 ? text[i] : '?';
        vera_at(0x1F000ul + (unsigned)c * 8, 1);
        for (int row = 0; row < 8; row++)
            glyph[row] = VERA.data0;
        char inside = x >= 0 && x + 8 <= BITMAP_WIDTH;
        for (int row = 0; row < 8; row++) {
            i32 py = y + row;
            if (py < 0 || py >= BITMAP_HEIGHT)
                continue;
            unsigned char bits = glyph[row];
            if (inside && background >= 0) {        // a whole row of 8 in one go
                for (int bit = 0; bit < 8; bit++)
                    line[bit] = (bits & (0x80 >> bit)) ? (unsigned char)foreground : (unsigned char)background;
                vera_at_pixel((unsigned)x, (unsigned)py, 1);
                for (int bit = 0; bit < 8; bit++)
                    VERA.data0 = line[bit];
                continue;
            }
            for (int bit = 0; bit < 8; bit++) {
                i32 px = x + bit;
                if (px < 0 || px >= BITMAP_WIDTH)
                    continue;
                if (bits & (0x80 >> bit))
                    plot_checked((int)px, (int)py, (unsigned char)foreground);
                else if (background >= 0)
                    plot_checked((int)px, (int)py, (unsigned char)background);
            }
        }
    }
}

i32 cs_cx16_columns(void)
{
    screen_mode_info_t info;
    cx16_k_screen_mode_get(&info);
    return info.columns;
}

i32 cs_cx16_rows(void)
{
    screen_mode_info_t info;
    cx16_k_screen_mode_get(&info);
    return info.rows;
}

// Writes UTF-16 text into the text layer from a column and row on, in one
// color (background in the high nibble, text in the low). The caller has
// checked that it fits.
void cs_cx16_print(i32 column, i32 row, const unsigned short *text, i32 count, i32 color)
{
    unsigned width = 32u << ((VERA.layer1.config >> 4) & 3);
    u32 map = (u32)VERA.layer1.mapbase << 9;
    vera_at(map + ((u32)row * width + (unsigned)column) * 2, 1);
    for (unsigned i = 0; i < (unsigned)count; i++) {
        unsigned short c = text[i];
        VERA.data0 = c < 256 ? (unsigned char)c : '?';
        VERA.data0 = (unsigned char)color;
    }
}

// Returns when the video chip has started a new frame (the clock, which its
// interrupt counts, has moved on). The emulator's headless mode (-testbench,
// which cx16/run.sh uses) draws nothing and has no such interrupt, so
// there the wait gives up after about two frames' worth of time.
void cs_cx16_wait_frame(void)
{
    unsigned long start = jiffies();
    unsigned spins = 0;
    while (jiffies() == start && (!in_emulator || ++spins < 3000)) {}
}

// The key pressed since the last call, in ASCII, or 0. In a test the keys
// come from the cartridge input instead (see above), one at a time.
i32 cs_cx16_key(void)
{
    int key;
    if (cart_input()) {
        key = cart_getc();
        return key < 0 ? 0 : key;
    }
    key = cbm_k_getin();
    if (!iso_mode) {        // PETSCII: the letters are in the wrong places
        if (key >= 0x41 && key <= 0x5A)
            return key + 0x20;
        if (key >= 0xC1 && key <= 0xDA)
            return key - 0x80;
    }
    return key == 13 ? '\n' : key;
}

u32 cs_cx16_ticks(void) { return jiffies(); }

// A joystick: 0 is the keyboard's (cursor keys, Ctrl, Alt), 1-4 are the
// controller ports. Up, Down, Left, Right, A and B in the low bits, 1 a
// button pressed (the KERNAL has them the other way round); bit 7 is set for
// a controller that is not there.
i32 cs_cx16_joystick(i32 number)
{
    JoyState joy = cx16_k_joystick_get((unsigned char)number);
    if (joy.detached)
        return 128;
    unsigned char d0 = (unsigned char)~joy.data0;   // B Y Sel Start Up Down Left Right
    unsigned char d1 = (unsigned char)~joy.data1;   // A X L R
    return (d0 & 8 ? 1 : 0) | (d0 & 4 ? 2 : 0) | (d0 & 2 ? 4 : 0) | (d0 & 1 ? 8 : 0)
        | (d1 & 128 ? 16 : 0) | (d0 & 128 ? 32 : 0);
}

// The mouse, through the KERNAL's driver: with a cursor to show, it moves
// sprite 0 itself, in every frame's interrupt. show: 0 off, 1 the default
// arrow, $FF the sprite as the program left it. The area is the screen's
// pixels divided by 8.
void cs_cx16_mouse_config(i32 show)
{
    screen_mode_info_t info;
    cx16_k_screen_mode_get(&info);
    cx16_k_mouse_config((unsigned char)show, info.columns, info.rows);
    mouse_show = (unsigned char)show;
    mouse_on = show != 0;
}

// 0: the pointer's x, 1: its y, 2: the buttons (1 left, 2 right, 4 middle).
i32 cs_cx16_mouse(i32 field)
{
    mouse_pos_t position;
    unsigned char buttons = cx16_k_mouse_get(&position);
    return field == 0 ? position.x : field == 1 ? position.y : buttons;
}

// The YM2151 (FM): an address register at $9F40, a data register at $9F41 (which reads as the status:
// bit 7 is "busy" for about 150 cycles after a write). The chip drops a write that comes while it is
// busy, and wants a pause between the address and the data. The registers can't be read back.
#define YM_ADDRESS (*(volatile unsigned char *)0x9F40)
#define YM_DATA (*(volatile unsigned char *)0x9F41)

void cs_cx16_ym_write(i32 reg, i32 value)
{
    for (unsigned spin = 0; (YM_DATA & 0x80) && spin < 30000u; spin++) {}      // not for ever, if no chip is there
    YM_ADDRESS = (unsigned char)reg;
    __asm__ volatile("nop\n\tnop\n\tnop\n\tnop\n\tnop\n\tnop" ::: "memory");
    YM_DATA = (unsigned char)value;
}

i32 cs_cx16_in_emulator(void) { return in_emulator; }

// The emulator can record what it plays (started with -wav FILE): this makes it start now.
void cs_cx16_start_recording(void)
{
    if (in_emulator)
        *(volatile unsigned char *)0x9FB6 = 1;
}

// The sound chip's 16 voices: 4 bytes each at $1F9C0 in the video memory.
void cs_cx16_voice(i32 voice, i32 frequency, i32 volume, i32 waveform)
{
    unsigned char bytes[4];
    bytes[0] = (unsigned char)frequency;
    bytes[1] = (unsigned char)(frequency >> 8);
    bytes[2] = (unsigned char)(0xC0 | (volume & 63));   // both speakers
    bytes[3] = (unsigned char)(waveform << 6 | 63);
    cs_cx16_vcopy(0x1F9C0u + (unsigned)voice * 4, bytes, 4);
}

// ---- banked RAM, for lib/Banks.cs --------------------------------------------------
//
// The 8 KB window at $A000, and the register at $00 that says which bank is in
// it. Bank 0 is the KERNAL's, so lib/Banks.cs only asks for banks 1 and up.
// Each call switches the bank, moves its bytes and puts the old bank back,
// with interrupts off: a handler that runs in between must find the bank it
// left.

// The number of RAM banks, from the KERNAL's MEMTOP (called with carry set to
// read it, as on the C64; 0 stands for 256).
i32 cs_cx16_bank_count(void)
{
    static unsigned count;
    if (count == 0) {
        unsigned char banks;
        __asm__ volatile("sec\n\tjsr $FF99" : "=a"(banks) : : "x", "y", "c", "memory");
        count = banks == 0 ? 256 : banks;
    }
    return (i32)count - CODE_BANKS;     // the code's banks are not the program's
}

#define WITH_BANK(bank, body) do { \
    unsigned char saved = RAM_BANK; \
    __asm__ volatile("sei" ::: "memory"); \
    RAM_BANK = (unsigned char)((bank) + CODE_BANKS); \
    body; \
    RAM_BANK = saved; \
    __asm__ volatile("cli" ::: "memory"); \
} while (0)

void cs_cx16_bank_read(i32 bank, i32 offset, void *destination, i32 count)
{
    WITH_BANK(bank, memcpy(destination, (const void *)(0xA000u + (unsigned)offset), (size_t)count));
}

void cs_cx16_bank_write(i32 bank, i32 offset, const void *source, i32 count)
{
    WITH_BANK(bank, memcpy((void *)(0xA000u + (unsigned)offset), source, (size_t)count));
}

void cs_cx16_bank_fill(i32 bank, i32 offset, i32 value, i32 count)
{
    WITH_BANK(bank, memset((void *)(0xA000u + (unsigned)offset), (int)value, (size_t)count));
}
