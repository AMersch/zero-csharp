// x64.S for Windows: the same routines, for the Windows x64 calling
// convention (arguments in rcx, rdx, r8, r9; 32 bytes of "shadow space" the
// caller reserves for the callee; xmm6-xmm15 kept by the callee), plus the
// stack probe Windows code needs. Assembled by clang as a COFF object.
//
// (Comments after an instruction start with # here.)

    .intel_syntax noprefix
    .text

// Storing an object reference (see x64.S).
// rcx: address to store to
// rdx: the object reference
    .globl RhpAssignRef
    .globl RhpCheckedAssignRef
RhpAssignRef:
RhpCheckedAssignRef:
    mov     [rcx], rdx
    ret

// Copying a reference from one place to another (struct copies). The compiler
// uses rdi and rsi for this one on Windows too.
// rdi: address to store to (advanced by 8 on return)
// rsi: address to load from (advanced by 8 on return)
// rcx: scratch
    .globl RhpByRefAssignRef
RhpByRefAssignRef:
    mov     rcx, [rsi]
    mov     [rdi], rcx
    add     rdi, 8
    add     rsi, 8
    ret

// Calling an interface method on an object whose type isn't known at build
// time (see x64.S and Runtime/InterfaceDispatch.cs).
// rcx: the object
// r11: the call site's dispatch cell
    .globl RhpInitialDynamicInterfaceDispatch
RhpInitialDynamicInterfaceDispatch:
    test    rcx, rcx
    jz      1f                      # null: let the slow path report it
    mov     r10, [r11 + 8]
    test    r10b, 3
    jnz     1f                      # not a cache pointer yet
    mov     rax, [rcx]              # the object's class
    cmp     rax, [r10]
    je      2f
    add     r10, 16                 # next entry
    cmp     rax, [r10]
    je      2f
    add     r10, 16
    cmp     rax, [r10]
    je      2f
    add     r10, 16
    cmp     rax, [r10]
    jne     1f
2:
    mov     r10, [r10 + 8]
    test    r10, r10
    jz      1f                      # entry claimed but not filled in yet
    jmp     r10

// Slow path: find the method, put every argument register back, and jump to
// it. 152 bytes: 32 of shadow space for the call, 6 integer registers, 4
// vector registers, and 8 to keep the stack 16-byte aligned. (The callee
// keeps xmm6-xmm15 itself.)
1:
    sub     rsp, 152
    mov     [rsp + 32], rcx
    mov     [rsp + 40], rdx
    mov     [rsp + 48], r8
    mov     [rsp + 56], r9
    mov     [rsp + 64], rax
    mov     [rsp + 72], r10
    movups  [rsp + 80], xmm0
    movups  [rsp + 96], xmm1
    movups  [rsp + 112], xmm2
    movups  [rsp + 128], xmm3
    mov     rdx, r11                # RhpResolveInterfaceMethod(object, cell)
    call    RhpResolveInterfaceMethod
    mov     r11, rax
    mov     rcx, [rsp + 32]
    mov     rdx, [rsp + 40]
    mov     r8, [rsp + 48]
    mov     r9, [rsp + 56]
    mov     rax, [rsp + 64]
    mov     r10, [rsp + 72]
    movups  xmm0, [rsp + 80]
    movups  xmm1, [rsp + 96]
    movups  xmm2, [rsp + 112]
    movups  xmm3, [rsp + 128]
    add     rsp, 152
    jmp     r11

// Windows grows a thread's stack one guard page at a time, so a function
// whose frame is larger than a page must touch each page on the way down, in
// order. The compiler calls this from such a function's prologue.
// r11: the lowest address of the frame being allocated (kept)
// rsp: somewhere on the last page already touched
// rax: scratch
    .globl RhpStackProbe
RhpStackProbe:
    mov     rax, rsp
    and     rax, -4096              # start of the last page touched
3:
    sub     rax, 4096
    test    dword ptr [rax], eax    # touch the next page down
    cmp     rax, r11
    jg      3b
    ret
