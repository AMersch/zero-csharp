// Calls a native library built from fixtures/nativelib from C and prints what
// it got: the first eight lines of fixtures/nativelib-caller/expected.txt.
//
//   cc main.c -o caller && ./caller <library>        (dlopen, or LoadLibrary on Windows)
//   cc -DLINKED main.c <library> -Wl,-rpath,<dir>     (linked against the library)
#include <stdint.h>
#include <stdio.h>

typedef struct { int32_t x, y; } Point;

#ifdef LINKED
int32_t nl_add(int32_t a, int32_t b);
int64_t nl_mul_long(int64_t a, int64_t b);
double nl_scale(double x, double f);
int64_t nl_sum(const int32_t *values, int32_t count);
Point nl_offset(Point p, int32_t d);
void nl_negate(Point *p);
int32_t nl_remember(int32_t value);
int32_t nl_cctor_value(void);
#else
#ifdef _WIN32
#include <windows.h>
#define open_library(path) ((void *)LoadLibraryA(path))
#define find(library, name) ((void *)GetProcAddress((HMODULE)(library), name))
#else
#include <dlfcn.h>
#define open_library(path) dlopen(path, RTLD_NOW)
#define find(library, name) dlsym(library, name)
#endif
static int32_t (*nl_add)(int32_t, int32_t);
static int64_t (*nl_mul_long)(int64_t, int64_t);
static double (*nl_scale)(double, double);
static int64_t (*nl_sum)(const int32_t *, int32_t);
static Point (*nl_offset)(Point, int32_t);
static void (*nl_negate)(Point *);
static int32_t (*nl_remember)(int32_t);
static int32_t (*nl_cctor_value)(void);
#endif

int main(int argc, char **argv)
{
#ifndef LINKED
    if (argc < 2) {
        fprintf(stderr, "usage: caller <library>\n");
        return 2;
    }
    void *library = open_library(argv[1]);
    if (!library) {
        fprintf(stderr, "can't load %s\n", argv[1]);
        return 1;
    }
    *(void **)&nl_add = find(library, "nl_add");
    *(void **)&nl_mul_long = find(library, "nl_mul_long");
    *(void **)&nl_scale = find(library, "nl_scale");
    *(void **)&nl_sum = find(library, "nl_sum");
    *(void **)&nl_offset = find(library, "nl_offset");
    *(void **)&nl_negate = find(library, "nl_negate");
    *(void **)&nl_remember = find(library, "nl_remember");
    *(void **)&nl_cctor_value = find(library, "nl_cctor_value");
    if (!nl_add || !nl_mul_long || !nl_scale || !nl_sum || !nl_offset || !nl_negate || !nl_remember || !nl_cctor_value) {
        fprintf(stderr, "an export is missing\n");
        return 1;
    }
#else
    (void)argc;
    (void)argv;
#endif
    int32_t values[] = { 1, 2, 3, 4 };
    Point offset = nl_offset((Point){ 1, 12 }, 10);
    Point negated = { 3, 4 };
    nl_negate(&negated);
    printf("add %d\n", nl_add(2, 3));
    printf("mul %lld\n", (long long)nl_mul_long(60000, 100000));
    printf("scale %g\n", nl_scale(2.5, 3.0));
    printf("sum %lld\n", (long long)nl_sum(values, 4));
    printf("offset %d,%d\n", offset.x, offset.y);
    printf("negate %d,%d\n", negated.x, negated.y);
    nl_remember(5);
    nl_remember(6);
    printf("remember %d\n", nl_remember(7));
    printf("cctor %d\n", nl_cctor_value());
    return 0;
}
