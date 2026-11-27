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

// For every function here that switches the RAM bank, or puts it back after the KERNAL: it
// must stay in fixed RAM. The link optimizes the program and this file as one, and would
// otherwise build such a function into a caller in a code bank, which then switches away the
// very code that is running. (Seen with constant arguments: Banks.Fill(0, 400, 1) hung.)
#define FIXED_RAM __attribute__((noinline))

static char in_emulator;
static int file_errno;          // what lib/FileSystem.cs reads after a failed call: C's error numbers
static unsigned char program_device;    // the device the program was loaded from (set in main)
#ifdef CX16_FILES
static void files_shutdown(void);
#endif

// Leaves the program: back to BASIC's READY (the build links llvm-mos's
// save-basic.o, which puts back the memory BASIC needs). The exit code has
// nowhere to go on an X16, so in the emulator it is written to $9FB9, where
// cx16/run.sh finds it ("User debug 1: $xx" on the emulator's standard output)
// and ends the emulator.
static char iso_mode;       // cs_cx16_clear switched the screen to ISO text

// Set by the PCM code once it has hooked the interrupt, which must not stay hooked.
static void (*pcm_shutdown)(void);

static void leave(int code)
{
    if (pcm_shutdown)
        pcm_shutdown();
#ifdef CX16_FILES
    files_shutdown();
#endif
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
    program_device = *(volatile unsigned char *)0x00BA ? *(volatile unsigned char *)0x00BA : 8;   // before anything else opens
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
void *cs_realloc(void *pointer, u32 size) { return size > 0xFFF0 ? 0 : realloc(pointer, (size_t)size); }

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
// In the headless emulator no frames pass, and the clock stands still: what a program
// sleeps there is added here, so that sleepers wake and timers fire.
static u32 slept;

static u32 jiffies(void) { return cx16_k_rdtim() + slept; }

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
    // The wall clock is read before the jiffies are looked at: reading it takes a while, and
    // a jiffy that passes meanwhile would make the time since then -1, which without a sign
    // is 828 days. Jiffies below those of the reading (a count that has started again) mean
    // another reading.
    if (clock == 0 && (!wall_known || jiffies() < wall_jiffies))
        read_wall_clock();
    u32 ticks = jiffies();
    if (clock == 0) {
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

// ---- integer division and multiplication ---------------------------------------------
//
// As in c64/runtime.c, from which this section is copied: change both. The
// differences are the two loops marked "two pairs": the X16's imaginary
// registers have a gap between __rc9 ($09) and __rc10 ($18), so an index
// mustn't run across it. tests/cx16-math.sh checks the section. The cycle counts
// below are the C64's.
//
// The 6502 can't multiply or divide. For `/`, `%` and `*` it can't do with
// shifts, llvm-mos calls __udivsi3, __mulsi3 and the like from its library,
// written in C: dividing C#'s 32-bit numbers there costs 1,100-5,300 cycles
// of a frame's 19,700, multiplying 1,250-2,500, and printing a number
// (lib/Str.cs, Fmt.UInt: ulong % 10 and / 10) up to 16,000 a digit. The
// library keeps them in three pieces (twenty divisions from 8 to 64 bits;
// the four that give quotient and remainder together; four
// multiplications), and replacing part of a piece means defining all of
// it, here.
//
// The 16-, 32- and 64-bit divisions and the 32-bit multiplication are in
// assembly: 2-5 times faster than the library's for the numbers programs
// use, 1.3-7 for multiplying. The rest are C around them, or the library's
// own method in C, as fast as before (on 8 bits it is hard to beat). The
// library is faster only where its divisor and dividend are within a bit
// or two of each other, by a few hundred cycles.
//
// Arguments and results follow llvm-mos's calling convention: the first
// number's bytes in A, X, __rc2, __rc3, the second's after them, low byte
// first; the result in the first's place. __rc2-__rc19, A, X and Y are the
// caller's to save.

typedef signed char i8;
typedef unsigned char u8;
typedef short i16;
typedef unsigned short u16;

// Unsigned division in assembly. Long division, a bit at a time: the
// dividend shifts left into the remainder, and wherever the divisor fits it
// is subtracted and a 1 shifts into the quotient (where the dividend's bit
// was). Two shortcuts first: a dividend below the divisor is the remainder,
// and the dividend's top bytes go into the remainder whole while it stays
// below the divisor (a zero byte always does). The remainder is then kept
// as wide as the divisor, in A when that is a byte (about 35 cycles a bit),
// with the carry as its extra top bit.
//
// zero_udivmod32: n in A, X, __rc2, __rc3 and d (not 0) in __rc4-__rc7;
// the quotient comes out in __rc8-__rc11, the remainder in __rc12-__rc15.
__asm__(
    "  .section .text.zero_udivmod32,\"ax\",@progbits\n"
    "zero_udivmod32:\n"
    "  sta __rc8\n"
    "  stx __rc9\n"
    "  lda __rc2\n"
    "  sta __rc10\n"
    "  lda __rc3\n"
    "  sta __rc11\n"
    "  lda __rc8\n"                   // n < d: the quotient is 0
    "  cmp __rc4\n"
    "  lda __rc9\n"
    "  sbc __rc5\n"
    "  lda __rc10\n"
    "  sbc __rc6\n"
    "  lda __rc11\n"
    "  sbc __rc7\n"
    "  bcs .Ldiv32\n"
    "  lda __rc8\n"
    "  sta __rc12\n"
    "  lda __rc9\n"
    "  sta __rc13\n"
    "  lda __rc10\n"
    "  sta __rc14\n"
    "  lda __rc11\n"
    "  sta __rc15\n"
    "  lda #0\n"
    "  sta __rc8\n"
    "  sta __rc9\n"
    "  sta __rc10\n"
    "  sta __rc11\n"
    "  rts\n"
    ".Ldiv32:\n"
    "  lda __rc10\n"                  // n under 65,536 (and so d): 16 bits
    "  ora __rc11\n"
    "  bne .Ldiv32_wide\n"
    "  sta __rc14\n"
    "  sta __rc15\n"
    "  jmp zero_udivmod16_from32\n"
    ".Ldiv32_wide:\n"
    "  lda #0\n"
    "  sta __rc12\n"
    "  sta __rc13\n"
    "  sta __rc14\n"
    "  sta __rc15\n"
    "  ldy #32\n"                     // bits left
    ".Ldiv32_zero:\n"                 // a zero top byte (n >= d: not all)
    "  lda __rc11\n"
    "  bne .Ldiv32_byte_in\n"
    "  lda __rc10\n"
    "  sta __rc11\n"
    "  lda __rc9\n"
    "  sta __rc10\n"
    "  lda __rc8\n"
    "  sta __rc9\n"
    "  lda #0\n"
    "  sta __rc8\n"
    "  tya\n"
    "  sec\n"
    "  sbc #8\n"
    "  tay\n"
    "  jmp .Ldiv32_zero\n"
    ".Ldiv32_byte_in:\n"              // remainder * 256 + top byte < d?
    "  lda __rc15\n"
    "  bne .Ldiv32_bits\n"
    "  lda __rc11\n"
    "  cmp __rc4\n"
    "  lda __rc12\n"
    "  sbc __rc5\n"
    "  lda __rc13\n"
    "  sbc __rc6\n"
    "  lda __rc14\n"
    "  sbc __rc7\n"
    "  bcs .Ldiv32_bits\n"
    "  lda __rc14\n"                  // yes: it's the remainder, 8 zero bits
    "  sta __rc15\n"                  // of the quotient (n >= d: not all 32)
    "  lda __rc13\n"
    "  sta __rc14\n"
    "  lda __rc12\n"
    "  sta __rc13\n"
    "  lda __rc11\n"
    "  sta __rc12\n"
    "  lda __rc10\n"
    "  sta __rc11\n"
    "  lda __rc9\n"
    "  sta __rc10\n"
    "  lda __rc8\n"
    "  sta __rc9\n"
    "  lda #0\n"
    "  sta __rc8\n"
    "  tya\n"
    "  sec\n"
    "  sbc #8\n"
    "  tay\n"
    "  jmp .Ldiv32_byte_in\n"
    ".Ldiv32_bits:\n"
    "  lda __rc6\n"
    "  ora __rc7\n"
    "  bne .Ldiv32_long\n"
    "  lda __rc5\n"
    "  bne .Ldiv32_short\n"
    "  lda __rc12\n"                  // a byte: the remainder in A
    ".Ldiv32_byte:\n"
    "  asl __rc8\n"
    "  rol __rc9\n"
    "  rol __rc10\n"
    "  rol __rc11\n"
    "  rol a\n"
    "  bcs .Ldiv32_byte_fits\n"       // a ninth bit: more than d for sure
    "  cmp __rc4\n"
    "  bcc .Ldiv32_byte_next\n"
    ".Ldiv32_byte_fits:\n"
    "  sbc __rc4\n"                   // the carry is set both ways here
    "  inc __rc8\n"
    ".Ldiv32_byte_next:\n"
    "  dey\n"
    "  bne .Ldiv32_byte\n"
    "  sta __rc12\n"
    "  rts\n"
    ".Ldiv32_short:\n"                // 16 bits
    "  asl __rc8\n"
    "  rol __rc9\n"
    "  rol __rc10\n"
    "  rol __rc11\n"
    "  rol __rc12\n"
    "  rol __rc13\n"
    "  bcs .Ldiv32_short_fits\n"
    "  lda __rc12\n"
    "  cmp __rc4\n"
    "  lda __rc13\n"
    "  sbc __rc5\n"
    "  bcc .Ldiv32_short_next\n"
    ".Ldiv32_short_fits:\n"
    "  lda __rc12\n"
    "  sbc __rc4\n"
    "  sta __rc12\n"
    "  lda __rc13\n"
    "  sbc __rc5\n"
    "  sta __rc13\n"
    "  inc __rc8\n"
    ".Ldiv32_short_next:\n"
    "  dey\n"
    "  bne .Ldiv32_short\n"
    "  rts\n"
    ".Ldiv32_long:\n"                 // 32 bits
    "  asl __rc8\n"
    "  rol __rc9\n"
    "  rol __rc10\n"
    "  rol __rc11\n"
    "  rol __rc12\n"
    "  rol __rc13\n"
    "  rol __rc14\n"
    "  rol __rc15\n"
    "  bcs .Ldiv32_long_fits\n"
    "  lda __rc12\n"
    "  cmp __rc4\n"
    "  lda __rc13\n"
    "  sbc __rc5\n"
    "  lda __rc14\n"
    "  sbc __rc6\n"
    "  lda __rc15\n"
    "  sbc __rc7\n"
    "  bcc .Ldiv32_long_next\n"
    ".Ldiv32_long_fits:\n"
    "  lda __rc12\n"
    "  sbc __rc4\n"
    "  sta __rc12\n"
    "  lda __rc13\n"
    "  sbc __rc5\n"
    "  sta __rc13\n"
    "  lda __rc14\n"
    "  sbc __rc6\n"
    "  sta __rc14\n"
    "  lda __rc15\n"
    "  sbc __rc7\n"
    "  sta __rc15\n"
    "  inc __rc8\n"
    ".Ldiv32_long_next:\n"
    "  dey\n"
    "  bne .Ldiv32_long\n"
    "  rts\n"
    "  .section .text.__udivsi3,\"ax\",@progbits\n"
    "  .globl __udivsi3\n"
    "__udivsi3:\n"
    "  jsr zero_udivmod32\n"
    ".Ludivsi3_quotient:\n"
    "  lda __rc10\n"
    "  sta __rc2\n"
    "  lda __rc11\n"
    "  sta __rc3\n"
    "  lda __rc8\n"
    "  ldx __rc9\n"
    "  rts\n"
    "  .section .text.__umodsi3,\"ax\",@progbits\n"
    "  .globl __umodsi3\n"
    "__umodsi3:\n"
    "  jsr zero_udivmod32\n"
    "  lda __rc14\n"
    "  sta __rc2\n"
    "  lda __rc15\n"
    "  sta __rc3\n"
    "  lda __rc12\n"
    "  ldx __rc13\n"
    "  rts\n"
    "  .section .text.__udivmodsi4,\"ax\",@progbits\n"
    "  .globl __udivmodsi4\n"
    "__udivmodsi4:\n"                 // both: the remainder through a pointer,
    "  ldy __rc8\n"                   // in __rc8, __rc9
    "  sty __rc16\n"
    "  ldy __rc9\n"
    "  sty __rc17\n"
    "  jsr zero_udivmod32\n"
    "  ldy #0\n"
    "  lda __rc12\n"
    "  sta (__rc16),y\n"
    "  iny\n"
    "  lda __rc13\n"
    "  sta (__rc16),y\n"
    "  iny\n"
    "  lda __rc14\n"
    "  sta (__rc16),y\n"
    "  iny\n"
    "  lda __rc15\n"
    "  sta (__rc16),y\n"
    "  jmp .Ludivsi3_quotient\n"

    // zero_udivmod16, the same on 16 bits: n in A, X and d (not 0) in
    // __rc2, __rc3; the quotient comes out in __rc8, __rc9 and the
    // remainder in __rc12, __rc13. The 32-bit division comes here for a
    // dividend under 65,536. A dividend under 256, or whose top byte is
    // below d, takes 8 steps on a byte.
    "  .section .text.zero_udivmod16,\"ax\",@progbits\n"
    "zero_udivmod16:\n"
    "  ldy __rc2\n"
    "  sty __rc4\n"
    "  ldy __rc3\n"
    "  sty __rc5\n"
    "  sta __rc8\n"
    "  stx __rc9\n"
    "  cmp __rc4\n"                   // n < d: the quotient is 0
    "  txa\n"
    "  sbc __rc5\n"
    "  bcs zero_udivmod16_from32\n"
    "  lda __rc8\n"
    "  sta __rc12\n"
    "  stx __rc13\n"
    "  lda #0\n"
    "  sta __rc8\n"
    "  sta __rc9\n"
    "  rts\n"
    "zero_udivmod16_from32:\n"        // n >= d in __rc8, __rc9 and d in __rc4, __rc5
    "  lda #0\n"
    "  sta __rc13\n"
    "  ldy #8\n"
    "  ldx __rc9\n"
    "  bne .Ldiv16_two\n"
    ".Ldiv8:\n"                       // a byte left in __rc8, the remainder in A
    "  asl __rc8\n"
    "  rol a\n"
    "  bcs .Ldiv8_fits\n"
    "  cmp __rc4\n"
    "  bcc .Ldiv8_next\n"
    ".Ldiv8_fits:\n"
    "  sbc __rc4\n"
    "  inc __rc8\n"
    ".Ldiv8_next:\n"
    "  dey\n"
    "  bne .Ldiv8\n"
    "  sta __rc12\n"
    "  rts\n"
    ".Ldiv16_two:\n"
    "  lda __rc5\n"
    "  bne .Ldiv16_wide\n"
    "  cpx __rc4\n"                   // the top byte below d: it's the
    "  bcs .Ldiv16_bytes\n"           // remainder, and a byte is left
    "  txa\n"
    "  ldx #0\n"
    "  stx __rc9\n"
    "  beq .Ldiv8\n"
    ".Ldiv16_bytes:\n"                // 16 bits, the remainder in A
    "  lda #0\n"
    "  ldy #16\n"
    ".Ldiv16_byte:\n"
    "  asl __rc8\n"
    "  rol __rc9\n"
    "  rol a\n"
    "  bcs .Ldiv16_byte_fits\n"
    "  cmp __rc4\n"
    "  bcc .Ldiv16_byte_next\n"
    ".Ldiv16_byte_fits:\n"
    "  sbc __rc4\n"
    "  inc __rc8\n"
    ".Ldiv16_byte_next:\n"
    "  dey\n"
    "  bne .Ldiv16_byte\n"
    "  sta __rc12\n"
    "  rts\n"
    ".Ldiv16_wide:\n"                 // d above a byte: the top byte is the
    "  stx __rc12\n"                  // remainder, a byte is left
    "  lda #0\n"
    "  sta __rc9\n"
    ".Ldiv16_short:\n"
    "  asl __rc8\n"
    "  rol __rc12\n"
    "  rol __rc13\n"
    "  bcs .Ldiv16_short_fits\n"
    "  lda __rc12\n"
    "  cmp __rc4\n"
    "  lda __rc13\n"
    "  sbc __rc5\n"
    "  bcc .Ldiv16_short_next\n"
    ".Ldiv16_short_fits:\n"
    "  lda __rc12\n"
    "  sbc __rc4\n"
    "  sta __rc12\n"
    "  lda __rc13\n"
    "  sbc __rc5\n"
    "  sta __rc13\n"
    "  inc __rc8\n"
    ".Ldiv16_short_next:\n"
    "  dey\n"
    "  bne .Ldiv16_short\n"
    "  rts\n"
    "  .section .text.__udivhi3,\"ax\",@progbits\n"
    "  .globl __udivhi3\n"
    "__udivhi3:\n"
    "  jsr zero_udivmod16\n"
    "  lda __rc8\n"
    "  ldx __rc9\n"
    "  rts\n"
    "  .section .text.__umodhi3,\"ax\",@progbits\n"
    "  .globl __umodhi3\n"
    "__umodhi3:\n"
    "  jsr zero_udivmod16\n"
    "  lda __rc12\n"
    "  ldx __rc13\n"
    "  rts\n"
    "  .text\n");

u32 __udivsi3(u32 n, u32 d);
u32 __umodsi3(u32 n, u32 d);
u32 __udivmodsi4(u32 n, u32 d, u32 *rem);
u16 __udivhi3(u16 n, u16 d);
u16 __umodhi3(u16 n, u16 d);

// Unsigned division in C, the library's way: the divisor shifts left up to
// the dividend's top bit, and back down a bit at a time, subtracted
// wherever it fits.
#define ZERO_UDIV(T, udiv, umod)                                                \
    T udiv(T n, T d)                                                            \
    {                                                                           \
        if (d == 0 || d > n)                                                    \
            return 0;                                                           \
        u8 shift = zero_align_##T(n, &d);                                       \
        T q = 1;                                                                \
        n -= d;                                                                 \
        for (; shift != 0; shift--) {                                           \
            d >>= 1;                                                            \
            q <<= 1;                                                            \
            if (d <= n) {                                                       \
                n -= d;                                                         \
                q |= 1;                                                         \
            }                                                                   \
        }                                                                       \
        return q;                                                               \
    }                                                                           \
    T umod(T n, T d)                                                            \
    {                                                                           \
        if (d == 0 || d > n)                                                    \
            return n;                                                           \
        u8 shift = zero_align_##T(n, &d);                                       \
        n -= d;                                                                 \
        for (; shift != 0; shift--) {                                           \
            d >>= 1;                                                            \
            if (d <= n)                                                         \
                n -= d;                                                         \
        }                                                                       \
        return n;                                                               \
    }
// d, at most n, shifted left as far as it stays so; returns how far.
#define ZERO_ALIGN(T)                                                           \
    static inline u8 zero_align_##T(T n, T *dp)                                 \
    {                                                                           \
        T d = *dp;                                                              \
        u8 shift = 0;                                                           \
        while (!(d >> (sizeof(T) * 8 - 1)) && (T)(d << 1) <= n) {              \
            d <<= 1;                                                            \
            shift++;                                                            \
        }                                                                       \
        *dp = d;                                                                \
        return shift;                                                           \
    }
ZERO_ALIGN(u8)
ZERO_UDIV(u8, __udivqi3, __umodqi3)

// zero_udivmod64_short: n in A, X, __rc2-__rc7, d under 65,536 (not 0) in
// __rc8, __rc9 and a pointer for the remainder in __rc10, __rc11; the
// quotient comes back in n's place. The dividend shifts in __rc12-__rc19,
// the remainder in __rc2, __rc3.
u64 zero_udivmod64_short(u64 n, u16 d, u64 *rem);
__asm__(
    "  .section .text.zero_udivmod64_short,\"ax\",@progbits\n"
    "zero_udivmod64_short:\n"
    "  sta __rc12\n"
    "  stx __rc13\n"
    "  ldx #5\n"
    ".Ldiv64_copy:\n"
    "  lda __rc2,x\n"
    "  sta __rc14,x\n"
    "  dex\n"
    "  bpl .Ldiv64_copy\n"
    "  lda #0\n"
    "  sta __rc2\n"
    "  sta __rc3\n"
    "  ldy #64\n"
    ".Ldiv64_skip:\n"                 // the dividend's zero top bytes
    "  lda __rc19\n"
    "  bne .Ldiv64_bits\n"
    "  ldx #6\n"
    ".Ldiv64_move:\n"
    "  lda __rc12,x\n"
    "  sta __rc13,x\n"
    "  dex\n"
    "  bpl .Ldiv64_move\n"
    "  lda #0\n"
    "  sta __rc12\n"
    "  tya\n"
    "  sec\n"
    "  sbc #8\n"
    "  tay\n"
    "  bne .Ldiv64_skip\n"
    "  beq .Ldiv64_out\n"             // n was 0
    ".Ldiv64_bits:\n"
    "  lda __rc9\n"
    "  bne .Ldiv64_short\n"
    "  lda #0\n"                      // a byte: the remainder in A
    ".Ldiv64_byte:\n"
    "  asl __rc12\n"
    "  rol __rc13\n"
    "  rol __rc14\n"
    "  rol __rc15\n"
    "  rol __rc16\n"
    "  rol __rc17\n"
    "  rol __rc18\n"
    "  rol __rc19\n"
    "  rol a\n"
    "  bcs .Ldiv64_byte_fits\n"
    "  cmp __rc8\n"
    "  bcc .Ldiv64_byte_next\n"
    ".Ldiv64_byte_fits:\n"
    "  sbc __rc8\n"
    "  inc __rc12\n"
    ".Ldiv64_byte_next:\n"
    "  dey\n"
    "  bne .Ldiv64_byte\n"
    "  sta __rc2\n"
    "  beq .Ldiv64_out\n"             // Y is 0
    ".Ldiv64_short:\n"
    "  asl __rc12\n"
    "  rol __rc13\n"
    "  rol __rc14\n"
    "  rol __rc15\n"
    "  rol __rc16\n"
    "  rol __rc17\n"
    "  rol __rc18\n"
    "  rol __rc19\n"
    "  rol __rc2\n"
    "  rol __rc3\n"
    "  bcs .Ldiv64_short_fits\n"
    "  lda __rc2\n"
    "  cmp __rc8\n"
    "  lda __rc3\n"
    "  sbc __rc9\n"
    "  bcc .Ldiv64_short_next\n"
    ".Ldiv64_short_fits:\n"
    "  lda __rc2\n"
    "  sbc __rc8\n"
    "  sta __rc2\n"
    "  lda __rc3\n"
    "  sbc __rc9\n"
    "  sta __rc3\n"
    "  inc __rc12\n"
    ".Ldiv64_short_next:\n"
    "  dey\n"
    "  bne .Ldiv64_short\n"
    ".Ldiv64_out:\n"
    "  lda __rc10\n"                  // the remainder, if wanted
    "  ora __rc11\n"
    "  beq .Ldiv64_quotient\n"
    "  ldy #0\n"
    "  lda __rc2\n"
    "  sta (__rc10),y\n"
    "  iny\n"
    "  lda __rc3\n"
    "  sta (__rc10),y\n"
    "  lda #0\n"
    ".Ldiv64_rest:\n"
    "  iny\n"
    "  sta (__rc10),y\n"
    "  cpy #7\n"
    "  bne .Ldiv64_rest\n"
    ".Ldiv64_quotient:\n"
    "  ldx #5\n"
    ".Ldiv64_back:\n"
    "  lda __rc14,x\n"
    "  sta __rc2,x\n"
    "  dex\n"
    "  bpl .Ldiv64_back\n"
    "  lda __rc12\n"
    "  ldx __rc13\n"
    "  rts\n"
    "  .text\n");

// 64 bits (C#'s long and ulong, and the decimal and double formatting
// that works in them). zero_udivmod64: n in A, X, __rc2-__rc7 and d (not
// 0) in __rc8-__rc15; the quotient comes back in n's place and the
// remainder in zero_rem64. A dividend below the divisor is the remainder,
// one that fits in 32 bits goes to the 32-bit division, and a divisor
// under 65,536 to zero_udivmod64_short. Others (rare) go the library's
// way: the divisor shifts left up to the dividend's top bit, and back down
// a bit at a time, subtracted wherever it fits.
__asm__(
    "  .section .noinit.zero_rem64,\"aw\",@nobits\n"
    "  .globl zero_rem64\n"
    "zero_rem64:\n"
    "  .zero 8\n"
    "  .section .text.zero_udivmod64,\"ax\",@progbits\n"
    "zero_udivmod64:\n"
    "  sta __rc18\n"
    "  stx __rc19\n"
    "  cmp __rc8\n"                   // n < d: the quotient is 0
    "  txa\n"
    "  sbc __rc9\n"
    "  lda __rc2\n"
    "  sbc __rc10\n"
    "  lda __rc3\n"
    "  sbc __rc11\n"
    "  lda __rc4\n"
    "  sbc __rc12\n"
    "  lda __rc5\n"
    "  sbc __rc13\n"
    "  lda __rc6\n"
    "  sbc __rc14\n"
    "  lda __rc7\n"
    "  sbc __rc15\n"
    "  bcs .Ldiv64_ge\n"
    "  lda __rc18\n"                  // the remainder is n
    "  sta zero_rem64\n"
    "  lda __rc19\n"
    "  sta zero_rem64+1\n"
    "  ldx #5\n"
    ".Ldiv64_n:\n"
    "  lda __rc2,x\n"
    "  sta zero_rem64+2,x\n"
    "  lda #0\n"
    "  sta __rc2,x\n"
    "  dex\n"
    "  bpl .Ldiv64_n\n"
    "  tax\n"
    "  rts\n"
    ".Ldiv64_ge:\n"
    "  lda __rc4\n"                   // n in 32 bits (and so d)?
    "  ora __rc5\n"
    "  ora __rc6\n"
    "  ora __rc7\n"
    "  bne .Ldiv64_wide\n"
    "  ldx #1\n"                      // two pairs: __rc8-__rc11 aren't
    ".Ldiv64_d32:\n"                  // one block here
    "  lda __rc8,x\n"
    "  sta __rc4,x\n"
    "  lda __rc10,x\n"
    "  sta __rc6,x\n"
    "  dex\n"
    "  bpl .Ldiv64_d32\n"
    "  lda __rc18\n"
    "  ldx __rc19\n"
    "  jsr zero_udivmod32\n"
    "  ldx #3\n"
    ".Ldiv64_r32:\n"
    "  lda __rc12,x\n"
    "  sta zero_rem64,x\n"
    "  lda #0\n"
    "  sta zero_rem64+4,x\n"
    "  sta __rc4,x\n"
    "  dex\n"
    "  bpl .Ldiv64_r32\n"
    "  lda __rc10\n"
    "  sta __rc2\n"
    "  lda __rc11\n"
    "  sta __rc3\n"
    "  lda __rc8\n"
    "  ldx __rc9\n"
    "  rts\n"
    ".Ldiv64_wide:\n"
    "  lda __rc10\n"                  // d under 65,536?
    "  ora __rc11\n"
    "  ora __rc12\n"
    "  ora __rc13\n"
    "  ora __rc14\n"
    "  ora __rc15\n"
    "  bne .Ldiv64_long\n"
    "  lda #mos16lo(zero_rem64)\n"
    "  sta __rc10\n"
    "  lda #mos16hi(zero_rem64)\n"
    "  sta __rc11\n"
    "  lda __rc18\n"
    "  ldx __rc19\n"
    "  jmp zero_udivmod64_short\n"
    ".Ldiv64_long:\n"   // n in __rc18, __rc19, __rc2-__rc7; d shifts
    "  ldy #0\n"   // up to n's top bit, a byte then a bit at a time
    ".Ldiv64_long_bytes:\n"
    "  lda __rc15\n"
    "  bne .Ldiv64_long_bits\n"
    "  lda __rc19\n"
    "  cmp __rc8\n"
    "  lda __rc2\n"
    "  sbc __rc9\n"
    "  lda __rc3\n"
    "  sbc __rc10\n"
    "  lda __rc4\n"
    "  sbc __rc11\n"
    "  lda __rc5\n"
    "  sbc __rc12\n"
    "  lda __rc6\n"
    "  sbc __rc13\n"
    "  lda __rc7\n"
    "  sbc __rc14\n"
    "  bcc .Ldiv64_long_bits\n"
    "  lda __rc14\n"
    "  sta __rc15\n"
    "  lda __rc13\n"
    "  sta __rc14\n"
    "  lda __rc12\n"
    "  sta __rc13\n"
    "  lda __rc11\n"
    "  sta __rc12\n"
    "  lda __rc10\n"
    "  sta __rc11\n"
    "  lda __rc9\n"
    "  sta __rc10\n"
    "  lda __rc8\n"
    "  sta __rc9\n"
    "  lda #0\n"
    "  sta __rc8\n"
    "  tya\n"
    "  clc\n"
    "  adc #8\n"
    "  tay\n"
    "  jmp .Ldiv64_long_bytes\n"
    ".Ldiv64_long_bits:\n"
    "  lda __rc15\n"
    "  bmi .Ldiv64_long_aligned\n"
    "  asl __rc8\n"
    "  rol __rc9\n"
    "  rol __rc10\n"
    "  rol __rc11\n"
    "  rol __rc12\n"
    "  rol __rc13\n"
    "  rol __rc14\n"
    "  rol __rc15\n"
    "  lda __rc18\n"
    "  cmp __rc8\n"
    "  lda __rc19\n"
    "  sbc __rc9\n"
    "  lda __rc2\n"
    "  sbc __rc10\n"
    "  lda __rc3\n"
    "  sbc __rc11\n"
    "  lda __rc4\n"
    "  sbc __rc12\n"
    "  lda __rc5\n"
    "  sbc __rc13\n"
    "  lda __rc6\n"
    "  sbc __rc14\n"
    "  lda __rc7\n"
    "  sbc __rc15\n"
    "  bcc .Ldiv64_long_back\n"
    "  iny\n"
    "  jmp .Ldiv64_long_bits\n"
    ".Ldiv64_long_back:\n"
    "  lsr __rc15\n"
    "  ror __rc14\n"
    "  ror __rc13\n"
    "  ror __rc12\n"
    "  ror __rc11\n"
    "  ror __rc10\n"
    "  ror __rc9\n"
    "  ror __rc8\n"
    ".Ldiv64_long_aligned:\n"   // the quotient, in zero_rem64 for now: 1
    "  lda #1\n"
    "  sta zero_rem64\n"
    "  lda #0\n"
    "  ldx #6\n"
    ".Ldiv64_long_q:\n"
    "  sta zero_rem64+1,x\n"
    "  dex\n"
    "  bpl .Ldiv64_long_q\n"
    "  sec\n"
    "  lda __rc18\n"
    "  sbc __rc8\n"
    "  sta __rc18\n"
    "  lda __rc19\n"
    "  sbc __rc9\n"
    "  sta __rc19\n"
    "  lda __rc2\n"
    "  sbc __rc10\n"
    "  sta __rc2\n"
    "  lda __rc3\n"
    "  sbc __rc11\n"
    "  sta __rc3\n"
    "  lda __rc4\n"
    "  sbc __rc12\n"
    "  sta __rc4\n"
    "  lda __rc5\n"
    "  sbc __rc13\n"
    "  sta __rc5\n"
    "  lda __rc6\n"
    "  sbc __rc14\n"
    "  sta __rc6\n"
    "  lda __rc7\n"
    "  sbc __rc15\n"
    "  sta __rc7\n"
    ".Ldiv64_long_step:\n"   // back down a bit at a time
    "  tya\n"
    "  bne .Ldiv64_long_more\n"
    "  jmp .Ldiv64_long_done\n"
    ".Ldiv64_long_more:\n"
    "  dey\n"
    "  lsr __rc15\n"
    "  ror __rc14\n"
    "  ror __rc13\n"
    "  ror __rc12\n"
    "  ror __rc11\n"
    "  ror __rc10\n"
    "  ror __rc9\n"
    "  ror __rc8\n"
    "  asl zero_rem64\n"
    "  rol zero_rem64+1\n"
    "  rol zero_rem64+2\n"
    "  rol zero_rem64+3\n"
    "  rol zero_rem64+4\n"
    "  rol zero_rem64+5\n"
    "  rol zero_rem64+6\n"
    "  rol zero_rem64+7\n"
    "  lda __rc18\n"
    "  cmp __rc8\n"
    "  lda __rc19\n"
    "  sbc __rc9\n"
    "  lda __rc2\n"
    "  sbc __rc10\n"
    "  lda __rc3\n"
    "  sbc __rc11\n"
    "  lda __rc4\n"
    "  sbc __rc12\n"
    "  lda __rc5\n"
    "  sbc __rc13\n"
    "  lda __rc6\n"
    "  sbc __rc14\n"
    "  lda __rc7\n"
    "  sbc __rc15\n"
    "  bcs .Ldiv64_long_fits\n"
    "  jmp .Ldiv64_long_step\n"
    ".Ldiv64_long_fits:\n"
    "  lda __rc18\n"
    "  sbc __rc8\n"
    "  sta __rc18\n"
    "  lda __rc19\n"
    "  sbc __rc9\n"
    "  sta __rc19\n"
    "  lda __rc2\n"
    "  sbc __rc10\n"
    "  sta __rc2\n"
    "  lda __rc3\n"
    "  sbc __rc11\n"
    "  sta __rc3\n"
    "  lda __rc4\n"
    "  sbc __rc12\n"
    "  sta __rc4\n"
    "  lda __rc5\n"
    "  sbc __rc13\n"
    "  sta __rc5\n"
    "  lda __rc6\n"
    "  sbc __rc14\n"
    "  sta __rc6\n"
    "  lda __rc7\n"
    "  sbc __rc15\n"
    "  sta __rc7\n"
    "  inc zero_rem64\n"
    "  jmp .Ldiv64_long_step\n"
    ".Ldiv64_long_done:\n"   // what's left of n is the remainder: swap
    "  lda zero_rem64+2\n"
    "  ldx __rc2\n"
    "  stx zero_rem64+2\n"
    "  sta __rc2\n"
    "  lda zero_rem64+3\n"
    "  ldx __rc3\n"
    "  stx zero_rem64+3\n"
    "  sta __rc3\n"
    "  lda zero_rem64+4\n"
    "  ldx __rc4\n"
    "  stx zero_rem64+4\n"
    "  sta __rc4\n"
    "  lda zero_rem64+5\n"
    "  ldx __rc5\n"
    "  stx zero_rem64+5\n"
    "  sta __rc5\n"
    "  lda zero_rem64+6\n"
    "  ldx __rc6\n"
    "  stx zero_rem64+6\n"
    "  sta __rc6\n"
    "  lda zero_rem64+7\n"
    "  ldx __rc7\n"
    "  stx zero_rem64+7\n"
    "  sta __rc7\n"
    "  lda zero_rem64\n"
    "  ldx __rc18\n"
    "  stx zero_rem64\n"
    "  sta __rc18\n"
    "  lda zero_rem64+1\n"
    "  ldx __rc19\n"
    "  stx zero_rem64+1\n"
    "  sta __rc19\n"
    "  lda __rc18\n"
    "  ldx __rc19\n"
    "  rts\n"

    // The library's three, on top of it. __udivmoddi4's pointer comes on
    // the stack: the numbers fill the registers.
    "  .section .text.__udivdi3,\"ax\",@progbits\n"
    "  .globl __udivdi3\n"
    "__udivdi3:\n"
    "  jmp zero_udivmod64\n"
    "  .section .text.__umoddi3,\"ax\",@progbits\n"
    "  .globl __umoddi3\n"
    "__umoddi3:\n"
    "  jsr zero_udivmod64\n"
    "  ldx #5\n"
    ".Lumoddi3_copy:\n"
    "  lda zero_rem64+2,x\n"
    "  sta __rc2,x\n"
    "  dex\n"
    "  bpl .Lumoddi3_copy\n"
    "  lda zero_rem64\n"
    "  ldx zero_rem64+1\n"
    "  rts\n"
    "  .section .text.__udivmoddi4,\"ax\",@progbits\n"
    "  .globl __udivmoddi4\n"
    "__udivmoddi4:\n"
    "  jsr zero_udivmod64\n"
    "  sta __rc18\n"
    "  ldy #0\n"
    "  lda (__rc0),y\n"
    "  sta __rc16\n"
    "  iny\n"
    "  lda (__rc0),y\n"
    "  sta __rc17\n"
    "  ora __rc16\n"
    "  beq .Ludivmoddi4_done\n"
    "  ldy #7\n"
    ".Ludivmoddi4_copy:\n"
    "  lda zero_rem64,y\n"
    "  sta (__rc16),y\n"
    "  dey\n"
    "  bpl .Ludivmoddi4_copy\n"
    ".Ludivmoddi4_done:\n"
    "  lda __rc18\n"
    "  rts\n"
    "  .text\n");

u64 __udivdi3(u64 n, u64 d);
u64 __umoddi3(u64 n, u64 d);
u64 __udivmoddi4(u64 n, u64 d, u64 *rem);

// Signed: rounded towards zero, and the remainder has the dividend's sign,
// as in C and C#. The minimum value divided by -1 overflows to itself (C#
// throws before it gets here).
#define ZERO_DIVMOD(S, U, div, mod, udiv, umod)                                 \
    S div(S n, S d)                                                             \
    {                                                                           \
        U q = udiv(n < 0 ? (U)(0 - (U)n) : (U)n, d < 0 ? (U)(0 - (U)d) : (U)d);  \
        return (S)((n < 0) != (d < 0) ? (U)(0 - q) : q);                        \
    }                                                                           \
    S mod(S n, S d)                                                             \
    {                                                                           \
        U r = umod(n < 0 ? (U)(0 - (U)n) : (U)n, d < 0 ? (U)(0 - (U)d) : (U)d);  \
        return (S)(n < 0 ? (U)(0 - r) : r);                                     \
    }
ZERO_DIVMOD(i8, u8, __divqi3, __modqi3, __udivqi3, __umodqi3)
ZERO_DIVMOD(i16, u16, __divhi3, __modhi3, __udivhi3, __umodhi3)
ZERO_DIVMOD(i32, u32, __divsi3, __modsi3, __udivsi3, __umodsi3)
ZERO_DIVMOD(i64, u64, __divdi3, __moddi3, __udivdi3, __umoddi3)

// Both at once, the remainder through a pointer.
#define ZERO_SDIVMOD(S, U, divmod, udivmod)                                     \
    S divmod(S n, S d, S *rem)                                                  \
    {                                                                           \
        U r;                                                                    \
        U q = udivmod(n < 0 ? 0 - (U)n : (U)n, d < 0 ? 0 - (U)d : (U)d, &r);    \
        *rem = (S)(n < 0 ? 0 - r : r);                                          \
        return (S)((n < 0) != (d < 0) ? 0 - q : q);                             \
    }
ZERO_SDIVMOD(i32, u32, __divmodsi4, __udivmodsi4)
ZERO_SDIVMOD(i64, u64, __divmoddi4, __udivmoddi4)
u8 __udivmodqi4(u8 n, u8 d, u8 *rem) { *rem = __umodqi3(n, d); return __udivqi3(n, d); }
u16 __udivmodhi4(u16 n, u16 d, u16 *rem) { *rem = __umodhi3(n, d); return __udivhi3(n, d); }
i8 __divmodqi4(i8 n, i8 d, i8 *rem) { *rem = __modqi3(n, d); return __divqi3(n, d); }
i16 __divmodhi4(i16 n, i16 d, i16 *rem) { *rem = __modhi3(n, d); return __divhi3(n, d); }

// The low 32 bits of a * b (the same for signed and unsigned numbers), in
// assembly: a in A, X, __rc2, __rc3 and b in __rc4-__rc7. The factor with
// fewer bytes is the multiplier, taken from the bottom a byte at a time:
// for each 1 bit the multiplicand is added, and it shifts left a bit for
// the next. A byte ends with its last 1 bit, a zero byte costs a byte
// move, and the work ends with the multiplier's last nonzero byte.
__asm__(
    "  .section .text.__mulsi3,\"ax\",@progbits\n"
    "  .globl __mulsi3\n"
    "__mulsi3:\n"
    "  sta __rc8\n"                   // the multiplicand: __rc8-__rc11
    "  stx __rc9\n"
    "  lda __rc2\n"
    "  sta __rc10\n"
    "  lda __rc3\n"
    "  sta __rc11\n"
    "  lda __rc6\n"                   // b above 16 bits: a is shorter?
    "  ora __rc7\n"
    "  beq .Lmul_b_short\n"
    "  lda __rc10\n"
    "  ora __rc11\n"
    "  beq .Lmul_swap\n"
    "  bne .Lmul_start\n"
    ".Lmul_b_short:\n"                // b above 8 bits: is a a byte?
    "  lda __rc5\n"
    "  beq .Lmul_start\n"
    "  lda __rc9\n"
    "  ora __rc10\n"
    "  ora __rc11\n"
    "  bne .Lmul_start\n"
    ".Lmul_swap:\n"
    "  ldx #1\n"                      // two pairs: __rc8-__rc11 aren't
    ".Lmul_swap_byte:\n"              // one block here
    "  lda __rc4,x\n"
    "  ldy __rc8,x\n"
    "  sta __rc8,x\n"
    "  sty __rc4,x\n"
    "  lda __rc6,x\n"
    "  ldy __rc10,x\n"
    "  sta __rc10,x\n"
    "  sty __rc6,x\n"
    "  dex\n"
    "  bpl .Lmul_swap_byte\n"
    ".Lmul_start:\n"
    "  lda #0\n"                      // the product: __rc12-__rc15
    "  sta __rc12\n"
    "  sta __rc13\n"
    "  sta __rc14\n"
    "  sta __rc15\n"
    ".Lmul_byte:\n"
    "  lda __rc4\n"
    "  beq .Lmul_zero_byte\n"
    "  ldy #8\n"                      // shifts of the multiplicand left
    ".Lmul_bit:\n"
    "  lsr __rc4\n"
    "  bcc .Lmul_shift\n"
    "  clc\n"
    "  lda __rc12\n"
    "  adc __rc8\n"
    "  sta __rc12\n"
    "  lda __rc13\n"
    "  adc __rc9\n"
    "  sta __rc13\n"
    "  lda __rc14\n"
    "  adc __rc10\n"
    "  sta __rc14\n"
    "  lda __rc15\n"
    "  adc __rc11\n"
    "  sta __rc15\n"
    ".Lmul_shift:\n"
    "  asl __rc8\n"
    "  rol __rc9\n"
    "  rol __rc10\n"
    "  rol __rc11\n"
    "  dey\n"
    "  lda __rc4\n"
    "  bne .Lmul_bit\n"
    "  lda __rc5\n"                   // the byte's last 1 bit: more bytes?
    "  ora __rc6\n"
    "  ora __rc7\n"
    "  beq .Lmul_done\n"
    "  tya\n"
    "  beq .Lmul_next\n"
    ".Lmul_rest:\n"                   // the byte's remaining shifts
    "  asl __rc8\n"
    "  rol __rc9\n"
    "  rol __rc10\n"
    "  rol __rc11\n"
    "  dey\n"
    "  bne .Lmul_rest\n"
    "  beq .Lmul_next\n"
    ".Lmul_zero_byte:\n"
    "  lda __rc5\n"
    "  ora __rc6\n"
    "  ora __rc7\n"
    "  beq .Lmul_done\n"
    "  lda __rc10\n"                  // shift the multiplicand a byte
    "  sta __rc11\n"
    "  lda __rc9\n"
    "  sta __rc10\n"
    "  lda __rc8\n"
    "  sta __rc9\n"
    "  lda #0\n"
    "  sta __rc8\n"
    ".Lmul_next:\n"                   // and the multiplier the other way
    "  lda __rc5\n"
    "  sta __rc4\n"
    "  lda __rc6\n"
    "  sta __rc5\n"
    "  lda __rc7\n"
    "  sta __rc6\n"
    "  lda #0\n"
    "  sta __rc7\n"
    "  beq .Lmul_byte\n"
    ".Lmul_done:\n"
    "  lda __rc14\n"
    "  sta __rc2\n"
    "  lda __rc15\n"
    "  sta __rc3\n"
    "  lda __rc12\n"
    "  ldx __rc13\n"
    "  rts\n"
    "  .text\n");

// The other widths in C, the library's way: add a, shifting left, for each
// 1 bit of b, until b has none left.
#define ZERO_MUL(T, name)                                                       \
    T name(T a, T b)                                                            \
    {                                                                           \
        T p = 0;                                                                \
        while (b != 0) {                                                        \
            if (b & 1)                                                          \
                p += a;                                                         \
            a <<= 1;                                                            \
            b >>= 1;                                                            \
        }                                                                       \
        return p;                                                               \
    }
ZERO_MUL(u8, __mulqi3)
ZERO_MUL(u16, __mulhi3)
ZERO_MUL(u64, __muldi3)

// ---- end of integer division ----------------------------------------------------------

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
// ---- files, for lib/File.cs and lib/Disk.cs ------------------------------------------
//
// Only in a program that uses them (the build defines CX16_FILES then).
//
// The KERNAL's file calls, used directly: llvm-mos's stdio is made for the old Commodore
// drives. It sends "name,R" without a file type, which CMDR-DOS (the SD card's) doesn't
// take for reading (a missing file then opens, and an existing one is refused as "FILE
// EXISTS"), and it allows 16 characters of a name.
//
// A file is on the disk device the program came from (8: the SD card, or an IEC drive).
// A path may start with "#" and a device number and a colon ("#9:notes.txt") to name
// another. The rest is CBM DOS's: "name", or "//folder/:name".
//
// What the KERNAL can't do is move around in a file. So a file is a handle (1 to
// MAX_FILES) and this keeps the position itself. A file opened for reading can be moved
// anywhere: forward by reading and throwing the bytes away, backward by opening it again,
// and the end by reading to it. That is slow (File.Length reads a file through twice) but
// it works. A file opened for writing or appending can only "move" to where it is.
//
// Every file is read and written byte for byte. CBM DOS won't write over a file, so one
// that is written is removed first. Logical file numbers and secondary addresses: 2 to 5
// for the files, 7 for a directory, 15 for the command channel, whose answer says
// whether an open worked.

#define MAX_FILES 4
#define MAX_NAME 80
#define DIR_FILE 7

i32 cs_cx16_bank_count(void);

static struct {
    unsigned long position;
    unsigned long size;         // of a file that is read and written ('m')
    char path[MAX_NAME];
    unsigned char device;
    char mode;          // 'r', 'w', 'a' or 'm' (read and written); 0: the slot is free
    char ended;         // the last byte has been read
    char moved;         // 'm': the drive's position isn't this one
} files[MAX_FILES];

static u32 command_channels;            // a bit for each drive whose command channel is open

// The path without a "#9:" in front of it, and the device that said: the
// program's if nothing did. 0 if the device is not a number from 8 to 30.
static const char *take_device(const char *path, unsigned char *device)
{
    *device = program_device;
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

// take_device, and the path as a plain one, "folder/name" or "/folder/name". CBM DOS's own
// forms ("//folder/:name" from the root, "/folder/:name", "0:name") are put that way, since
// the emulator's host folder takes no folder in them: removing "folder/:name" there would
// remove the root's "name". Two results can be held at a time. 0 for a path that is too long.
static const char *take_path(const char *given, unsigned char *device)
{
    static char plain[2][MAX_NAME];
    static unsigned char turn;
    const char *path = take_device(given, device);
    if (path == 0 || strlen(path) >= MAX_NAME)
        return 0;
    const char *colon = strchr(path, ':');
    if (colon == 0)
        return path;
    char *into = plain[turn ^= 1];
    const char *folder = path;
    while (folder < colon && *folder >= '0' && *folder <= '9')
        folder++;                           // a drive number
    unsigned length = (unsigned)(colon - folder);
    if (length && folder[0] == '/')
        folder++, length--;                 // the slash that starts a folder; a second one is the root
    memcpy(into, folder, length);
    strcpy(into + length, colon + 1);
    return into;
}

// The RAM bank, which a call to the KERNAL must not lose: the caller may be code in a bank
// (see load_code_banks). ROM R47 comes back with bank 0 from a device that isn't there (R49
// doesn't), and the program would go on in the KERNAL's data. So every function here that
// talks to a drive puts back the bank it was called with.
static void put_back_bank(const unsigned char *bank) { RAM_BANK = *bank; }
#define KEEP_RAM_BANK __attribute__((cleanup(put_back_bank), unused)) unsigned char bank_kept = RAM_BANK

// Opening anything on a device that isn't there leaves the KERNAL unable to open
// anything, on any device, until it has closed everything. So that is what
// happens: every open file of the program is closed too.
static void forget_everything(void)
{
    cbm_k_clrch();
    cbm_k_clall();
    for (int i = 0; i < MAX_FILES; i++)
        files[i].mode = 0;
    command_channels = 0;
}

// The command channel of a drive (logical file 90 + its number) is opened when first
// needed and stays open: on CBM drives, closing it closes every file on the drive.

// Sends a command to the drive (an empty one just asks) and reads its answer: the code,
// 0 for "OK", 62 for "FILE NOT FOUND", ... -1: no such device.
static unsigned dos_count;      // the number after the message: how many files a scratch removed

static int dos_command(unsigned char device, const char *command)
{
    unsigned char lfn = (unsigned char)(90 + device);
    if (!(command_channels & (1ul << device))) {
        cbm_k_setnam("");
        cbm_k_setlfs(lfn, device, 15);
        if (cbm_k_open() || (cbm_k_readst() & 0x80)) {
            forget_everything();
            return -1;
        }
        command_channels |= 1ul << device;
    }
    if (*command) {
        if (cbm_k_ckout(lfn) || (cbm_k_readst() & 0x80)) {
            forget_everything();
            return -1;
        }
        while (*command)
            cbm_k_chrout((unsigned char)*command++);
        cbm_k_clrch();                      // the end of it: the drive does it now
    }
    if (cbm_k_chkin(lfn) || (cbm_k_readst() & 0x80)) {
        forget_everything();
        return -1;
    }
    int code = 0;
    unsigned char digits = 0, commas = 0;
    dos_count = 0;
    for (unsigned char i = 0; i < 64; i++) {            // "62, FILE NOT FOUND,00,00" and a CR
        unsigned char c = cbm_k_chrin();
        unsigned char status = cbm_k_readst();
        if (status & 0x82) {
            forget_everything();
            return -1;
        }
        if (digits < 2 && c >= '0' && c <= '9') {
            code = code * 10 + (c - '0');
            digits++;
        } else {
            digits = 2;
        }
        if (c == ',')
            commas++;
        else if (commas == 2 && c >= '0' && c <= '9')
            dos_count = dos_count * 10 + (c - '0');
        if (c == 13 || status)
            break;
    }
    cbm_k_clrch();
    return code;
}

// "0:name,S,R" for the drive (the letters of commands, types and modes are capitals for it); 0 if it doesn't fit. A name that has a colon of its own
// (a folder: "//folder/:name") gets no "0:".
static char dos_name(char *into, const char *prefix, const char *path, const char *suffix)
{
    if (strlen(path) == 0 || strlen(path) >= MAX_NAME - 8 || strchr(path, ',') != 0)
        return 0;
    strcpy(into, prefix);
    if (strchr(path, ':') == 0)
        strcat(into, "0:");
    strcat(into, path);
    strcat(into, suffix);
    return 1;
}

// A path in the form commands and listings need, appended to `into`: ":name" for the
// current folder (the emulator's host folder knows no other), else "/folder/:name" from
// there and "//folder/:name" from the root ("//:name" for the root itself). (To open a
// file, the drive takes "folder/name" as it is.) 0 if it doesn't fit.
static char dos_form(char *into, unsigned room, const char *path)
{
    const char *slash = strrchr(path, '/');
    unsigned used = strlen(into);
    unsigned folder = slash ? (unsigned)(slash - path) : 0;
    const char *name = slash ? slash + 1 : path;
    if (used + folder + strlen(name) + 5 >= room || strchr(path, ',') != 0 || strchr(path, ':') != 0)
        return 0;
    char *p = into + used;
    if (slash) {
        *p++ = '/';
        if (folder) {                   // "/folder" is from the root: it has its own slash
            memcpy(p, path, folder);
            p += folder;
            *p++ = '/';
        } else {
            *p++ = '/';
        }
    }
    *p++ = ':';
    strcpy(p, name);
    return 1;
}

// Opens slot's data channel; 1 if the drive says OK.
static char open_channel(int slot, unsigned char device, const char *path, char mode)
{
    char name[MAX_NAME + 8];
    if (!dos_name(name, "", path, mode == 'r' ? ",S,R" : mode == 'w' ? ",S,W" : mode == 'a' ? ",S,A" : ",S,M"))
        return 0;
    unsigned char lfn = (unsigned char)(2 + slot);
    cbm_k_setnam(name);
    cbm_k_setlfs(lfn, device, lfn);
    if (cbm_k_open() || (cbm_k_readst() & 0x80)) {
        forget_everything();
        return 0;
    }
    int code = dos_command(device, "");
    if (code < 0)
        return 0;
    if (code >= 20) {
        cbm_k_close(lfn);
        // CBM DOS's numbers as C's: not found, there already, write protected, disk full.
        file_errno = code == 62 ? 2 : code == 63 ? 17 : code == 26 ? 30 : code == 72 ? 28 : 5;
        return 0;
    }
    return 1;
}

static int file_slot(i32 handle)
{
    return handle >= 1 && handle <= MAX_FILES && files[handle - 1].mode ? (int)handle - 1 : -1;
}

static char look_up(unsigned char device, const char *given, u32 *size, long long *seconds);

static char kind_of(unsigned char device, const char *path)
{
    u32 size;
    long long seconds;
    return look_up(device, path, &size, &seconds);
}

// Sends a command that removes `path`, which was seen to be there; whether it went. The
// answers alone don't say: the SD card answers "01" with the number that went, also when
// that is 0; the emulator's host folder says "01" with 0 for what did go.
static char remove_with(unsigned char device, const char *command, const char *path)
{
    int code = dos_command(device, command);
    return code >= 0 && code <= 1 && (dos_count != 0 || kind_of(device, path) == 0);
}

// A command with a plain path, which both the SD card and the host folder follow.
static char path_command(char *into, unsigned room, const char *verb, const char *path)
{
    if (strlen(verb) + strlen(path) >= room || strchr(path, ',') != 0)
        return 0;
    strcpy(into, verb);
    strcat(into, path);
    return 1;
}

// Removes a file; whether one went.
static char scratch(unsigned char device, const char *path)
{
    char command[MAX_NAME + 8];
    return path_command(command, sizeof command, "S0:", path) && kind_of(device, path) == 1 && remove_with(device, command, path);
}

static long file_size(unsigned char device, const char *path);

FIXED_RAM i32 cs_fopen(const char *given, const char *mode)
{
    KEEP_RAM_BANK;
    unsigned char device;
    file_errno = 22;                        // a path or a mode that won't do
    const char *path = take_path(given, &device);
    if (path == 0 || strlen(path) >= MAX_NAME - 8)
        return 0;
    char how = mode[0];
    // "r+" and "w+" (what FileStream asks for any file it makes) are CMDR-DOS's ",M":
    // read and written at will. A drive on the serial bus has no such thing.
    char both = strchr(mode, '+') != 0 && how != 'a';
    int slot = 0;
    while (slot < MAX_FILES && files[slot].mode)
        slot++;
    if (slot == MAX_FILES || (how != 'r' && how != 'w' && how != 'a'))
        return 0;
    long size = 0;
    if (how == 'w')
        scratch(device, path);
    if (both) {
        // ",M" makes a file that isn't there, which "r+" must not do; "w+" makes it first.
        if (!open_channel(slot, device, path, how))
            return 0;
        cbm_k_close((unsigned char)(2 + slot));
        if (how == 'r' && (size = file_size(device, path)) < 0)
            return 0;
        how = 'm';
    }
    if (!open_channel(slot, device, path, how)) {
        // Appending to a file that isn't there makes it.
        if (how != 'a' || !open_channel(slot, device, path, 'w'))
            return 0;
    }
    files[slot].device = device;
    files[slot].mode = how;
    files[slot].position = 0;
    files[slot].size = (unsigned long)size;
    files[slot].ended = 0;
    files[slot].moved = 0;
    strcpy(files[slot].path, path);
    return slot + 1;
}

// Before a file that is read and written is used: the drive's position put where ours is.
// Once its end has been read the channel is dead, so it is opened again. 0 on failure.
static char settle(int slot)
{
    unsigned char lfn = (unsigned char)(2 + slot);
    if (files[slot].ended) {
        cbm_k_close(lfn);
        if (!open_channel(slot, files[slot].device, files[slot].path, 'm')) {
            files[slot].mode = 0;
            return 0;
        }
        files[slot].ended = 0;
        files[slot].moved = 1;
    }
    if (files[slot].moved) {
        // "P", the channel (the secondary address), and the position, low byte first.
        unsigned long at = files[slot].position;
        if (cbm_k_ckout((unsigned char)(90 + files[slot].device)))
            return 0;
        cbm_k_chrout('P');
        cbm_k_chrout(lfn);
        for (unsigned char i = 0; i < 4; i++, at >>= 8)
            cbm_k_chrout((unsigned char)at);
        cbm_k_clrch();
        if (dos_command(files[slot].device, "") > 1)
            return 0;
        files[slot].moved = 0;
    }
    return 1;
}

// Up to count bytes from slot's file; what was read.
static unsigned read_bytes(int slot, unsigned char *buffer, unsigned count)
{
    if (files[slot].mode == 'm') {
        if (files[slot].position >= files[slot].size)
            return 0;
        unsigned long left = files[slot].size - files[slot].position;
        if (count > left)
            count = (unsigned)left;
        if (!settle(slot))
            return 0;
    } else if (files[slot].mode != 'r' || files[slot].ended) {
        return 0;
    }
    if (count == 0 || cbm_k_chkin((unsigned char)(2 + slot)))
        return 0;
    unsigned n = 0;
    while (n < count) {
        unsigned char c = cbm_k_chrin();
        unsigned char status = cbm_k_readst();
        if (status & 0x82) {                // no byte: the end of an empty file, or an error
            files[slot].ended = 1;
            break;
        }
        if (buffer)
            buffer[n] = c;
        n++;
        if (status & 0x40) {                // that was the last byte
            files[slot].ended = 1;
            break;
        }
    }
    cbm_k_clrch();
    files[slot].position += n;
    return n;
}

FIXED_RAM u32 cs_fread(void *buffer, u32 size, u32 count, i32 handle)
{
    KEEP_RAM_BANK;
    int slot = file_slot(handle);
    if (slot < 0 || size == 0 || size > 0xFFFF || count > 0xFFFF || size * count > 0xFFFF)
        return 0;
    return read_bytes(slot, (unsigned char *)buffer, (unsigned)(size * count)) / (unsigned)size;
}

// count bytes (zeros if bytes is 0) to slot's file at its position; what was written.
static unsigned write_bytes(int slot, const unsigned char *bytes, unsigned count)
{
    if (files[slot].mode == 'm' && !settle(slot))
        return 0;
    if (cbm_k_ckout((unsigned char)(2 + slot)))
        return 0;
    unsigned n = 0;
    while (n < count) {
        cbm_k_chrout(bytes ? bytes[n] : 0);
        if (cbm_k_readst() & 0x82)
            break;
        n++;
    }
    cbm_k_clrch();
    files[slot].position += n;
    if (files[slot].mode == 'm' && files[slot].position > files[slot].size)
        files[slot].size = files[slot].position;
    return n;
}

FIXED_RAM u32 cs_fwrite(const void *buffer, u32 size, u32 count, i32 handle)
{
    KEEP_RAM_BANK;
    int slot = file_slot(handle);
    if (slot < 0 || files[slot].mode == 'r' || size == 0 || count == 0 || size > 0xFFFF || count > 0xFFFF || size * count > 0xFFFF)
        return 0;
    if (files[slot].mode == 'm' && files[slot].position > files[slot].size) {
        // Past the end: what is between is zeros, as elsewhere.
        unsigned long to = files[slot].position;
        files[slot].position = files[slot].size;
        files[slot].moved = 1;
        while (files[slot].position < to) {
            unsigned long left = to - files[slot].position;
            if (write_bytes(slot, 0, left < 256 ? (unsigned)left : 256) == 0)
                return 0;
        }
    }
    return write_bytes(slot, (const unsigned char *)buffer, (unsigned)(size * count)) / (unsigned)size;
}

FIXED_RAM i32 cs_fclose(i32 handle)
{
    KEEP_RAM_BANK;
    int slot = file_slot(handle);
    if (slot < 0)
        return -1;
    cbm_k_close((unsigned char)(2 + slot));
    files[slot].mode = 0;
    return 0;
}

// Reads and drops bytes until the position is `target`, or the file ends.
static void skip_to(int slot, unsigned long target)
{
    while (files[slot].position < target) {
        unsigned long left = target - files[slot].position;
        if (read_bytes(slot, 0, left < 256 ? (unsigned)left : 256) == 0)
            return;
    }
}

FIXED_RAM i32 cs_fseeko(i32 handle, long long offset, i32 origin)
{
    KEEP_RAM_BANK;
    int slot = file_slot(handle);
    if (slot < 0)
        return -1;
    if (files[slot].mode == 'm') {
        long long at = offset + (origin == 1 ? (long long)files[slot].position : origin == 2 ? (long long)files[slot].size : 0);
        if (at < 0 || at > 0x7FFFFFFFLL)
            return -1;
        if ((unsigned long)at != files[slot].position) {
            files[slot].position = (unsigned long)at;
            files[slot].moved = 1;
        }
        return 0;
    }
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
        cbm_k_close((unsigned char)(2 + slot));
        files[slot].position = 0;
        files[slot].ended = 0;
        if (!open_channel(slot, files[slot].device, files[slot].path, 'r')) {
            files[slot].mode = 0;
            return -1;
        }
    }
    skip_to(slot, (unsigned long)target);
    return files[slot].position == (unsigned long)target ? 0 : -1;
}

long long cs_ftello(i32 handle)
{
    int slot = file_slot(handle);
    return slot < 0 ? -1 : (long long)files[slot].position;
}

FIXED_RAM i32 cs_remove(const char *given)
{
    KEEP_RAM_BANK;
    unsigned char device;
    const char *path = take_path(given, &device);
    file_errno = 22;
    if (path == 0 || *path == 0 || strchr(path, '*') != 0 || strchr(path, '?') != 0)
        return -1;                          // (the drive would take those for any name)
    file_errno = 2;
    return scratch(device, path) ? 0 : -1;
}

// A command to the drive, such as "md:folder" or "cd:..": its answer's code (0 and 1 are
// fine), or -1 if there is no such drive. The word before the colon is made capitals.
FIXED_RAM i32 cs_cx16_dos(const char *given)
{
    KEEP_RAM_BANK;
    unsigned char device;
    const char *command = take_device(given, &device);
    if (command == 0 || *command == 0 || strlen(command) >= MAX_NAME)
        return -1;
    char text[MAX_NAME];
    strcpy(text, command);
    for (char *p = text; *p && *p != ':'; p++)
        if (*p >= 'a' && *p <= 'z')
            *p -= 'a' - 'A';
    return dos_command(device, text);
}

// ---- a directory: the listing the drive makes, as a BASIC program ------------------------
//
// After two bytes of load address come lines: a link (0 ends them), a number (a file's
// size in blocks) and text ending in 0. The first line is the volume's name, a file's has
// its name in quotes and then its type, and the last one says how much is free. The SD
// card, asked with "$=L", adds the date, an attribute and the exact size in hex.

// CMDR-DOS can't have a listing open while a file is opened or read: it then ends early,
// gives nonsense or hangs. So the whole listing is read at once, into the topmost RAM bank
// (which Banks doesn't count in a program that lists folders), and then handed out from
// there. An entry: the name's length, whether it is a folder, the size, the name.

static char dir_name[MAX_NAME];
static unsigned char dir_name_length, dir_is_folder, dir_bank, dir_complete = 1;
static u32 dir_size;
static unsigned dir_used, dir_at;       // bytes of entries in the bank; the next one

static int dir_byte(void)
{
    unsigned char c = cbm_k_chrin();
    return cbm_k_readst() & 0x82 ? -1 : c;
}

// One line's number and text; 0 at the end.
static char dir_line(unsigned *number, char *text, unsigned char room)
{
    int low = dir_byte(), high = dir_byte();
    if (low < 0 || high < 0 || (low | high) == 0)
        return 0;
    low = dir_byte();
    high = dir_byte();
    if (low < 0 || high < 0)
        return 0;
    *number = (unsigned)low | (unsigned)high << 8;
    unsigned char n = 0;
    for (;;) {
        int c = dir_byte();
        if (c <= 0)
            break;
        if (n < room - 1)
            text[n++] = (char)c;
    }
    text[n] = 0;
    return 1;
}

// Copies to or from the bank of entries, with interrupts off: a handler must find the
// bank it left.
FIXED_RAM static void dir_copy(unsigned at, unsigned char *bytes, unsigned char count, char store)
{
    unsigned char saved = RAM_BANK;
    volatile unsigned char *window = (volatile unsigned char *)(0xA000u + at);
    __asm__ volatile("sei" ::: "memory");
    RAM_BANK = dir_bank;
    for (unsigned char i = 0; i < count; i++) {
        if (store)
            window[i] = bytes[i];
        else
            bytes[i] = window[i];
    }
    RAM_BANK = saved;
    __asm__ volatile("cli" ::: "memory");
}

// What a line of the listing says about a file or folder.
typedef struct {
    char *name;
    unsigned char length, folder;
    u32 size;
    long long seconds;          // since 1970; 0 if the drive doesn't say
} entry;

// Days from 1970-01-01 to a date of the years 1970 to 2099.
static long days_before(unsigned year, unsigned month, unsigned day)
{
    static const unsigned before[12] = { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334 };
    long days = (long)(year - 1970) * 365 + (year - 1969) / 4 + before[(month - 1) % 12] + day - 1;
    return month > 2 && year % 4 == 0 ? days + 1 : days;
}

static unsigned digits_at(const char *p, unsigned char n)
{
    unsigned value = 0;
    while (n--)
        value = value * 10 + (unsigned)(*p++ - '0');
    return value;
}

// 0 for a line without a name: the one about free space. text is cut after the name.
static char parse_line(unsigned blocks, char *text, entry *found)
{
    char *first = strchr(text, '"');
    char *last = first ? strchr(first + 1, '"') : 0;
    if (last == 0)
        return 0;
    *last = 0;
    found->name = first + 1;
    found->length = 0;
    while (found->name[found->length] && found->length < MAX_NAME - 1)
        found->length++;
    const char *p = last + 1;
    while (*p == ' ')
        p++;
    found->folder = (p[0] & 0xDF) == 'D' && (p[1] & 0xDF) == 'I' && (p[2] & 0xDF) == 'R';
    found->seconds = 0;
    const char *date = strchr(p, '-');                  // "2026-11-26 07:42:52"
    if (date && date - p >= 4 && strlen(date) >= 15 && date[-4] >= '1' && date[-4] <= '2')
        found->seconds = (long long)days_before(digits_at(date - 4, 4), digits_at(date + 1, 2), digits_at(date + 4, 2)) * 86400
            + digits_at(date + 7, 2) * 3600L + digits_at(date + 10, 2) * 60 + digits_at(date + 13, 2);
    // The exact size: eight hex digits at the end of an "$=L" line. Else the blocks.
    const char *end = p + strlen(p);
    while (end > p && end[-1] == ' ')
        end--;
    u32 size = 0;
    unsigned char digits = 0;
    const char *h = end;
    while (h > p && h[-1] != ' ')
        h--;
    if (end - h == 8) {
        for (; h < end; h++, digits++) {
            unsigned char c = (unsigned char)*h;
            unsigned char value = c >= '0' && c <= '9' ? c - '0' : (c & 0xDF) >= 'A' && (c & 0xDF) <= 'F' ? (c & 0xDF) - 'A' + 10 : 255;
            if (value == 255)
                break;
            size = size << 4 | value;
        }
    }
    found->size = found->folder ? 0 : digits == 8 ? size : (u32)blocks * 254;
    return 1;
}

// Starts reading the listing of a folder ("" for the current one), only of `name` if that
// isn't 0: its channel is then read with dir_line, and ended with end_listing.
static char start_listing(unsigned char device, const char *folder, const char *name)
{
    char listing[MAX_NAME + 12];
    strcpy(listing, device == 8 ? "$=L" : "$");         // the exact sizes, from CMDR-DOS
    if (folder[0] || name) {
        char path[MAX_NAME + 4];
        if (strlen(folder) + (name ? strlen(name) : 1) + 2 >= sizeof path)
            return 0;
        strcpy(path, folder);
        if (folder[0] && folder[strlen(folder) - 1] != '/')
            strcat(path, "/");
        strcat(path, name ? name : "*");
        if (!dos_form(listing, sizeof listing, path))
            return 0;
    }
    cbm_k_setnam(listing);
    cbm_k_setlfs(DIR_FILE, device, 0);
    if (cbm_k_open() || (cbm_k_readst() & 0x80)) {
        forget_everything();
        return 0;
    }
    int code = dos_command(device, "");                 // a folder that isn't there: don't read, it never ends
    if (code < 0)
        return 0;
    if (code >= 20) {
        cbm_k_close(DIR_FILE);
        return 0;
    }
    if (cbm_k_chkin(DIR_FILE) || (cbm_k_readst() & 0x80)) {
        forget_everything();
        return 0;
    }
    char volume[8];
    unsigned number;
    int low = dir_byte(), high = dir_byte();            // the load address
    if (low < 0 || high < 0 || !dir_line(&number, volume, sizeof volume)) {
        cbm_k_clrch();
        cbm_k_close(DIR_FILE);
        return 0;
    }
    return 1;
}

static void end_listing(void)
{
    cbm_k_clrch();
    cbm_k_close(DIR_FILE);
}

// A line of the listing as an entry in the bank; 0 for the line about free space.
static char dir_keep(unsigned blocks, char *text)
{
    entry found;
    if (!parse_line(blocks, text, &found))
        return 0;
    if (strcmp(found.name, ".") == 0 || strcmp(found.name, "..") == 0)
        return 1;
    if (dir_used + 6u + found.length > 0x2000u) {
        dir_complete = 0;                               // the bank is full
        return 1;
    }
    u32 size = found.size;
    unsigned char head[6] = { found.length, found.folder, (unsigned char)size, (unsigned char)(size >> 8), (unsigned char)(size >> 16), (unsigned char)(size >> 24) };
    dir_copy(dir_used, head, 6, 1);
    dir_copy(dir_used + 6, (unsigned char *)found.name, found.length, 1);
    dir_used += 6u + found.length;
    return 1;
}

// Appends the entries of a folder to the bank. 0 if the folder can't be read.
static char read_folder(unsigned char device, const char *folder)
{
    if (!start_listing(device, folder, 0))
        return 0;
    dir_bank = (unsigned char)(cs_cx16_bank_count() + CODE_BANKS);     // the topmost bank
    char text[MAX_NAME + 48];
    unsigned number;
    while (dir_line(&number, text, sizeof text) && dir_keep(number, text)) {
    }
    end_listing();
    return 1;
}

void cs_cx16_dir_close(void)
{
    dir_used = dir_at = 0;
}

// given: "" for the current folder, "folder", "#9:". 1 if the folder was read.
FIXED_RAM i32 cs_cx16_dir_open(const char *given)
{
    KEEP_RAM_BANK;
    unsigned char device;
    const char *path = take_path(given, &device);
    cs_cx16_dir_close();
    dir_complete = 1;
    return path != 0 && strlen(path) < MAX_NAME - 12 && read_folder(device, path);
}

// Hands out the next file or folder; 0 when there are no more.
i32 cs_cx16_dir_next(void)
{
    if (dir_at >= dir_used) {
        dir_name_length = 0;
        return 0;
    }
    unsigned char head[6];
    dir_copy(dir_at, head, 6, 0);
    dir_name_length = head[0];
    dir_is_folder = head[1];
    dir_size = head[2] | (u32)head[3] << 8 | (u32)head[4] << 16 | (u32)head[5] << 24;
    dir_copy(dir_at + 6, (unsigned char *)dir_name, dir_name_length, 0);
    dir_at += 6u + dir_name_length;
    return 1;
}

i32 cs_cx16_dir_is_complete(void) { return dir_complete; }

i32 cs_cx16_dir_name_length(void) { return dir_name_length; }
i32 cs_cx16_dir_name_char(i32 i) { return i >= 0 && i < dir_name_length ? (unsigned char)dir_name[i] : 0; }
i32 cs_cx16_dir_size(void) { return (i32)dir_size; }
i32 cs_cx16_dir_is_folder(void) { return dir_is_folder; }

// At the program's end: files that are written aren't whole until they are closed.
static void files_shutdown(void)
{
    cbm_k_clrch();
    for (int i = 0; i < MAX_FILES; i++)
        if (files[i].mode)
            cbm_k_close((unsigned char)(2 + i));
    for (unsigned char device = 8; device <= 30; device++)
        if (command_channels & (1ul << device))
            cbm_k_close((unsigned char)(90 + device));
}

// ---- what lib/FileSystem.cs calls, with glibc's names and layouts ------------------------
//
// The drive has no stat: a file's size and date come from the listing of its folder. The
// errors are C's numbers.

static char same_name(const char *a, const char *b)
{
    for (; *a && *b; a++, b++) {                        // FAT32 doesn't tell capitals apart
        char x = *a >= 'a' && *a <= 'z' ? *a - 32 : *a, y = *b >= 'a' && *b <= 'z' ? *b - 32 : *b;
        if (x != y)
            return 0;
    }
    return *a == *b;
}

// What is at a path: 0 nothing, 1 a file, 2 a folder.
static char look_up(unsigned char device, const char *given, u32 *size, long long *seconds)
{
    char path[MAX_NAME];
    unsigned length = strlen(given);
    *size = 0;
    *seconds = 0;
    if (length == 0 || length >= sizeof path)
        return 0;
    strcpy(path, given);
    while (length > 1 && path[length - 1] == '/')
        path[--length] = 0;
    char *slash = strrchr(path, '/');
    char *name = slash ? slash + 1 : path;
    if (strcmp(path, "/") == 0 || strcmp(name, ".") == 0 || strcmp(name, "..") == 0)
        return 2;
    char folder[MAX_NAME] = "";
    if (slash) {
        unsigned n = slash == path ? 1 : (unsigned)(slash - path);
        memcpy(folder, path, n);
        folder[n] = 0;
    }
    if (strchr(name, '*') != 0 || strchr(name, '?') != 0 || !start_listing(device, folder, name))
        return 0;
    char text[MAX_NAME + 48];
    unsigned blocks;
    char kind = 0;
    entry found;
    while (dir_line(&blocks, text, sizeof text) && parse_line(blocks, text, &found)) {
        found.name[found.length] = 0;
        if (!kind && same_name(found.name, name)) {
            kind = found.folder ? 2 : 1;
            *size = found.size;
            *seconds = found.seconds;
        }
    }
    end_listing();
    if (folder[0] && kind != 2) {
        // The emulator's host folder lists its root for any folder, so what the listing
        // says of a file in one is checked: it is there if it opens.
        char open_name[MAX_NAME + 8];
        char opens = 0;
        if (dos_name(open_name, "", given, ",S,R")) {
            cbm_k_setnam(open_name);
            cbm_k_setlfs(DIR_FILE, device, DIR_FILE);
            if (!cbm_k_open() && !(cbm_k_readst() & 0x80)) {
                int code = dos_command(device, "");
                opens = code >= 0 && code < 20;
                cbm_k_close(DIR_FILE);
            }
        }
        kind = opens;
    }
    return kind;
}

static long file_size(unsigned char device, const char *path)
{
    u32 size;
    long long seconds;
    if (look_up(device, path, &size, &seconds) != 1) {
        file_errno = 2;
        return -1;
    }
    return (long)size;
}

// As glibc's struct stat on arm64, which lib/FileSystem.cs reads: the mode at 16, the size
// at 48, the time at 88 (seconds) and 96 (nanoseconds).
FIXED_RAM i32 cs___xstat64(i32 version, const char *given, unsigned char *status)
{
    KEEP_RAM_BANK;
    (void)version;
    unsigned char device;
    const char *path = take_path(given, &device);
    u32 size;
    long long seconds;
    char kind = path == 0 ? 0 : *path == 0 ? 2 : look_up(device, path, &size, &seconds);
    if (path != 0 && *path == 0)
        size = 0, seconds = 0;
    if (!kind) {
        file_errno = 2;
        return -1;
    }
    memset(status, 0, 112);
    u32 mode = kind == 2 ? 0x4000 : 0x8000;
    long long bytes = size;
    memcpy(status + 16, &mode, 4);
    memcpy(status + 48, &bytes, 8);
    memcpy(status + 88, &seconds, 8);
    return 0;
}

i32 cs___lxstat64(i32 version, const char *given, unsigned char *status)
{
    return cs___xstat64(version, given, status);        // there are no links
}

// "MD", "RD" or "CD" with a path.
static int folder_command(const char *verb, const char *given, unsigned char *device, const char **path)
{
    *path = take_path(given, device);
    file_errno = 22;
    if (*path == 0 || **path == 0 || strlen(*path) >= MAX_NAME - 8)
        return -1;
    char command[MAX_NAME + 8];
    if (!path_command(command, sizeof command, verb, *path))
        return -1;
    int code = dos_command(*device, command);
    file_errno = code == 63 ? 17 : code == 62 || code == 39 ? 2 : code == 26 ? 30 : code == 72 ? 28 : 5;
    return code;
}

FIXED_RAM i32 cs_mkdir(const char *given, u32 mode)
{
    KEEP_RAM_BANK;
    (void)mode;
    unsigned char device;
    const char *path;
    int code = folder_command("MD:", given, &device, &path);
    return code >= 0 && code <= 1 ? 0 : -1;
}

FIXED_RAM i32 cs_rmdir(const char *given)
{
    KEEP_RAM_BANK;
    unsigned char device;
    const char *path = take_path(given, &device);
    char command[MAX_NAME + 8];
    file_errno = 22;
    if (path == 0 || *path == 0 || !path_command(command, sizeof command, "RD:", path))
        return -1;
    char kind = kind_of(device, path);
    file_errno = kind == 1 ? 20 : 2;
    if (kind != 2)
        return -1;
    file_errno = 39;                    // it is there and doesn't go: it has something in it
    return remove_with(device, command, path) ? 0 : -1;
}

FIXED_RAM i32 cs_chdir(const char *given)
{
    KEEP_RAM_BANK;
    unsigned char device;
    const char *path;
    int code = folder_command("CD:", given, &device, &path);
    return code >= 0 && code <= 1 ? 0 : -1;
}

// "R0:new=old". As rename(2), it replaces a file that has the new name.
FIXED_RAM i32 cs_rename(const char *from_given, const char *to_given)
{
    KEEP_RAM_BANK;
    unsigned char device, to_device;
    const char *from = take_path(from_given, &device), *to = take_path(to_given, &to_device);
    file_errno = 22;
    if (from == 0 || to == 0 || device != to_device || strchr(from, ',') != 0 || strchr(to, ',') != 0)
        return -1;
    char command[2 * MAX_NAME + 12] = "R0:";
    if (strlen(from) + strlen(to) + 5 >= sizeof command)
        return -1;
    strcat(command, to);
    strcat(command, "=");
    strcat(command, from);
    int code = dos_command(device, command);
    if (code == 63) {
        u32 size;
        long long seconds;
        if (look_up(device, to, &size, &seconds) == 1 && !same_name(from, to) && scratch(device, to))
            code = dos_command(device, command);
    }
    file_errno = code == 63 ? 17 : code == 62 || code == 39 ? 2 : code == 26 ? 30 : 5;
    return code >= 0 && code <= 1 ? 0 : -1;
}

// "$=C" lists the current folder, its parent, and so on up to "/".
FIXED_RAM char *cs_getcwd(char *buffer, u32 room)
{
    KEEP_RAM_BANK;
    file_errno = 5;
    cbm_k_setnam("$=C");
    cbm_k_setlfs(DIR_FILE, program_device, 0);
    if (cbm_k_open() || (cbm_k_readst() & 0x80)) {
        forget_everything();
        return 0;
    }
    if (cbm_k_chkin(DIR_FILE)) {
        cbm_k_close(DIR_FILE);
        return 0;
    }
    char path[MAX_NAME] = "";
    char text[MAX_NAME + 48];
    unsigned number;
    entry found;
    dir_byte();
    dir_byte();                                         // the load address
    if (dir_line(&number, text, sizeof text)) {
        while (dir_line(&number, text, sizeof text) && parse_line(number, text, &found)) {
            found.name[found.length] = 0;
            if (strcmp(found.name, "/") == 0 || strlen(path) + found.length + 2 >= sizeof path)
                break;
            memmove(path + found.length + 1, path, strlen(path) + 1);     // the parent goes in front
            path[0] = '/';
            memcpy(path + 1, found.name, found.length);
        }
    }
    end_listing();
    if (path[0] == 0)
        strcpy(path, "/");
    if (strlen(path) >= room) {
        file_errno = 36;
        return 0;
    }
    strcpy(buffer, path);
    return buffer;
}

#ifdef CX16_DIR
// A directory for Directory.Enumerate: its entries in a block of the heap, gathered with
// the bank that Disk uses (what Disk holds there stays).
typedef struct {
    unsigned char *entries;
    unsigned size, at;
    unsigned char current[19 + MAX_NAME];       // struct dirent64: the type at 18, the name from 19
} folder_list;

FIXED_RAM void *cs_opendir(const char *given)
{
    KEEP_RAM_BANK;
    unsigned char device;
    const char *path = take_path(given, &device);
    file_errno = 22;
    if (path == 0 || strlen(path) >= MAX_NAME - 12)
        return 0;
    if (strcmp(path, ".") == 0)
        path = "";
    if (*path) {
        u32 size;
        long long seconds;
        char kind = look_up(device, path, &size, &seconds);
        file_errno = kind == 1 ? 20 : 2;
        if (kind != 2)
            return 0;
    }
    unsigned kept = dir_used, start = dir_used;
    char complete = dir_complete;
    char ok = read_folder(device, path);
    unsigned length = dir_used - start;
    dir_used = kept;
    dir_complete = complete;
    if (!ok)
        return 0;
    file_errno = 12;
    folder_list *list = (folder_list *)malloc(sizeof *list + length);
    if (list == 0)
        return 0;
    list->entries = (unsigned char *)(list + 1);
    list->size = length;
    list->at = 0;
    for (unsigned done = 0; done < length; done += 128)
        dir_copy(start + done, list->entries + done, (unsigned char)(length - done < 128 ? length - done : 128), 0);
    return list;
}

void *cs_readdir64(void *opened)
{
    folder_list *list = (folder_list *)opened;
    if (list->at >= list->size)
        return 0;
    const unsigned char *at = list->entries + list->at;
    unsigned char length = at[0];
    memset(list->current, 0, sizeof list->current);
    list->current[18] = at[1] ? 4 : 8;          // DT_DIR, DT_REG
    memcpy(list->current + 19, at + 6, length);
    list->at += 6u + length;
    return list->current;
}

i32 cs_closedir(void *opened)
{
    free(opened);
    return 0;
}
#endif
#endif // CX16_FILES

// ---- fibers (lib/Fibers.cs) ---------------------------------------------------------
//
// As on the C64 (c64/runtime.c), with the RAM bank as well: a fiber may switch away while
// a bank with code is in the window at $A000. A 6502 program has two stacks. llvm-mos keeps locals on a stack of its own
// in memory, whose pointer is __rc0/__rc1 in the zero page, and a fiber gets
// one of those like on any machine: its own memory. But JSR, RTS and the
// registers functions save go on the processor's stack, which is always page
// 1 ($0100-$01FF), 256 bytes for everyone. So a switch copies the part of
// page 1 in use (from the stack pointer S up to $01FF) onto the fiber's own
// stack, next to the registers a function must keep (__rc20 to __rc31), and
// copies the next fiber's part back. Interrupts are off while page 1 is
// being rewritten.
//
// The bank register is pushed onto page 1 before it is copied, and pulled after.
//
// The X16's imaginary registers aren't in a row: __rc20 to __rc29 are the KERNAL's r6 to
// r10 at $0E, __rc30 and __rc31 are at $24 (llvm-mos's cx16/imag-regs.ld).
//
// The frame a switch leaves, from the saved stack pointer up:
//   0   __rc20 to __rc31
//   12  n, the number of bytes of page 1 in use
//   13  those bytes, from S+1 to $01FF; the first two are where the switch
//       returns to
//
// A fiber's memory is [guard][pattern][shadow stack ->  <- stack][record]:
// managed code keeps its references on a shadow stack, which grows up from
// above the pattern Fibers.cs checks; the stack above grows down. Nothing
// stops either growing into the other.
//
// No memory protection, so no guard page: getpagesize reports the 6502's
// page, 256 bytes, which Fibers.cs reserves below each stack and takes for a
// small machine (stacks of 2 KB, at least 512 bytes). mprotect does nothing.
//
// ZeroFiberSwitch is assembly at the top level of this file, so the link-time
// optimizer can't look into it. That matters: llvm-mos gives a function that
// can't be running twice at once static memory for its locals instead of
// stack. To the optimizer, a function that might reach the switch might call
// back into any fiber's entry, so its locals stay on the stack, and two
// fibers waiting inside it each keep their own.

i32 cs_getpagesize(void) { return 256; }

i32 cs_mprotect(void *address, u32 length, i32 protection)
{
    (void)address; (void)length; (void)protection;
    return 0;
}

i32 cs_usleep(u32 microseconds)
{
    u32 start = jiffies(), wait = (microseconds + 16666) / 16667;
    unsigned spins = 0;
    while (jiffies() - start < wait) {
        if (in_emulator && ++spins == 3000) {           // no frames: see above
            slept += wait - (jiffies() - start);
            break;
        }
    }
    return 0;
}

void cs_ZeroFiberSwitch(void **save, void *resume);
void ZeroFiberStart(void);

// top: the stack's top; bottom: its lowest byte, where Fibers.cs's pattern
// (64 bytes) starts. Returns the stack pointer to give ZeroFiberSwitch, a
// frame whose page 1 holds only the return into ZeroFiberStart, with __rc20
// the entry, __rc22 its argument and __rc24 the shadow stack.
void *cs_ZeroFiberInit(char *top, char *bottom, void (*entry)(void *), void *argument)
{
    unsigned char *frame = (unsigned char *)top - 16;
    unsigned start = (unsigned)ZeroFiberStart - 1; // RTS adds 1
    memset(frame, 0, 16);
    *(void **)&frame[0] = (void *)entry;
    *(void **)&frame[2] = argument;
    *(void **)&frame[4] = bottom + 64;
    frame[12] = 3;
    frame[13] = RAM_BANK;
    frame[14] = (unsigned char)start;
    frame[15] = (unsigned char)(start >> 8);
    return frame;
}

__asm__(
    ".section .text.cs_ZeroFiberSwitch,\"ax\",@progbits\n"
    ".globl cs_ZeroFiberSwitch\n"
    "cs_ZeroFiberSwitch:\n"
    // __rc2/__rc3: where to store this stack's pointer; __rc4/__rc5: the
    // one to resume. __rc16 to __rc19 are free to use (the caller's).
    "    php\n"
    "    pla\n"
    "    sta __rc16\n"            // the flags, interrupts on or off
    "    sei\n"
    "    lda $00\n"
    "    pha\n"                  // the RAM bank, with page 1
    "    tsx\n"
    "    txa\n"
    "    eor #$ff\n"
    "    sta __rc18\n"            // n = $FF - S
    "    sec\n"                   // make room: 13 + n bytes
    "    lda __rc0\n"
    "    sbc #13\n"
    "    sta __rc0\n"
    "    bcs 1f\n"
    "    dec __rc1\n"
    "1:  sec\n"
    "    lda __rc0\n"
    "    sbc __rc18\n"
    "    sta __rc0\n"
    "    bcs 2f\n"
    "    dec __rc1\n"
    "2:  ldy #0\n"
    "3:  lda mos16(__rc20),y\n"
    "    sta (__rc0),y\n"
    "    iny\n"
    "    cpy #10\n"
    "    bne 3b\n"
    "    lda __rc30\n"
    "    sta (__rc0),y\n"
    "    iny\n"
    "    lda __rc31\n"
    "    sta (__rc0),y\n"
    "    iny\n"                  // Y = 12
    "    lda __rc18\n"
    "    sta (__rc0),y\n"
    "4:  cpx #$ff\n"              // page 1, from S+1 up
    "    beq 5f\n"
    "    inx\n"
    "    iny\n"
    "    lda $100,x\n"
    "    sta (__rc0),y\n"
    "    jmp 4b\n"
    "5:  ldy #0\n"                // *save = the stack pointer
    "    lda __rc0\n"
    "    sta (__rc2),y\n"
    "    iny\n"
    "    lda __rc1\n"
    "    sta (__rc2),y\n"
    "    lda __rc4\n"             // the other fiber's stack
    "    sta __rc0\n"
    "    lda __rc5\n"
    "    sta __rc1\n"
    "    ldy #0\n"
    "6:  lda (__rc0),y\n"
    "    sta mos16(__rc20),y\n"
    "    iny\n"
    "    cpy #10\n"
    "    bne 6b\n"
    "    lda (__rc0),y\n"
    "    sta __rc30\n"
    "    iny\n"
    "    lda (__rc0),y\n"
    "    sta __rc31\n"
    "    iny\n"
    "    lda (__rc0),y\n"
    "    sta __rc18\n"
    "    eor #$ff\n"
    "    tax\n"
    "    txs\n"                   // S = $FF - n
    "7:  cpx #$ff\n"
    "    beq 8f\n"
    "    inx\n"
    "    iny\n"
    "    lda (__rc0),y\n"
    "    sta $100,x\n"
    "    jmp 7b\n"
    "8:  clc\n"                   // drop the frame
    "    lda __rc0\n"
    "    adc #13\n"
    "    sta __rc0\n"
    "    bcc 9f\n"
    "    inc __rc1\n"
    "9:  clc\n"
    "    lda __rc0\n"
    "    adc __rc18\n"
    "    sta __rc0\n"
    "    bcc 10f\n"
    "    inc __rc1\n"
    "10: pla\n"
    "    sta $00\n"
    "    lda __rc16\n"
    "    pha\n"
    "    plp\n"
    "    rts\n"
    // Where a new fiber's first switch returns to: the entry is an ordinary
    // C# method, which takes its shadow stack as a hidden first argument, and
    // the argument after it. It never returns: it switches away for good.
    ".globl ZeroFiberStart\n"
    "ZeroFiberStart:\n"
    "    lda __rc24\n"
    "    sta __rc2\n"
    "    lda __rc25\n"
    "    sta __rc3\n"
    "    lda __rc22\n"
    "    sta __rc4\n"
    "    lda __rc23\n"
    "    sta __rc5\n"
    "    jmp (__rc20)\n");

// lib/FileSystem.cs asks which system this is: 10, the Commander X16.
i32 cs_ZeroSystem(void) { return 11; }
void *cs___errno_location(void) { return &file_errno; }
// FileStream buffers in C#: nothing to switch off or write out here.
i32 cs_setvbuf(i32 stream, void *buffer, i32 how, u32 size) { (void)stream; (void)buffer; (void)how; (void)size; return 0; }
i32 cs_fflush(i32 stream) { (void)stream; return 0; }

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

// The wheel's movement, collected from every read of the mouse until the program asks.
static int wheel;

// The KERNAL's mouse_get ($FF6B): the position into four bytes of the zero page (X says
// where), the buttons in A and, in X, how far the wheel turned since the last call, which
// that call forgets. llvm-mos's cx16_k_mouse_get throws X away, so every read goes
// through here.
__attribute__((section(".zp.bss"))) static volatile unsigned char mouse_position[4];

static unsigned char mouse_read(void)
{
    unsigned char buttons;
    signed char turned;
    __asm__ volatile("ldx #mos8(mouse_position)\n\tjsr $FF6B"
        : "=a"(buttons), "=x"(turned) : : "y", "c", "v", "memory");
    wheel += turned;
    return buttons;
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
    if (show == 0 || !mouse_on) {       // the wheel starts from rest
        mouse_read();
        wheel = 0;
    }
    mouse_show = (unsigned char)show;
    mouse_on = show != 0;
}

i32 cs_cx16_mouse_wheel(void)
{
    mouse_read();
    int turned = wheel;
    wheel = 0;
    return turned;
}

// 0: the pointer's x, 1: its y, 2: the buttons (1 left, 2 right, 4 middle).
i32 cs_cx16_mouse(i32 field)
{
    unsigned char buttons = mouse_read();
    int x = mouse_position[0] | mouse_position[1] << 8, y = mouse_position[2] | mouse_position[3] << 8;
    return field == 0 ? x : field == 1 ? y : buttons;
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

// ---- PCM audio, for lib/Pcm.cs ---------------------------------------------------------
//
// VERA plays samples from a 4 KB FIFO and can interrupt when it is less than a quarter
// full (AFLOW). The handler below then puts the next bytes into it, so a sound plays
// while the program does something else. It is hooked into the KERNAL's interrupt
// vector, CINV, where the registers are already saved and the KERNAL's ROM is switched
// in: for AFLOW alone it returns by itself (ply, plx, pla, rti, which goes back through
// the KERNAL's own stub), for anything else it goes on to the old handler. It is in
// assembly because C code in an interrupt would need the compiler's zero page registers
// saved. The data is read through the operand of an instruction it writes itself, from
// fixed RAM or, bank after bank, from the banked RAM.

__asm__(
    ".section .text.cx16_pcm,\"awx\",@progbits\n"
    ".global cx16_pcm_irq\n"
    "cx16_pcm_irq:\n"
    "  lda $9f27\n"                 // ISR
    "  and $9f26\n"                 // IEN
    "  and #$08\n"                  // AFLOW?
    "  beq 1f\n"
    "  stz cx16_pcm_budget\n"
    "  lda #6\n"                    // 1536 bytes: there is room, with less than 1 KB in the FIFO
    "  sta cx16_pcm_budget+1\n"
    "  jsr cx16_pcm_feed\n"
    "  lda $9f27\n"
    "  and $9f26\n"
    "  and #$07\n"                  // a frame or line interrupt as well?
    "  bne 1f\n"
    "  ply\n"
    "  plx\n"
    "  pla\n"
    "  rti\n"
    "1:\n"
    "  jmp (cx16_pcm_old)\n"

    // Puts at least cx16_pcm_budget bytes (at most 255 more) into the FIFO, or all that is left.
    ".global cx16_pcm_feed\n"
    "cx16_pcm_feed:\n"
    "  lda $00\n"                   // the program's RAM bank
    "  pha\n"
    "  lda cx16_pcm_bank\n"         // 0: the data is in fixed RAM
    "  beq 2f\n"
    "  sta $00\n"
    "2:\n"                          // the next run: 256 bytes (written 0), or less
    "  lda cx16_pcm_left+1\n"
    "  ora cx16_pcm_left+2\n"
    "  bne 3f\n"
    "  lda cx16_pcm_left\n"
    "  bne 4f\n"
    "  jmp 8f\n"                    // nothing left
    "3:\n"
    "  lda #0\n"
    "4:\n"
    "  sta cx16_pcm_run\n"
    "  ldx cx16_pcm_bank\n"         // not past the end of a bank
    "  beq 5f\n"
    "  ldx cx16_pcm_src+1\n"
    "  cpx #$bf\n"
    "  bne 5f\n"
    "  sec\n"
    "  lda #0\n"
    "  sbc cx16_pcm_src\n"          // what is left of the bank, 0 for all 256
    "  beq 5f\n"
    "  ldx cx16_pcm_run\n"
    "  beq 41f\n"
    "  cmp cx16_pcm_run\n"
    "  bcs 5f\n"
    "41:\n"
    "  sta cx16_pcm_run\n"
    "5:\n"
    "  ldy #0\n"
    "6:\n"
    ".global cx16_pcm_src\n"
    "cx16_pcm_src = . + 1\n"
    "  lda $ffff,y\n"
    "  sta $9f3d\n"
    "  iny\n"
    "  cpy cx16_pcm_run\n"
    "  bne 6b\n"
    // move on
    "  lda cx16_pcm_run\n"
    "  bne 61f\n"
    "  inc cx16_pcm_src+1\n"        // 256
    "  lda cx16_pcm_left+1\n"
    "  bne 62f\n"
    "  dec cx16_pcm_left+2\n"
    "62:\n"
    "  dec cx16_pcm_left+1\n"
    "  dec cx16_pcm_budget+1\n"
    "  bra 7f\n"
    "61:\n"
    "  clc\n"
    "  adc cx16_pcm_src\n"
    "  sta cx16_pcm_src\n"
    "  bcc 63f\n"
    "  inc cx16_pcm_src+1\n"
    "63:\n"
    "  sec\n"
    "  lda cx16_pcm_left\n"
    "  sbc cx16_pcm_run\n"
    "  sta cx16_pcm_left\n"
    "  lda cx16_pcm_left+1\n"
    "  sbc #0\n"
    "  sta cx16_pcm_left+1\n"
    "  lda cx16_pcm_left+2\n"
    "  sbc #0\n"
    "  sta cx16_pcm_left+2\n"
    "  sec\n"
    "  lda cx16_pcm_budget\n"
    "  sbc cx16_pcm_run\n"
    "  sta cx16_pcm_budget\n"
    "  lda cx16_pcm_budget+1\n"
    "  sbc #0\n"
    "  sta cx16_pcm_budget+1\n"
    "7:\n"
    "  lda cx16_pcm_bank\n"
    "  beq 71f\n"
    "  lda cx16_pcm_src+1\n"
    "  cmp #$c0\n"
    "  bne 71f\n"
    "  lda #$a0\n"                  // on into the next bank
    "  sta cx16_pcm_src+1\n"
    "  inc cx16_pcm_bank\n"
    "  lda cx16_pcm_bank\n"
    "  sta $00\n"
    "71:\n"
    "  lda cx16_pcm_left\n"
    "  ora cx16_pcm_left+1\n"
    "  ora cx16_pcm_left+2\n"
    "  beq 8f\n"
    "72:\n"
    "  lda cx16_pcm_budget+1\n"
    "  bmi 9f\n"                    // below 0
    "  ora cx16_pcm_budget\n"
    "  beq 9f\n"
    "  jmp 2b\n"
    "8:\n"                          // all sent: again from the start, or no more interrupts
    "  lda cx16_pcm_loop\n"
    "  beq 81f\n"
    "  ldx #3\n"                    // left (3 bytes) and bank lie together
    "80:\n"
    "  lda cx16_pcm_start,x\n"
    "  sta cx16_pcm_left,x\n"
    "  dex\n"
    "  bpl 80b\n"
    "  lda cx16_pcm_start+4\n"
    "  sta cx16_pcm_src\n"
    "  lda cx16_pcm_start+5\n"
    "  sta cx16_pcm_src+1\n"
    "  lda cx16_pcm_bank\n"
    "  beq 83f\n"
    "  sta $00\n"
    "83:\n"
    "  jmp 72b\n"
    "81:\n"                          // the end: silence after it, or the chip would hold the last sample
    "  stz $9f3d\n"                   // (4 bytes are whole samples in every format)
    "  stz $9f3d\n"
    "  stz $9f3d\n"
    "  stz $9f3d\n"
    "  lda $9f26\n"
    "  and #$f7\n"
    "  sta $9f26\n"
    "9:\n"
    "  pla\n"
    "  sta $00\n"
    "  rts\n"

    ".global cx16_pcm_old, cx16_pcm_budget, cx16_pcm_left, cx16_pcm_bank, cx16_pcm_loop, cx16_pcm_start\n"
    "cx16_pcm_old:    .zero 2\n"
    "cx16_pcm_budget: .zero 2\n"
    "cx16_pcm_run:    .zero 1\n"
    "cx16_pcm_left:   .zero 3\n"   // bytes not yet in the FIFO ...
    "cx16_pcm_bank:   .zero 1\n"   // ... and the bank they are in; these four are copied as one
    "cx16_pcm_loop:   .zero 1\n"
    "cx16_pcm_start:  .zero 6\n"   // left, bank and source as at the start, for a loop
);

extern unsigned char cx16_pcm_irq[], cx16_pcm_src[2], cx16_pcm_old[2], cx16_pcm_budget[2], cx16_pcm_left[3];
extern volatile unsigned char cx16_pcm_bank, cx16_pcm_loop, cx16_pcm_start[6];

#define AUDIO_CTRL (*(volatile unsigned char *)0x9F3B)
#define AUDIO_RATE (*(volatile unsigned char *)0x9F3C)
#define AUDIO_DATA (*(volatile unsigned char *)0x9F3D)
#define VERA_IEN (*(volatile unsigned char *)0x9F26)
#define CINV (*(void *volatile *)0x0314)

static unsigned char pcm_volume = 15;

void cs_cx16_pcm_stop(void)
{
    __asm__ volatile("sei" ::: "memory");
    VERA_IEN &= (unsigned char)~0x08;
    AUDIO_RATE = 0;
    AUDIO_CTRL = (unsigned char)(0x80 | pcm_volume);        // empties the FIFO
    if (pcm_shutdown) {                                     // the chip holds the last sample: play a silent one
        AUDIO_DATA = 0;
        AUDIO_RATE = 128;
        for (unsigned char spin = 0; !(AUDIO_CTRL & 0x40) && spin != 200; spin++) {}
        AUDIO_RATE = 0;
        AUDIO_CTRL = (unsigned char)(0x80 | pcm_volume);
    }
    cx16_pcm_left[0] = cx16_pcm_left[1] = cx16_pcm_left[2] = 0;
    cx16_pcm_loop = 0;
    __asm__ volatile("cli" ::: "memory");
}

static void pcm_unhook(void)
{
    cs_cx16_pcm_stop();
    __asm__ volatile("sei" ::: "memory");
    if (CINV == (void *)cx16_pcm_irq)
        CINV = *(void **)(void *)cx16_pcm_old;
    __asm__ volatile("cli" ::: "memory");
}

// format: bit 0 16 bits a sample, bit 1 stereo. rate: the chip's, 1 to 128 (128 is 48828 Hz).
static i32 pcm_start(unsigned address, unsigned char bank, u32 length, i32 rate, i32 format, i32 loop)
{
    unsigned char mode = (unsigned char)(((format & 1) ? 0x20 : 0) | ((format & 2) ? 0x10 : 0));
    unsigned frame = ((format & 1) ? 2 : 1) * ((format & 2) ? 2 : 1);
    length -= length % frame;
    if (length == 0 || length > 0xFFFFFFul || rate < 1 || rate > 128)
        return 0;
    cs_cx16_pcm_stop();
    if (pcm_shutdown == 0) {
        __asm__ volatile("sei" ::: "memory");
        *(void **)(void *)cx16_pcm_old = CINV;
        CINV = (void *)cx16_pcm_irq;
        __asm__ volatile("cli" ::: "memory");
        pcm_shutdown = pcm_unhook;
    }
    AUDIO_CTRL = (unsigned char)(0x80 | mode | pcm_volume);
    cx16_pcm_start[0] = cx16_pcm_left[0] = (unsigned char)length;
    cx16_pcm_start[1] = cx16_pcm_left[1] = (unsigned char)(length >> 8);
    cx16_pcm_start[2] = cx16_pcm_left[2] = (unsigned char)(length >> 16);
    cx16_pcm_start[3] = cx16_pcm_bank = bank;
    cx16_pcm_start[4] = cx16_pcm_src[0] = (unsigned char)address;
    cx16_pcm_start[5] = cx16_pcm_src[1] = (unsigned char)(address >> 8);
    cx16_pcm_loop = loop != 0;
    __asm__ volatile("sei" ::: "memory");
    cx16_pcm_budget[0] = 0;
    cx16_pcm_budget[1] = 14;                                // 3584 bytes to begin with
    __asm__ volatile("jsr cx16_pcm_feed" ::: "a", "x", "y", "c", "v", "memory");
    if (cx16_pcm_left[0] | cx16_pcm_left[1] | cx16_pcm_left[2] | cx16_pcm_loop)
        VERA_IEN |= 0x08;
    __asm__ volatile("cli" ::: "memory");
    AUDIO_RATE = (unsigned char)rate;
    return 1;
}

i32 cs_cx16_pcm_play(const unsigned char *data, i32 length, i32 rate, i32 format, i32 loop)
{
    return pcm_start((unsigned)data, 0, length < 0 ? 0 : (u32)length, rate, format, loop);
}

// address: as lib/Banks.cs counts, from the first byte of the program's first bank.
i32 cs_cx16_pcm_play_banked(i32 address, i32 length, i32 rate, i32 format, i32 loop)
{
    if (address < 0 || length < 0)
        return 0;
    unsigned char bank = (unsigned char)(((u32)address >> 13) + 1 + CODE_BANKS);
    return pcm_start(0xA000u + ((unsigned)address & 0x1FFF), bank, (u32)length, rate, format, loop);
}

// The bytes not yet handed to the chip (what a loop has left of its round).
i32 cs_cx16_pcm_left(void)
{
    __asm__ volatile("sei" ::: "memory");
    u32 left = cx16_pcm_left[0] | (u32)cx16_pcm_left[1] << 8 | (u32)cx16_pcm_left[2] << 16;
    __asm__ volatile("cli" ::: "memory");
    return (i32)left;
}

i32 cs_cx16_pcm_playing(void)
{
    return cs_cx16_pcm_left() != 0 || cx16_pcm_loop || !(AUDIO_CTRL & 0x40);        // bit 6: the FIFO is empty
}

void cs_cx16_pcm_volume(i32 volume)
{
    pcm_volume = (unsigned char)(volume < 0 ? 0 : volume > 15 ? 15 : volume);
    AUDIO_CTRL = (unsigned char)((AUDIO_CTRL & 0x30) | pcm_volume);
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
#ifdef CX16_DIR
    return (i32)count - CODE_BANKS - 1;     // nor is the topmost one, for a folder's list (below)
#else
    return (i32)count - CODE_BANKS;     // the code's banks are not the program's
#endif
}

#define WITH_BANK(bank, body) do { \
    unsigned char saved = RAM_BANK; \
    __asm__ volatile("sei" ::: "memory"); \
    RAM_BANK = (unsigned char)((bank) + CODE_BANKS); \
    body; \
    RAM_BANK = saved; \
    __asm__ volatile("cli" ::: "memory"); \
} while (0)

FIXED_RAM void cs_cx16_bank_read(i32 bank, i32 offset, void *destination, i32 count)
{
    WITH_BANK(bank, memcpy(destination, (const void *)(0xA000u + (unsigned)offset), (size_t)count));
}

FIXED_RAM void cs_cx16_bank_write(i32 bank, i32 offset, const void *source, i32 count)
{
    WITH_BANK(bank, memcpy((void *)(0xA000u + (unsigned)offset), source, (size_t)count));
}

FIXED_RAM void cs_cx16_bank_fill(i32 bank, i32 offset, i32 value, i32 count)
{
    WITH_BANK(bank, memset((void *)(0xA000u + (unsigned)offset), (int)value, (size_t)count));
}

// A file straight into the banks, for Banks.Load: the KERNAL's headerless LOAD
// (secondary address 2: every byte of the file counts), which carries on into
// the next bank when one is full. None of the file functions further down are
// needed for it, so a program that only loads stays small. The number of bytes
// read, or -1.
FIXED_RAM i32 cs_cx16_bank_load(i32 bank, i32 offset, const unsigned short *name, i32 length)
{
    char text[80];
    if (length <= 0 || length >= (i32)sizeof text)
        return -1;
    unsigned char count = (unsigned char)length;
    for (unsigned char i = 0; i < count; i++) {
        unsigned short c = name[i];
        if (c < ' ' || c > '~')
            return -1;                  // the card's names are ASCII
        text[i] = (char)c;
    }
    text[count] = 0;
    unsigned char saved = RAM_BANK;
    unsigned char first = (unsigned char)(bank + CODE_BANKS);
    unsigned start = 0xA000u + (unsigned)offset;
    RAM_BANK = first;
    cbm_k_setnam(text);
    cbm_k_setlfs(0, program_device, 2);
    unsigned end = (unsigned)cbm_k_load(0, (void *)start);
    unsigned char status = cbm_k_readst();
    unsigned char last = RAM_BANK;
    RAM_BANK = saved;                   // R47 comes back with bank 0 from a device that isn't there
    // A load that failed returns the KERNAL's error number, which is no address.
    if (end < 0xA000u || (status & 0xBF) != 0)
        return -1;
    return (i32)(unsigned char)(last - first) * 0x2000 + (i32)end - (i32)start;
}
