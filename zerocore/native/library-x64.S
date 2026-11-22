// A native library's startup (NativeLib=Shared, Linux x64): the dynamic
// loader runs every .init_array entry when the library loads, before any of
// its exports can be called. This one sets up zerocore (static fields), as an
// executable's main does. Executables don't link this file.
    .section .init_array, "aw"
    .p2align 3
    .quad zero_library_init

    .text
    .p2align 4
zero_library_init:
    jmp ZeroInitializeLibrary

    .section .note.GNU-stack, "", @progbits
