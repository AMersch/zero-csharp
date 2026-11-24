// The Seq module: FSharp.Core's names and behaviour (lazy where FSharp.Core
// is lazy), written for zero mode: no reflection, no catchable exceptions,
// and nothing is ever freed, so each enumeration allocates its enumerator.
#nowarn "35" "42" "44" "3370"

namespace Microsoft.FSharp.Collections

    open System
    open System.Collections
    open System.Collections.Generic
    open Microsoft.FSharp.Core
    open Microsoft.FSharp.Core.Operators
    open Microsoft.FSharp.Core.LanguagePrimitives.IntrinsicOperators
    open Microsoft.FSharp.Core.CompilerServices
    open Microsoft.FSharp.Primitives.Basics

    module internal SeqImpl =

        // An enumerator from a step function, which gives the next item or
        // ValueNone at the end.
        [<Sealed>]
        type StepEnumerator<'T>(step: unit -> 'T voption, dispose: unit -> unit) =
            let mutable started = false
            let mutable finished = false
            let mutable current = Unchecked.defaultof<'T>
            member _.Get() =
                IEnumerator.check started
                if finished then IEnumerator.alreadyFinished ()
                current
            interface IEnumerator<'T> with
                member x.Current = x.Get()
            interface IEnumerator with
                member x.Current = box (x.Get())
                member _.MoveNext() =
                    started <- true
                    if finished then false
                    else
                        match step () with
                        | ValueSome x ->
                            current <- x
                            true
                        | ValueNone ->
                            finished <- true
                            dispose ()
                            false
                member _.Reset() = IEnumerator.noReset ()
            interface IDisposable with
                member _.Dispose() =
                    if not finished then
                        finished <- true
                        dispose ()

        let inline noDispose () = ()

        let inline enumerator (step: unit -> 'T voption) dispose =
            new StepEnumerator<'T>(step, dispose) :> IEnumerator<'T>

        let inline checkNonNull (argName: string) (arg: 'T) =
            match box arg with
            | null -> nullArg argName
            | _ -> ()

        let invalidArgInputMustBeNonNegative (name: string) (count: int) =
            invalidArg name ("The input must be non-negative. " + name + " = " + string count)

        let emptyInput () = invalidArg "source" "The input sequence was empty."
        let notEnough () = invalidArg "source" "The input sequence has an insufficient number of elements."
        let notFound () = raise (ArgumentException("An index satisfying the predicate was not found in the collection."))

        let toArray (source: seq<'T>) : 'T array =
            match source with
            | :? ('T array) as a -> Array.copy a
            | _ ->
                let b = Buffer<'T>()
                for x in source do b.Add x
                b.ToArray()

    [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
    [<RequireQualifiedAccess>]
    module Seq =

        open SeqImpl

        [<CompiledName("Delay")>]
        let delay (generator: unit -> seq<'T>) : seq<'T> =
            IEnumerator.mkSeq (fun () -> generator().GetEnumerator())

        [<CompiledName("Empty")>]
        let empty<'T> : seq<'T> = IEnumerator.mkSeq (fun () -> IEnumerator.Empty<'T>())

        [<CompiledName("Singleton")>]
        let singleton (value: 'T) : seq<'T> = IEnumerator.mkSeq (fun () -> IEnumerator.Singleton value)

        [<CompiledName("Unfold")>]
        let unfold (generator: 'State -> ('T * 'State) option) (state: 'State) : seq<'T> =
            IEnumerator.mkSeq (fun () ->
                let s = ref state
                enumerator (fun () ->
                    match generator s.Value with
                    | Some(x, next) ->
                        s.Value <- next
                        ValueSome x
                    | None -> ValueNone) noDispose)

        [<CompiledName("InitializeInfinite")>]
        let initInfinite (initializer: int -> 'T) : seq<'T> =
            IEnumerator.mkSeq (fun () ->
                let i = ref 0
                enumerator (fun () ->
                    let x = initializer i.Value
                    i.Value <- i.Value + 1
                    ValueSome x) noDispose)

        [<CompiledName("Initialize")>]
        let init (count: int) (initializer: int -> 'T) : seq<'T> =
            if count < 0 then invalidArgInputMustBeNonNegative "count" count
            IEnumerator.mkSeq (fun () ->
                let i = ref 0
                enumerator (fun () ->
                    if i.Value < count then
                        let x = initializer i.Value
                        i.Value <- i.Value + 1
                        ValueSome x
                    else ValueNone) noDispose)

        [<CompiledName("Replicate")>]
        let replicate (count: int) (initial: 'T) : seq<'T> = init count (fun _ -> initial)

        [<CompiledName("Append")>]
        let append (source1: seq<'T>) (source2: seq<'T>) : seq<'T> =
            checkNonNull "source1" source1
            checkNonNull "source2" source2
            RuntimeHelpers.mkConcatSeq [ source1; source2 ]

        [<CompiledName("Concat")>]
        let concat (sources: seq<#seq<'T>>) : seq<'T> =
            checkNonNull "sources" sources
            RuntimeHelpers.mkConcatSeq sources

        [<CompiledName("Map")>]
        let map (mapping: 'T -> 'U) (source: seq<'T>) : seq<'U> =
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () ->
                let e = source.GetEnumerator()
                enumerator (fun () ->
                    if e.MoveNext() then
                        ValueSome(mapping e.Current)
                    else ValueNone) e.Dispose)

        [<CompiledName("Collect")>]
        let collect (mapping: 'T -> 'Collection) (source: seq<'T>) : seq<'U> when 'Collection :> seq<'U> =
            RuntimeHelpers.mkConcatSeq (map mapping source)

        [<CompiledName("MapIndexed")>]
        let mapi (mapping: int -> 'T -> 'U) (source: seq<'T>) : seq<'U> =
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () ->
                let e = source.GetEnumerator()
                let i = ref 0
                enumerator (fun () ->
                    if e.MoveNext() then
                        let x = mapping i.Value e.Current
                        i.Value <- i.Value + 1
                        ValueSome x
                    else ValueNone) e.Dispose)

        [<CompiledName("Map2")>]
        let map2 (mapping: 'T1 -> 'T2 -> 'U) (source1: seq<'T1>) (source2: seq<'T2>) : seq<'U> =
            checkNonNull "source1" source1
            checkNonNull "source2" source2
            IEnumerator.mkSeq (fun () ->
                let e1 = source1.GetEnumerator()
                let e2 = source2.GetEnumerator()
                enumerator (fun () ->
                    if e1.MoveNext() && e2.MoveNext() then
                        ValueSome(mapping e1.Current e2.Current)
                    else ValueNone) (fun () -> try e1.Dispose() finally e2.Dispose()))

        [<CompiledName("Indexed")>]
        let indexed (source: seq<'T>) : seq<int * 'T> = mapi (fun i x -> i, x) source

        [<CompiledName("Zip")>]
        let zip (source1: seq<'T1>) (source2: seq<'T2>) : seq<'T1 * 'T2> = map2 (fun x y -> x, y) source1 source2

        [<CompiledName("Zip3")>]
        let zip3 (source1: seq<'T1>) (source2: seq<'T2>) (source3: seq<'T3>) : seq<'T1 * 'T2 * 'T3> =
            map2 (fun x (y, z) -> x, y, z) source1 (zip source2 source3)

        [<CompiledName("Choose")>]
        let choose (chooser: 'T -> 'U option) (source: seq<'T>) : seq<'U> =
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () ->
                let e = source.GetEnumerator()
                enumerator (fun () ->
                    let mutable res = ValueNone
                    while res.IsNone && e.MoveNext() do
                        match chooser e.Current with
                        | Some v ->
                            res <- ValueSome v
                        | None -> ()
                    res) e.Dispose)

        [<CompiledName("Filter")>]
        let filter (predicate: 'T -> bool) (source: seq<'T>) : seq<'T> =
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () ->
                let e = source.GetEnumerator()
                enumerator (fun () ->
                    let mutable res = ValueNone
                    while res.IsNone && e.MoveNext() do
                        let x = e.Current
                        if predicate x then
                            res <- ValueSome x
                    res) e.Dispose)

        [<CompiledName("Where")>]
        let where (predicate: 'T -> bool) (source: seq<'T>) : seq<'T> = filter predicate source

        [<CompiledName("Iterate")>]
        let iter (action: 'T -> unit) (source: seq<'T>) =
            checkNonNull "source" source
            for x in source do action x

        [<CompiledName("IterateIndexed")>]
        let iteri (action: int -> 'T -> unit) (source: seq<'T>) =
            checkNonNull "source" source
            let mutable i = 0
            for x in source do
                action i x
                i <- i + 1

        [<CompiledName("Iterate2")>]
        let iter2 (action: 'T1 -> 'T2 -> unit) (source1: seq<'T1>) (source2: seq<'T2>) =
            checkNonNull "source1" source1
            checkNonNull "source2" source2
            use e1 = source1.GetEnumerator()
            use e2 = source2.GetEnumerator()
            while e1.MoveNext() && e2.MoveNext() do
                action e1.Current e2.Current

        [<CompiledName("Fold")>]
        let fold<'T, 'State> (folder: 'State -> 'T -> 'State) (state: 'State) (source: seq<'T>) =
            checkNonNull "source" source
            let mutable acc = state
            for x in source do acc <- folder acc x
            acc

        [<CompiledName("Fold2")>]
        let fold2<'T1, 'T2, 'State> (folder: 'State -> 'T1 -> 'T2 -> 'State) (state: 'State) (source1: seq<'T1>) (source2: seq<'T2>) =
            checkNonNull "source1" source1
            checkNonNull "source2" source2
            use e1 = source1.GetEnumerator()
            use e2 = source2.GetEnumerator()
            let mutable acc = state
            while e1.MoveNext() && e2.MoveNext() do
                acc <- folder acc e1.Current e2.Current
            acc

        [<CompiledName("Reduce")>]
        let reduce (reduction: 'T -> 'T -> 'T) (source: seq<'T>) =
            checkNonNull "source" source
            use e = source.GetEnumerator()
            if not (e.MoveNext()) then emptyInput ()
            let mutable acc = e.Current
            while e.MoveNext() do
                acc <- reduction acc e.Current
            acc

        [<CompiledName("Scan")>]
        let scan<'T, 'State> (folder: 'State -> 'T -> 'State) (state: 'State) (source: seq<'T>) : seq<'State> =
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () ->
                let e = source.GetEnumerator()
                let acc = ref state
                let first = ref true
                enumerator (fun () ->
                    if first.Value then
                        first.Value <- false
                        ValueSome acc.Value
                    elif e.MoveNext() then
                        acc.Value <- folder acc.Value e.Current
                        ValueSome acc.Value
                    else ValueNone) e.Dispose)

        [<CompiledName("ToArray")>]
        let toArray (source: seq<'T>) : 'T array =
            checkNonNull "source" source
            SeqImpl.toArray source

        [<CompiledName("OfArray")>]
        let ofArray (source: 'T array) : seq<'T> =
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () -> (source :> seq<'T>).GetEnumerator())

        [<CompiledName("ToList")>]
        let toList (source: seq<'T>) : 'T list =
            checkNonNull "source" source
            match source with
            | :? ('T list) as l -> l
            | _ ->
                let mutable c = ListCollector<'T>()
                for x in source do c.Add x
                c.Close()

        [<CompiledName("OfList")>]
        let ofList (source: 'T list) : seq<'T> = (source :> seq<'T>)

        [<CompiledName("FoldBack")>]
        let foldBack<'T, 'State> (folder: 'T -> 'State -> 'State) (source: seq<'T>) (state: 'State) =
            checkNonNull "source" source
            let items = SeqImpl.toArray source
            let mutable acc = state
            for i in Array.length items - 1 .. -1 .. 0 do
                acc <- folder items.[i] acc
            acc

        [<CompiledName("ReduceBack")>]
        let reduceBack (reduction: 'T -> 'T -> 'T) (source: seq<'T>) =
            checkNonNull "source" source
            let items = SeqImpl.toArray source
            let n = Array.length items
            if n = 0 then emptyInput ()
            let mutable acc = items.[n - 1]
            for i in n - 2 .. -1 .. 0 do
                acc <- reduction items.[i] acc
            acc

        [<CompiledName("Length")>]
        let length (source: seq<'T>) =
            checkNonNull "source" source
            match source with
            | :? ('T array) as a -> Array.length a
            | :? ('T list) as l -> l.Length
            | _ ->
                use e = source.GetEnumerator()
                let mutable n = 0
                while e.MoveNext() do n <- n + 1
                n

        [<CompiledName("IsEmpty")>]
        let isEmpty (source: seq<'T>) =
            checkNonNull "source" source
            match source with
            | :? ('T array) as a -> Array.length a = 0
            | :? ('T list) as l -> l.IsEmpty
            | _ ->
                use e = source.GetEnumerator()
                not (e.MoveNext())

        [<CompiledName("Head")>]
        let head (source: seq<'T>) =
            checkNonNull "source" source
            use e = source.GetEnumerator()
            if e.MoveNext() then e.Current else emptyInput ()

        [<CompiledName("TryHead")>]
        let tryHead (source: seq<'T>) =
            checkNonNull "source" source
            use e = source.GetEnumerator()
            if e.MoveNext() then Some e.Current else None

        [<CompiledName("Last")>]
        let last (source: seq<'T>) =
            checkNonNull "source" source
            use e = source.GetEnumerator()
            if not (e.MoveNext()) then emptyInput ()
            let mutable res = e.Current
            while e.MoveNext() do res <- e.Current
            res

        [<CompiledName("TryLast")>]
        let tryLast (source: seq<'T>) =
            checkNonNull "source" source
            use e = source.GetEnumerator()
            if e.MoveNext() then
                let mutable res = e.Current
                while e.MoveNext() do res <- e.Current
                Some res
            else None

        [<CompiledName("ExactlyOne")>]
        let exactlyOne (source: seq<'T>) =
            checkNonNull "source" source
            use e = source.GetEnumerator()
            if not (e.MoveNext()) then emptyInput ()
            let v = e.Current
            if e.MoveNext() then invalidArg "source" "The input sequence contains more than one element."
            v

        [<CompiledName("TryExactlyOne")>]
        let tryExactlyOne (source: seq<'T>) =
            checkNonNull "source" source
            use e = source.GetEnumerator()
            if e.MoveNext() then
                let v = e.Current
                if e.MoveNext() then None else Some v
            else None

        [<CompiledName("TryItem")>]
        let tryItem (index: int) (source: seq<'T>) =
            checkNonNull "source" source
            if index < 0 then None
            else
                use e = source.GetEnumerator()
                let mutable i = 0
                let mutable res = None
                while res.IsNone && e.MoveNext() do
                    if i = index then res <- Some e.Current
                    i <- i + 1
                res

        [<CompiledName("Item")>]
        let item (index: int) (source: seq<'T>) =
            if index < 0 then invalidArgInputMustBeNonNegative "index" index
            match tryItem index source with
            | Some v -> v
            | None -> notEnough ()

        [<CompiledName("Get")>]
        let nth (index: int) (source: seq<'T>) = item index source

        [<CompiledName("Exists")>]
        let exists (predicate: 'T -> bool) (source: seq<'T>) =
            checkNonNull "source" source
            use e = source.GetEnumerator()
            let mutable state = false
            while not state && e.MoveNext() do
                state <- predicate e.Current
            state

        [<CompiledName("ForAll")>]
        let forall (predicate: 'T -> bool) (source: seq<'T>) =
            checkNonNull "source" source
            use e = source.GetEnumerator()
            let mutable state = true
            while state && e.MoveNext() do
                state <- predicate e.Current
            state

        [<CompiledName("Exists2")>]
        let exists2 (predicate: 'T1 -> 'T2 -> bool) (source1: seq<'T1>) (source2: seq<'T2>) =
            use e1 = source1.GetEnumerator()
            use e2 = source2.GetEnumerator()
            let mutable state = false
            while not state && e1.MoveNext() && e2.MoveNext() do
                state <- predicate e1.Current e2.Current
            state

        [<CompiledName("ForAll2")>]
        let forall2 (predicate: 'T1 -> 'T2 -> bool) (source1: seq<'T1>) (source2: seq<'T2>) =
            use e1 = source1.GetEnumerator()
            use e2 = source2.GetEnumerator()
            let mutable state = true
            while state && e1.MoveNext() && e2.MoveNext() do
                state <- predicate e1.Current e2.Current
            state

        [<CompiledName("Contains")>]
        let inline contains (value: 'T) (source: seq<'T>) =
            use e = source.GetEnumerator()
            let mutable state = false
            while not state && e.MoveNext() do
                state <- value = e.Current
            state

        [<CompiledName("TryFind")>]
        let tryFind (predicate: 'T -> bool) (source: seq<'T>) =
            checkNonNull "source" source
            use e = source.GetEnumerator()
            let mutable res = None
            while res.IsNone && e.MoveNext() do
                let c = e.Current
                if predicate c then res <- Some c
            res

        [<CompiledName("Find")>]
        let find (predicate: 'T -> bool) (source: seq<'T>) =
            match tryFind predicate source with
            | Some x -> x
            | None -> notFound ()

        [<CompiledName("TryFindBack")>]
        let tryFindBack (predicate: 'T -> bool) (source: seq<'T>) =
            checkNonNull "source" source
            let mutable res = None
            for x in source do
                if predicate x then res <- Some x
            res

        [<CompiledName("FindBack")>]
        let findBack (predicate: 'T -> bool) (source: seq<'T>) =
            match tryFindBack predicate source with
            | Some x -> x
            | None -> notFound ()

        [<CompiledName("TryFindIndex")>]
        let tryFindIndex (predicate: 'T -> bool) (source: seq<'T>) =
            checkNonNull "source" source
            use e = source.GetEnumerator()
            let mutable res = None
            let mutable i = 0
            while res.IsNone && e.MoveNext() do
                if predicate e.Current then res <- Some i
                i <- i + 1
            res

        [<CompiledName("FindIndex")>]
        let findIndex (predicate: 'T -> bool) (source: seq<'T>) =
            match tryFindIndex predicate source with
            | Some x -> x
            | None -> notFound ()

        [<CompiledName("TryPick")>]
        let tryPick (chooser: 'T -> 'U option) (source: seq<'T>) =
            checkNonNull "source" source
            use e = source.GetEnumerator()
            let mutable res = None
            while res.IsNone && e.MoveNext() do
                res <- chooser e.Current
            res

        [<CompiledName("Pick")>]
        let pick (chooser: 'T -> 'U option) (source: seq<'T>) =
            match tryPick chooser source with
            | Some x -> x
            | None -> notFound ()

        [<CompiledName("Take")>]
        let take (count: int) (source: seq<'T>) : seq<'T> =
            checkNonNull "source" source
            if count < 0 then invalidArgInputMustBeNonNegative "count" count
            IEnumerator.mkSeq (fun () ->
                let e = source.GetEnumerator()
                let i = ref 0
                enumerator (fun () ->
                    if i.Value < count then
                        if not (e.MoveNext()) then notEnough ()
                        i.Value <- i.Value + 1
                        ValueSome e.Current
                    else ValueNone) e.Dispose)

        [<CompiledName("Truncate")>]
        let truncate (count: int) (source: seq<'T>) : seq<'T> =
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () ->
                let e = source.GetEnumerator()
                let i = ref 0
                enumerator (fun () ->
                    if i.Value < count && e.MoveNext() then
                        i.Value <- i.Value + 1
                        ValueSome e.Current
                    else ValueNone) e.Dispose)

        [<CompiledName("Skip")>]
        let skip (count: int) (source: seq<'T>) : seq<'T> =
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () ->
                let e = source.GetEnumerator()
                let skipped = ref false
                enumerator (fun () ->
                    if not skipped.Value then
                        skipped.Value <- true
                        for _ in 1 .. count do
                            if not (e.MoveNext()) then notEnough ()
                    if e.MoveNext() then
                        ValueSome(e.Current)
                    else ValueNone) e.Dispose)

        [<CompiledName("TakeWhile")>]
        let takeWhile (predicate: 'T -> bool) (source: seq<'T>) : seq<'T> =
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () ->
                let e = source.GetEnumerator()
                enumerator (fun () ->
                    if e.MoveNext() && predicate e.Current then
                        ValueSome(e.Current)
                    else ValueNone) e.Dispose)

        [<CompiledName("SkipWhile")>]
        let skipWhile (predicate: 'T -> bool) (source: seq<'T>) : seq<'T> =
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () ->
                let e = source.GetEnumerator()
                let skipping = ref true
                enumerator (fun () ->
                    let mutable res = ValueNone
                    let mutable more = true
                    while res.IsNone && more do
                        if e.MoveNext() then
                            let x = e.Current
                            if not (skipping.Value && predicate x) then
                                skipping.Value <- false
                                res <- ValueSome x
                        else more <- false
                    res) e.Dispose)

        [<CompiledName("Tail")>]
        let tail (source: seq<'T>) : seq<'T> =
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () ->
                let e = source.GetEnumerator()
                let first = ref true
                enumerator (fun () ->
                    if first.Value then
                        first.Value <- false
                        if not (e.MoveNext()) then emptyInput ()
                    if e.MoveNext() then
                        ValueSome(e.Current)
                    else ValueNone) e.Dispose)

        [<CompiledName("Pairwise")>]
        let pairwise (source: seq<'T>) : seq<'T * 'T> =
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () ->
                let e = source.GetEnumerator()
                let started = ref false
                let last = ref Unchecked.defaultof<'T>
                enumerator (fun () ->
                    let ok =
                        if started.Value then true
                        else
                            started.Value <- true
                            if e.MoveNext() then
                                last.Value <- e.Current
                                true
                            else false
                    if ok && e.MoveNext() then
                        let x = e.Current
                        let pair = (last.Value, x)
                        last.Value <- x
                        ValueSome pair
                    else ValueNone) e.Dispose)

        [<CompiledName("Windowed")>]
        let windowed (windowSize: int) (source: seq<'T>) : seq<'T array> =
            checkNonNull "source" source
            if windowSize <= 0 then invalidArg "windowSize" "The input must be positive."
            IEnumerator.mkSeq (fun () ->
                let e = source.GetEnumerator()
                let ring = Array.zeroCreateUnchecked windowSize
                let n = ref 0
                enumerator (fun () ->
                    let mutable ok = true
                    while ok && n.Value < windowSize - 1 do
                        if e.MoveNext() then
                            ring.[n.Value] <- e.Current
                            n.Value <- n.Value + 1
                        else ok <- false
                    if ok && e.MoveNext() then
                        ring.[n.Value % windowSize] <- e.Current
                        n.Value <- n.Value + 1
                        let window = Array.zeroCreateUnchecked windowSize
                        for i in 0 .. windowSize - 1 do
                            window.[i] <- ring.[(n.Value + i) % windowSize]
                        ValueSome(window)
                    else ValueNone) e.Dispose)

        [<CompiledName("ChunkBySize")>]
        let chunkBySize (chunkSize: int) (source: seq<'T>) : seq<'T array> =
            checkNonNull "source" source
            if chunkSize <= 0 then invalidArg "chunkSize" "The input must be positive."
            IEnumerator.mkSeq (fun () ->
                let e = source.GetEnumerator()
                enumerator (fun () ->
                    let b = Buffer<'T>()
                    while b.Count < chunkSize && e.MoveNext() do b.Add e.Current
                    if b.Count > 0 then
                        ValueSome(b.ToArray())
                    else ValueNone) e.Dispose)

        [<CompiledName("Reverse")>]
        let rev (source: seq<'T>) : seq<'T> =
            checkNonNull "source" source
            delay (fun () ->
                let items = SeqImpl.toArray source
                let n = Array.length items
                let r = Array.zeroCreateUnchecked n
                for i in 0 .. n - 1 do r.[i] <- items.[n - 1 - i]
                r :> seq<'T>)

        [<CompiledName("SortWith")>]
        let sortWith (comparer: 'T -> 'T -> int) (source: seq<'T>) : seq<'T> =
            checkNonNull "source" source
            delay (fun () ->
                let items = SeqImpl.toArray source
                Array.stableSortInPlace comparer items
                items :> seq<'T>)

        [<CompiledName("SortBy")>]
        let sortBy (projection: 'T -> 'Key) (source: seq<'T>) : seq<'T> when 'Key: comparison =
            sortWith (fun x y -> compare (projection x) (projection y)) source

        [<CompiledName("Sort")>]
        let sort (source: seq<'T>) : seq<'T> when 'T: comparison = sortWith compare source

        [<CompiledName("SortByDescending")>]
        let inline sortByDescending (projection: 'T -> 'Key) (source: seq<'T>) : seq<'T> =
            sortWith (fun x y -> compare (projection y) (projection x)) source

        [<CompiledName("SortDescending")>]
        let inline sortDescending (source: seq<'T>) : seq<'T> = sortWith (fun x y -> compare y x) source

        [<CompiledName("Distinct")>]
        let distinct (source: seq<'T>) : seq<'T> when 'T: equality =
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () ->
                let seen = HashTable<'T, bool>(LanguagePrimitives.FastGenericEqualityComparer<'T>)
                let e = source.GetEnumerator()
                enumerator (fun () ->
                    let mutable res = ValueNone
                    while res.IsNone && e.MoveNext() do
                        let x = e.Current
                        if seen.Set(x, true) then
                            res <- ValueSome x
                    res) e.Dispose)

        [<CompiledName("DistinctBy")>]
        let distinctBy (projection: 'T -> 'Key) (source: seq<'T>) : seq<'T> when 'Key: equality =
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () ->
                let seen = HashTable<'Key, bool>(LanguagePrimitives.FastGenericEqualityComparer<'Key>)
                let e = source.GetEnumerator()
                enumerator (fun () ->
                    let mutable res = ValueNone
                    while res.IsNone && e.MoveNext() do
                        let x = e.Current
                        if seen.Set(projection x, true) then
                            res <- ValueSome x
                    res) e.Dispose)

        [<CompiledName("Except")>]
        let except (itemsToExclude: seq<'T>) (source: seq<'T>) : seq<'T> when 'T: equality =
            checkNonNull "itemsToExclude" itemsToExclude
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () ->
                let seen = HashTable<'T, bool>(LanguagePrimitives.FastGenericEqualityComparer<'T>)
                for x in itemsToExclude do seen.Set(x, true) |> ignore
                let e = source.GetEnumerator()
                enumerator (fun () ->
                    let mutable res = ValueNone
                    while res.IsNone && e.MoveNext() do
                        let x = e.Current
                        if seen.Set(x, true) then
                            res <- ValueSome x
                    res) e.Dispose)

        [<CompiledName("GroupBy")>]
        let groupBy (projection: 'T -> 'Key) (source: seq<'T>) : seq<'Key * seq<'T>> when 'Key: equality =
            checkNonNull "source" source
            delay (fun () ->
                let groups = HashTable<'Key, Buffer<'T>>(LanguagePrimitives.FastGenericEqualityComparer<'Key>)
                for x in source do
                    let key = projection x
                    let mutable b = Unchecked.defaultof<Buffer<'T>>
                    if not (groups.TryFind(key, &b)) then
                        b <- Buffer<'T>()
                        groups.Set(key, b) |> ignore
                    b.Add x
                let result = Array.zeroCreateUnchecked groups.Count
                for i in 0 .. groups.Count - 1 do
                    result.[i] <- (groups.KeyAt i, (groups.ValueAt i).ToArray() :> seq<'T>)
                result :> seq<'Key * seq<'T>>)

        [<CompiledName("CountBy")>]
        let countBy (projection: 'T -> 'Key) (source: seq<'T>) : seq<'Key * int> when 'Key: equality =
            checkNonNull "source" source
            delay (fun () ->
                let counts = HashTable<'Key, int>(LanguagePrimitives.FastGenericEqualityComparer<'Key>)
                for x in source do
                    let key = projection x
                    let mutable n = 0
                    counts.TryFind(key, &n) |> ignore
                    counts.Set(key, n + 1) |> ignore
                let result = Array.zeroCreateUnchecked counts.Count
                for i in 0 .. counts.Count - 1 do
                    result.[i] <- (counts.KeyAt i, counts.ValueAt i)
                result :> seq<'Key * int>)

        [<CompiledName("Sum")>]
        let inline sum (source: seq< ^T >) : ^T =
            use e = source.GetEnumerator()
            let mutable acc = LanguagePrimitives.GenericZero< ^T>
            while e.MoveNext() do
                acc <- acc + e.Current
            acc

        [<CompiledName("SumBy")>]
        let inline sumBy ([<InlineIfLambda>] projection: 'T -> ^U) (source: seq<'T>) : ^U =
            use e = source.GetEnumerator()
            let mutable acc = LanguagePrimitives.GenericZero< ^U>
            while e.MoveNext() do
                acc <- acc + projection e.Current
            acc

        [<CompiledName("Average")>]
        let inline average (source: seq< ^T >) : ^T =
            use e = source.GetEnumerator()
            let mutable acc = LanguagePrimitives.GenericZero< ^T>
            let mutable count = 0
            while e.MoveNext() do
                acc <- acc + e.Current
                count <- count + 1
            if count = 0 then invalidArg "source" "The input sequence was empty."
            LanguagePrimitives.DivideByInt< ^T> acc count

        [<CompiledName("AverageBy")>]
        let inline averageBy ([<InlineIfLambda>] projection: 'T -> ^U) (source: seq<'T>) : ^U =
            use e = source.GetEnumerator()
            let mutable acc = LanguagePrimitives.GenericZero< ^U>
            let mutable count = 0
            while e.MoveNext() do
                acc <- acc + projection e.Current
                count <- count + 1
            if count = 0 then invalidArg "source" "The input sequence was empty."
            LanguagePrimitives.DivideByInt< ^U> acc count

        [<CompiledName("Min")>]
        let inline min (source: seq<'T>) =
            use e = source.GetEnumerator()
            if not (e.MoveNext()) then invalidArg "source" "The input sequence was empty."
            let mutable acc = e.Current
            while e.MoveNext() do
                let curr = e.Current
                if curr < acc then acc <- curr
            acc

        [<CompiledName("MinBy")>]
        let inline minBy ([<InlineIfLambda>] projection: 'T -> 'U) (source: seq<'T>) : 'T =
            use e = source.GetEnumerator()
            if not (e.MoveNext()) then invalidArg "source" "The input sequence was empty."
            let first = e.Current
            let mutable acc = first
            let mutable accv = projection first
            while e.MoveNext() do
                let currv = projection e.Current
                if currv < accv then
                    acc <- e.Current
                    accv <- currv
            acc

        [<CompiledName("Max")>]
        let inline max (source: seq<'T>) =
            use e = source.GetEnumerator()
            if not (e.MoveNext()) then invalidArg "source" "The input sequence was empty."
            let mutable acc = e.Current
            while e.MoveNext() do
                let curr = e.Current
                if curr > acc then acc <- curr
            acc

        [<CompiledName("MaxBy")>]
        let inline maxBy ([<InlineIfLambda>] projection: 'T -> 'U) (source: seq<'T>) : 'T =
            use e = source.GetEnumerator()
            if not (e.MoveNext()) then invalidArg "source" "The input sequence was empty."
            let first = e.Current
            let mutable acc = first
            let mutable accv = projection first
            while e.MoveNext() do
                let currv = projection e.Current
                if currv > accv then
                    acc <- e.Current
                    accv <- currv
            acc

        [<CompiledName("CompareWith")>]
        let compareWith (comparer: 'T -> 'T -> int) (source1: seq<'T>) (source2: seq<'T>) : int =
            use e1 = source1.GetEnumerator()
            use e2 = source2.GetEnumerator()
            let mutable res = 0
            let mutable go = true
            while go do
                let m1 = e1.MoveNext()
                let m2 = e2.MoveNext()
                if not m1 then
                    res <- (if m2 then -1 else 0)
                    go <- false
                elif not m2 then
                    res <- 1
                    go <- false
                else
                    let c = comparer e1.Current e2.Current
                    if c <> 0 then
                        res <- c
                        go <- false
            res

        [<CompiledName("AllPairs")>]
        let allPairs (source1: seq<'T1>) (source2: seq<'T2>) : seq<'T1 * 'T2> =
            checkNonNull "source1" source1
            checkNonNull "source2" source2
            delay (fun () ->
                let second = SeqImpl.toArray source2
                let b = Buffer<'T1 * 'T2>()
                for x in source1 do
                    for y in second do b.Add(x, y)
                b.ToArray() :> seq<'T1 * 'T2>)

        [<CompiledName("MapFold")>]
        let mapFold<'T, 'State, 'Result> (mapping: 'State -> 'T -> 'Result * 'State) (state: 'State) (source: seq<'T>) : seq<'Result> * 'State =
            checkNonNull "source" source
            let b = Buffer<'Result>()
            let mutable acc = state
            for x in source do
                let r, s = mapping acc x
                b.Add r
                acc <- s
            (b.ToArray() :> seq<'Result>), acc

        // Enumerates the source once, however many times the result is.
        [<CompiledName("Cache")>]
        let cache (source: seq<'T>) : seq<'T> =
            checkNonNull "source" source
            let items = Buffer<'T>()
            let sourceEnum: IEnumerator<'T> ref = ref null
            let finished = ref false
            let fetch (i: int) =
                if i < items.Count then true
                elif finished.Value then false
                else
                    if isNull sourceEnum.Value then sourceEnum.Value <- source.GetEnumerator()
                    if sourceEnum.Value.MoveNext() then
                        items.Add sourceEnum.Value.Current
                        true
                    else
                        finished.Value <- true
                        sourceEnum.Value.Dispose()
                        false
            IEnumerator.mkSeq (fun () ->
                let i = ref 0
                enumerator (fun () ->
                    if fetch i.Value then
                        let x = items.[i.Value]
                        i.Value <- i.Value + 1
                        ValueSome x
                    else ValueNone) noDispose)

        [<CompiledName("ReadOnly")>]
        let readonly (source: seq<'T>) : seq<'T> =
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () -> source.GetEnumerator())

        [<CompiledName("Cast")>]
        let cast (source: IEnumerable) : seq<'T> =
            checkNonNull "source" source
            IEnumerator.mkSeq (fun () ->
                let e = source.GetEnumerator()
                enumerator (fun () ->
                    if e.MoveNext() then
                        ValueSome(unbox e.Current : 'T)
                    else ValueNone) noDispose)

        [<CompiledName("SplitInto")>]
        let splitInto (count: int) (source: seq<'T>) : seq<'T array> =
            checkNonNull "source" source
            if count <= 0 then invalidArg "count" "The input must be positive."
            delay (fun () ->
                let items = SeqImpl.toArray source
                let n = Array.length items
                let parts = if n < count then n else count
                let result = Array.zeroCreateUnchecked parts
                let small = if parts = 0 then 0 else n / parts
                let extra = if parts = 0 then 0 else n % parts
                let mutable start = 0
                for i in 0 .. parts - 1 do
                    let size = if i < extra then small + 1 else small
                    result.[i] <- Array.copyRange items start size
                    start <- start + size
                result :> seq<'T array>)

        [<CompiledName("Transpose")>]
        let transpose (source: seq<#seq<'T>>) : seq<seq<'T>> =
            checkNonNull "source" source
            delay (fun () ->
                let rows = SeqImpl.toArray (map (fun (r: #seq<'T>) -> SeqImpl.toArray (r :> seq<'T>)) source)
                let nrows = Array.length rows
                if nrows = 0 then (Array.zeroCreateUnchecked 0 : seq<'T> array) :> seq<seq<'T>>
                else
                    let ncols = Array.length rows.[0]
                    for r in rows do
                        if Array.length r <> ncols then invalidArg "source" "The input sequences have different lengths."
                    let result = Array.zeroCreateUnchecked ncols
                    for c in 0 .. ncols - 1 do
                        let col = Array.zeroCreateUnchecked nrows
                        for r in 0 .. nrows - 1 do col.[r] <- rows.[r].[c]
                        result.[c] <- (col :> seq<'T>)
                    result :> seq<seq<'T>>)
