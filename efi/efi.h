// What the UEFI C files (start.c, runtime.c, libm.c) use from the UEFI
// specification (version 2.10): its types, the system table, the boot and
// runtime services, and the few protocols. There are no UEFI headers here, so
// each table is written out in the specification's order; a function this
// code doesn't call is still declared, as a plain pointer, to keep the
// layout. Only what is used has a real type.
//
// UEFI uses the Windows x64 (or arm64) calling convention, which is what
// clang targets with x86_64-pc-windows-msvc / aarch64-pc-windows-msvc, so
// the function pointers need no attribute.

#pragma once

typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
typedef unsigned long long u64;
typedef int i32;
typedef long long i64;
typedef u64 usize;
typedef u16 char16;

typedef u64 Status;
typedef void *Handle;
typedef void *Event;

#define EFI_SUCCESS 0
#define EFI_ERROR_BIT (1ull << 63)
#define EFI_ERROR(status) (((status) & EFI_ERROR_BIT) != 0)
#define EFI_BUFFER_TOO_SMALL (EFI_ERROR_BIT | 5)
#define EFI_NOT_READY (EFI_ERROR_BIT | 6)
#define EFI_UNSUPPORTED (EFI_ERROR_BIT | 3)
#define EFI_NOT_FOUND (EFI_ERROR_BIT | 14)

typedef struct { u32 a; u16 b, c; u8 d[8]; } Guid;

typedef struct {
    u64 signature;
    u32 revision, header_size, crc32, reserved;
} TableHeader;

typedef struct {
    u16 year;
    u8 month, day, hour, minute, second, pad1;
    u32 nanosecond;
    short time_zone;
    u8 daylight, pad2;
} Time;

// ---- console ----------------------------------------------------------------

typedef struct { u16 scan_code; char16 unicode_char; } InputKey;

typedef struct TextInput {
    void *reset;
    Status (*read_key_stroke)(struct TextInput *self, InputKey *key);
    Event wait_for_key;
} TextInput;

typedef struct {
    int max_mode, mode, attribute, cursor_column, cursor_row;
    u8 cursor_visible;
} TextOutputMode;

typedef struct TextOutput {
    void *reset;
    Status (*output_string)(struct TextOutput *self, const char16 *text);
    void *test_string;
    Status (*query_mode)(struct TextOutput *self, usize mode, usize *columns, usize *rows);
    Status (*set_mode)(struct TextOutput *self, usize mode);
    Status (*set_attribute)(struct TextOutput *self, usize attribute);
    Status (*clear_screen)(struct TextOutput *self);
    Status (*set_cursor_position)(struct TextOutput *self, usize column, usize row);
    Status (*enable_cursor)(struct TextOutput *self, u8 visible);
    TextOutputMode *mode;
} TextOutput;

// Simple Text Input Ex: key state as well as characters (arrows held down).
typedef struct { u32 key_shift_state; u8 key_toggle_state; } KeyState;
typedef struct { InputKey key; KeyState key_state; } KeyData;

typedef struct TextInputEx {
    void *reset;
    Status (*read_key_stroke_ex)(struct TextInputEx *self, KeyData *key);
    Event wait_for_key_ex;
    void *set_state, *register_key_notify, *unregister_key_notify;
} TextInputEx;

// ---- services ---------------------------------------------------------------

enum { AllocateAnyPages = 0 };
enum { EfiLoaderData = 2 };
enum { EfiResetCold = 0, EfiResetWarm = 1, EfiResetShutdown = 2 };

typedef struct {
    TableHeader header;
    void *raise_tpl, *restore_tpl;
    Status (*allocate_pages)(int type, int memory_type, usize pages, u64 *memory);
    Status (*free_pages)(u64 memory, usize pages);
    void *get_memory_map;
    Status (*allocate_pool)(int memory_type, usize size, void **buffer);
    Status (*free_pool)(void *buffer);
    void *create_event, *set_timer;
    Status (*wait_for_event)(usize count, Event *events, usize *index);
    void *signal_event, *close_event;
    Status (*check_event)(Event event);
    void *install_protocol_interface, *reinstall_protocol_interface, *uninstall_protocol_interface;
    Status (*handle_protocol)(Handle handle, const Guid *protocol, void **interface);
    void *reserved, *register_protocol_notify, *locate_handle, *locate_device_path,
        *install_configuration_table;
    void *load_image, *start_image;
    Status (*exit)(Handle image, Status status, usize data_size, char16 *data);
    void *unload_image, *exit_boot_services;
    void *get_next_monotonic_count;
    Status (*stall)(usize microseconds);
    Status (*set_watchdog_timer)(usize timeout, u64 code, usize data_size, char16 *data);
    void *connect_controller, *disconnect_controller;
    Status (*open_protocol)(Handle handle, const Guid *protocol, void **interface,
                            Handle agent, Handle controller, u32 attributes);
    void *close_protocol, *open_protocol_information;
    void *protocols_per_handle, *locate_handle_buffer;
    Status (*locate_protocol)(const Guid *protocol, void *registration, void **interface);
    void *install_multiple_protocol_interfaces, *uninstall_multiple_protocol_interfaces;
    void *calculate_crc32, *copy_mem, *set_mem, *create_event_ex;
} BootServices;

typedef struct {
    TableHeader header;
    Status (*get_time)(Time *time, void *capabilities);
    void *set_time, *get_wakeup_time, *set_wakeup_time;
    void *set_virtual_address_map, *convert_pointer;
    void *get_variable, *get_next_variable_name, *set_variable;
    void *get_next_high_monotonic_count;
    void (*reset_system)(int type, Status status, usize data_size, void *data);
    void *update_capsule, *query_capsule_capabilities, *query_variable_info;
} RuntimeServices;

typedef struct {
    TableHeader header;
    char16 *firmware_vendor;
    u32 firmware_revision;
    Handle console_in_handle;
    TextInput *con_in;
    Handle console_out_handle;
    TextOutput *con_out;
    Handle standard_error_handle;
    TextOutput *std_err;
    RuntimeServices *runtime_services;
    BootServices *boot_services;
    usize number_of_table_entries;
    void *configuration_table;
} SystemTable;

// ---- protocols --------------------------------------------------------------

#define LOADED_IMAGE_GUID { 0x5B1B31A1, 0x9562, 0x11d2, { 0x8E, 0x3F, 0x00, 0xA0, 0xC9, 0x69, 0x72, 0x3B } }

typedef struct {
    u32 revision;
    Handle parent_handle;
    SystemTable *system_table;
    Handle device_handle;
    void *file_path;
    void *reserved;
    u32 load_options_size;
    void *load_options;
    void *image_base;
    u64 image_size;
    int image_code_type, image_data_type;
    void *unload;
} LoadedImage;

#define SIMPLE_FILE_SYSTEM_GUID { 0x964E5B22, 0x6459, 0x11d2, { 0x8E, 0x39, 0x00, 0xA0, 0xC9, 0x69, 0x72, 0x3B } }
#define FILE_INFO_GUID { 0x09576E92, 0x6D3F, 0x11d2, { 0x8E, 0x39, 0x00, 0xA0, 0xC9, 0x69, 0x72, 0x3B } }

#define FILE_MODE_READ 1ull
#define FILE_MODE_WRITE 2ull
#define FILE_MODE_CREATE 0x8000000000000000ull

typedef struct File {
    u64 revision;
    Status (*open)(struct File *self, struct File **opened, const char16 *name, u64 mode, u64 attributes);
    Status (*close)(struct File *self);
    Status (*delete)(struct File *self);
    Status (*read)(struct File *self, usize *size, void *buffer);
    Status (*write)(struct File *self, usize *size, const void *buffer);
    Status (*get_position)(struct File *self, u64 *position);
    Status (*set_position)(struct File *self, u64 position);
    Status (*get_info)(struct File *self, const Guid *type, usize *size, void *buffer);
    void *set_info;
    Status (*flush)(struct File *self);
} File;

typedef struct {
    u64 size, file_size, physical_size;
    Time create_time, last_access_time, modification_time;
    u64 attribute;
    char16 file_name[];
} FileInfo;

typedef struct SimpleFileSystem {
    u64 revision;
    Status (*open_volume)(struct SimpleFileSystem *self, File **root);
} SimpleFileSystem;

// What the UEFI Shell installs on a program it runs: the arguments, split,
// and its standard input, output and error, which are files when redirected.
#define SHELL_PARAMETERS_GUID { 0x752f3136, 0x4e16, 0x4fdc, { 0xa2, 0x2a, 0xe5, 0xf4, 0x68, 0x12, 0xf4, 0xca } }

typedef struct {
    char16 **argv;
    usize argc;
    File *std_in, *std_out, *std_err;
} ShellParameters;

// The UEFI Shell itself; only its environment is used (Environment's
// GetEnvironmentVariable).
#define SHELL_GUID { 0x6302d008, 0x7f9b, 0x4f30, { 0x87, 0xac, 0x60, 0xc9, 0xfe, 0xf5, 0xda, 0x4e } }

typedef struct {
    void *execute;
    const char16 *(*get_env)(const char16 *name);
} Shell;

#define GRAPHICS_OUTPUT_GUID { 0x9042A9DE, 0x23DC, 0x4A38, { 0x96, 0xFB, 0x7A, 0xDE, 0xD0, 0x80, 0x51, 0x6A } }

enum { PixelRedGreenBlueReserved8 = 0, PixelBlueGreenRedReserved8 = 1, PixelBitMask = 2, PixelBltOnly = 3 };
enum { BltVideoFill = 0, BltVideoToBuffer = 1, BltBufferToVideo = 2 };

typedef struct {
    u32 version, horizontal_resolution, vertical_resolution;
    int pixel_format;
    u32 red_mask, green_mask, blue_mask, reserved_mask;
    u32 pixels_per_scan_line;
} GraphicsModeInfo;

typedef struct {
    u32 max_mode, mode;
    GraphicsModeInfo *info;
    usize size_of_info;
    u64 frame_buffer_base;
    usize frame_buffer_size;
} GraphicsMode;

typedef struct GraphicsOutput {
    Status (*query_mode)(struct GraphicsOutput *self, u32 mode, usize *size, GraphicsModeInfo **info);
    Status (*set_mode)(struct GraphicsOutput *self, u32 mode);
    Status (*blt)(struct GraphicsOutput *self, void *buffer, int operation, usize source_x, usize source_y,
                  usize destination_x, usize destination_y, usize width, usize height, usize delta);
    GraphicsMode *mode;
} GraphicsOutput;

#define TEXT_INPUT_EX_GUID { 0xdd9e7534, 0x7762, 0x4698, { 0x8c, 0x14, 0xf5, 0x85, 0x17, 0xa6, 0x25, 0xaa } }

#define RNG_GUID { 0x3152bca5, 0xeade, 0x433d, { 0x86, 0x2e, 0xc0, 0x1c, 0xdc, 0x29, 0x1f, 0x44 } }

typedef struct Rng {
    void *get_info;
    Status (*get_rng)(struct Rng *self, const Guid *algorithm, usize size, u8 *buffer);
} Rng;

// ---- shared between start.c and runtime.c -----------------------------------

extern Handle efi_image;
extern SystemTable *efi_system;
extern ShellParameters *efi_shell;

void efi_runtime_start(void);
