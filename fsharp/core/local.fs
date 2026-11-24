// Helpers the collection modules share: arrays without the Array module, a
// growable buffer, a stable merge sort and a small hash table (zerocore has
// no List<T> or Dictionary, and FSharp.Core doesn't reference lib/).
#nowarn "35" "42" "44" "3370"

namespace Microsoft.FSharp.Primitives.Basics

    open System
    open System.Collections.Generic
    open Microsoft.FSharp.Core
    open Microsoft.FSharp.Core.Operators
    open Microsoft.FSharp.Core.LanguagePrimitives.IntrinsicOperators

    module internal Array =

        let inline zeroCreateUnchecked (count: int) : 'T array = (# "newarr !0" type ('T) count : 'T array #)

        let inline length (array: 'T array) = (# "ldlen conv.i4" array : int #)

        let copyRange (source: 'T array) (start: int) (count: int) : 'T array =
            let result = zeroCreateUnchecked count
            for i in 0 .. count - 1 do result.[i] <- source.[start + i]
            result

        let copy (source: 'T array) = copyRange source 0 (length source)

        // Stable: equal items keep their order (List.sort and Seq.sort promise it).
        let stableSortInPlace (cmp: 'T -> 'T -> int) (items: 'T array) =
            let n = length items
            if n > 1 then
                let mutable src = items
                let mutable dst = zeroCreateUnchecked n
                let mutable width = 1
                while width < n do
                    let mutable lo = 0
                    while lo < n do
                        let mid = min (lo + width) n
                        let hi = min (lo + 2 * width) n
                        let mutable i = lo
                        let mutable j = mid
                        let mutable k = lo
                        while k < hi do
                            if i < mid && (j >= hi || cmp src.[i] src.[j] <= 0) then
                                dst.[k] <- src.[i]
                                i <- i + 1
                            else
                                dst.[k] <- src.[j]
                                j <- j + 1
                            k <- k + 1
                        lo <- hi
                    let t = src
                    src <- dst
                    dst <- t
                    width <- width * 2
                if not (LanguagePrimitives.PhysicalEquality src items) then
                    for i in 0 .. n - 1 do items.[i] <- src.[i]

    // An array that doubles as items are added.
    [<Sealed>]
    type internal Buffer<'T>() =
        let mutable items: 'T array = Array.zeroCreateUnchecked 4
        let mutable count = 0
        member _.Count = count
        member _.Item with get (i: int) = items.[i] and set (i: int) (v: 'T) = items.[i] <- v
        member _.Add(value: 'T) =
            if count = Array.length items then
                let bigger = Array.zeroCreateUnchecked (count * 2)
                for i in 0 .. count - 1 do bigger.[i] <- items.[i]
                items <- bigger
            items.[count] <- value
            count <- count + 1
        member _.ToArray() = Array.copyRange items 0 count

    // Keys in the order first added, so groupBy and countBy keep the order of
    // first appearance, as FSharp.Core's do.
    [<Sealed>]
    type internal HashTable<'Key, 'Value>(comparer: IEqualityComparer<'Key>) =
        let mutable buckets: int array = Array.zeroCreateUnchecked 16 // entry index + 1, 0 = empty
        let mutable keys: 'Key array = Array.zeroCreateUnchecked 16
        let mutable values: 'Value array = Array.zeroCreateUnchecked 16
        let mutable hashes: int array = Array.zeroCreateUnchecked 16
        let mutable next: int array = Array.zeroCreateUnchecked 16
        let mutable count = 0

        let hashOf (key: 'Key) = comparer.GetHashCode(key) &&& 0x7fffffff

        let find (key: 'Key) (h: int) =
            let mutable i = buckets.[h % Array.length buckets] - 1
            while i >= 0 && not (hashes.[i] = h && comparer.Equals(keys.[i], key)) do
                i <- next.[i] - 1
            i

        let grow () =
            let size = Array.length keys * 2
            let grown (a: 'a array) =
                let b = Array.zeroCreateUnchecked size
                for i in 0 .. count - 1 do b.[i] <- a.[i]
                b
            keys <- grown keys
            values <- grown values
            hashes <- grown hashes
            next <- Array.zeroCreateUnchecked size
            buckets <- Array.zeroCreateUnchecked size
            for i in 0 .. count - 1 do
                let b = hashes.[i] % size
                next.[i] <- buckets.[b]
                buckets.[b] <- i + 1

        member _.Count = count
        member _.KeyAt(i: int) = keys.[i]
        member _.ValueAt(i: int) = values.[i]

        member _.TryFind(key: 'Key, value: byref<'Value>) =
            let i = find key (hashOf key)
            if i >= 0 then
                value <- values.[i]
                true
            else false

        member _.ContainsKey(key: 'Key) = find key (hashOf key) >= 0

        // True when the key was new.
        member _.Set(key: 'Key, value: 'Value) =
            let h = hashOf key
            let i = find key h
            if i >= 0 then
                values.[i] <- value
                false
            else
                if count = Array.length keys then grow ()
                keys.[count] <- key
                values.[count] <- value
                hashes.[count] <- h
                let b = h % Array.length buckets
                next.[count] <- buckets.[b]
                buckets.[b] <- count + 1
                count <- count + 1
                true

    module internal Text =

        // Map's and Set's ToString show their items this way.
        let anyToStringShowingNull (x: 'T) =
            match box x with
            | null -> "null"
            | o -> o.ToString()
