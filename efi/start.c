// A UEFI program's entry point. (runtime.c has the C library functions
// zerocore and lib/ call.)
//
// The firmware, or the UEFI Shell, calls EfiMain (bflat links with
// /entry:EfiMain) with the program's image handle and the system table. This
// sets up what a C program's startup would on Linux: the arguments as UTF-8
// argc/argv, and a stack as large as Linux gives (the firmware only promises
// 128 KiB). Then it calls the compiler's main and hands its result back as the
// program's status.
//
// Compiled with clang for x86_64-pc-windows-msvc or aarch64-pc-windows-msvc.

#include "efi.h"

int __managed__Main(int argc, char **argv);

// Calls function(argument) on another stack (efi/x64.S, efi/arm64.S).
int efi_call_on_stack(int (*function)(void *), void *argument, void *stack_top);

#define STACK_SIZE (8u << 20)

static void *allocate(usize size)
{
    void *memory = 0;
    if (EFI_ERROR(efi_system->boot_services->allocate_pool(EfiLoaderData, size, &memory)))
        return 0;
    return memory;
}

// UTF-16 (with surrogate pairs) to UTF-8, NUL-terminated.
static char *to_utf8(const char16 *text)
{
    usize size = 1;
    for (const char16 *p = text; *p; p++)
        size += *p < 0x80 ? 1 : *p < 0x800 ? 2 : 3; // a surrogate pair: 3 + 3 >= 4
    char *out = allocate(size);
    if (!out)
        return 0;
    char *next = out;
    for (const char16 *p = text; *p; p++) {
        u32 c = *p;
        if (c >= 0xD800 && c < 0xDC00 && p[1] >= 0xDC00 && p[1] < 0xE000) {
            c = 0x10000 + ((c - 0xD800) << 10) + (p[1] - 0xDC00);
            p++;
        }
        if (c < 0x80) {
            *next++ = (char)c;
        } else if (c < 0x800) {
            *next++ = (char)(0xC0 | (c >> 6));
            *next++ = (char)(0x80 | (c & 0x3F));
        } else if (c < 0x10000) {
            *next++ = (char)(0xE0 | (c >> 12));
            *next++ = (char)(0x80 | ((c >> 6) & 0x3F));
            *next++ = (char)(0x80 | (c & 0x3F));
        } else {
            *next++ = (char)(0xF0 | (c >> 18));
            *next++ = (char)(0x80 | ((c >> 12) & 0x3F));
            *next++ = (char)(0x80 | ((c >> 6) & 0x3F));
            *next++ = (char)(0x80 | (c & 0x3F));
        }
    }
    *next = 0;
    return out;
}

#if defined(__x86_64__)
// The firmware sets the processor up for SSE but not for AVX: until CR4's
// OSXSAVE bit and the XCR0 register say the AVX registers' state is managed,
// an AVX instruction is an invalid opcode. A UEFI program runs in ring 0, so
// it can do that itself, for what the processor has: AVX, and the AVX-512
// state too if present. (Programs built with -m x86-64-v3 need AVX2.)
static void enable_avx(void)
{
    u32 a, b, c, d;
    __asm__ volatile("cpuid" : "=a"(a), "=b"(b), "=c"(c), "=d"(d) : "a"(1), "c"(0));
    if (!(c & (1u << 26)) || !(c & (1u << 28))) // XSAVE, AVX
        return;
    u64 cr4;
    __asm__ volatile("mov %%cr4, %0" : "=r"(cr4));
    __asm__ volatile("mov %0, %%cr4" : : "r"(cr4 | (1ull << 18)));
    u32 supported;
    __asm__ volatile("cpuid" : "=a"(supported), "=b"(b), "=c"(c), "=d"(d) : "a"(0xD), "c"(0));
    u32 low, high;
    __asm__ volatile("xgetbv" : "=a"(low), "=d"(high) : "c"(0));
    low |= 7; // x87, SSE, AVX
    if ((supported & 0xE0) == 0xE0)
        low |= 0xE0; // AVX-512: opmask, upper halves of zmm0-15, zmm16-31
    __asm__ volatile("xsetbv" : : "a"(low), "d"(high), "c"(0));
}
#endif

typedef struct { int argc; char **argv; } Arguments;

static int run_main(void *parameter)
{
    Arguments *arguments = parameter;
    return __managed__Main(arguments->argc, arguments->argv);
}

static char program_name[] = "program";
static char *no_arguments[] = { program_name, 0 };

Status EfiMain(Handle image, SystemTable *system)
{
    efi_image = image;
    efi_system = system;
    BootServices *boot = system->boot_services;

    // When the firmware starts a program from its boot menu, it gives it five
    // minutes and then resets the machine. A program decides itself how
    // long it runs.
    boot->set_watchdog_timer(0, 0, 0, 0);
#if defined(__x86_64__)
    enable_avx();
#endif

    // Run by the UEFI Shell, a program finds its arguments (and redirected
    // input and output) in the shell's parameters. Started by the firmware
    // directly, it has no arguments.
    static const Guid shell_parameters = SHELL_PARAMETERS_GUID;
    ShellParameters *shell = 0;
    if (EFI_ERROR(boot->open_protocol(image, &shell_parameters, (void **)&shell, image, 0, 2 /* GET_PROTOCOL */)))
        shell = 0;
    efi_shell = shell;
    efi_runtime_start();

    Arguments arguments = { 1, no_arguments };
    if (shell && shell->argc > 0) {
        char **argv = allocate((shell->argc + 1) * sizeof(char *));
        if (argv) {
            for (usize i = 0; i < shell->argc; i++)
                argv[i] = to_utf8(shell->argv[i]);
            argv[shell->argc] = 0;
            arguments.argc = (int)shell->argc;
            arguments.argv = argv;
        }
    }

    int code;
    u64 stack = 0;
    if (!EFI_ERROR(boot->allocate_pages(AllocateAnyPages, EfiLoaderData, STACK_SIZE / 4096, &stack)))
        code = efi_call_on_stack(run_main, &arguments, (void *)(stack + STACK_SIZE));
    else
        code = run_main(&arguments);

    // The exit code is the status, as a warning (the high bit clear), which
    // the shell keeps in %lasterror%. Exit codes are 0-255, as on Linux.
    return (Status)(u8)code;
}
