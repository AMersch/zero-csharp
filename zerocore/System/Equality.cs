namespace System
{
    public interface IEquatable<T>
    {
        bool Equals(T other);
    }

    public interface IFormatProvider { }
}

namespace System.Collections.Generic
{
    // What records and anonymous types compare and hash their fields with.
    // Default is created the first time each T asks for it, and kept.
    // Comparing calls T's Equals(object) override, through a constrained call
    // when T is a struct; for the primitives the compiler sees the exact
    // method and needs no box.
    public abstract class EqualityComparer<T>
    {
        private static EqualityComparer<T> s_default;

        public static EqualityComparer<T> Default => s_default ??= new ObjectEqualityComparer<T>();

        public abstract bool Equals(T x, T y);

        public abstract int GetHashCode(T obj);
    }

    internal sealed class ObjectEqualityComparer<T> : EqualityComparer<T>
    {
        public override bool Equals(T x, T y)
        {
            if (x == null)
                return y == null;
            if (y == null)
                return false;
            return x.Equals(y);
        }

        public override int GetHashCode(T obj) => obj == null ? 0 : obj.GetHashCode();
    }
}
