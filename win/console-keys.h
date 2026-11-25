// Keys one at a time from a Windows console, for Console.ReadKey and
// KeyAvailable (lib/InKeys.cs): the POSIX calls those make (isatty,
// tcgetattr, cfmakeraw, tcsetattr, poll), and read() of standard input in
// raw mode. Included by win/runtime.c, and by dos/runtime.c, whose HX
// extender has the same console functions.
//
// There is no terminal driver to put in raw mode: a "termios" here is one
// byte, 1 for raw, and tcsetattr remembers it. In raw mode, read() returns
// the next key press as the bytes a Unix terminal would send: its character
// in UTF-8, or for a key without one an escape sequence (ESC [ A for the up
// arrow, ESC [ 1 ; 5 C for Ctrl+Right), which lib/InKeys.cs turns back into
// a key. Key releases, and Shift, Ctrl and Alt on their own, are skipped.
//
// The includer declares, for the console's input records:
//   KEYS_READ(console, record, count)  ReadConsoleInputW (or ...A, with KEYS_ANSI)
//   KEYS_PEEK(console, record, count)  PeekConsoleInputW (or ...A)
//   GetNumberOfConsoleInputEvents, GetConsoleMode, GetFileType, GetStdHandle,
//   STD_INPUT_HANDLE

struct keys_record { // INPUT_RECORD, for a key event
    unsigned short type, padding;
    int down;
    unsigned short repeat, virtual_key, scan_code;
    unsigned short character; // UTF-16 (ReadConsoleInputW); ...A puts a byte in the low half
    u32 control_keys;
};

struct pollfd {
    int fd;
    short events, revents;
};

#define KEYS_POLLIN 1

static int keys_raw;
static unsigned short keys_high_surrogate;

static void *keys_console(void) { return GetStdHandle(STD_INPUT_HANDLE); }

// A console, not a file or a pipe. GetConsoleMode alone isn't enough: HX's
// answers yes for a file given with "<" too. 2: FILE_TYPE_CHAR.
int isatty(int fd)
{
    u32 mode;
    void *console = keys_console();
    return fd == 0 && GetFileType(console) == 2 && GetConsoleMode(console, &mode) ? 1 : 0;
}

int tcgetattr(int fd, unsigned char *termios)
{
    if (!isatty(fd))
        return -1;
    termios[0] = 0;
    return 0;
}

void cfmakeraw(unsigned char *termios) { termios[0] = 1; }

int tcsetattr(int fd, int when, const unsigned char *termios)
{
    (void)when;
    if (!isatty(fd))
        return -1;
    keys_raw = termios[0];
    return 0;
}

static int keys_utf8(u32 c, unsigned char *out)
{
    if (c < 0x80) {
        out[0] = (unsigned char)c;
        return 1;
    }
    if (c < 0x800) {
        out[0] = (unsigned char)(0xC0 | (c >> 6));
        out[1] = (unsigned char)(0x80 | (c & 0x3F));
        return 2;
    }
    if (c < 0x10000) {
        out[0] = (unsigned char)(0xE0 | (c >> 12));
        out[1] = (unsigned char)(0x80 | ((c >> 6) & 0x3F));
        out[2] = (unsigned char)(0x80 | (c & 0x3F));
        return 3;
    }
    out[0] = (unsigned char)(0xF0 | (c >> 18));
    out[1] = (unsigned char)(0x80 | ((c >> 12) & 0x3F));
    out[2] = (unsigned char)(0x80 | ((c >> 6) & 0x3F));
    out[3] = (unsigned char)(0x80 | (c & 0x3F));
    return 4;
}

// The virtual-key code for a PC scan code, for a console that reports only
// the scan code (HX on DOS may: lib/Dos.cs's keys use nothing else). Only
// keys without a character come here, so the keypad's digits, which share
// the arrows' scan codes, stay digits.
static unsigned short keys_from_scan_code(unsigned short scan_code)
{
    static const unsigned char scan_codes[] = {
        0x48, 0x50, 0x4D, 0x4B, 0x47, 0x4F, 0x52, 0x53, 0x49, 0x51,
        0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44, 0x57, 0x58,
    };
    static const unsigned char virtual_keys[] = {
        0x26, 0x28, 0x27, 0x25, 0x24, 0x23, 0x2D, 0x2E, 0x21, 0x22,
        0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x7B,
    };
    for (unsigned i = 0; i < sizeof scan_codes; i++)
        if (scan_codes[i] == scan_code)
            return virtual_keys[i];
    return 0;
}

// A key press as a terminal's bytes, or 0 bytes for an event that isn't one.
// out has room for 16.
static int keys_bytes(const struct keys_record *e, unsigned char *out)
{
    if (e->type != 1 || !e->down) // 1: a key event
        return 0;
    int shift = (e->control_keys & 0x10) != 0;
    int alt = (e->control_keys & 0x03) != 0;
    int ctrl = (e->control_keys & 0x0C) != 0;
    int modifier = 1 + shift + 2 * alt + 4 * ctrl;
    char final = 0;
    int number = 0;
    unsigned short virtual_key = e->virtual_key;
    if (virtual_key == 0 && (e->character & 0xFF) == 0)
        virtual_key = keys_from_scan_code(e->scan_code);
    switch (virtual_key) {
    case 0x26: final = 'A'; break; // up
    case 0x28: final = 'B'; break; // down
    case 0x27: final = 'C'; break; // right
    case 0x25: final = 'D'; break; // left
    case 0x24: final = 'H'; break; // home
    case 0x23: final = 'F'; break; // end
    case 0x2D: number = 2; break;  // insert
    case 0x2E: number = 3; break;  // delete
    case 0x21: number = 5; break;  // page up
    case 0x22: number = 6; break;  // page down
    case 0x70: final = 'P'; break; // F1-F4
    case 0x71: final = 'Q'; break;
    case 0x72: final = 'R'; break;
    case 0x73: final = 'S'; break;
    case 0x74: number = 15; break; // F5-F12
    case 0x75: number = 17; break;
    case 0x76: number = 18; break;
    case 0x77: number = 19; break;
    case 0x78: number = 20; break;
    case 0x79: number = 21; break;
    case 0x7A: number = 23; break;
    case 0x7B: number = 24; break;
    case 0x09:
        if (shift) {
            out[0] = 0x1B; out[1] = '['; out[2] = 'Z';
            return 3;
        }
        break;
    }
    if (final || number) {
        int n = 0;
        out[n++] = 0x1B;
        out[n++] = '[';
        if (number >= 10)
            out[n++] = (unsigned char)('0' + number / 10);
        if (number)
            out[n++] = (unsigned char)('0' + number % 10);
        else if (modifier > 1)
            out[n++] = '1';
        if (modifier > 1) {
            out[n++] = ';';
            out[n++] = (unsigned char)('0' + modifier);
        }
        out[n++] = number ? '~' : (unsigned char)final;
        return n;
    }

    u32 c = e->character;
#ifdef KEYS_ANSI
    c &= 0xFF;
    if (c >= 0x80)
        return 0; // a code page character: not passed on
#endif
    if (c == 0)
        return 0;
    if (c >= 0xD800 && c <= 0xDBFF) {
        keys_high_surrogate = (unsigned short)c;
        return 0;
    }
    if (c >= 0xDC00 && c <= 0xDFFF) {
        if (!keys_high_surrogate)
            return 0;
        c = 0x10000 + ((u32)(keys_high_surrogate - 0xD800) << 10) + (c - 0xDC00);
        keys_high_surrogate = 0;
    }
    int n = 0;
    if (alt && !ctrl) // AltGr is Ctrl+Alt: its characters come as they are
        out[n++] = 0x1B;
    return n + keys_utf8(c, out + n);
}

// Standard input in raw mode: the next key press, waiting for one. Bytes that
// don't fit in count are dropped (lib/InKeys.cs asks for 256).
static int keys_read(void *buffer, u32 count)
{
    void *console = keys_console();
    for (;;) {
        struct keys_record record;
        u32 got = 0;
        if (!KEYS_READ(console, &record, 1, &got) || got == 0)
            return -1;
        unsigned char bytes[16];
        int n = keys_bytes(&record, bytes);
        if (n == 0)
            continue;
        if ((u32)n > count)
            n = (int)count;
        for (int i = 0; i < n; i++)
            ((unsigned char *)buffer)[i] = bytes[i];
        return n;
    }
}

// Whether a key press waits; events before it that aren't one are dropped.
static int keys_waiting(void)
{
    void *console = keys_console();
    u32 events;
    while (GetNumberOfConsoleInputEvents(console, &events) && events > 0) {
        struct keys_record record;
        u32 got = 0;
        if (!KEYS_PEEK(console, &record, 1, &got) || got == 0)
            return 0;
        unsigned char bytes[16];
        if (keys_bytes(&record, bytes) > 0)
            return 1;
        KEYS_READ(console, &record, 1, &got);
    }
    return 0;
}

// Only standard input is asked about, and only whether it can be read now.
int poll(struct pollfd *fds, unsigned long count, int timeout)
{
    (void)timeout;
    int ready = 0;
    for (unsigned long i = 0; i < count; i++) {
        fds[i].revents = 0;
        if (fds[i].fd == 0 && (fds[i].events & KEYS_POLLIN) && (!isatty(0) || keys_waiting())) {
            fds[i].revents = KEYS_POLLIN;
            ready++;
        }
    }
    return ready;
}
