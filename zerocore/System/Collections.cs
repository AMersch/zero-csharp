// The enumeration interfaces. The C# compiler wants them declared before it
// compiles any foreach, even one over an array (which it turns into an
// indexed loop). Arrays don't implement them here.

namespace System.Collections
{
    public interface IEnumerable
    {
        IEnumerator GetEnumerator();
    }

    public interface IEnumerator
    {
        object Current { get; }
        bool MoveNext();
        void Reset();
    }
}

namespace System.Collections.Generic
{
    public interface IEnumerable<out T> : IEnumerable
    {
        new IEnumerator<T> GetEnumerator();
    }

    public interface IEnumerator<out T> : IEnumerator, IDisposable
    {
        new T Current { get; }
    }
}
