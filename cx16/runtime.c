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
// (RDTIM, from the vertical blank interrupt). That is all the clock there is:
// both of clock_gettime's clocks start at power-on.
static u32 jiffies(void) { return cx16_k_rdtim(); }

i32 cs_clock_gettime(i32 clock, i64 *timespec)
{
    u32 ticks = jiffies();
    (void)clock;
    timespec[0] = ticks / 60;
    timespec[1] = (i64)(ticks % 60) * 16666667;
    return 0;
}

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
}

// 0: the pointer's x, 1: its y, 2: the buttons (1 left, 2 right, 4 middle).
i32 cs_cx16_mouse(i32 field)
{
    mouse_pos_t position;
    unsigned char buttons = cx16_k_mouse_get(&position);
    return field == 0 ? position.x : field == 1 ? position.y : buttons;
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
