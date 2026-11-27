// printf, sprintf, failwithf and the others, without reflection.
//
// FSharp.Core's printf makes the curried function a format's type asks for
// (int -> string -> string for "%d %s") at run time, from the type, with
// reflection. Here they're inline, and the function is made where they're
// called, from the type the compiler gave the format: an overload of
// PrintfBuilder.Make per number of arguments, which boxes them and formats
// (FSharp.Core's PrintfImpl). A program gets code only for the shapes it uses.
// A function wrapping them that takes the format as a parameter must be
// inline too.
#nowarn "9" "35" "42" "44" "64" "77" "3370"

namespace Microsoft.FSharp.Core

    open System
    open Microsoft.FSharp.Core
    open Microsoft.FSharp.Core.PrintfImpl

    /// Makes the function a format's type asks for (see the top of the file):
    /// one overload per number of arguments, each the whole function type, so
    /// the compiler picks one by unifying types; the state's result type
    /// (string for sprintf) tells "no more arguments" apart. Up to 12.
    /// (A recursive overload, one argument per step, makes the compiler's
    /// work grow threefold with each argument.)
    // From five arguments the function is one closure per argument, with a
    // let between so the compiler doesn't merge them: a merged one is a chain
    // of OptimizedClosures (five arguments each), which a call site that
    // splits the arguments differently applies partially, and bflat's
    // compiler gives up on those partial applications' generic types when
    // they nest deeply (IL3054), with no type loader to make them at run time.
    type PrintfBuilder =
        static member inline Make(_: PrintfBuilder, _: 'Result, state: State<'Result>) : 'Result = state.Finish()

        static member inline Make(_: PrintfBuilder, _: 'A1 -> 'Result, state: State<'Result>) : 'A1 -> 'Result =
            fun (a1: 'A1) -> state.Add(box a1).Finish()

        static member inline Make(_: PrintfBuilder, _: 'A1 -> 'A2 -> 'Result, state: State<'Result>) : 'A1 -> 'A2 -> 'Result =
            fun (a1: 'A1) (a2: 'A2) -> state.Add(box a1).Add(box a2).Finish()

        static member inline Make(_: PrintfBuilder, _: 'A1 -> 'A2 -> 'A3 -> 'Result, state: State<'Result>) : 'A1 -> 'A2 -> 'A3 -> 'Result =
            fun (a1: 'A1) (a2: 'A2) (a3: 'A3) -> state.Add(box a1).Add(box a2).Add(box a3).Finish()

        static member inline Make(_: PrintfBuilder, _: 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'Result, state: State<'Result>) : 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'Result =
            fun (a1: 'A1) (a2: 'A2) (a3: 'A3) (a4: 'A4) -> state.Add(box a1).Add(box a2).Add(box a3).Add(box a4).Finish()

        static member inline Make(_: PrintfBuilder, _: 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'A5 -> 'Result, state: State<'Result>) : 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'A5 -> 'Result =
            fun (a1: 'A1) ->
                let s1 = state.Add(box a1)
                fun (a2: 'A2) ->
                    let s2 = s1.Add(box a2)
                    fun (a3: 'A3) ->
                        let s3 = s2.Add(box a3)
                        fun (a4: 'A4) ->
                            let s4 = s3.Add(box a4)
                            fun (a5: 'A5) -> s4.Add(box a5).Finish()

        static member inline Make(_: PrintfBuilder, _: 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'A5 -> 'A6 -> 'Result, state: State<'Result>) : 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'A5 -> 'A6 -> 'Result =
            fun (a1: 'A1) ->
                let s1 = state.Add(box a1)
                fun (a2: 'A2) ->
                    let s2 = s1.Add(box a2)
                    fun (a3: 'A3) ->
                        let s3 = s2.Add(box a3)
                        fun (a4: 'A4) ->
                            let s4 = s3.Add(box a4)
                            fun (a5: 'A5) ->
                                let s5 = s4.Add(box a5)
                                fun (a6: 'A6) -> s5.Add(box a6).Finish()

        static member inline Make(_: PrintfBuilder, _: 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'A5 -> 'A6 -> 'A7 -> 'Result, state: State<'Result>) : 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'A5 -> 'A6 -> 'A7 -> 'Result =
            fun (a1: 'A1) ->
                let s1 = state.Add(box a1)
                fun (a2: 'A2) ->
                    let s2 = s1.Add(box a2)
                    fun (a3: 'A3) ->
                        let s3 = s2.Add(box a3)
                        fun (a4: 'A4) ->
                            let s4 = s3.Add(box a4)
                            fun (a5: 'A5) ->
                                let s5 = s4.Add(box a5)
                                fun (a6: 'A6) ->
                                    let s6 = s5.Add(box a6)
                                    fun (a7: 'A7) -> s6.Add(box a7).Finish()

        static member inline Make(_: PrintfBuilder, _: 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'A5 -> 'A6 -> 'A7 -> 'A8 -> 'Result, state: State<'Result>) : 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'A5 -> 'A6 -> 'A7 -> 'A8 -> 'Result =
            fun (a1: 'A1) ->
                let s1 = state.Add(box a1)
                fun (a2: 'A2) ->
                    let s2 = s1.Add(box a2)
                    fun (a3: 'A3) ->
                        let s3 = s2.Add(box a3)
                        fun (a4: 'A4) ->
                            let s4 = s3.Add(box a4)
                            fun (a5: 'A5) ->
                                let s5 = s4.Add(box a5)
                                fun (a6: 'A6) ->
                                    let s6 = s5.Add(box a6)
                                    fun (a7: 'A7) ->
                                        let s7 = s6.Add(box a7)
                                        fun (a8: 'A8) -> s7.Add(box a8).Finish()

        static member inline Make(_: PrintfBuilder, _: 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'A5 -> 'A6 -> 'A7 -> 'A8 -> 'A9 -> 'Result, state: State<'Result>) : 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'A5 -> 'A6 -> 'A7 -> 'A8 -> 'A9 -> 'Result =
            fun (a1: 'A1) ->
                let s1 = state.Add(box a1)
                fun (a2: 'A2) ->
                    let s2 = s1.Add(box a2)
                    fun (a3: 'A3) ->
                        let s3 = s2.Add(box a3)
                        fun (a4: 'A4) ->
                            let s4 = s3.Add(box a4)
                            fun (a5: 'A5) ->
                                let s5 = s4.Add(box a5)
                                fun (a6: 'A6) ->
                                    let s6 = s5.Add(box a6)
                                    fun (a7: 'A7) ->
                                        let s7 = s6.Add(box a7)
                                        fun (a8: 'A8) ->
                                            let s8 = s7.Add(box a8)
                                            fun (a9: 'A9) -> s8.Add(box a9).Finish()

        static member inline Make(_: PrintfBuilder, _: 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'A5 -> 'A6 -> 'A7 -> 'A8 -> 'A9 -> 'A10 -> 'Result, state: State<'Result>) : 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'A5 -> 'A6 -> 'A7 -> 'A8 -> 'A9 -> 'A10 -> 'Result =
            fun (a1: 'A1) ->
                let s1 = state.Add(box a1)
                fun (a2: 'A2) ->
                    let s2 = s1.Add(box a2)
                    fun (a3: 'A3) ->
                        let s3 = s2.Add(box a3)
                        fun (a4: 'A4) ->
                            let s4 = s3.Add(box a4)
                            fun (a5: 'A5) ->
                                let s5 = s4.Add(box a5)
                                fun (a6: 'A6) ->
                                    let s6 = s5.Add(box a6)
                                    fun (a7: 'A7) ->
                                        let s7 = s6.Add(box a7)
                                        fun (a8: 'A8) ->
                                            let s8 = s7.Add(box a8)
                                            fun (a9: 'A9) ->
                                                let s9 = s8.Add(box a9)
                                                fun (a10: 'A10) -> s9.Add(box a10).Finish()

        static member inline Make(_: PrintfBuilder, _: 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'A5 -> 'A6 -> 'A7 -> 'A8 -> 'A9 -> 'A10 -> 'A11 -> 'Result, state: State<'Result>) : 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'A5 -> 'A6 -> 'A7 -> 'A8 -> 'A9 -> 'A10 -> 'A11 -> 'Result =
            fun (a1: 'A1) ->
                let s1 = state.Add(box a1)
                fun (a2: 'A2) ->
                    let s2 = s1.Add(box a2)
                    fun (a3: 'A3) ->
                        let s3 = s2.Add(box a3)
                        fun (a4: 'A4) ->
                            let s4 = s3.Add(box a4)
                            fun (a5: 'A5) ->
                                let s5 = s4.Add(box a5)
                                fun (a6: 'A6) ->
                                    let s6 = s5.Add(box a6)
                                    fun (a7: 'A7) ->
                                        let s7 = s6.Add(box a7)
                                        fun (a8: 'A8) ->
                                            let s8 = s7.Add(box a8)
                                            fun (a9: 'A9) ->
                                                let s9 = s8.Add(box a9)
                                                fun (a10: 'A10) ->
                                                    let s10 = s9.Add(box a10)
                                                    fun (a11: 'A11) -> s10.Add(box a11).Finish()

        static member inline Make(_: PrintfBuilder, _: 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'A5 -> 'A6 -> 'A7 -> 'A8 -> 'A9 -> 'A10 -> 'A11 -> 'A12 -> 'Result, state: State<'Result>) : 'A1 -> 'A2 -> 'A3 -> 'A4 -> 'A5 -> 'A6 -> 'A7 -> 'A8 -> 'A9 -> 'A10 -> 'A11 -> 'A12 -> 'Result =
            fun (a1: 'A1) ->
                let s1 = state.Add(box a1)
                fun (a2: 'A2) ->
                    let s2 = s1.Add(box a2)
                    fun (a3: 'A3) ->
                        let s3 = s2.Add(box a3)
                        fun (a4: 'A4) ->
                            let s4 = s3.Add(box a4)
                            fun (a5: 'A5) ->
                                let s5 = s4.Add(box a5)
                                fun (a6: 'A6) ->
                                    let s6 = s5.Add(box a6)
                                    fun (a7: 'A7) ->
                                        let s7 = s6.Add(box a7)
                                        fun (a8: 'A8) ->
                                            let s8 = s7.Add(box a8)
                                            fun (a9: 'A9) ->
                                                let s9 = s8.Add(box a9)
                                                fun (a10: 'A10) ->
                                                    let s10 = s9.Add(box a10)
                                                    fun (a11: 'A11) ->
                                                        let s11 = s10.Add(box a11)
                                                        fun (a12: 'A12) -> s11.Add(box a12).Finish()

        // ^U, the format's argument types as a tuple, is known only once the
        // compiler has read the format string: with it in the constraint, the
        // overload isn't picked before (as when fsc first sees that the result
        // is applied to arguments, and would take the one-argument overload).
        // ^U, the format's argument types as a tuple, is known only once the
        // compiler has read the format string: with it in the constraint, the
        // overload isn't picked before that (fsc first sees that the result is
        // applied to arguments, and would take the one-argument overload).
        // In FSharp.Core this crashes fsc 10 when a program's own inline
        // function wraps printf (FS0193 while writing its optimization data),
        // hence this library.
        static member inline Invoke(format: PrintfFormat< ^T, 'State, 'Residue, 'Result, ^U>, finish: string -> 'Result) : ^T =
            let state = State(format.Value, format.Captures, finish, [])
            let inline call (w: ^W) (t: ^T) (_: PrintfFormat< ^T, 'State, 'Residue, 'Result, ^U>) =
                ((^W or ^T or ^U): (static member Make: ^W * ^T * State<'Result> -> ^T) (w, t, state))
            call Unchecked.defaultof<PrintfBuilder> Unchecked.defaultof< ^T> format

    [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
    module Printf =

        type StringFormat<'T, 'Result> = Format<'T, unit, string, 'Result>
        type StringFormat<'T> = StringFormat<'T, string>
        /// printf's format: zero mode has no TextWriter, so the state is unit.
        type TextWriterFormat<'T, 'Result> = Format<'T, unit, unit, 'Result>
        type TextWriterFormat<'T> = TextWriterFormat<'T, unit>

        /// Formats, then gives the text to the continuation.
        [<CompiledName("PrintFormatToStringThen")>]
        let inline ksprintf (continuation: string -> 'Result) (format: PrintfFormat< ^T, unit, string, 'Result, ^U>) : ^T =
            PrintfBuilder.Invoke(format, continuation)

        [<CompiledName("PrintFormatToStringThenFail")>]
        let inline failwithf (format: PrintfFormat< ^T, unit, string, 'Result, ^U>) : ^T =
            PrintfBuilder.Invoke(format, (fun (s: string) -> raise (Exception(s))))

        [<CompiledName("PrintFormatToStringThenIdentity")>]
        let inline sprintf (format: PrintfFormat< ^T, unit, string, string, ^U>) : ^T =
            PrintfBuilder.Invoke(format, (fun (s: string) -> s))

        let inline kprintf (continuation: string -> 'Result) (format: PrintfFormat< ^T, unit, string, 'Result, ^U>) : ^T =
            ksprintf continuation format

        /// Standard output, through lib/'s Out (so it keeps its order with
        /// Out's own writes), shown at once as in .NET: printf "Working... "
        /// is on the screen before the work starts.
        let writeOut (text: string) =
            Out.Write(text)
            Out.Flush()
        let writeOutLine (text: string) = Out.Line(text)

        [<System.Runtime.InteropServices.DllImport("libc")>]
        extern nativeint private write(int fd, byte* buffer, unativeint count)

        /// Standard error, as UTF-8, after what Out holds.
        let writeError (text: string) =
            Out.Flush()
            let bytes = (Array.zeroCreate (text.Length * 3 + 1) : byte array)
            let mutable n = 0
            let mutable i = 0
            while i < text.Length do
                let c = int (text.Chars(i))
                if c < 0x80 then
                    bytes.[n] <- byte c; n <- n + 1
                elif c < 0x800 then
                    bytes.[n] <- byte (0xC0 ||| (c >>> 6)); bytes.[n + 1] <- byte (0x80 ||| (c &&& 0x3F)); n <- n + 2
                elif c >= 0xD800 && c <= 0xDBFF && i + 1 < text.Length && int (text.Chars(i + 1)) >= 0xDC00 && int (text.Chars(i + 1)) <= 0xDFFF then
                    let cp = 0x10000 + ((c - 0xD800) <<< 10) + (int (text.Chars(i + 1)) - 0xDC00)
                    bytes.[n] <- byte (0xF0 ||| (cp >>> 18)); bytes.[n + 1] <- byte (0x80 ||| ((cp >>> 12) &&& 0x3F))
                    bytes.[n + 2] <- byte (0x80 ||| ((cp >>> 6) &&& 0x3F)); bytes.[n + 3] <- byte (0x80 ||| (cp &&& 0x3F))
                    n <- n + 4
                    i <- i + 1
                else
                    bytes.[n] <- byte (0xE0 ||| (c >>> 12)); bytes.[n + 1] <- byte (0x80 ||| ((c >>> 6) &&& 0x3F))
                    bytes.[n + 2] <- byte (0x80 ||| (c &&& 0x3F)); n <- n + 3
                i <- i + 1
            if n > 0 then
                use p = fixed bytes
                write(2, p, unativeint n) |> ignore

        let writeErrorLine (text: string) = writeError (text + "\n")

        [<CompiledName("PrintFormatToConsole")>]
        let inline printf (format: PrintfFormat< ^T, unit, unit, unit, ^U>) : ^T =
            PrintfBuilder.Invoke(format, writeOut)

        [<CompiledName("PrintFormatLineToConsole")>]
        let inline printfn (format: PrintfFormat< ^T, unit, unit, unit, ^U>) : ^T =
            PrintfBuilder.Invoke(format, writeOutLine)

        [<CompiledName("PrintFormatToError")>]
        let inline eprintf (format: PrintfFormat< ^T, unit, unit, unit, ^U>) : ^T =
            PrintfBuilder.Invoke(format, writeError)

        [<CompiledName("PrintFormatLineToError")>]
        let inline eprintfn (format: PrintfFormat< ^T, unit, unit, unit, ^U>) : ^T =
            PrintfBuilder.Invoke(format, writeErrorLine)

    [<AutoOpen>]
    module PrintfOperators =
        [<CompiledName("PrintFormatToStringThenIdentity")>]
        let inline sprintf (format: PrintfFormat< ^T, unit, string, string, ^U>) : ^T = Printf.sprintf format

        [<CompiledName("PrintFormatToStringThenFail")>]
        let inline failwithf (format: PrintfFormat< ^T, unit, string, 'Result, ^U>) : ^T = Printf.failwithf format

        [<CompiledName("PrintFormatToConsole")>]
        let inline printf (format: PrintfFormat< ^T, unit, unit, unit, ^U>) : ^T = Printf.printf format

        [<CompiledName("PrintFormatLineToConsole")>]
        let inline printfn (format: PrintfFormat< ^T, unit, unit, unit, ^U>) : ^T = Printf.printfn format

        [<CompiledName("PrintFormatToError")>]
        let inline eprintf (format: PrintfFormat< ^T, unit, unit, unit, ^U>) : ^T = Printf.eprintf format

        [<CompiledName("PrintFormatLineToError")>]
        let inline eprintfn (format: PrintfFormat< ^T, unit, unit, unit, ^U>) : ^T = Printf.eprintfn format

namespace Microsoft.FSharp.Core

    open System

    /// lib/'s Catch.Run (lib/Catch.cs) as F# would have it. A raise, failwith
    /// or any other throw inside comes back as Error; nothing between it and
    /// here runs on the way, not even try/with or try/finally, but files and
    /// sockets opened inside and left open are closed.
    ///
    ///   match Catch.result (fun () -> download url path) with
    ///   | Ok size -> printfn "%d bytes" size
    ///   | Error (:? IOException as e) -> printfn "couldn't: %s" e.Message
    ///   | Error e -> raise e
    ///
    /// Catch.Run itself works from F# as well: it returns the exception, or
    /// null.
    [<RequireQualifiedAccess>]
    [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
    module Catch =
        /// Runs f: Ok with what it returns, or Error with what it throws.
        [<CompiledName("Result")>]
        let result (f: unit -> 'T) : Result<'T, exn> =
            let mutable value = Unchecked.defaultof<'T>
            match global.Catch.Run(Func<'T>(f), &value) with
            | null -> Ok value
            | e -> Error e

namespace Microsoft.FSharp
    // After FSharp.Core's: these printf functions are the ones programs see.
    // The namespace too, for Printf.ksprintf: FSharp.Core's opens only its own
    // part of it.
    [<assembly: AutoOpen("Microsoft.FSharp.Core")>]
    [<assembly: AutoOpen("Microsoft.FSharp.Core.PrintfOperators")>]
    do ()
