// What lib/Uefi.cs calls: the machine as a program sees it under UEFI, made to
// look like the PC under DOS (lib/Dos.cs), so a program for one runs on the
// other. The screen is a 320x200 picture with 256 colors, drawn on the
// firmware's graphics output, scaled up as far as it fits; keys come with PC
// scan codes; files are on the volume the program was loaded from. The PC
// speaker and port I/O exist on x64 only. It also has the C library's file
// functions (fopen, fread, ...), which lib/File.cs calls, over the same files.
//
// Compiled with clang for x86_64-pc-windows-msvc or aarch64-pc-windows-msvc.

#include "efi.h"

void *malloc(usize size);
void free(void *block);
void *memset(void *destination, int value, usize count);
int clock_gettime(int clock, i64 *timespec);

static BootServices *boot(void) { return efi_system->boot_services; }

static void *locate(const Guid *protocol)
{
    void *interface = 0;
    if (EFI_ERROR(boot()->locate_protocol(protocol, 0, &interface)))
        return 0;
    return interface;
}

// ---- the clock --------------------------------------------------------------

u32 uefi_ticks(void)
{
    i64 now[2];
    clock_gettime(1, now);
    return (u32)(now[0] * 1000 + now[1] / 1000000);
}

void uefi_sleep(u32 milliseconds) { boot()->stall((usize)milliseconds * 1000); }

// ---- the screen -------------------------------------------------------------

#define WIDTH 320
#define HEIGHT 200

static GraphicsOutput *graphics;
static u32 palette[256];             // each color as the screen's pixel
static u32 *frame;                   // the scaled picture, as pixels
static u8 picture_read[WIDTH * HEIGHT];
static usize scale, left, top;
static u32 next_frame;

// The screen's pixel for a color: blue, green, red, then a spare byte, unless
// the screen says red comes first.
static u32 pixel(u32 red, u32 green, u32 blue)
{
    if (graphics && graphics->mode->info->pixel_format == PixelRedGreenBlueReserved8)
        return red | green << 8 | blue << 16;
    return blue | green << 8 | red << 16;
}

// Switches to graphics: the firmware's current mode (normally the screen's own
// resolution), cleared to black. Returns 0 if there is no graphics output.
int uefi_graphics(void)
{
    static const Guid guid = GRAPHICS_OUTPUT_GUID;
    if (!graphics)
        graphics = locate(&guid);
    if (!graphics)
        return 0;
    GraphicsModeInfo *info = graphics->mode->info;
    usize width = info->horizontal_resolution, height = info->vertical_resolution;
    scale = width / WIDTH < height / HEIGHT ? width / WIDTH : height / HEIGHT;
    if (scale == 0)
        scale = 1;
    left = width > WIDTH * scale ? (width - WIDTH * scale) / 2 : 0;
    top = height > HEIGHT * scale ? (height - HEIGHT * scale) / 2 : 0;
    if (!frame)
        frame = malloc(WIDTH * scale * HEIGHT * scale * 4);
    if (!frame)
        return 0;
    efi_system->con_out->enable_cursor(efi_system->con_out, 0);
    u32 black = 0;
    graphics->blt(graphics, &black, BltVideoFill, 0, 0, 0, 0, width, height, 0);
    return 1;
}

// Back to the text console: the firmware redraws it when it is cleared.
void uefi_text(void)
{
    efi_system->con_out->clear_screen(efi_system->con_out);
    efi_system->con_out->enable_cursor(efi_system->con_out, 1);
}

// Red, green and blue are 0-63, as on the VGA.
void uefi_set_color(int index, int red, int green, int blue)
{
    if (index < 0 || index > 255)
        return;
    u32 r = (u32)red & 63, g = (u32)green & 63, b = (u32)blue & 63;
    palette[index] = pixel(r << 2 | r >> 4, g << 2 | g >> 4, b << 2 | b >> 4);
}

void uefi_show(const u8 *picture)
{
    if (!graphics || !frame)
        return;
    usize row_pixels = WIDTH * scale;
    for (usize y = 0; y < HEIGHT; y++) {
        u32 *row = frame + y * scale * row_pixels;
        const u8 *source = picture + y * WIDTH;
        u32 *out = row;
        for (usize x = 0; x < WIDTH; x++) {
            u32 color = palette[source[x]];
            for (usize i = 0; i < scale; i++)
                *out++ = color;
        }
        for (usize i = 1; i < scale; i++) {
            u32 *copy = row + i * row_pixels;
            for (usize x = 0; x < row_pixels; x++)
                copy[x] = row[x];
        }
    }
    graphics->blt(graphics, frame, BltBufferToVideo, 0, 0, left, top, row_pixels, HEIGHT * scale, 0);
}

// What the screen shows, read back from it, as color numbers: for each of the
// 320x200 points, the first color whose pixel is the one on the screen (255 if
// none is). A program can check that what it drew arrived.
u8 *uefi_screen(void)
{
    if (!graphics || !frame)
        return picture_read;
    usize row_pixels = WIDTH * scale;
    if (EFI_ERROR(graphics->blt(graphics, frame, BltVideoToBuffer, left, top, 0, 0, row_pixels, HEIGHT * scale, 0)))
        return picture_read;
    for (usize y = 0; y < HEIGHT; y++) {
        for (usize x = 0; x < WIDTH; x++) {
            u32 shown = frame[y * scale * row_pixels + x * scale] & 0xFFFFFF;
            int found = 255;
            for (int c = 0; c < 256; c++) {
                if ((palette[c] & 0xFFFFFF) == shown) {
                    found = c;
                    break;
                }
            }
            picture_read[y * WIDTH + x] = (u8)found;
        }
    }
    return picture_read;
}

// 70 frames a second, as the VGA's graphics mode: waits for the next one.
void uefi_wait_frame(void)
{
    u32 now = uefi_ticks();
    if ((i32)(next_frame - now) > 0 && next_frame - now <= 15)
        uefi_sleep(next_frame - now);
    now = uefi_ticks();
    next_frame = (i32)(next_frame + 14 - now) > 0 ? next_frame + 14 : now + 14;
}

// ---- the keyboard -----------------------------------------------------------
//
// UEFI reports key presses, and repeats them while a key is held, but never
// says when a key is let go. So a key counts as down until no repeat has come
// for a while: 550 ms after its first press (the usual delay before a key
// repeats), 120 ms once it repeats.

static TextInputEx *keyboard_ex;
static int keyboard_looked;
static u32 last_seen[128], first_seen[128];
static u8 repeating[128];
static u16 queue[64];
static u32 queue_start, queue_end;

// PC scan codes (set 1) for the characters UEFI reports, and for its own
// codes for keys without a character.
static u8 scan_for_char(u32 c)
{
    static const char rows[4][14] = { "1234567890-=", "qwertyuiop[]", "asdfghjkl;'`", "zxcvbnm,./" };
    static const u8 starts[4] = { 0x02, 0x10, 0x1E, 0x2C };
    if (c >= 'A' && c <= 'Z')
        c += 'a' - 'A';
    for (int r = 0; r < 4; r++)
        for (int i = 0; rows[r][i]; i++)
            if ((u32)rows[r][i] == c)
                return (u8)(starts[r] + i);
    switch (c) {
    case '\r': case '\n': return 0x1C;
    case ' ': return 0x39;
    case 8: return 0x0E;
    case 9: return 0x0F;
    case 27: return 0x01;
    case '\\': return 0x2B;
    }
    return 0;
}

static u8 scan_for_efi(u32 code)
{
    static const u8 table[] = {
        0,    0x48, 0x50, 0x4D, 0x4B, 0x47, 0x4F, 0x52, 0x53, 0x49, 0x51, // up down right left home end ins del pgup pgdn
        0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44,       // F1-F10
        0x57, 0x58, 0x01,                                                 // F11 F12 Esc
    };
    return code < sizeof table ? table[code] : 0;
}

static void key_pressed(u32 scan, u32 c)
{
    u32 now = uefi_ticks();
    if (scan == 0 && c != 0)
        scan = scan_for_char(c);
    if (scan == 0 || scan >= 128)
        return;
    int held = last_seen[scan] != 0 && now - last_seen[scan] < (repeating[scan] ? 120u : 550u);
    if (held) {
        repeating[scan] = 1;
    } else {
        repeating[scan] = 0;
        first_seen[scan] = now;
    }
    last_seen[scan] = now ? now : 1;
    if (!held && (queue_end - queue_start) < 64)
        queue[queue_end++ % 64] = (u16)(scan << 8 | (c < 0x80 ? c : 0));
}

int uefi_poll_keys(void)
{
    static const Guid guid = TEXT_INPUT_EX_GUID;
    if (!keyboard_looked) {
        keyboard_looked = 1;
        boot()->handle_protocol(efi_system->console_in_handle, &guid, (void **)&keyboard_ex);
    }
    for (;;) {
        u32 scan, c;
        if (keyboard_ex) {
            KeyData key;
            if (EFI_ERROR(keyboard_ex->read_key_stroke_ex(keyboard_ex, &key)))
                break;
            scan = key.key.scan_code;
            c = key.key.unicode_char;
        } else {
            InputKey key;
            if (EFI_ERROR(efi_system->con_in->read_key_stroke(efi_system->con_in, &key)))
                break;
            scan = key.scan_code;
            c = key.unicode_char;
        }
        key_pressed(scan_for_efi(scan), c);
    }
    return (int)(queue_end - queue_start);
}

int uefi_key_down(int scan)
{
    if (scan <= 0 || scan >= 128 || last_seen[scan] == 0)
        return 0;
    u32 since = uefi_ticks() - last_seen[scan];
    return since < (repeating[scan] ? 120u : 550u);
}

int uefi_read_key(void)
{
    if (queue_start == queue_end)
        uefi_poll_keys();
    return queue_start == queue_end ? 0 : queue[queue_start++ % 64];
}

// ---- the PC speaker and ports (x64) -----------------------------------------

#if defined(__x86_64__)
static void out_byte(u16 port, u8 value) { __asm__ volatile("outb %0, %1" : : "a"(value), "Nd"(port)); }
static u8 in_byte(u16 port)
{
    u8 value;
    __asm__ volatile("inb %1, %0" : "=a"(value) : "Nd"(port));
    return value;
}
#endif

int uefi_in(int port)
{
#if defined(__x86_64__)
    return in_byte((u16)port);
#else
    (void)port;
    return 0;
#endif
}

void uefi_out(int port, int value)
{
#if defined(__x86_64__)
    out_byte((u16)port, (u8)value);
#else
    (void)port;
    (void)value;
#endif
}

// The speaker sounds the timer's channel 2, at 1193182 Hz divided by the count.
void uefi_sound(u32 hertz)
{
#if defined(__x86_64__)
    if (hertz == 0 || hertz > 1193182) {
        out_byte(0x61, in_byte(0x61) & 0xFC);
        return;
    }
    u32 count = 1193182 / hertz;
    out_byte(0x43, 0xB6);
    out_byte(0x42, (u8)count);
    out_byte(0x42, (u8)(count >> 8));
    out_byte(0x61, in_byte(0x61) | 3);
#else
    (void)hertz;
#endif
}

int uefi_has_speaker(void)
{
#if defined(__x86_64__)
    return 1;
#else
    return 0;
#endif
}

// ---- files, on the volume the program was loaded from -----------------------

static File *root_directory(void)
{
    static File *root;
    if (root)
        return root;
    static const Guid loaded_image = LOADED_IMAGE_GUID, file_system = SIMPLE_FILE_SYSTEM_GUID;
    LoadedImage *image;
    SimpleFileSystem *volume;
    if (EFI_ERROR(boot()->handle_protocol(efi_image, &loaded_image, (void **)&image)) ||
        EFI_ERROR(boot()->handle_protocol(image->device_handle, &file_system, (void **)&volume)) ||
        EFI_ERROR(volume->open_volume(volume, &root)))
        root = 0;
    return root;
}

// A UTF-8 path to UTF-16, with "/" as "\". Returns 0 if it doesn't fit.
static int wide_path(const u8 *path, char16 *out, usize room)
{
    usize n = 0;
    while (*path) {
        u32 c = *path++;
        if (c >= 0xC0) {
            int extra = c >= 0xF0 ? 3 : c >= 0xE0 ? 2 : 1;
            c &= 0x3F >> extra;
            while (extra-- && (*path & 0xC0) == 0x80)
                c = c << 6 | (*path++ & 0x3F);
        }
        if (c == '/')
            c = '\\';
        if (n + 2 >= room)
            return 0;
        if (c >= 0x10000) {
            c -= 0x10000;
            out[n++] = (char16)(0xD800 + (c >> 10));
            c = 0xDC00 + (c & 0x3FF);
        }
        out[n++] = (char16)c;
    }
    out[n] = 0;
    return 1;
}

enum { ForReading = 0, ForWriting = 1, ForAppending = 2 };

static File *open_path(const u8 *path, u64 mode)
{
    File *root = root_directory();
    char16 name[260];
    File *file;
    if (!root || !wide_path(path, name, 260) || EFI_ERROR(root->open(root, &file, name, mode, 0)))
        return 0;
    return file;
}

static i64 file_size(File *file)
{
    static const Guid info_guid = FILE_INFO_GUID;
    u8 buffer[sizeof(FileInfo) + 520];
    usize size = sizeof buffer;
    if (EFI_ERROR(file->get_info(file, &info_guid, &size, buffer)))
        return -1;
    return (i64)((FileInfo *)buffer)->file_size;
}

void *uefi_file_open(const u8 *path, int mode)
{
    const u64 read_write = FILE_MODE_READ | FILE_MODE_WRITE;
    File *file;
    switch (mode) {
    case ForReading:
        return open_path(path, FILE_MODE_READ);
    case ForWriting:
        // Emptied: deleted, if it exists, and made again.
        file = open_path(path, read_write);
        if (file)
            file->delete(file);
        return open_path(path, read_write | FILE_MODE_CREATE);
    case ForAppending:
        file = open_path(path, read_write | FILE_MODE_CREATE);
        if (file)
            file->set_position(file, ~0ull); // the end
        return file;
    }
    return 0;
}

int uefi_file_read(File *file, void *buffer, u32 count)
{
    usize size = count;
    return EFI_ERROR(file->read(file, &size, buffer)) ? -1 : (int)size;
}

int uefi_file_write(File *file, const void *buffer, u32 count)
{
    usize size = count;
    return EFI_ERROR(file->write(file, &size, buffer)) ? -1 : (int)size;
}

// origin: 0 from the start, 1 from here, 2 from the end. Returns the new position.
int uefi_file_seek(File *file, int offset, int origin)
{
    i64 base = 0;
    u64 here;
    if (origin == 1) {
        if (EFI_ERROR(file->get_position(file, &here)))
            return -1;
        base = (i64)here;
    } else if (origin == 2) {
        base = file_size(file);
    }
    i64 position = base + offset;
    if (base < 0 || position < 0 || EFI_ERROR(file->set_position(file, (u64)position)))
        return -1;
    return (int)position;
}

int uefi_file_size(File *file) { return (int)file_size(file); }

void uefi_file_close(File *file) { file->close(file); }

// Delete closes the file; it only warns when it couldn't delete it.
int uefi_file_delete(const u8 *path)
{
    File *file = open_path(path, FILE_MODE_READ | FILE_MODE_WRITE);
    return file && file->delete(file) == EFI_SUCCESS;
}

// The C library's file functions, which lib/File.cs calls on every target,
// over the same protocol. A FILE * is the protocol's File itself: there is no
// buffering, so each fread or fwrite is one call to the firmware.
//
// The mode is C's: "r", "w" or "a", then "+" to read and write both, and "x"
// (with "w") to fail if the file exists. Writing always opens the file for
// reading too, so "+" only matters with "r".
void *fopen(const char *path, const char *mode)
{
    int plus = 0, exclusive = 0;
    for (const char *m = mode + 1; *m; m++) {
        plus |= *m == '+';
        exclusive |= *m == 'x';
    }
    if (mode[0] == 'r' && plus)
        return open_path((const u8 *)path, FILE_MODE_READ | FILE_MODE_WRITE);
    if (mode[0] == 'w' && exclusive) {
        File *existing = open_path((const u8 *)path, FILE_MODE_READ);
        if (existing) {
            existing->close(existing);
            return 0;
        }
    }
    int how = mode[0] == 'r' ? ForReading : mode[0] == 'w' ? ForWriting : mode[0] == 'a' ? ForAppending : -1;
    return uefi_file_open((const u8 *)path, how);
}

// Nothing is buffered here, so there's nothing to write out.
int fflush(void *file) { (void)file; return 0; }

// FileStream turns the C library's buffering off; there is none here.
int setvbuf(void *file, char *buffer, int how, usize size)
{
    (void)file; (void)buffer; (void)how; (void)size;
    return 0;
}

usize fread(void *buffer, usize size, usize count, File *file)
{
    usize bytes = size * count;
    if (size == 0 || EFI_ERROR(file->read(file, &bytes, buffer)))
        return 0;
    return bytes / size;
}

usize fwrite(const void *buffer, usize size, usize count, File *file)
{
    usize bytes = size * count;
    if (size == 0 || EFI_ERROR(file->write(file, &bytes, buffer)))
        return 0;
    return bytes / size;
}

// origin: 0 (SEEK_SET) from the start, 1 from here, 2 from the end.
int fseeko(File *file, i64 offset, int origin)
{
    i64 base = 0;
    u64 here;
    if (origin == 1) {
        if (EFI_ERROR(file->get_position(file, &here)))
            return -1;
        base = (i64)here;
    } else if (origin == 2) {
        base = file_size(file);
    } else if (origin != 0) {
        return -1;
    }
    i64 position = base + offset;
    if (base < 0 || position < 0 || EFI_ERROR(file->set_position(file, (u64)position)))
        return -1;
    return 0;
}

i64 ftello(File *file)
{
    u64 here;
    return EFI_ERROR(file->get_position(file, &here)) ? -1 : (i64)here;
}

int fclose(File *file) { return EFI_ERROR(file->close(file)) ? -1 : 0; }

int remove(const char *path) { return uefi_file_delete((const u8 *)path) ? 0 : -1; }

// ---- power ------------------------------------------------------------------

void uefi_shutdown(void) { efi_system->runtime_services->reset_system(EfiResetShutdown, 0, 0, 0); }

void uefi_reboot(void) { efi_system->runtime_services->reset_system(EfiResetCold, 0, 0, 0); }
