// fixtures/nativelib's exports in F#, for the same C caller
// (fixtures/nativelib-c): the first eight lines of nativelib-caller/expected.txt.
namespace NativeLib

open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop

#nowarn "9" // native pointers

// docs:point
[<Struct>]
type Point =
    val mutable X: int
    val mutable Y: int
// docs:end

module Native =
    [<DllImport("libc")>]
    extern int abs(int value)

module State =
    // Set up when first used (the file's static initializer), with a call the
    // compiler can't evaluate at build time.
    let answer = Native.abs -41 + 1
    let mutable history: int array = Array.zeroCreate 0

module Exports =
    // docs:exports
    [<UnmanagedCallersOnly(EntryPoint = "nl_add")>]
    let add (a: int) (b: int) = a + b

    [<UnmanagedCallersOnly(EntryPoint = "nl_mul_long")>]
    let mulLong (a: int64) (b: int64) = a * b

    [<UnmanagedCallersOnly(EntryPoint = "nl_scale")>]
    let scale (x: float) (f: float) = x * f

    [<UnmanagedCallersOnly(EntryPoint = "nl_sum")>]
    let sum (values: nativeptr<int>) (count: int) : int64 =
        let mutable total = 0L
        for i in 0 .. count - 1 do
            total <- total + int64 (NativePtr.get values i)
        total
    // docs:end

    [<UnmanagedCallersOnly(EntryPoint = "nl_offset")>]
    let offset (p: Point) (d: int) =
        let mutable q = p
        q.X <- p.X + d
        q.Y <- p.Y + d
        q

    [<UnmanagedCallersOnly(EntryPoint = "nl_negate")>]
    let negate (p: nativeptr<Point>) =
        let point = NativePtr.toByRef p
        point.X <- -point.X
        point.Y <- -point.Y

    [<UnmanagedCallersOnly(EntryPoint = "nl_remember")>]
    let remember (value: int) =
        let old = State.history
        let next = Array.zeroCreate (old.Length + 1)
        for i in 0 .. old.Length - 1 do
            next.[i] <- old.[i]
        next.[old.Length] <- value
        State.history <- next
        next.Length

    [<UnmanagedCallersOnly(EntryPoint = "nl_cctor_value")>]
    let cctorValue () = State.answer
