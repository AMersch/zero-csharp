// Calls each export of fixtures/interoplib through its F# bindings.
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop
open Shapes

type Twice = delegate of int -> int

let mutable shape = Shape()
shape.Origin.X <- 1.0f
shape.Origin.Y <- 2.0f
shape.Color <- Color.Red
shape.Id <- 6
let mutable by = Vec()
by.X <- 2.0f
by.Y <- 3.0f
let moved = Native.Move(shape, by)
printfn "move %s,%s %s %d" (string moved.Origin.X) (string moved.Origin.Y) (string moved.Color) moved.Id

let mutable name = Name()
let bytes = NativePtr.ofVoidPtr<byte> (NativePtr.toVoidPtr &&name.Bytes)
for i in 0 .. 4 do NativePtr.set bytes i (byte 'a')
printfn "name %d" (Native.NameLength(&&name))

let mutable word = Word()
word.Value <- 0x1234u
printfn "low %d" (Native.Low(word))

let mutable third = Node(Value = 3)
let mutable second = Node(Value = 2, Next = &&third)
let mutable first = Node(Value = 1, Next = &&second)
printfn "list %d" (Native.ListSum(&&first))

let twice = Twice(fun x -> 2 * x)
let pointer = Marshal.GetFunctionPointerForDelegate twice
printfn "apply %d" (Native.Apply(pointer, 21))
System.GC.KeepAlive twice
printfn "big %s %d" (string (Native.BigOne())) (int64 Big.Min)

let mutable flag = false
Native.SetFlag(&&flag)
printfn "flag %s" (string flag)

printfn "add %d %d" (Native.api_add(2, 3)) (Native.other_add(2, 3))
printfn "color %d %d %d %d" (byte Color.Blue) sizeof<Shape> sizeof<Name> sizeof<Word>
