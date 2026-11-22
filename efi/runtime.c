// The C side of a UEFI program: the C library functions zerocore and lib/
// call (memory, write, read, abort, the clock, the pthreads lib/Threads.cs
// uses), written over the firmware's boot services, since there is no C
// library underneath. libm.c has the math functions; start.c the entry point.
//
// Compiled with clang for x86_64-pc-windows-msvc or aarch64-pc-windows-msvc.

#include "efi.h"

Handle efi_image;
SystemTable *efi_system;
ShellParameters *efi_shell;

// The compiler references this from any C file that uses floating point on
// Windows targets; the C library would define it.
int _fltused = 0;

// ---- where the compiler's module list is ------------------------------------
//
// As in win/runtime.c: the compiler puts a pointer to each module's header in
// section ".modules$I", and the linker sorts these two around it.

#pragma section(".modules$A", read)
#pragma section(".modules$Z", read)
__declspec(allocate(".modules$A")) void *__start___modules[1] = { 0 };
__declspec(allocate(".modules$Z")) void *__stop___modules[1] = { 0 };

// ---- memory -----------------------------------------------------------------

void *memset(void *destination, int value, usize count)
{
    u8 *d = destination;
    while (count--)
        *d++ = (u8)value;
    return destination;
}

void *memcpy(void *destination, const void *source, usize count)
{
    u8 *d = destination;
    const u8 *s = source;
    while (count--)
        *d++ = *s++;
    return destination;
}

void *memmove(void *destination, const void *source, usize count)
{
    u8 *d = destination;
    const u8 *s = source;
    if (d < s) {
        while (count--)
            *d++ = *s++;
    } else {
        d += count;
        s += count;
        while (count--)
            *--d = *--s;
    }
    return destination;
}

int memcmp(const void *a, const void *b, usize count)
{
    const u8 *x = a, *y = b;
    for (; count; count--, x++, y++)
        if (*x != *y)
            return *x - *y;
    return 0;
}

// Pool memory is 8-byte aligned; C's malloc promises 16, which SIMD code may
// count on. Each block keeps the pool's own pointer just before it.
void *malloc(usize size)
{
    u8 *pool = 0;
    if (size > (usize)-32 || EFI_ERROR(efi_system->boot_services->allocate_pool(EfiLoaderData, size + 16, (void **)&pool)))
        return 0;
    u8 *block = (u8 *)(((usize)pool + 8 + 15) & ~(usize)15);
    ((void **)block)[-1] = pool;
    return block;
}

void *calloc(usize count, usize size)
{
    if (size != 0 && count > (usize)-1 / size)
        return 0;
    void *block = malloc(count * size);
    if (block)
        memset(block, 0, count * size);
    return block;
}

void free(void *block)
{
    if (block)
        efi_system->boot_services->free_pool(((void **)block)[-1]);
}

// ---- standard input and output ----------------------------------------------
//
// Run by the UEFI Shell with "> file" or "< file", the program's output or
// input is a file (the shell's parameters say which); bytes go to and from it
// unchanged. Otherwise the console is UTF-16: write converts from UTF-8 and
// turns "\n" into "\r\n", and read collects a line from the keyboard, showing
// it as it is typed.

static File *file_or_null(File *file)
{
    if (!file)
        return 0;
    static const Guid file_info = FILE_INFO_GUID;
    usize size = 0;
    // A real file answers that the buffer is too small; the shell's console
    // stand-ins don't support the question.
    return file->get_info(file, &file_info, &size, 0) == EFI_BUFFER_TOO_SMALL ? file : 0;
}

static File *output_file, *error_file, *input_file;

void efi_runtime_start(void)
{
    if (efi_shell) {
        output_file = file_or_null(efi_shell->std_out);
        error_file = file_or_null(efi_shell->std_err);
        input_file = file_or_null(efi_shell->std_in);
    }
}

typedef struct { u32 value; int needed; } Decoder;

static Decoder output_decoder, error_decoder;

static void console_write(TextOutput *console, Decoder *decoder, const u8 *bytes, usize count)
{
    char16 text[260];
    usize length = 0;
    for (usize i = 0; i < count; i++) {
        u32 b = bytes[i];
        u32 c;
        if (decoder->needed > 0) {
            if ((b & 0xC0) == 0x80) {
                decoder->value = (decoder->value << 6) | (b & 0x3F);
                if (--decoder->needed > 0)
                    continue;
                c = decoder->value;
            } else {
                decoder->needed = 0;
                c = 0xFFFD;
                i--; // this byte starts something new
            }
        } else if (b < 0x80) {
            c = b;
        } else if (b >= 0xC2 && b < 0xE0) {
            decoder->value = b & 0x1F;
            decoder->needed = 1;
            continue;
        } else if (b >= 0xE0 && b < 0xF0) {
            decoder->value = b & 0x0F;
            decoder->needed = 2;
            continue;
        } else if (b >= 0xF0 && b < 0xF5) {
            decoder->value = b & 0x07;
            decoder->needed = 3;
            continue;
        } else {
            c = 0xFFFD;
        }

        if (c == '\n') {
            text[length++] = '\r';
            text[length++] = '\n';
        } else if (c >= 0x10000 && c <= 0x10FFFF) {
            c -= 0x10000;
            text[length++] = (char16)(0xD800 + (c >> 10));
            text[length++] = (char16)(0xDC00 + (c & 0x3FF));
        } else if (c > 0x10FFFF || (c >= 0xD800 && c < 0xE000)) {
            text[length++] = 0xFFFD;
        } else {
            text[length++] = (char16)c;
        }
        if (length >= 256) {
            text[length] = 0;
            console->output_string(console, text);
            length = 0;
        }
    }
    if (length > 0) {
        text[length] = 0;
        console->output_string(console, text);
    }
}

i64 write(int fd, const void *buffer, usize count)
{
    File *file = fd == 1 ? output_file : fd == 2 ? error_file : 0;
    if (file) {
        usize size = count;
        return EFI_ERROR(file->write(file, &size, buffer)) ? -1 : (i64)size;
    }
    if (fd == 1)
        console_write(efi_system->con_out, &output_decoder, buffer, count);
    else if (fd == 2)
        console_write(efi_system->std_err ? efi_system->std_err : efi_system->con_out, &error_decoder, buffer, count);
    else
        return -1;
    return (i64)count;
}

int puts(const char *text)
{
    usize length = 0;
    while (text[length])
        length++;
    write(1, text, length);
    write(1, "\n", 1);
    return 0;
}

// A line typed at the keyboard, as UTF-8, and how much of it read has handed out.
static u8 line[1024];
static usize line_length, line_given;
static int input_ended;

static void echo(const char *text)
{
    char16 wide[8];
    usize i = 0;
    for (; text[i] && i < 7; i++)
        wide[i] = (u8)text[i];
    wide[i] = 0;
    efi_system->con_out->output_string(efi_system->con_out, wide);
}

static void read_line(void)
{
    TextInput *keyboard = efi_system->con_in;
    line_length = line_given = 0;
    for (;;) {
        InputKey key;
        Status status = keyboard->read_key_stroke(keyboard, &key);
        if (status == EFI_NOT_READY) {
            usize index;
            efi_system->boot_services->wait_for_event(1, &keyboard->wait_for_key, &index);
            continue;
        }
        if (EFI_ERROR(status)) {
            input_ended = 1;
            return;
        }
        u32 c = key.unicode_char;
        if (c == '\r' || c == '\n') {
            echo("\r\n");
            line[line_length++] = '\n';
            return;
        }
        if (c == 4 || c == 26) { // Ctrl+D or Ctrl+Z: the end of the input
            if (line_length == 0)
                input_ended = 1;
            return;
        }
        if (c == 8) { // backspace: remove the last character, all its bytes
            if (line_length > 0) {
                do
                    line_length--;
                while (line_length > 0 && (line[line_length] & 0xC0) == 0x80);
                echo("\b \b");
            }
            continue;
        }
        if (c < 0x20 || line_length + 4 >= sizeof line)
            continue;
        char16 shown[2] = { (char16)c, 0 };
        efi_system->con_out->output_string(efi_system->con_out, shown);
        if (c < 0x80) {
            line[line_length++] = (u8)c;
        } else if (c < 0x800) {
            line[line_length++] = (u8)(0xC0 | (c >> 6));
            line[line_length++] = (u8)(0x80 | (c & 0x3F));
        } else {
            line[line_length++] = (u8)(0xE0 | (c >> 12));
            line[line_length++] = (u8)(0x80 | ((c >> 6) & 0x3F));
            line[line_length++] = (u8)(0x80 | (c & 0x3F));
        }
    }
}

i64 read(int fd, void *buffer, usize count)
{
    if (fd != 0)
        return -1;
    if (input_file) {
        usize size = count;
        return EFI_ERROR(input_file->read(input_file, &size, buffer)) ? -1 : (i64)size;
    }
    if (line_given == line_length) {
        if (input_ended)
            return 0;
        read_line();
        if (line_length == 0)
            return 0;
    }
    usize n = line_length - line_given;
    if (n > count)
        n = count;
    memcpy(buffer, line + line_given, n);
    line_given += n;
    return (i64)n;
}

// zerocore prints why before it calls this. 134 is what a shell reports for
// a program that aborted on Linux (128 + SIGABRT).
__declspec(noreturn) void abort(void)
{
    efi_system->boot_services->exit(efi_image, 134, 0, 0);
    for (;;) {
    }
}

// ---- the clock --------------------------------------------------------------
//
// The firmware has a calendar clock (GetTime) but no monotonic one with good
// resolution. The processor's counter is one: on x64 the time-stamp counter,
// whose rate is measured once against the firmware's Stall; on arm64 the
// generic timer, whose rate the processor reports.

static u64 counter(void)
{
#if defined(__x86_64__)
    u32 low, high;
    __asm__ volatile("rdtsc" : "=a"(low), "=d"(high));
    return ((u64)high << 32) | low;
#else
    u64 value;
    __asm__ volatile("isb; mrs %0, cntvct_el0" : "=r"(value));
    return value;
#endif
}

static u64 counter_frequency(void)
{
    static u64 frequency;
    if (frequency == 0) {
#if defined(__x86_64__)
        u64 start = counter();
        efi_system->boot_services->stall(20000);
        frequency = (counter() - start) * 50;
#else
        __asm__ volatile("mrs %0, cntfrq_el0" : "=r"(frequency));
#endif
        if (frequency == 0)
            frequency = 1;
    }
    return frequency;
}

// CLOCK_MONOTONIC only, which is all the samples ask for.
int clock_gettime(int clock, i64 *timespec)
{
    (void)clock;
    u64 frequency = counter_frequency();
    u64 count = counter();
    timespec[0] = (i64)(count / frequency);
    timespec[1] = (i64)((count % frequency) * 1000000000 / frequency);
    return 0;
}

// ---- threads ----------------------------------------------------------------
//
// One processor, no scheduler: as on DOS, a thread runs to its end when it is
// created, and joining it just reports that it ended.

int pthread_create(void **thread, void *attributes, void *(*start)(void *), void *argument)
{
    (void)attributes;
    *thread = 0;
    start(argument);
    return 0;
}

int pthread_join(void *thread, void **result)
{
    (void)thread;
    if (result)
        *result = 0;
    return 0;
}

int sched_yield(void) { return 0; }

int get_nprocs(void) { return 1; }

#define KEY_COUNT 64
static void *key_values[KEY_COUNT];
static u32 key_count;

int pthread_key_create(u64 *key, void (*destructor)(void *))
{
    (void)destructor;
    if (key_count >= KEY_COUNT)
        return 11; // EAGAIN
    *key = key_count++;
    return 0;
}

void *pthread_getspecific(u64 key) { return key < key_count ? key_values[key] : 0; }

int pthread_setspecific(u64 key, void *value)
{
    if (key >= key_count)
        return 22; // EINVAL
    key_values[key] = value;
    return 0;
}
