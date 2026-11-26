// The one place a `throw` can come back to: lib/Catch.cs's Catch.Run.
//
// Catch.Run marks a point on the stack and runs its callback. A `throw`
// anywhere inside, however deep, jumps straight back to that point, as C's
// longjmp does: nothing between the throw and the mark runs, so neither do the
// catch and finally blocks or the using disposals of the methods it skips. A
// `throw` with no mark on this thread (or fiber) still prints its message and
// ends the program (RhpThrowEx in RuntimeExports.cs).
//
// Saving and jumping are native code: ZeroCatchCall saves what a function
// must keep for its caller, as ZeroFiberSwitch does, and calls the callback;
// ZeroCatchJump puts all that back and returns from ZeroCatchCall as if the
// callback had returned. To the C# code around, ZeroCatchCall is a call like
// any other: it returns once, with the registers and the stack as a call
// leaves them. In assembly next to ZeroFiberSwitch on x64 and arm64
// (native/*.S), with setjmp in C on DOS, microcontrollers and the C64
// (dos, arm and c64/runtime.c). WebAssembly has neither: there ZeroCatchJump
// returns, and a `throw` ends the program as before. It also returns on a
// microcontroller when the throw is in an interrupt handler and the mark in
// the code it interrupted (arm/runtime.c).
//
// They're runtime imports rather than P/Invokes so that on the targets that
// pass a shadow stack (see wasm/runtime.c) ZeroCatchCall gets the caller's,
// to hand to the callback.
//
// A program that never calls Catch.Run has none of this in it: `throw`, the
// static constructor runner and the fiber scheduler reach it through the
// pointers below, which the first Run sets. (A direct call would bring
// the thread-local storage code into every program that has a `throw`,
// 2 KB for a program of 7.)

using System.Runtime.CompilerServices;

namespace System.Runtime
{
    // One mark, in the frame of the Catch.Run that made it.
    internal unsafe struct CatchFrame
    {
        public void* Saved;          // what ZeroCatchJump needs to come back
        public CatchFrame* Outer;    // the mark further up the stack, if any
        public Exception Exception;  // what was thrown, once it was
        public void* Handles;        // lib/Catch.cs's: the files and sockets to close if something is
        public bool Inner;           // one of zerocore's own (Lazy's): it has no Handles
    }

    internal static unsafe class CatchFrames
    {
#if WASM
        // One thread (WebAssembly, DOS, microcontrollers, the C64): a plain
        // static, which spares a small machine the thread-local storage code
        // (1.2 KB on a C64).
        private static CatchFrame* t_innermost;
#else
        [ThreadStatic]
        private static CatchFrame* t_innermost;
#endif

        // Null until the first Run: see the top of the file.
        internal static delegate*<object, void> s_throw;                              // Throw
        internal static delegate*<void*, void*> s_swap;                               // Swap

        [RuntimeImport("*", "ZeroCatchCall"), MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void ZeroCatchCall(void** saved, delegate*<void*, void> body, void* argument);

        [RuntimeImport("*", "ZeroCatchJump"), MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void ZeroCatchJump(void* saved);

        // Runs body(argument) with frame as the innermost mark. On return,
        // frame->Exception holds what it threw, or null. frame must be in
        // the caller's own stack frame.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Run(CatchFrame* frame, delegate*<void*, void> body, void* argument)
        {
            if (s_throw == null)
            {
                s_swap = &Swap;
                s_throw = &Throw;
            }
            RunInner(frame, body, argument);
        }

        // The same without setting the pointers: for zerocore's own marks
        // (Lazy's), which matter only once lib/'s Catch.Run has set them.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void RunInner(CatchFrame* frame, delegate*<void*, void> body, void* argument)
        {
            frame->Exception = null;
            frame->Outer = t_innermost;
            t_innermost = frame;
            ZeroCatchCall(&frame->Saved, body, argument);
            t_innermost = frame->Outer;
        }

        // From RhpThrowEx: back to the innermost mark, or return if there's
        // none. `throw null` throws a NullReferenceException, as in .NET.
        private static void Throw(object exception)
        {
            CatchFrame* frame = t_innermost;
            if (frame == null)
                return;
            frame->Exception = exception as Exception ?? new NullReferenceException();
            t_innermost = frame->Outer;
            ZeroCatchJump(frame->Saved);
        }

        // The innermost mark on this thread or fiber, or null.
        internal static CatchFrame* Innermost => t_innermost;

        // Puts another chain of marks in place and returns the one that was.
        // Each fiber has its own (they're on its own stack): the scheduler
        // (lib/Fibers.cs) swaps on every switch. A static constructor runs
        // with none (ObjectAllocator.RunStaticConstructor).
        private static void* Swap(void* innermost)
        {
            void* previous = t_innermost;
            t_innermost = (CatchFrame*)innermost;
            return previous;
        }
    }
}
