// The one thing start.c can't do in C: switch to a larger stack. Assembled
// by clang as a COFF object for x86_64-pc-windows-msvc.
//
// int efi_call_on_stack(int (*function)(void *), void *argument, void *stack_top)
// rcx: function, rdx: argument, r8: the new stack's top (16-byte aligned)

    .intel_syntax noprefix
    .text
    .globl efi_call_on_stack
efi_call_on_stack:
    push    rbp
    mov     rbp, rsp
    mov     rsp, r8
    sub     rsp, 32                 # the callee's shadow space
    mov     rax, rcx
    mov     rcx, rdx
    call    rax
    mov     rsp, rbp
    pop     rbp
    ret
