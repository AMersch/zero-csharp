// The C side of an Android program: what Android's C library (Bionic) lacks
// of glibc's names, which lib/FileSystem.cs calls everywhere (see there), and
// ZeroSystem's answer. bflat links the program against Bionic itself
// (--libc bionic); everything else zerocore and lib/ call is there under the
// same name as in glibc. No headers, so any clang compiles it, without the
// Android NDK.

// 10: Android (lib/FileSystem.cs, SystemKind). zerocore/native/arm64.S's
// ZeroSystem, which says Linux, is weak, so this one is used instead.
int ZeroSystem(void)
{
    return 10;
}

// Bionic's errno is __errno(); glibc's is __errno_location().
int *__errno(void);

int *__errno_location(void)
{
    return __errno();
}

// glibc's old stat entry points, with a version argument. Bionic's struct
// stat on arm64 is the kernel's, which is glibc's arm64 layout too: the mode
// at 16, the size at 48, the modification time at 88.
int stat(const char *path, void *buffer);
int lstat(const char *path, void *buffer);

int __xstat64(int version, const char *path, void *buffer)
{
    (void)version;
    return stat(path, buffer);
}

int __lxstat64(int version, const char *path, void *buffer)
{
    (void)version;
    return lstat(path, buffer);
}
