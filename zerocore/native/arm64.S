// Storing an object reference, as arm64 code compiled by bflat does it.
//
// A garbage collector needs to know about every such store, so the compiler
// routes them through these functions and passes arguments in registers of
// its own choosing. That calling convention is why they're in assembly.
// Without a garbage collector they only have to do the store.

    .text

// x14: address to store to (advanced by 8 on return)
// x15: the object reference
    .global RhpAssignRefArm64
    .global RhpCheckedAssignRefArm64
    .type RhpAssignRefArm64, %function
    .type RhpCheckedAssignRefArm64, %function
RhpAssignRefArm64:
RhpCheckedAssignRefArm64:
    str     x15, [x14], #8
    ret

// Copying a reference from one place to another (struct copies).
// x13: address to load from (advanced by 8 on return)
// x14: address to store to (advanced by 8 on return)
// x15: scratch
    .global RhpByRefAssignRefArm64
    .type RhpByRefAssignRefArm64, %function
RhpByRefAssignRefArm64:
    ldr     x15, [x13], #8
    str     x15, [x14], #8
    ret

// Calling an interface method on an object whose type isn't known at build
// time (see Runtime/InterfaceDispatch.cs). The call arrives here with the
// method's own arguments in place, plus:
// x0:  the object
// x11: the call site's dispatch cell
//
// Fast path: after its first call, the cell's second word points at a cache
// of four entries, each a class and the method it resolved to. If the object's
// class is among them, jump. x9, x10 and x12 are free to use here.
    .global RhpInitialDynamicInterfaceDispatch
    .type RhpInitialDynamicInterfaceDispatch, %function
RhpInitialDynamicInterfaceDispatch:
    cbz     x0, 1f                  // null: let the slow path report it
    ldr     x9, [x11, #8]
    tst     x9, #3
    b.ne    1f                      // not a cache pointer yet
    ldr     x10, [x0]               // the object's class
    ldr     x12, [x9]
    cmp     x10, x12
    b.eq    2f
    ldr     x12, [x9, #16]!         // next entry
    cmp     x10, x12
    b.eq    2f
    ldr     x12, [x9, #16]!
    cmp     x10, x12
    b.eq    2f
    ldr     x12, [x9, #16]!
    cmp     x10, x12
    b.ne    1f
2:
    ldr     x9, [x9, #8]
    cbz     x9, 1f                  // entry claimed but not filled in yet
    br      x9

// Slow path: find the method, put every argument register back, and jump to it.
1:
    stp     x29, x30, [sp, #-224]!
    mov     x29, sp
    stp     x0, x1, [sp, #16]
    stp     x2, x3, [sp, #32]
    stp     x4, x5, [sp, #48]
    stp     x6, x7, [sp, #64]
    str     x8, [sp, #80]           // where a large struct result goes
    stp     q0, q1, [sp, #96]
    stp     q2, q3, [sp, #128]
    stp     q4, q5, [sp, #160]
    stp     q6, q7, [sp, #192]
    mov     x1, x11
    bl      RhpResolveInterfaceMethod
    mov     x9, x0
    ldp     x0, x1, [sp, #16]
    ldp     x2, x3, [sp, #32]
    ldp     x4, x5, [sp, #48]
    ldp     x6, x7, [sp, #64]
    ldr     x8, [sp, #80]
    ldp     q0, q1, [sp, #96]
    ldp     q2, q3, [sp, #128]
    ldp     q4, q5, [sp, #160]
    ldp     q6, q7, [sp, #192]
    ldp     x29, x30, [sp], #224
    br      x9

    .section .note.GNU-stack, "", %progbits
