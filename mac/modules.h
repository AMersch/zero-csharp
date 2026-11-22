// zerocore looks for the compiler's module list between __start___modules and
// __stop___modules (GNU ld's names); on macOS those bound a table in
// zerocore/native/mac-arm64.S, which this fills from the __DATA,__modules
// section's bounds (section$start$/section$end$ in Mach-O linkers' names).
// Used by start.c (programs) and library.c (native libraries).

extern void *zero_modules_first[] __asm("section$start$__DATA$__modules");
extern void *zero_modules_last[] __asm("section$end$__DATA$__modules");
extern void *__start___modules[];
extern void *__stop___modules[];
long write(int fd, const void *buffer, unsigned long count);
void abort(void);

static inline void zero_copy_modules(void)
{
    long count = zero_modules_last - zero_modules_first;
    if (count > __stop___modules - __start___modules)
    {
        static const char message[] = "zerocore: more compiled modules than mac-arm64.S has room for\n";
        write(2, message, sizeof message - 1);
        abort();
    }
    for (long i = 0; i < count; i++)
        __start___modules[i] = zero_modules_first[i];
}
