// A library for zero-mode programs. It allocates nothing of its own: memory
// that must outlive a call comes from the caller's arena, and errors are
// returned, not thrown (a throw would end the caller's program).
module ZeroLibrary1.Greeter

open System

/// Writes "Hello, name!" to standard output.
let greet (name: ReadOnlySpan<char>) =
    Out.Write "Hello, "
    Out.Write name
    Out.Line "!"
