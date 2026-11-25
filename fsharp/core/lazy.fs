// `lazy expr`, Lazy<'T> and Lazy.Force, as in FSharp.Core's lazy.fs.
// The compiler turns `lazy expr` into LazyExtensions.Create (fun () -> expr)
// and finds System.Lazy`1 in zerocore (zerocore/System/Lazy.cs), which, unlike
// .NET's default, isn't thread-safe.
#nowarn "35" "42" "44" "3370"

namespace Microsoft.FSharp.Control

    open System
    open Microsoft.FSharp.Core
    open Microsoft.FSharp.Core.Operators

    type Lazy<'T> = System.Lazy<'T>

    and [<CompiledName("Lazy`1")>] 'T ``lazy`` = Lazy<'T>

    [<AutoOpen>]
    module LazyExtensions =

        type System.Lazy<'T> with

            [<CompiledName("Create")>]
            static member Create(creator: unit -> 'T) : Lazy<'T> =
                System.Lazy<'T>(Func<'T>(creator))

            [<CompiledName("CreateFromValue")>]
            static member CreateFromValue(value: 'T) : Lazy<'T> =
                System.Lazy<'T>(value)

            [<CompiledName("Force")>]
            member x.Force() = x.Value

        /// Forces a lazy value and matches what it gives.
        [<CompiledName("LazyPattern")>]
        let (|Lazy|) (input: Lazy<'T>) = input.Force()
