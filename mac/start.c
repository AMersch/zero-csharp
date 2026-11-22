// A macOS program's entry point: hands zerocore the compiler's module list,
// then runs the program. (runtime.c has the functions programs and libraries
// share; library.c is a native library's startup instead of this.)
#include "modules.h"

int __managed__Main(int argc, char **argv);

int main(int argc, char **argv)
{
    zero_copy_modules();
    return __managed__Main(argc, argv);
}
