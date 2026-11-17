namespace System
{
    // T[] derives from this. The length is the first field after the method table.
    public abstract class Array
    {
        private int _length;

        public int Length => _length;
    }

    // The compiler makes every T[] derive from this. In .NET it's where arrays
    // get IList<T> and friends; here it adds nothing.
    public class Array<T> : Array { }
}
