// Calls each export of fixtures/interoplib through its generated bindings.
using System;
using System.Runtime.InteropServices;
using Shapes;

public static unsafe class Program
{
    [UnmanagedCallersOnly]
    private static int Twice(int x) => 2 * x;

    public static void Main()
    {
        var shape = new Shape { Origin = new Vec { X = 1, Y = 2 }, Color = Color.Red, Id = 6 };
        Shape moved = Native.Move(shape, new Vec { X = 2, Y = 3 });
        Console.WriteLine($"move {moved.Origin.X},{moved.Origin.Y} {moved.Color} {moved.Id}");

        Name name = default;
        for (int i = 0; i < 5; i++) name.Bytes[i] = (byte)'a';
        Console.WriteLine($"name {Native.NameLength(&name)}");

        Console.WriteLine($"low {Native.Low(new Word { Value = 0x1234 })}");

        Node third = new Node { Value = 3 };
        Node second = new Node { Value = 2, Next = &third };
        Node first = new Node { Value = 1, Next = &second };
        Console.WriteLine($"list {Native.ListSum(&first)}");

        Console.WriteLine($"apply {Native.Apply(&Twice, 21)}");
        Console.WriteLine($"big {Native.BigOne()} {(long)Big.Min}");

        bool flag = false;
        Native.SetFlag(&flag);
        Console.WriteLine($"flag {flag}");

        Console.WriteLine($"add {Native.api_add(2, 3)} {Native.other_add(2, 3)}");
        Console.WriteLine($"color {(byte)Color.Blue} {sizeof(Shape)} {sizeof(Name)} {sizeof(Word)}");
    }
}
