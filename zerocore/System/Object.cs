using System.Runtime;
using System.Runtime.CompilerServices;
using Internal.Runtime;

namespace System
{
    // Every object starts with a pointer to its type's method table.
    public unsafe class Object
    {
        internal MethodTable* _methodTable;

        public Object() { }
    }

    public abstract class ValueType { }

    public abstract class Enum : ValueType { }

    public struct Nullable<T> where T : struct
    {
        private readonly bool _hasValue;
        private T _value;

        public Nullable(T value)
        {
            _hasValue = true;
            _value = value;
        }

        public readonly bool HasValue => _hasValue;

        public readonly T Value
        {
            get
            {
                if (!_hasValue)
                    Environment.FailFast("Nullable object must have a value");
                return _value;
            }
        }

        public readonly T GetValueOrDefault() => _value;

        public readonly T GetValueOrDefault(T defaultValue) => _hasValue ? _value : defaultValue;

        public static implicit operator T?(T value) => new T?(value);

        public static explicit operator T(T? value) => value.Value;
    }

    public abstract class Type { }

    // The compiler asks for this type by name.
    internal sealed class RuntimeType : Type { }

    public struct RuntimeTypeHandle
    {
        private nint _value;
    }

    public struct RuntimeMethodHandle
    {
        private nint _value;
    }

    public struct RuntimeFieldHandle
    {
        private nint _value;
    }

    public interface IDisposable
    {
        void Dispose();
    }

    public struct ValueTuple<T1, T2>
    {
        public T1 Item1;
        public T2 Item2;

        public ValueTuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }
    }

    public struct ValueTuple<T1, T2, T3>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;

        public ValueTuple(T1 item1, T2 item2, T3 item3)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
        }
    }

    public struct ValueTuple<T1, T2, T3, T4>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
        }
    }

    public struct ValueTuple<T1, T2, T3, T4, T5>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
        }
    }

    public struct ValueTuple<T1, T2, T3, T4, T5, T6>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;
        public T6 Item6;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
        }
    }

    public struct ValueTuple<T1, T2, T3, T4, T5, T6, T7>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;
        public T6 Item6;
        public T7 Item7;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
            Item7 = item7;
        }
    }

    // The fields and the Initialize methods are the compiler's contract for
    // creating and invoking delegates.
    public abstract class Delegate
    {
        private object _firstParameter;
        private object _helperObject;
        private nint _extraFunctionPointerOrData;
        private nint _functionPointer;

        // A delegate to an instance method: call _functionPointer with _firstParameter as `this`.
        private void InitializeClosedInstance(object firstParameter, nint functionPointer)
        {
            if (firstParameter == null)
                Environment.FailFast("Delegate to an instance method needs a non-null target");
            _functionPointer = functionPointer;
            _firstParameter = firstParameter;
        }

        // A delegate to a static method: the thunk receives the delegate as `this`
        // and calls the real method, stored in _extraFunctionPointerOrData.
        private void InitializeOpenStaticThunk(object firstParameter, nint functionPointer, nint functionPointerThunk)
        {
            _firstParameter = this;
            _functionPointer = functionPointerThunk;
            _extraFunctionPointerOrData = functionPointer;
        }
    }

    public abstract class MulticastDelegate : Delegate { }

    public delegate void Action();
    public delegate void Action<in T>(T arg);
    public delegate void Action<in T1, in T2>(T1 arg1, T2 arg2);
    public delegate TResult Func<out TResult>();
    public delegate TResult Func<in T, out TResult>(T arg);
    public delegate TResult Func<in T1, in T2, out TResult>(T1 arg1, T2 arg2);
}
