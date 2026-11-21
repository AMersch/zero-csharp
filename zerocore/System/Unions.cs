// What C# 15 `union` declarations need from the library.
//
// `union Result(Ok, Err);` compiles to a struct marked [Union] that implements
// IUnion and keeps its case in a field of type object. A struct case is
// boxed, and in zero mode the box is never freed.

namespace System.Runtime.CompilerServices
{
    public interface IUnion
    {
        object Value { get; }
    }

    public sealed class UnionAttribute : Attribute { }
}
