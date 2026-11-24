// A Cortex-M program on a real Raspberry Pi Pico 2 (RP2350, Arm cores,
// secure state): no host to lean on, so this file drives the chip itself.
// See arm/runtime.c for the rest.
//
//   clocks   the 12 MHz crystal (XOSC) for the reference and system clocks,
//            and the peripherals. The chip can run at 150 MHz from its PLL;
//            that also needs the flash interface's timing changed, and comes
//            once this much is proven on a board.
//   console  UART0 at 115200 baud, 8N1, on GP0 (TX, pin 1) and GP1 (RX,
//            pin 2): the Raspberry Pi Debug Probe's UART port, or any 3.3 V
//            USB-serial adapter. Typed lines are echoed and can be edited
//            with backspace; Ctrl-D at the start of a line ends the input.
//   clock    TIMER0, counting microseconds from the start.
//   exit     prints "--- exit code N ---" and stops the core.
//
// Files and command-line arguments don't exist here: open fails, and the
// program gets only its name. Register addresses and values are from the
// pico-sdk's hardware_regs headers (src/rp2350/hardware_regs) and its
// hardware_uart and pico_runtime_init sources.

#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <sys/types.h>

#define REG(address) (*(volatile uint32_t *)(address))
#define SET 0x2000u // a register's atomic "set these bits" alias
#define CLR 0x3000u // and "clear these bits"

// ---- the clocks -----------------------------------------------------------------

#define XOSC 0x40048000u
#define XOSC_CTRL (XOSC + 0x00)
#define XOSC_STATUS (XOSC + 0x04)
#define XOSC_STATUS_STABLE (1u << 31)
// FREQ_RANGE 1-15 MHz (0xaa0), ENABLE (0xfab in bits 23:12).
#define XOSC_CTRL_ENABLE_12MHZ ((0xfabu << 12) | 0xaa0u)

#define CLOCKS 0x40010000u
#define CLK_REF_CTRL (CLOCKS + 0x30)
#define CLK_REF_SELECTED (CLOCKS + 0x38)
#define CLK_SYS_CTRL (CLOCKS + 0x3c)
#define CLK_SYS_SELECTED (CLOCKS + 0x44)
#define CLK_PERI_CTRL (CLOCKS + 0x48)
#define CLK_REF_SRC_XOSC 2u
#define CLK_SYS_SRC_CLK_REF 0u
#define CLK_PERI_ENABLE (1u << 11)
#define CLK_PERI_AUXSRC_CLK_SYS (0u << 5)

#define RESETS 0x40020000u
#define RESETS_RESET (RESETS + 0x00)
#define RESETS_RESET_DONE (RESETS + 0x08)
#define RESET_IO_BANK0 (1u << 6)
#define RESET_PADS_BANK0 (1u << 9)
#define RESET_TIMER0 (1u << 23)
#define RESET_UART0 (1u << 26)

#define TICKS 0x40108000u
#define TICKS_TIMER0_CTRL (TICKS + 0x18)
#define TICKS_TIMER0_CYCLES (TICKS + 0x1c)

#define TIMER0 0x400b0000u
#define TIMER0_TIMERAWH (TIMER0 + 0x24)
#define TIMER0_TIMERAWL (TIMER0 + 0x28)

static void start_clocks(void)
{
    // The crystal, then the reference clock from it, then the system clock
    // from the reference clock. Each switch is done when its SELECTED
    // register shows the new source.
    REG(XOSC_CTRL) = XOSC_CTRL_ENABLE_12MHZ;
    while (!(REG(XOSC_STATUS) & XOSC_STATUS_STABLE))
        ;
    REG(CLK_REF_CTRL) = CLK_REF_SRC_XOSC;
    while (!(REG(CLK_REF_SELECTED) & (1u << CLK_REF_SRC_XOSC)))
        ;
    REG(CLK_SYS_CTRL) = (REG(CLK_SYS_CTRL) & ~1u) | CLK_SYS_SRC_CLK_REF;
    while (!(REG(CLK_SYS_SELECTED) & 1u))
        ;
    REG(CLK_PERI_CTRL) = CLK_PERI_ENABLE | CLK_PERI_AUXSRC_CLK_SYS;

    // Take what this file uses out of reset.
    uint32_t used = RESET_IO_BANK0 | RESET_PADS_BANK0 | RESET_TIMER0 | RESET_UART0;
    REG(RESETS_RESET + CLR) = used;
    while ((REG(RESETS_RESET_DONE) & used) != used)
        ;

    // TIMER0 counts the ticks of its tick generator: one each 12 cycles of
    // the 12 MHz reference clock, a microsecond.
    REG(TICKS_TIMER0_CYCLES) = 12;
    REG(TICKS_TIMER0_CTRL) = 1;
}

static uint64_t microseconds(void)
{
    uint32_t high, low;
    do {
        high = REG(TIMER0_TIMERAWH);
        low = REG(TIMER0_TIMERAWL);
    } while (high != REG(TIMER0_TIMERAWH));
    return ((uint64_t)high << 32) | low;
}

// There is no calendar clock: every clock counts from the start.
int clock_gettime(int clock, int64_t *timespec)
{
    (void)clock;
    uint64_t now = microseconds();
    timespec[0] = (int64_t)(now / 1000000u);
    timespec[1] = (int64_t)(now % 1000000u) * 1000;
    return 0;
}

// ---- the console ----------------------------------------------------------------

#define IO_BANK0 0x40028000u
#define GPIO_CTRL(n) (IO_BANK0 + 0x04 + 8 * (n))
#define GPIO_FUNC_UART 2u

#define PADS_BANK0 0x40038000u
#define PAD(n) (PADS_BANK0 + 0x04 + 4 * (n))
// 4 mA drive (DRIVE = 1), input enabled, Schmitt trigger; not isolated (ISO,
// bit 8, is set at reset), output not disabled (OD, bit 7, clear).
#define PAD_TX ((1u << 4) | (1u << 6) | (1u << 1))
#define PAD_RX (PAD_TX | (1u << 3)) // and a pull-up: a line with nothing on it reads idle

#define UART0 0x40070000u
#define UART_DR (UART0 + 0x00)
#define UART_FR (UART0 + 0x18)
#define UART_IBRD (UART0 + 0x24)
#define UART_FBRD (UART0 + 0x28)
#define UART_LCR_H (UART0 + 0x2c)
#define UART_CR (UART0 + 0x30)
#define UART_FR_BUSY (1u << 3)
#define UART_FR_RXFE (1u << 4)
#define UART_FR_TXFF (1u << 5)

#define CLK_PERI_HZ 12000000u
#define BAUD 115200u

static void start_console(void)
{
    REG(PAD(0)) = PAD_TX;
    REG(PAD(1)) = PAD_RX;
    REG(GPIO_CTRL(0)) = GPIO_FUNC_UART;
    REG(GPIO_CTRL(1)) = GPIO_FUNC_UART;

    // The baud rate divisor in 1/64ths, rounded, as the pico-sdk's
    // uart_set_baudrate works it out.
    uint32_t divisor = 8 * CLK_PERI_HZ / BAUD + 1;
    REG(UART_IBRD) = divisor >> 7;
    REG(UART_FBRD) = (divisor & 0x7f) >> 1;
    // 8 data bits (WLEN = 3), FIFOs on; this write also latches the divisor.
    REG(UART_LCR_H) = (3u << 5) | (1u << 4);
    REG(UART_CR) = (1u << 0) | (1u << 8) | (1u << 9); // UARTEN, TXE, RXE
}

static void put(char c)
{
    while (REG(UART_FR) & UART_FR_TXFF)
        ;
    REG(UART_DR) = (uint8_t)c;
}

static char get(void)
{
    while (REG(UART_FR) & UART_FR_RXFE)
        ;
    return (char)REG(UART_DR);
}

// Standard output and error. A terminal wants "\r\n" for a new line.
ssize_t write(int fd, const void *buffer, size_t count)
{
    if (fd != 1 && fd != 2)
        return -1;
    const char *bytes = buffer;
    for (size_t i = 0; i < count; i++) {
        if (bytes[i] == '\n')
            put('\r');
        put(bytes[i]);
    }
    return (ssize_t)count;
}

// Standard input, a line at a time: what's typed is echoed, backspace takes
// back a character, Enter ends the line, and Ctrl-D on an empty line is the
// end of the input.
static char line[256];
static size_t line_length, line_read;
static int input_ended;

ssize_t read(int fd, void *buffer, size_t count)
{
    if (fd != 0)
        return -1;
    if (line_read == line_length) {
        if (input_ended)
            return 0;
        line_length = line_read = 0;
        for (;;) {
            char c = get();
            if (c == '\r' || c == '\n') {
                line[line_length++] = '\n';
                put('\r');
                put('\n');
                break;
            }
            if (c == 0x04 && line_length == 0) { // Ctrl-D
                input_ended = 1;
                return 0;
            }
            if ((c == 0x08 || c == 0x7f) && line_length > 0) { // backspace, delete
                line_length--;
                put('\b');
                put(' ');
                put('\b');
                continue;
            }
            if ((unsigned char)c >= ' ' && line_length < sizeof line - 1) {
                line[line_length++] = c;
                put(c);
            }
        }
    }
    size_t n = line_length - line_read;
    if (n > count)
        n = count;
    for (size_t i = 0; i < n; i++)
        ((char *)buffer)[i] = line[line_read + i];
    line_read += n;
    return (ssize_t)n;
}

// The C library's stdin, stdout and stderr (puts, printf): the same console.
static int console_put(char c, FILE *file)
{
    (void)file;
    if (c == '\n')
        put('\r');
    put(c);
    return (unsigned char)c;
}

static int console_get(FILE *file)
{
    (void)file;
    char c;
    return read(0, &c, 1) == 1 ? (unsigned char)c : _FDEV_EOF;
}

static FILE console = FDEV_SETUP_STREAM(console_put, console_get, NULL, _FDEV_SETUP_RW);
FILE *const stdin = &console;
FILE *const stdout = &console;
FILE *const stderr = &console;

// ---- what a C library asks of the system -----------------------------------------

int open(const char *path, int flags, ...) { (void)path; (void)flags; return -1; }
int close(int fd) { (void)fd; return -1; }
off_t lseek(int fd, off_t offset, int whence) { (void)fd; (void)offset; (void)whence; return -1; }
int unlink(const char *path) { (void)path; return -1; }
int isatty(int fd) { return fd >= 0 && fd <= 2; }

struct stat;
int fstat(int fd, struct stat *status) { (void)fd; (void)status; return -1; }

void _exit(int code)
{
    static const char digits[] = "0123456789";
    char text[16];
    int n = 0;
    unsigned value = code < 0 ? (unsigned)-code : (unsigned)code;
    do {
        text[n++] = digits[value % 10];
        value /= 10;
    } while (value);
    write(1, "\n--- exit code ", 15);
    if (code < 0)
        put('-');
    while (n > 0)
        put(text[--n]);
    write(1, " ---\n", 5);
    while (REG(UART_FR) & UART_FR_BUSY)
        ;
    for (;;)
        __asm__ volatile("wfi");
}

// ---- the LED -------------------------------------------------------------------
//
// The Pico 2's green LED is on GP25, driven by the processor through SIO.

#define SIO 0xd0000000u
#define SIO_GPIO_OUT (SIO + 0x10)
#define SIO_GPIO_OE (SIO + 0x30)
#define GPIO_FUNC_SIO 5u
#define LED_PIN 25

// Changes some bits of a register, with interrupts held off so that a tick
// handler changing the same register can't come in between the read and the
// write. (SIO also has registers that set or clear bits in one write, but
// Wokwi's simulator ignores them in secure state, so the tests couldn't
// check them.)
static void change_bits(uint32_t address, uint32_t mask, int set)
{
    uint32_t primask;
    __asm__ volatile("mrs %0, primask\n\tcpsid i" : "=r"(primask) :: "memory");
    REG(address) = set ? (REG(address) | mask) : (REG(address) & ~mask);
    __asm__ volatile("msr primask, %0" :: "r"(primask) : "memory");
}

static void start_led(void)
{
    REG(PAD(LED_PIN)) = (1u << 4) | (1u << 6); // 4 mA, input enabled, not isolated
    REG(GPIO_CTRL(LED_PIN)) = GPIO_FUNC_SIO;
    change_bits(SIO_GPIO_OUT, 1u << LED_PIN, 0);
    change_bits(SIO_GPIO_OE, 1u << LED_PIN, 1);
}

void zero_led(int32_t on) { change_bits(SIO_GPIO_OUT, 1u << LED_PIN, on); }
int32_t zero_led_is_on(void) { return (int32_t)((REG(SIO_GPIO_OUT) >> LED_PIN) & 1u); }

// The processor runs from the crystal (start_clocks).
uint32_t zero_cpu_hz(void) { return 12000000u; }

// ---- start --------------------------------------------------------------------

void zero_start(int *argc, char ***argv)
{
    static char name[] = "program";
    static char *arguments[] = {name, 0};
    start_clocks();
    start_console();
    start_led();
    *argc = 1;
    *argv = arguments;
}
