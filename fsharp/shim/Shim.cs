// BCL types the F# compiler and FSharp.Core expect that zerocore doesn't define.
namespace System
{
    public class Tuple<T1>
    {
        public Tuple(T1 item1) { Item1 = item1; }
        public T1 Item1 { get; }
        public override string ToString() => "(" + Show(Item1) + ")";
        private static string Show(object value) => value == null ? "null" : value.ToString();
    }
    public class Tuple<T1, T2>
    {
        public Tuple(T1 item1, T2 item2) { Item1 = item1; Item2 = item2; }
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public override string ToString() => "(" + Show(Item1) + ", " + Show(Item2) + ")";
        private static string Show(object value) => value == null ? "null" : value.ToString();
    }
    public class Tuple<T1, T2, T3>
    {
        public Tuple(T1 item1, T2 item2, T3 item3) { Item1 = item1; Item2 = item2; Item3 = item3; }
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        public override string ToString() => "(" + Show(Item1) + ", " + Show(Item2) + ", " + Show(Item3) + ")";
        private static string Show(object value) => value == null ? "null" : value.ToString();
    }
    public class Tuple<T1, T2, T3, T4>
    {
        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; }
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        public T4 Item4 { get; }
        public override string ToString() => "(" + Show(Item1) + ", " + Show(Item2) + ", " + Show(Item3) + ", " + Show(Item4) + ")";
        private static string Show(object value) => value == null ? "null" : value.ToString();
    }
    public class Tuple<T1, T2, T3, T4, T5>
    {
        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; }
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        public T4 Item4 { get; }
        public T5 Item5 { get; }
        public override string ToString() => "(" + Show(Item1) + ", " + Show(Item2) + ", " + Show(Item3) + ", " + Show(Item4) + ", " + Show(Item5) + ")";
        private static string Show(object value) => value == null ? "null" : value.ToString();
    }
    public class Tuple<T1, T2, T3, T4, T5, T6>
    {
        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; Item6 = item6; }
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        public T4 Item4 { get; }
        public T5 Item5 { get; }
        public T6 Item6 { get; }
        public override string ToString() => "(" + Show(Item1) + ", " + Show(Item2) + ", " + Show(Item3) + ", " + Show(Item4) + ", " + Show(Item5) + ", " + Show(Item6) + ")";
        private static string Show(object value) => value == null ? "null" : value.ToString();
    }
    public class Tuple<T1, T2, T3, T4, T5, T6, T7>
    {
        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; Item6 = item6; Item7 = item7; }
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        public T4 Item4 { get; }
        public T5 Item5 { get; }
        public T6 Item6 { get; }
        public T7 Item7 { get; }
        public override string ToString() => "(" + Show(Item1) + ", " + Show(Item2) + ", " + Show(Item3) + ", " + Show(Item4) + ", " + Show(Item5) + ", " + Show(Item6) + ", " + Show(Item7) + ")";
        private static string Show(object value) => value == null ? "null" : value.ToString();
    }
    public class Tuple<T1, T2, T3, T4, T5, T6, T7, TRest>
    {
        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, TRest rest) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; Item6 = item6; Item7 = item7; Rest = rest; }
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        public T4 Item4 { get; }
        public T5 Item5 { get; }
        public T6 Item6 { get; }
        public T7 Item7 { get; }
        public TRest Rest { get; }
        public override string ToString()
        {
            string rest = Show(Rest);
            return "(" + Show(Item1) + ", " + Show(Item2) + ", " + Show(Item3) + ", " + Show(Item4) + ", " + Show(Item5) + ", " + Show(Item6) + ", " + Show(Item7) + ", " + rest.Substring(1);
        }
        private static string Show(object value) => value == null ? "null" : value.ToString();
    }
}

namespace System.Collections
{
    public interface ICollection : IEnumerable { int Count { get; } }
    public interface IList : ICollection { object this[int index] { get; set; } }
    public interface IComparer { int Compare(object x, object y); }
    public interface IEqualityComparer { bool Equals(object x, object y); int GetHashCode(object obj); }
    public interface IStructuralEquatable { bool Equals(object other, IEqualityComparer comparer); int GetHashCode(IEqualityComparer comparer); }
    public interface IStructuralComparable { int CompareTo(object other, IComparer comparer); }
}

namespace System.Collections.Generic
{
    public interface ICollection<T> : IEnumerable<T>
    {
        int Count { get; }
        bool IsReadOnly { get; }
        void Add(T item);
        void Clear();
        bool Contains(T item);
        void CopyTo(T[] array, int arrayIndex);
        bool Remove(T item);
    }
    public interface IList<T> : ICollection<T>
    {
        T this[int index] { get; set; }
        int IndexOf(T item);
        void Insert(int index, T item);
        void RemoveAt(int index);
    }
    public interface IReadOnlyCollection<out T> : IEnumerable<T> { int Count { get; } }
    public interface IReadOnlyList<out T> : IReadOnlyCollection<T> { T this[int index] { get; } }
    public class KeyNotFoundException : Exception
    {
        public KeyNotFoundException() : base("The given key was not present in the dictionary.") { }
        public KeyNotFoundException(string message) : base(message) { }
    }
    public readonly struct KeyValuePair<TKey, TValue>
    {
        public KeyValuePair(TKey key, TValue value) { Key = key; Value = value; }
        public TKey Key { get; }
        public TValue Value { get; }
        public void Deconstruct(out TKey key, out TValue value) { key = Key; value = Value; }
        public override string ToString()
        {
            object key = Key, value = Value;
            return "[" + (key == null ? "" : key.ToString()) + ", " + (value == null ? "" : value.ToString()) + "]";
        }
    }
}

namespace System.ComponentModel
{
    public enum EditorBrowsableState { Always, Never, Advanced }
    [AttributeUsage(AttributeTargets.All)]
    public sealed class EditorBrowsableAttribute : Attribute
    {
        public EditorBrowsableAttribute(EditorBrowsableState state) { State = state; }
        public EditorBrowsableState State { get; }
    }
}

namespace System.Diagnostics
{
    [AttributeUsage(AttributeTargets.All)]
    public sealed class DebuggerNonUserCodeAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.All)]
    public sealed class DebuggerHiddenAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.All)]
    public sealed class DebuggerStepThroughAttribute : Attribute { }
    public enum DebuggerBrowsableState { Never = 0, Collapsed = 2, RootHidden = 3 }
    [AttributeUsage(AttributeTargets.All)]
    public sealed class DebuggerBrowsableAttribute : Attribute
    {
        public DebuggerBrowsableAttribute(DebuggerBrowsableState state) { State = state; }
        public DebuggerBrowsableState State { get; }
    }
    [AttributeUsage(AttributeTargets.All)]
    public sealed class DebuggerDisplayAttribute : Attribute
    {
        public DebuggerDisplayAttribute(string value) { Value = value; }
        public string Value { get; }
        public string Type { get; set; }
    }
    public sealed class DebuggableAttribute : Attribute
    {
        [Flags] public enum DebuggingModes { None = 0, Default = 1, DisableOptimizations = 256, IgnoreSymbolStoreSequencePoints = 2, EnableEditAndContinue = 4 }
        public DebuggableAttribute(DebuggingModes modes) { }
        public DebuggableAttribute(bool isJITTrackingEnabled, bool isJITOptimizerDisabled) { }
    }
}

namespace System.Diagnostics
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public sealed class DebuggerTypeProxyAttribute : Attribute
    {
        public DebuggerTypeProxyAttribute(string typeName) { }
        public DebuggerTypeProxyAttribute(Type type) { }
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    [Flags]
    public enum DynamicallyAccessedMemberTypes
    {
        None = 0, PublicParameterlessConstructor = 0x0001, PublicConstructors = 0x0003, NonPublicConstructors = 0x0004,
        PublicMethods = 0x0008, NonPublicMethods = 0x0010, PublicFields = 0x0020, NonPublicFields = 0x0040,
        PublicNestedTypes = 0x0080, NonPublicNestedTypes = 0x0100, PublicProperties = 0x0200, NonPublicProperties = 0x0400,
        PublicEvents = 0x0800, NonPublicEvents = 0x1000, Interfaces = 0x2000, All = -1
    }
    [AttributeUsage(AttributeTargets.All, Inherited = false)]
    public sealed class DynamicallyAccessedMembersAttribute : Attribute
    {
        public DynamicallyAccessedMembersAttribute(DynamicallyAccessedMemberTypes memberTypes) { MemberTypes = memberTypes; }
        public DynamicallyAccessedMemberTypes MemberTypes { get; }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = false)]
    public sealed class RequiresDynamicCodeAttribute : Attribute
    {
        public RequiresDynamicCodeAttribute(string message) { Message = message; }
        public string Message { get; }
    }
}
