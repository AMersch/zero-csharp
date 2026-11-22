// The one thing start.c can't do in C: switch to a larger stack. Assembled
// by clang as a COFF object for aarch64-pc-windows-msvc.
//
// int efi_call_on_stack(int (*function)(void *), void *argument, void *stack_top)
// x0: function, x1: argument, x2: the new stack's top (16-byte aligned)

    .text
    .globl efi_call_on_stack
efi_call_on_stack:
    stp     x29, x30, [sp, #-16]!
    mov     x29, sp
    mov     sp, x2
    mov     x9, x0
    mov     x0, x1
    blr     x9
    mov     sp, x29                 // x29 is kept by the callee
    ldp     x29, x30, [sp], #16
    ret
