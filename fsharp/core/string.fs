// FSharp.Core's String module, over zerocore's string and StringBuilder. A
// null string acts as an empty one, as in FSharp.Core.
#nowarn "35" "42" "44" "3370"

namespace Microsoft.FSharp.Core

    open System
    open System.Text
    open Microsoft.FSharp.Core
    open Microsoft.FSharp.Core.Operators
    open Microsoft.FSharp.Core.LanguagePrimitives.IntrinsicOperators
    open Microsoft.FSharp.Collections

    [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
    [<RequireQualifiedAccess>]
    module String =

        let inline private emptyIfNull (str: string) = match str with null -> "" | _ -> str

        [<CompiledName("Length")>]
        let length (str: string) = match str with null -> 0 | _ -> str.Length

        [<CompiledName("Concat")>]
        let concat (sep: string) (strings: seq<string>) =
            let sep = emptyIfNull sep
            let b = StringBuilder()
            let mutable first = true
            for s in strings do
                if not first then b.Append(sep) |> ignore
                b.Append(emptyIfNull s) |> ignore
                first <- false
            b.ToString()

        [<CompiledName("Iterate")>]
        let iter (action: char -> unit) (str: string) =
            let str = emptyIfNull str
            for i in 0 .. str.Length - 1 do action str.[i]

        [<CompiledName("IterateIndexed")>]
        let iteri (action: int -> char -> unit) (str: string) =
            let str = emptyIfNull str
            for i in 0 .. str.Length - 1 do action i str.[i]

        [<CompiledName("Map")>]
        let map (mapping: char -> char) (str: string) =
            let str = emptyIfNull str
            let b = StringBuilder()
            for i in 0 .. str.Length - 1 do b.Append(mapping str.[i]) |> ignore
            b.ToString()

        [<CompiledName("MapIndexed")>]
        let mapi (mapping: int -> char -> char) (str: string) =
            let str = emptyIfNull str
            let b = StringBuilder()
            for i in 0 .. str.Length - 1 do b.Append(mapping i str.[i]) |> ignore
            b.ToString()

        [<CompiledName("Filter")>]
        let filter (predicate: char -> bool) (str: string) =
            let str = emptyIfNull str
            let b = StringBuilder()
            for i in 0 .. str.Length - 1 do
                let c = str.[i]
                if predicate c then b.Append(c) |> ignore
            b.ToString()

        [<CompiledName("Collect")>]
        let collect (mapping: char -> string) (str: string) =
            let str = emptyIfNull str
            let b = StringBuilder()
            for i in 0 .. str.Length - 1 do b.Append(emptyIfNull (mapping str.[i])) |> ignore
            b.ToString()

        [<CompiledName("Initialize")>]
        let init (count: int) (initializer: int -> string) =
            if count < 0 then invalidArg "count" "The input must be non-negative."
            let b = StringBuilder()
            for i in 0 .. count - 1 do b.Append(emptyIfNull (initializer i)) |> ignore
            b.ToString()

        [<CompiledName("Replicate")>]
        let replicate (count: int) (str: string) =
            if count < 0 then invalidArg "count" "The input must be non-negative."
            let str = emptyIfNull str
            let b = StringBuilder()
            for _ in 1 .. count do b.Append(str) |> ignore
            b.ToString()

        [<CompiledName("ForAll")>]
        let forall (predicate: char -> bool) (str: string) =
            let str = emptyIfNull str
            let mutable i = 0
            while i < str.Length && predicate str.[i] do i <- i + 1
            i = str.Length

        [<CompiledName("Exists")>]
        let exists (predicate: char -> bool) (str: string) =
            let str = emptyIfNull str
            let mutable i = 0
            while i < str.Length && not (predicate str.[i]) do i <- i + 1
            i < str.Length
