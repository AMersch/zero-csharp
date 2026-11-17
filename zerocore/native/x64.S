// Storing an object reference, as x64 code compiled by bflat does it.
//
// A garbage collector needs to know about every such store, so the compiler
// routes them through these functions, which must leave every register alone
// except the ones noted. That contract is why they're in assembly. Without a
// garbage collector they only have to do the store.

    .intel_syntax noprefix
    .text

// rdi: address to store to
// rsi: the object reference
    .global RhpAssignRef
    .global RhpCheckedAssignRef
    .type RhpAssignRef, @function
    .type RhpCheckedAssignRef, @function
RhpAssignRef:
RhpCheckedAssignRef:
    mov     [rdi], rsi
    ret

// Copying a reference from one place to another (struct copies).
// rdi: address to store to (advanced by 8 on return)
// rsi: address to load from (advanced by 8 on return)
// rcx: scratch
    .global RhpByRefAssignRef
    .type RhpByRefAssignRef, @function
RhpByRefAssignRef:
    mov     rcx, [rsi]
    mov     [rdi], rcx
    add     rdi, 8
    add     rsi, 8
    ret

// Calling an interface method on an object whose type isn't known at build
// time (see Runtime/InterfaceDispatch.cs). The call arrives here with the
// method's own arguments in place, plus:
// rdi: the object
// r11: the call site's dispatch cell
// Find the method, put every argument register back, and jump to it.
// 200 bytes: 8 integer registers, 8 vector registers, and 8 to keep the stack
// 16-byte aligned for the call.
    .global RhpInitialDynamicInterfaceDispatch
    .type RhpInitialDynamicInterfaceDispatch, @function
RhpInitialDynamicInterfaceDispatch:
    sub     rsp, 200
    mov     [rsp], rdi
    mov     [rsp + 8], rsi
    mov     [rsp + 16], rdx
    mov     [rsp + 24], rcx
    mov     [rsp + 32], r8
    mov     [rsp + 40], r9
    mov     [rsp + 48], rax
    mov     [rsp + 56], r10
    movups  [rsp + 64], xmm0
    movups  [rsp + 80], xmm1
    movups  [rsp + 96], xmm2
    movups  [rsp + 112], xmm3
    movups  [rsp + 128], xmm4
    movups  [rsp + 144], xmm5
    movups  [rsp + 160], xmm6
    movups  [rsp + 176], xmm7
    mov     rsi, r11
    call    RhpResolveInterfaceMethod
    mov     r11, rax
    mov     rdi, [rsp]
    mov     rsi, [rsp + 8]
    mov     rdx, [rsp + 16]
    mov     rcx, [rsp + 24]
    mov     r8, [rsp + 32]
    mov     r9, [rsp + 40]
    mov     rax, [rsp + 48]
    mov     r10, [rsp + 56]
    movups  xmm0, [rsp + 64]
    movups  xmm1, [rsp + 80]
    movups  xmm2, [rsp + 96]
    movups  xmm3, [rsp + 112]
    movups  xmm4, [rsp + 128]
    movups  xmm5, [rsp + 144]
    movups  xmm6, [rsp + 160]
    movups  xmm7, [rsp + 176]
    add     rsp, 200
    jmp     r11

    .section .note.GNU-stack, "", @progbits
