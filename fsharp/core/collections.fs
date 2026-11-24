// FSharp.Core's Array and List modules, with its names and behaviour. Many
// functions read their input as a sequence and reuse Seq's version.
#nowarn "35" "42" "44" "3370"

namespace Microsoft.FSharp.Collections

    open System
    open System.Collections.Generic
    open Microsoft.FSharp.Core
    open Microsoft.FSharp.Core.Operators
    open Microsoft.FSharp.Core.LanguagePrimitives.IntrinsicOperators
    open Microsoft.FSharp.Core.CompilerServices
    open Microsoft.FSharp.Primitives.Basics

    [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
    [<RequireQualifiedAccess>]
    module Array =

        let inline private checkNonNull (argName: string) (arg: 'T array) =
            match box arg with
            | null -> nullArg argName
            | _ -> ()

        let private emptyInput () = invalidArg "array" "The input array was empty."
        let private notFound () = raise (ArgumentException("An index satisfying the predicate was not found in the collection."))

        [<CompiledName("ZeroCreate")>]
        let zeroCreate (count: int) : 'T array =
            if count < 0 then invalidArg "count" "The count can't be negative."
            (# "newarr !0" type ('T) count : 'T array #)

        [<CompiledName("Length")>]
        let inline length (array: 'T array) = (# "ldlen conv.i4" array : int #)

        [<CompiledName("Empty")>]
        let empty<'T> : 'T array = (# "newarr !0" type ('T) 0 : 'T array #)

        [<CompiledName("IsEmpty")>]
        let inline isEmpty (array: 'T array) = length array = 0

        [<CompiledName("Get")>]
        let inline get (array: 'T array) (index: int) = array.[index]

        [<CompiledName("Set")>]
        let inline set (array: 'T array) (index: int) (value: 'T) = array.[index] <- value

        [<CompiledName("Create")>]
        let create (count: int) (value: 'T) : 'T array =
            let result = zeroCreate count
            for i in 0 .. count - 1 do result.[i] <- value
            result

        [<CompiledName("Replicate")>]
        let replicate (count: int) (initial: 'T) : 'T array = create count initial

        [<CompiledName("Singleton")>]
        let singleton (value: 'T) : 'T array = [| value |]

        [<CompiledName("Initialize")>]
        let inline init (count: int) ([<InlineIfLambda>] initializer: int -> 'T) : 'T array =
            let result = zeroCreate count
            for i in 0 .. count - 1 do result.[i] <- initializer i
            result

        [<CompiledName("Copy")>]
        let copy (array: 'T array) : 'T array =
            checkNonNull "array" array
            Microsoft.FSharp.Primitives.Basics.Array.copy array

        [<CompiledName("GetSubArray")>]
        let sub (array: 'T array) (startIndex: int) (count: int) : 'T array =
            checkNonNull "array" array
            if startIndex < 0 || count < 0 || startIndex + count > length array then
                invalidArg "startIndex" "The index is outside the legal range."
            Microsoft.FSharp.Primitives.Basics.Array.copyRange array startIndex count

        [<CompiledName("CopyTo")>]
        let blit (source: 'T array) (sourceIndex: int) (target: 'T array) (targetIndex: int) (count: int) =
            if sourceIndex < 0 || count < 0 || sourceIndex + count > length source
               || targetIndex < 0 || targetIndex + count > length target then
                invalidArg "count" "The index is outside the legal range."
            if targetIndex <= sourceIndex then
                for i in 0 .. count - 1 do target.[targetIndex + i] <- source.[sourceIndex + i]
            else
                for i in count - 1 .. -1 .. 0 do target.[targetIndex + i] <- source.[sourceIndex + i]

        [<CompiledName("Fill")>]
        let fill (target: 'T array) (targetIndex: int) (count: int) (value: 'T) =
            if targetIndex < 0 || count < 0 || targetIndex + count > length target then
                invalidArg "count" "The index is outside the legal range."
            for i in targetIndex .. targetIndex + count - 1 do target.[i] <- value

        [<CompiledName("Append")>]
        let append (array1: 'T array) (array2: 'T array) : 'T array =
            checkNonNull "array1" array1
            checkNonNull "array2" array2
            let n1 = length array1
            let result = zeroCreate (n1 + length array2)
            for i in 0 .. n1 - 1 do result.[i] <- array1.[i]
            for i in 0 .. length array2 - 1 do result.[n1 + i] <- array2.[i]
            result

        [<CompiledName("OfSeq")>]
        let ofSeq (source: seq<'T>) : 'T array = Seq.toArray source

        [<CompiledName("ToSeq")>]
        let toSeq (array: 'T array) : seq<'T> = Seq.ofArray array

        [<CompiledName("OfList")>]
        let ofList (list: 'T list) : 'T array =
            let result = zeroCreate list.Length
            let mutable i = 0
            for x in list do
                result.[i] <- x
                i <- i + 1
            result

        [<CompiledName("ToList")>]
        let toList (array: 'T array) : 'T list =
            checkNonNull "array" array
            let mutable res = []
            for i in length array - 1 .. -1 .. 0 do res <- array.[i] :: res
            res

        [<CompiledName("Concat")>]
        let concat (arrays: seq<'T array>) : 'T array =
            let b = Buffer<'T>()
            for a in arrays do
                for x in a do b.Add x
            b.ToArray()

        [<CompiledName("Collect")>]
        let collect (mapping: 'T -> 'U array) (array: 'T array) : 'U array =
            checkNonNull "array" array
            let b = Buffer<'U>()
            for x in array do
                for y in mapping x do b.Add y
            b.ToArray()

        [<CompiledName("Iterate")>]
        let inline iter ([<InlineIfLambda>] action: 'T -> unit) (array: 'T array) =
            for i in 0 .. length array - 1 do action array.[i]

        [<CompiledName("IterateIndexed")>]
        let iteri (action: int -> 'T -> unit) (array: 'T array) =
            checkNonNull "array" array
            for i in 0 .. length array - 1 do action i array.[i]

        [<CompiledName("Iterate2")>]
        let iter2 (action: 'T1 -> 'T2 -> unit) (array1: 'T1 array) (array2: 'T2 array) =
            if length array1 <> length array2 then invalidArg "array2" "The arrays have different lengths."
            for i in 0 .. length array1 - 1 do action array1.[i] array2.[i]

        [<CompiledName("Map")>]
        let inline map ([<InlineIfLambda>] mapping: 'T -> 'U) (array: 'T array) : 'U array =
            let result = zeroCreate (length array)
            for i in 0 .. length array - 1 do result.[i] <- mapping array.[i]
            result

        [<CompiledName("MapIndexed")>]
        let mapi (mapping: int -> 'T -> 'U) (array: 'T array) : 'U array =
            checkNonNull "array" array
            let result = zeroCreate (length array)
            for i in 0 .. length array - 1 do result.[i] <- mapping i array.[i]
            result

        [<CompiledName("Map2")>]
        let map2 (mapping: 'T1 -> 'T2 -> 'U) (array1: 'T1 array) (array2: 'T2 array) : 'U array =
            if length array1 <> length array2 then invalidArg "array2" "The arrays have different lengths."
            let result = zeroCreate (length array1)
            for i in 0 .. length array1 - 1 do result.[i] <- mapping array1.[i] array2.[i]
            result

        [<CompiledName("Indexed")>]
        let indexed (array: 'T array) : (int * 'T) array = mapi (fun i x -> (i, x)) array

        [<CompiledName("Zip")>]
        let zip (array1: 'T1 array) (array2: 'T2 array) : ('T1 * 'T2) array = map2 (fun x y -> (x, y)) array1 array2

        [<CompiledName("Zip3")>]
        let zip3 (array1: 'T1 array) (array2: 'T2 array) (array3: 'T3 array) : ('T1 * 'T2 * 'T3) array =
            if length array1 <> length array2 || length array1 <> length array3 then
                invalidArg "array3" "The arrays have different lengths."
            init (length array1) (fun i -> (array1.[i], array2.[i], array3.[i]))

        [<CompiledName("Unzip")>]
        let unzip (array: ('T1 * 'T2) array) : 'T1 array * 'T2 array =
            let n = length array
            let r1 = zeroCreate n
            let r2 = zeroCreate n
            for i in 0 .. n - 1 do
                let (x, y) = array.[i]
                r1.[i] <- x
                r2.[i] <- y
            r1, r2

        [<CompiledName("Unzip3")>]
        let unzip3 (array: ('T1 * 'T2 * 'T3) array) : 'T1 array * 'T2 array * 'T3 array =
            let n = length array
            let r1 = zeroCreate n
            let r2 = zeroCreate n
            let r3 = zeroCreate n
            for i in 0 .. n - 1 do
                let (x, y, z) = array.[i]
                r1.[i] <- x
                r2.[i] <- y
                r3.[i] <- z
            r1, r2, r3

        [<CompiledName("Filter")>]
        let filter (predicate: 'T -> bool) (array: 'T array) : 'T array =
            checkNonNull "array" array
            let b = Buffer<'T>()
            for x in array do
                if predicate x then b.Add x
            b.ToArray()

        [<CompiledName("Where")>]
        let where (predicate: 'T -> bool) (array: 'T array) : 'T array = filter predicate array

        [<CompiledName("Choose")>]
        let choose (chooser: 'T -> 'U option) (array: 'T array) : 'U array =
            checkNonNull "array" array
            let b = Buffer<'U>()
            for x in array do
                match chooser x with
                | Some y -> b.Add y
                | None -> ()
            b.ToArray()

        [<CompiledName("Partition")>]
        let partition (predicate: 'T -> bool) (array: 'T array) : 'T array * 'T array =
            checkNonNull "array" array
            let yes = Buffer<'T>()
            let no = Buffer<'T>()
            for x in array do
                if predicate x then yes.Add x else no.Add x
            yes.ToArray(), no.ToArray()

        [<CompiledName("Fold")>]
        let inline fold ([<InlineIfLambda>] folder: 'State -> 'T -> 'State) (state: 'State) (array: 'T array) =
            let mutable acc = state
            for i in 0 .. length array - 1 do acc <- folder acc array.[i]
            acc

        [<CompiledName("FoldBack")>]
        let foldBack (folder: 'T -> 'State -> 'State) (array: 'T array) (state: 'State) =
            checkNonNull "array" array
            let mutable acc = state
            for i in length array - 1 .. -1 .. 0 do acc <- folder array.[i] acc
            acc

        [<CompiledName("Fold2")>]
        let fold2 (folder: 'State -> 'T1 -> 'T2 -> 'State) (state: 'State) (array1: 'T1 array) (array2: 'T2 array) =
            if length array1 <> length array2 then invalidArg "array2" "The arrays have different lengths."
            let mutable acc = state
            for i in 0 .. length array1 - 1 do acc <- folder acc array1.[i] array2.[i]
            acc

        [<CompiledName("Reduce")>]
        let reduce (reduction: 'T -> 'T -> 'T) (array: 'T array) =
            checkNonNull "array" array
            if length array = 0 then emptyInput ()
            let mutable acc = array.[0]
            for i in 1 .. length array - 1 do acc <- reduction acc array.[i]
            acc

        [<CompiledName("ReduceBack")>]
        let reduceBack (reduction: 'T -> 'T -> 'T) (array: 'T array) =
            checkNonNull "array" array
            let n = length array
            if n = 0 then emptyInput ()
            let mutable acc = array.[n - 1]
            for i in n - 2 .. -1 .. 0 do acc <- reduction array.[i] acc
            acc

        [<CompiledName("Scan")>]
        let scan (folder: 'State -> 'T -> 'State) (state: 'State) (array: 'T array) : 'State array =
            checkNonNull "array" array
            let result = zeroCreate (length array + 1)
            result.[0] <- state
            for i in 0 .. length array - 1 do result.[i + 1] <- folder result.[i] array.[i]
            result

        [<CompiledName("Sum")>]
        let inline sum (array: ^T array) : ^T =
            let mutable acc = LanguagePrimitives.GenericZero< ^T>
            for i in 0 .. length array - 1 do acc <- acc + array.[i]
            acc

        [<CompiledName("SumBy")>]
        let inline sumBy ([<InlineIfLambda>] projection: 'T -> ^U) (array: 'T array) : ^U =
            let mutable acc = LanguagePrimitives.GenericZero< ^U>
            for i in 0 .. length array - 1 do acc <- acc + projection array.[i]
            acc

        [<CompiledName("Average")>]
        let inline average (array: ^T array) : ^T =
            if length array = 0 then invalidArg "array" "The input array was empty."
            LanguagePrimitives.DivideByInt< ^T> (sum array) (length array)

        [<CompiledName("AverageBy")>]
        let inline averageBy ([<InlineIfLambda>] projection: 'T -> ^U) (array: 'T array) : ^U =
            if length array = 0 then invalidArg "array" "The input array was empty."
            LanguagePrimitives.DivideByInt< ^U> (sumBy projection array) (length array)

        [<CompiledName("Min")>]
        let inline min (array: 'T array) =
            if length array = 0 then invalidArg "array" "The input array was empty."
            let mutable acc = array.[0]
            for i in 1 .. length array - 1 do
                if array.[i] < acc then acc <- array.[i]
            acc

        [<CompiledName("Max")>]
        let inline max (array: 'T array) =
            if length array = 0 then invalidArg "array" "The input array was empty."
            let mutable acc = array.[0]
            for i in 1 .. length array - 1 do
                if array.[i] > acc then acc <- array.[i]
            acc

        [<CompiledName("MinBy")>]
        let inline minBy ([<InlineIfLambda>] projection: 'T -> 'U) (array: 'T array) =
            if length array = 0 then invalidArg "array" "The input array was empty."
            let mutable acc = array.[0]
            let mutable accv = projection acc
            for i in 1 .. length array - 1 do
                let v = projection array.[i]
                if v < accv then
                    acc <- array.[i]
                    accv <- v
            acc

        [<CompiledName("MaxBy")>]
        let inline maxBy ([<InlineIfLambda>] projection: 'T -> 'U) (array: 'T array) =
            if length array = 0 then invalidArg "array" "The input array was empty."
            let mutable acc = array.[0]
            let mutable accv = projection acc
            for i in 1 .. length array - 1 do
                let v = projection array.[i]
                if v > accv then
                    acc <- array.[i]
                    accv <- v
            acc

        [<CompiledName("Exists")>]
        let exists (predicate: 'T -> bool) (array: 'T array) =
            checkNonNull "array" array
            let mutable i = 0
            while i < length array && not (predicate array.[i]) do i <- i + 1
            i < length array

        [<CompiledName("ForAll")>]
        let forall (predicate: 'T -> bool) (array: 'T array) =
            checkNonNull "array" array
            let mutable i = 0
            while i < length array && predicate array.[i] do i <- i + 1
            i = length array

        [<CompiledName("Exists2")>]
        let exists2 (predicate: 'T1 -> 'T2 -> bool) (array1: 'T1 array) (array2: 'T2 array) =
            if length array1 <> length array2 then invalidArg "array2" "The arrays have different lengths."
            Seq.exists2 predicate array1 array2

        [<CompiledName("ForAll2")>]
        let forall2 (predicate: 'T1 -> 'T2 -> bool) (array1: 'T1 array) (array2: 'T2 array) =
            if length array1 <> length array2 then invalidArg "array2" "The arrays have different lengths."
            Seq.forall2 predicate array1 array2

        [<CompiledName("Contains")>]
        let inline contains (value: 'T) (array: 'T array) =
            let mutable i = 0
            while i < length array && not (value = array.[i]) do i <- i + 1
            i < length array

        [<CompiledName("TryFindIndex")>]
        let tryFindIndex (predicate: 'T -> bool) (array: 'T array) =
            checkNonNull "array" array
            let mutable i = 0
            while i < length array && not (predicate array.[i]) do i <- i + 1
            if i < length array then Some i else None

        [<CompiledName("FindIndex")>]
        let findIndex (predicate: 'T -> bool) (array: 'T array) =
            match tryFindIndex predicate array with
            | Some i -> i
            | None -> notFound ()

        [<CompiledName("TryFind")>]
        let tryFind (predicate: 'T -> bool) (array: 'T array) =
            match tryFindIndex predicate array with
            | Some i -> Some array.[i]
            | None -> None

        [<CompiledName("Find")>]
        let find (predicate: 'T -> bool) (array: 'T array) = array.[findIndex predicate array]

        [<CompiledName("TryFindIndexBack")>]
        let tryFindIndexBack (predicate: 'T -> bool) (array: 'T array) =
            checkNonNull "array" array
            let mutable i = length array - 1
            while i >= 0 && not (predicate array.[i]) do i <- i - 1
            if i >= 0 then Some i else None

        [<CompiledName("FindIndexBack")>]
        let findIndexBack (predicate: 'T -> bool) (array: 'T array) =
            match tryFindIndexBack predicate array with
            | Some i -> i
            | None -> notFound ()

        [<CompiledName("TryFindBack")>]
        let tryFindBack (predicate: 'T -> bool) (array: 'T array) =
            match tryFindIndexBack predicate array with
            | Some i -> Some array.[i]
            | None -> None

        [<CompiledName("FindBack")>]
        let findBack (predicate: 'T -> bool) (array: 'T array) = array.[findIndexBack predicate array]

        [<CompiledName("TryPick")>]
        let tryPick (chooser: 'T -> 'U option) (array: 'T array) = Seq.tryPick chooser array

        [<CompiledName("Pick")>]
        let pick (chooser: 'T -> 'U option) (array: 'T array) = Seq.pick chooser array

        [<CompiledName("Head")>]
        let head (array: 'T array) =
            if length array = 0 then emptyInput ()
            array.[0]

        [<CompiledName("TryHead")>]
        let tryHead (array: 'T array) = if length array = 0 then None else Some array.[0]

        [<CompiledName("Last")>]
        let last (array: 'T array) =
            if length array = 0 then emptyInput ()
            array.[length array - 1]

        [<CompiledName("TryLast")>]
        let tryLast (array: 'T array) = if length array = 0 then None else Some array.[length array - 1]

        [<CompiledName("Item")>]
        let item (index: int) (array: 'T array) = array.[index]

        [<CompiledName("TryItem")>]
        let tryItem (index: int) (array: 'T array) =
            if index < 0 || index >= length array then None else Some array.[index]

        [<CompiledName("ExactlyOne")>]
        let exactlyOne (array: 'T array) =
            if length array = 1 then array.[0]
            elif length array = 0 then emptyInput ()
            else invalidArg "array" "The input sequence contains more than one element."

        [<CompiledName("TryExactlyOne")>]
        let tryExactlyOne (array: 'T array) = if length array = 1 then Some array.[0] else None

        [<CompiledName("Tail")>]
        let tail (array: 'T array) : 'T array =
            if length array = 0 then emptyInput ()
            sub array 1 (length array - 1)

        [<CompiledName("Take")>]
        let take (count: int) (array: 'T array) : 'T array =
            if count < 0 then invalidArg "count" "The input must be non-negative."
            if count > length array then invalidArg "count" "The input sequence has an insufficient number of elements."
            sub array 0 count

        [<CompiledName("Truncate")>]
        let truncate (count: int) (array: 'T array) : 'T array =
            sub array 0 (if count < 0 then 0 elif count > length array then length array else count)

        [<CompiledName("Skip")>]
        let skip (count: int) (array: 'T array) : 'T array =
            if count > length array then invalidArg "count" "The input sequence has an insufficient number of elements."
            let count = if count < 0 then 0 else count
            sub array count (length array - count)

        [<CompiledName("TakeWhile")>]
        let takeWhile (predicate: 'T -> bool) (array: 'T array) : 'T array =
            let mutable i = 0
            while i < length array && predicate array.[i] do i <- i + 1
            sub array 0 i

        [<CompiledName("SkipWhile")>]
        let skipWhile (predicate: 'T -> bool) (array: 'T array) : 'T array =
            let mutable i = 0
            while i < length array && predicate array.[i] do i <- i + 1
            sub array i (length array - i)

        [<CompiledName("SplitAt")>]
        let splitAt (index: int) (array: 'T array) : 'T array * 'T array =
            if index < 0 || index > length array then invalidArg "index" "The index is outside the legal range."
            sub array 0 index, sub array index (length array - index)

        [<CompiledName("Reverse")>]
        let rev (array: 'T array) : 'T array =
            checkNonNull "array" array
            let n = length array
            init n (fun i -> array.[n - 1 - i])

        [<CompiledName("Pairwise")>]
        let pairwise (array: 'T array) : ('T * 'T) array =
            if length array < 2 then empty
            else init (length array - 1) (fun i -> (array.[i], array.[i + 1]))

        [<CompiledName("Windowed")>]
        let windowed (windowSize: int) (array: 'T array) : 'T array array =
            if windowSize <= 0 then invalidArg "windowSize" "The input must be positive."
            if windowSize > length array then empty
            else init (length array - windowSize + 1) (fun i -> sub array i windowSize)

        [<CompiledName("ChunkBySize")>]
        let chunkBySize (chunkSize: int) (array: 'T array) : 'T array array = Seq.toArray (Seq.chunkBySize chunkSize array)

        [<CompiledName("SplitInto")>]
        let splitInto (count: int) (array: 'T array) : 'T array array = Seq.toArray (Seq.splitInto count array)

        [<CompiledName("Transpose")>]
        let transpose (arrays: seq<'T array>) : 'T array array =
            Seq.toArray (Seq.map Seq.toArray (Seq.transpose arrays))

        [<CompiledName("SortInPlaceWith")>]
        let sortInPlaceWith (comparer: 'T -> 'T -> int) (array: 'T array) =
            checkNonNull "array" array
            Microsoft.FSharp.Primitives.Basics.Array.stableSortInPlace comparer array

        [<CompiledName("SortInPlaceBy")>]
        let sortInPlaceBy (projection: 'T -> 'Key) (array: 'T array) =
            sortInPlaceWith (fun x y -> compare (projection x) (projection y)) array

        [<CompiledName("SortInPlace")>]
        let sortInPlace (array: 'T array) = sortInPlaceWith compare array

        [<CompiledName("SortWith")>]
        let sortWith (comparer: 'T -> 'T -> int) (array: 'T array) : 'T array =
            let result = copy array
            sortInPlaceWith comparer result
            result

        [<CompiledName("SortBy")>]
        let sortBy (projection: 'T -> 'Key) (array: 'T array) : 'T array =
            sortWith (fun x y -> compare (projection x) (projection y)) array

        [<CompiledName("Sort")>]
        let sort (array: 'T array) : 'T array = sortWith compare array

        [<CompiledName("SortByDescending")>]
        let inline sortByDescending (projection: 'T -> 'Key) (array: 'T array) : 'T array =
            sortWith (fun x y -> compare (projection y) (projection x)) array

        [<CompiledName("SortDescending")>]
        let inline sortDescending (array: 'T array) : 'T array = sortWith (fun x y -> compare y x) array

        [<CompiledName("Distinct")>]
        let distinct (array: 'T array) : 'T array = Seq.toArray (Seq.distinct array)

        [<CompiledName("DistinctBy")>]
        let distinctBy (projection: 'T -> 'Key) (array: 'T array) : 'T array = Seq.toArray (Seq.distinctBy projection array)

        [<CompiledName("Except")>]
        let except (itemsToExclude: seq<'T>) (array: 'T array) : 'T array = Seq.toArray (Seq.except itemsToExclude array)

        [<CompiledName("GroupBy")>]
        let groupBy (projection: 'T -> 'Key) (array: 'T array) : ('Key * 'T array) array =
            Seq.toArray (Seq.map (fun (k, (items: seq<'T>)) -> (k, (items :?> 'T array))) (Seq.groupBy projection array))

        [<CompiledName("CountBy")>]
        let countBy (projection: 'T -> 'Key) (array: 'T array) : ('Key * int) array = Seq.toArray (Seq.countBy projection array)

        [<CompiledName("AllPairs")>]
        let allPairs (array1: 'T1 array) (array2: 'T2 array) : ('T1 * 'T2) array = Seq.toArray (Seq.allPairs array1 array2)

        [<CompiledName("MapFold")>]
        let mapFold (mapping: 'State -> 'T -> 'Result * 'State) (state: 'State) (array: 'T array) : 'Result array * 'State =
            let result = zeroCreate (length array)
            let mutable acc = state
            for i in 0 .. length array - 1 do
                let r, s = mapping acc array.[i]
                result.[i] <- r
                acc <- s
            result, acc

        [<CompiledName("CompareWith")>]
        let compareWith (comparer: 'T -> 'T -> int) (array1: 'T array) (array2: 'T array) = Seq.compareWith comparer array1 array2

        [<CompiledName("Unfold")>]
        let unfold (generator: 'State -> ('T * 'State) option) (state: 'State) : 'T array = Seq.toArray (Seq.unfold generator state)

    [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
    [<RequireQualifiedAccess>]
    module List =

        let private emptyInput () = invalidArg "list" "The input list was empty."
        let private notFound () = raise (ArgumentException("An index satisfying the predicate was not found in the collection."))

        [<CompiledName("Empty")>]
        let empty<'T> : 'T list = []

        [<CompiledName("IsEmpty")>]
        let inline isEmpty (list: 'T list) = match list with [] -> true | _ -> false

        [<CompiledName("Length")>]
        let length (list: 'T list) = list.Length

        [<CompiledName("Head")>]
        let head (list: 'T list) = match list with x :: _ -> x | [] -> emptyInput ()

        [<CompiledName("TryHead")>]
        let tryHead (list: 'T list) = match list with x :: _ -> Some x | [] -> None

        [<CompiledName("Tail")>]
        let tail (list: 'T list) = match list with _ :: t -> t | [] -> emptyInput ()

        [<CompiledName("Last")>]
        let last (list: 'T list) = match Seq.tryLast list with Some x -> x | None -> emptyInput ()

        [<CompiledName("TryLast")>]
        let tryLast (list: 'T list) = Seq.tryLast list

        [<CompiledName("Singleton")>]
        let singleton (value: 'T) = [ value ]

        [<CompiledName("Reverse")>]
        let rev (list: 'T list) =
            let mutable acc = []
            for x in list do acc <- x :: acc
            acc

        [<CompiledName("Iterate")>]
        let inline iter ([<InlineIfLambda>] action: 'T -> unit) (list: 'T list) =
            for x in list do action x

        [<CompiledName("IterateIndexed")>]
        let iteri (action: int -> 'T -> unit) (list: 'T list) =
            let mutable i = 0
            for x in list do
                action i x
                i <- i + 1

        [<CompiledName("Iterate2")>]
        let iter2 (action: 'T1 -> 'T2 -> unit) (list1: 'T1 list) (list2: 'T2 list) =
            if list1.Length <> list2.Length then invalidArg "list2" "The lists had different lengths."
            Seq.iter2 action list1 list2

        [<CompiledName("Fold")>]
        let inline fold ([<InlineIfLambda>] folder: 'State -> 'T -> 'State) (state: 'State) (list: 'T list) =
            let mutable acc = state
            for x in list do acc <- folder acc x
            acc

        [<CompiledName("FoldBack")>]
        let foldBack (folder: 'T -> 'State -> 'State) (list: 'T list) (state: 'State) =
            let mutable acc = state
            for x in rev list do acc <- folder x acc
            acc

        [<CompiledName("Fold2")>]
        let fold2 (folder: 'State -> 'T1 -> 'T2 -> 'State) (state: 'State) (list1: 'T1 list) (list2: 'T2 list) =
            if list1.Length <> list2.Length then invalidArg "list2" "The lists had different lengths."
            Seq.fold2 folder state list1 list2

        [<CompiledName("Reduce")>]
        let reduce (reduction: 'T -> 'T -> 'T) (list: 'T list) =
            match list with
            | [] -> emptyInput ()
            | h :: t -> fold reduction h t

        [<CompiledName("ReduceBack")>]
        let reduceBack (reduction: 'T -> 'T -> 'T) (list: 'T list) =
            match rev list with
            | [] -> emptyInput ()
            | h :: t -> fold (fun acc x -> reduction x acc) h t

        [<CompiledName("Scan")>]
        let scan (folder: 'State -> 'T -> 'State) (state: 'State) (list: 'T list) : 'State list = Seq.toList (Seq.scan folder state list)

        [<CompiledName("Map")>]
        let map (mapping: 'T -> 'U) (list: 'T list) : 'U list =
            let mutable c = ListCollector<'U>()
            for x in list do c.Add(mapping x)
            c.Close()

        [<CompiledName("MapIndexed")>]
        let mapi (mapping: int -> 'T -> 'U) (list: 'T list) : 'U list =
            let mutable c = ListCollector<'U>()
            let mutable i = 0
            for x in list do
                c.Add(mapping i x)
                i <- i + 1
            c.Close()

        [<CompiledName("Map2")>]
        let map2 (mapping: 'T1 -> 'T2 -> 'U) (list1: 'T1 list) (list2: 'T2 list) : 'U list =
            if list1.Length <> list2.Length then invalidArg "list2" "The lists had different lengths."
            Seq.toList (Seq.map2 mapping list1 list2)

        [<CompiledName("Map3")>]
        let map3 (mapping: 'T1 -> 'T2 -> 'T3 -> 'U) (list1: 'T1 list) (list2: 'T2 list) (list3: 'T3 list) : 'U list =
            if list1.Length <> list2.Length || list1.Length <> list3.Length then invalidArg "list3" "The lists had different lengths."
            Seq.toList (Seq.map2 (fun x (y, z) -> mapping x y z) list1 (Seq.zip list2 list3))

        [<CompiledName("Indexed")>]
        let indexed (list: 'T list) : (int * 'T) list = mapi (fun i x -> (i, x)) list

        [<CompiledName("Zip")>]
        let zip (list1: 'T1 list) (list2: 'T2 list) : ('T1 * 'T2) list = map2 (fun x y -> (x, y)) list1 list2

        [<CompiledName("Zip3")>]
        let zip3 (list1: 'T1 list) (list2: 'T2 list) (list3: 'T3 list) : ('T1 * 'T2 * 'T3) list =
            map3 (fun x y z -> (x, y, z)) list1 list2 list3

        [<CompiledName("Unzip")>]
        let unzip (list: ('T1 * 'T2) list) : 'T1 list * 'T2 list =
            let mutable c1 = ListCollector<'T1>()
            let mutable c2 = ListCollector<'T2>()
            for (x, y) in list do
                c1.Add x
                c2.Add y
            c1.Close(), c2.Close()

        [<CompiledName("Unzip3")>]
        let unzip3 (list: ('T1 * 'T2 * 'T3) list) : 'T1 list * 'T2 list * 'T3 list =
            let mutable c1 = ListCollector<'T1>()
            let mutable c2 = ListCollector<'T2>()
            let mutable c3 = ListCollector<'T3>()
            for (x, y, z) in list do
                c1.Add x
                c2.Add y
                c3.Add z
            c1.Close(), c2.Close(), c3.Close()

        [<CompiledName("Filter")>]
        let filter (predicate: 'T -> bool) (list: 'T list) : 'T list =
            let mutable c = ListCollector<'T>()
            for x in list do
                if predicate x then c.Add x
            c.Close()

        [<CompiledName("Where")>]
        let where (predicate: 'T -> bool) (list: 'T list) : 'T list = filter predicate list

        [<CompiledName("Choose")>]
        let choose (chooser: 'T -> 'U option) (list: 'T list) : 'U list =
            let mutable c = ListCollector<'U>()
            for x in list do
                match chooser x with
                | Some y -> c.Add y
                | None -> ()
            c.Close()

        [<CompiledName("Partition")>]
        let partition (predicate: 'T -> bool) (list: 'T list) : 'T list * 'T list =
            let mutable yes = ListCollector<'T>()
            let mutable no = ListCollector<'T>()
            for x in list do
                if predicate x then yes.Add x else no.Add x
            yes.Close(), no.Close()

        [<CompiledName("Append")>]
        let append (list1: 'T list) (list2: 'T list) : 'T list = list1 @ list2

        [<CompiledName("Concat")>]
        let concat (lists: seq<'T list>) : 'T list =
            let mutable c = ListCollector<'T>()
            for l in lists do c.AddMany l
            c.Close()

        [<CompiledName("Collect")>]
        let collect (mapping: 'T -> 'U list) (list: 'T list) : 'U list =
            let mutable c = ListCollector<'U>()
            for x in list do c.AddMany(mapping x)
            c.Close()

        [<CompiledName("Initialize")>]
        let init (length: int) (initializer: int -> 'T) : 'T list =
            if length < 0 then invalidArg "length" "The input must be non-negative."
            let mutable c = ListCollector<'T>()
            for i in 0 .. length - 1 do c.Add(initializer i)
            c.Close()

        [<CompiledName("Replicate")>]
        let replicate (count: int) (initial: 'T) : 'T list = init count (fun _ -> initial)

        [<CompiledName("OfSeq")>]
        let ofSeq (source: seq<'T>) : 'T list = Seq.toList source

        [<CompiledName("ToSeq")>]
        let toSeq (list: 'T list) : seq<'T> = (list :> seq<'T>)

        [<CompiledName("OfArray")>]
        let ofArray (array: 'T array) : 'T list = Array.toList array

        [<CompiledName("ToArray")>]
        let toArray (list: 'T list) : 'T array = Array.ofList list

        [<CompiledName("Sum")>]
        let inline sum (list: ^T list) : ^T =
            let mutable acc = LanguagePrimitives.GenericZero< ^T>
            for x in list do acc <- acc + x
            acc

        [<CompiledName("SumBy")>]
        let inline sumBy ([<InlineIfLambda>] projection: 'T -> ^U) (list: 'T list) : ^U =
            let mutable acc = LanguagePrimitives.GenericZero< ^U>
            for x in list do acc <- acc + projection x
            acc

        [<CompiledName("Average")>]
        let inline average (list: ^T list) : ^T =
            match list with
            | [] -> invalidArg "list" "The input list was empty."
            | _ -> LanguagePrimitives.DivideByInt< ^T> (sum list) list.Length

        [<CompiledName("AverageBy")>]
        let inline averageBy ([<InlineIfLambda>] projection: 'T -> ^U) (list: 'T list) : ^U =
            match list with
            | [] -> invalidArg "list" "The input list was empty."
            | _ -> LanguagePrimitives.DivideByInt< ^U> (sumBy projection list) list.Length

        [<CompiledName("Min")>]
        let inline min (list: 'T list) =
            match list with
            | [] -> invalidArg "list" "The input list was empty."
            | h :: t ->
                let mutable acc = h
                for x in t do
                    if x < acc then acc <- x
                acc

        [<CompiledName("Max")>]
        let inline max (list: 'T list) =
            match list with
            | [] -> invalidArg "list" "The input list was empty."
            | h :: t ->
                let mutable acc = h
                for x in t do
                    if x > acc then acc <- x
                acc

        [<CompiledName("MinBy")>]
        let inline minBy ([<InlineIfLambda>] projection: 'T -> 'U) (list: 'T list) =
            match list with
            | [] -> invalidArg "list" "The input list was empty."
            | h :: t ->
                let mutable acc = h
                let mutable accv = projection h
                for x in t do
                    let v = projection x
                    if v < accv then
                        acc <- x
                        accv <- v
                acc

        [<CompiledName("MaxBy")>]
        let inline maxBy ([<InlineIfLambda>] projection: 'T -> 'U) (list: 'T list) =
            match list with
            | [] -> invalidArg "list" "The input list was empty."
            | h :: t ->
                let mutable acc = h
                let mutable accv = projection h
                for x in t do
                    let v = projection x
                    if v > accv then
                        acc <- x
                        accv <- v
                acc

        [<CompiledName("Exists")>]
        let exists (predicate: 'T -> bool) (list: 'T list) = Seq.exists predicate list

        [<CompiledName("ForAll")>]
        let forall (predicate: 'T -> bool) (list: 'T list) = Seq.forall predicate list

        [<CompiledName("Exists2")>]
        let exists2 (predicate: 'T1 -> 'T2 -> bool) (list1: 'T1 list) (list2: 'T2 list) =
            if list1.Length <> list2.Length then invalidArg "list2" "The lists had different lengths."
            Seq.exists2 predicate list1 list2

        [<CompiledName("ForAll2")>]
        let forall2 (predicate: 'T1 -> 'T2 -> bool) (list1: 'T1 list) (list2: 'T2 list) =
            if list1.Length <> list2.Length then invalidArg "list2" "The lists had different lengths."
            Seq.forall2 predicate list1 list2

        [<CompiledName("Contains")>]
        let inline contains (value: 'T) (list: 'T list) =
            let mutable rest = list
            let mutable found = false
            while not found && not rest.IsEmpty do
                match rest with
                | h :: t ->
                    if value = h then found <- true
                    rest <- t
                | [] -> ()
            found

        [<CompiledName("TryFind")>]
        let tryFind (predicate: 'T -> bool) (list: 'T list) = Seq.tryFind predicate list

        [<CompiledName("Find")>]
        let find (predicate: 'T -> bool) (list: 'T list) = match tryFind predicate list with Some x -> x | None -> notFound ()

        [<CompiledName("TryFindBack")>]
        let tryFindBack (predicate: 'T -> bool) (list: 'T list) = Seq.tryFindBack predicate list

        [<CompiledName("FindBack")>]
        let findBack (predicate: 'T -> bool) (list: 'T list) = match tryFindBack predicate list with Some x -> x | None -> notFound ()

        [<CompiledName("TryFindIndex")>]
        let tryFindIndex (predicate: 'T -> bool) (list: 'T list) = Seq.tryFindIndex predicate list

        [<CompiledName("FindIndex")>]
        let findIndex (predicate: 'T -> bool) (list: 'T list) = match tryFindIndex predicate list with Some x -> x | None -> notFound ()

        [<CompiledName("TryPick")>]
        let tryPick (chooser: 'T -> 'U option) (list: 'T list) = Seq.tryPick chooser list

        [<CompiledName("Pick")>]
        let pick (chooser: 'T -> 'U option) (list: 'T list) = match tryPick chooser list with Some x -> x | None -> notFound ()

        [<CompiledName("Item")>]
        let item (index: int) (list: 'T list) =
            match Seq.tryItem index list with
            | Some x -> x
            | None -> invalidArg "index" "The index was outside the range of elements in the list."

        [<CompiledName("TryItem")>]
        let tryItem (index: int) (list: 'T list) = Seq.tryItem index list

        [<CompiledName("Get")>]
        let nth (list: 'T list) (index: int) = item index list

        [<CompiledName("ExactlyOne")>]
        let exactlyOne (list: 'T list) =
            match list with
            | [ x ] -> x
            | [] -> emptyInput ()
            | _ -> invalidArg "list" "The input sequence contains more than one element."

        [<CompiledName("TryExactlyOne")>]
        let tryExactlyOne (list: 'T list) = match list with [ x ] -> Some x | _ -> None

        [<CompiledName("Take")>]
        let take (count: int) (list: 'T list) : 'T list = Seq.toList (Seq.take count list)

        [<CompiledName("Truncate")>]
        let truncate (count: int) (list: 'T list) : 'T list = Seq.toList (Seq.truncate count list)

        [<CompiledName("Skip")>]
        let skip (count: int) (list: 'T list) : 'T list =
            if count <= 0 then list
            else
                let mutable rest = list
                for _ in 1 .. count do
                    match rest with
                    | _ :: t -> rest <- t
                    | [] -> invalidArg "count" "The input list has an insufficient number of elements."
                rest

        [<CompiledName("TakeWhile")>]
        let takeWhile (predicate: 'T -> bool) (list: 'T list) : 'T list = Seq.toList (Seq.takeWhile predicate list)

        [<CompiledName("SkipWhile")>]
        let skipWhile (predicate: 'T -> bool) (list: 'T list) : 'T list =
            let mutable rest = list
            while (match rest with h :: _ -> predicate h | [] -> false) do
                rest <- rest.Tail
            rest

        [<CompiledName("SplitAt")>]
        let splitAt (index: int) (list: 'T list) : 'T list * 'T list =
            if index < 0 then invalidArg "index" "The input must be non-negative."
            take index list, skip index list

        [<CompiledName("Pairwise")>]
        let pairwise (list: 'T list) : ('T * 'T) list = Seq.toList (Seq.pairwise list)

        [<CompiledName("Windowed")>]
        let windowed (windowSize: int) (list: 'T list) : 'T list list =
            Seq.toList (Seq.map Array.toList (Seq.windowed windowSize list))

        [<CompiledName("ChunkBySize")>]
        let chunkBySize (chunkSize: int) (list: 'T list) : 'T list list =
            Seq.toList (Seq.map Array.toList (Seq.chunkBySize chunkSize list))

        [<CompiledName("SplitInto")>]
        let splitInto (count: int) (list: 'T list) : 'T list list =
            Seq.toList (Seq.map Array.toList (Seq.splitInto count list))

        [<CompiledName("Transpose")>]
        let transpose (lists: seq<'T list>) : 'T list list = Seq.toList (Seq.map Seq.toList (Seq.transpose lists))

        [<CompiledName("SortWith")>]
        let sortWith (comparer: 'T -> 'T -> int) (list: 'T list) : 'T list =
            let items = Array.ofList list
            Array.sortInPlaceWith comparer items
            Array.toList items

        [<CompiledName("SortBy")>]
        let sortBy (projection: 'T -> 'Key) (list: 'T list) : 'T list =
            sortWith (fun x y -> compare (projection x) (projection y)) list

        [<CompiledName("Sort")>]
        let sort (list: 'T list) : 'T list = sortWith compare list

        [<CompiledName("SortByDescending")>]
        let inline sortByDescending (projection: 'T -> 'Key) (list: 'T list) : 'T list =
            sortWith (fun x y -> compare (projection y) (projection x)) list

        [<CompiledName("SortDescending")>]
        let inline sortDescending (list: 'T list) : 'T list = sortWith (fun x y -> compare y x) list

        [<CompiledName("Distinct")>]
        let distinct (list: 'T list) : 'T list = Seq.toList (Seq.distinct list)

        [<CompiledName("DistinctBy")>]
        let distinctBy (projection: 'T -> 'Key) (list: 'T list) : 'T list = Seq.toList (Seq.distinctBy projection list)

        [<CompiledName("Except")>]
        let except (itemsToExclude: seq<'T>) (list: 'T list) : 'T list = Seq.toList (Seq.except itemsToExclude list)

        [<CompiledName("GroupBy")>]
        let groupBy (projection: 'T -> 'Key) (list: 'T list) : ('Key * 'T list) list =
            Seq.toList (Seq.map (fun (k, (items: seq<'T>)) -> (k, Seq.toList items)) (Seq.groupBy projection list))

        [<CompiledName("CountBy")>]
        let countBy (projection: 'T -> 'Key) (list: 'T list) : ('Key * int) list = Seq.toList (Seq.countBy projection list)

        [<CompiledName("AllPairs")>]
        let allPairs (list1: 'T1 list) (list2: 'T2 list) : ('T1 * 'T2) list = Seq.toList (Seq.allPairs list1 list2)

        [<CompiledName("MapFold")>]
        let mapFold (mapping: 'State -> 'T -> 'Result * 'State) (state: 'State) (list: 'T list) : 'Result list * 'State =
            let mutable c = ListCollector<'Result>()
            let mutable acc = state
            for x in list do
                let r, s = mapping acc x
                c.Add r
                acc <- s
            c.Close(), acc

        [<CompiledName("CompareWith")>]
        let compareWith (comparer: 'T -> 'T -> int) (list1: 'T list) (list2: 'T list) = Seq.compareWith comparer list1 list2

        [<CompiledName("Unfold")>]
        let unfold (generator: 'State -> ('T * 'State) option) (state: 'State) : 'T list = Seq.toList (Seq.unfold generator state)
