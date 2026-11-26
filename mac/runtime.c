// The C side of a macOS program or native library: fix-ups where macOS
// differs from Linux (the processor count, the clock, file information and
// directory entries). The entry point is start.c's for a program,
// library.c's constructor for a native library. Everything else zerocore and
// lib/ call (calloc, write, read, abort, puts, the math
// functions) is in macOS's own C library under the same name as on Linux.

// lib/Threads.cs asks get_nprocs for the processor count, as on Linux;
// macOS's sysconf has it under another number than Linux's.
long sysconf(int name);
#define SC_NPROCESSORS_ONLN 58

int get_nprocs(void)
{
    return (int)sysconf(SC_NPROCESSORS_ONLN);
}

// lib/Clock.cs and the samples call clock_gettime with Linux's numbers: 0 for
// the wall clock, which is 0 on macOS too, and 1 for CLOCK_MONOTONIC, which is
// no clock at all on macOS. This definition comes before the C library's, so
// it answers 0 with the wall clock and anything else with the monotonic one.
typedef unsigned long long u64;
u64 clock_gettime_nsec_np(int clock);
#define CLOCK_REALTIME 0
#define CLOCK_UPTIME_RAW 8

int clock_gettime(int clock, long long *timespec)
{
    u64 now = clock_gettime_nsec_np(clock == 0 ? CLOCK_REALTIME : CLOCK_UPTIME_RAW);
    timespec[0] = (long long)(now / 1000000000);
    timespec[1] = (long long)(now % 1000000000);
    return 0;
}

// lib/FileSystem.cs calls glibc's names, with glibc's layouts: these give
// macOS's answers in them. glibc's struct stat as on arm64: the mode at 16,
// the size at 48, the modification time at 88 (seconds) and 96 (nanoseconds).
// macOS's (arm64): the mode at 4 (16 bits), the modification time at 48 and
// 56, the size at 96. Both systems number the file types (S_IFDIR...) alike.
int stat(const char *path, void *buffer);
int lstat(const char *path, void *buffer);
int *__error(void);
void *readdir(void *dir);
void *memset(void *to, int value, unsigned long size);
void *memcpy(void *to, const void *from, unsigned long size);

static int to_glibc_stat(int result, const unsigned char *mac, unsigned char *out)
{
    if (result != 0)
        return result;
    unsigned mode = *(const unsigned short *)(mac + 4);
    memset(out, 0, 128);
    memcpy(out + 16, &mode, 4);
    memcpy(out + 48, mac + 96, 8);
    memcpy(out + 88, mac + 48, 16);
    return 0;
}

int __xstat64(int version, const char *path, unsigned char *out)
{
    (void)version;
    unsigned char mac[144];
    return to_glibc_stat(stat(path, mac), mac, out);
}

int __lxstat64(int version, const char *path, unsigned char *out)
{
    (void)version;
    unsigned char mac[144];
    return to_glibc_stat(lstat(path, mac), mac, out);
}

// errno: macOS's numbers, which lib/FileSystem.cs knows.
int *__errno_location(void) { return __error(); }

// glibc's struct dirent64: d_type at 18 and d_name from 19. macOS's: d_type
// at 20, d_name from 21; d_type's numbers are the same. Good until the
// thread's next readdir64, as readdir's entry is.
struct glibc_dirent64 {
    unsigned long long d_ino;
    long long d_off;
    unsigned short d_reclen;
    unsigned char d_type;
    char d_name[1024];
};

void *readdir64(void *dir)
{
    static __thread struct glibc_dirent64 out;
    const unsigned char *entry = readdir(dir);
    if (!entry)
        return 0;
    unsigned short length = *(const unsigned short *)(entry + 18);
    if (length > 1023)
        length = 1023;
    out.d_type = entry[20];
    memcpy(out.d_name, entry + 21, length);
    out.d_name[length] = 0;
    return &out;
}
