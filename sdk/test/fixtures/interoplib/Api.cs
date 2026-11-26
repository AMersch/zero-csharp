using System.Runtime.InteropServices;

namespace Shapes
{
    public enum Color : byte { Red = 1, Green = 2, Blue = 200 }

    public enum Big : long { Min = long.MinValue, One = 1 }

    public struct Vec
    {
        public float X;
        public float Y;
    }

    public struct Shape
    {
        public Vec Origin;
        public Color Color;
        public int Id { get; set; }    // its backing field, named Id in the bindings
    }

    public struct Name
    {
        public fixed byte Bytes[8];
        public int Length;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct Word
    {
        [FieldOffset(0)] public safe uint Value;
        [FieldOffset(0)] public safe byte Low;
    }

    public struct Node
    {
        public int Value;
        public Node* Next;
    }

    public static class Api
    {
        [UnmanagedCallersOnly(EntryPoint = "shape_move")]
        public static Shape Move(Shape shape, Vec by)
        {
            shape.Origin.X += by.X;
            shape.Origin.Y += by.Y;
            shape.Color = Color.Blue;
            shape.Id++;
            return shape;
        }

        [UnmanagedCallersOnly(EntryPoint = "name_length")]
        public static unsafe int NameLength(Name* name)
        {
            // SAFETY: the caller passes one readable Name.
            unsafe
            {
                int n = 0;
                while (n < 8 && name->Bytes[n] != 0) n++;
                return n;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "word_low")]
        public static byte Low(Word word) => word.Low;

        [UnmanagedCallersOnly(EntryPoint = "list_sum")]
        public static unsafe int ListSum(Node* node)
        {
            int sum = 0;
            // SAFETY: the caller passes a null-terminated list.
            unsafe
            {
                for (; node != null; node = node->Next) sum += node->Value;
            }
            return sum;
        }

        [UnmanagedCallersOnly(EntryPoint = "apply")]
        public static unsafe int Apply(delegate* unmanaged<int, int> function, int value)
        {
            // SAFETY: the caller passes a function that takes and returns an int.
            unsafe { return function(value); }
        }

        [UnmanagedCallersOnly(EntryPoint = "big_one")]
        public static Big BigOne() => Big.One;

        [UnmanagedCallersOnly(EntryPoint = "flag_set")]
        public static unsafe void SetFlag(bool* flag)
        {
            // SAFETY: the caller passes one writable bool.
            unsafe { *flag = true; }
        }

        // Two exports named Add with the same parameters: the bindings name
        // them after their entry points instead.
        [UnmanagedCallersOnly(EntryPoint = "api_add")]
        public static int Add(int a, int b) => a + b;

        // A struct from another assembly (lib/'s): left out, with ZERO008.
        [UnmanagedCallersOnly(EntryPoint = "calendar_year")]
        public static int Year(CalendarTime time) => time.Year;
    }

    public static class Other
    {
        [UnmanagedCallersOnly(EntryPoint = "other_add")]
        public static int Add(int a, int b) => a * b;
    }
}
