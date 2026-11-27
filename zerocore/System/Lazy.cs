// Lazy<T>: a value made by a factory the first time it's read, then kept.
// F#'s `lazy expr` compiles to one (FSharp.Core's Lazy.Create), and C#
// programs can use it directly.
//
// Thread-safe as .NET's default (LazyThreadSafetyMode.ExecutionAndPublication):
// the factory runs once, and other threads reading Value meanwhile wait for
// it, spinning (there's nothing to block on without a runtime). Reading Value
// from inside its own factory throws InvalidOperationException (which ends the
// program in zero mode), as .NET does: each thread has an id for that check,
// so the waiting thread can tell recursion from another thread's work.
//
// The factory delegate is dropped once the value exists, so whatever it
// captured can be freed.
//
// A factory that throws inside a Catch.Run (lib/Catch.cs) leaves the Lazy as
// it was before: the next read of Value runs the factory again. (.NET keeps
// the exception and throws it again on every read; here that exception may
// be in an arena that is freed meanwhile.)

using System.Runtime;
using System.Runtime.CompilerServices;
using System.Threading;

namespace System
{
    // For LazyThread.RunFactory: a call C can make, whatever T is.
    internal interface ILazyFactory
    {
        void RunFactory();
    }

    public unsafe class Lazy<T> : ILazyFactory
    {
        private Func<T> _factory;
        private T _value;
        private int _state;   // NotCreated, Creating or Created
        private int _creator; // the id of the thread running the factory

        private const int NotCreated = 0, Creating = 1, Created = 2;

        public Lazy(Func<T> valueFactory)
        {
            if (valueFactory == null)
                throw new ArgumentNullException("valueFactory");
            _factory = valueFactory;
        }

        /// <summary>A Lazy whose value already exists.</summary>
        public Lazy(T value)
        {
            _value = value;
            _state = Created;
        }

        public bool IsValueCreated => Volatile.Read(ref _state) == Created;

        public T Value => Volatile.Read(ref _state) == Created ? _value : Create();

        private T Create()
        {
            int me = LazyThread.Id;
            while (true)
            {
                int state = Interlocked.CompareExchange(ref _state, Creating, NotCreated);
                if (state == NotCreated)
                {
                    Volatile.Write(ref _creator, me);
                    // Null in a program that hasn't called Catch.Run: a throw
                    // ends it then, and there's nothing to put back.
                    if (CatchFrames.s_throw == null)
                    {
                        _value = _factory();
                    }
                    else
                    {
                        // A mark of this Lazy's own, to come back to and
                        // throw again from.
                        object self = this;
                        CatchFrame mark;
                        mark.Saved = null;
                        mark.Handles = null;
                        mark.Inner = true;
                        CatchFrames.RunInner(&mark, &LazyThread.RunFactory, Unsafe.AsPointer(ref self));
                        Exception thrown = mark.Exception;
                        if (thrown != null)
                        {
                            Volatile.Write(ref _creator, 0);
                            Volatile.Write(ref _state, NotCreated);
                            throw thrown;
                        }
                    }
                    T value = _value;
                    _factory = null;
                    Volatile.Write(ref _state, Created);
                    return value;
                }
                if (state == Created)
                    return _value;
                if (Volatile.Read(ref _creator) == me)
                    throw new InvalidOperationException("ValueFactory attempted to access the Value property of this instance.");
            }
        }

        void ILazyFactory.RunFactory() => _value = _factory();

        public override string ToString() => IsValueCreated ? (_value?.ToString() ?? "") : "Value is not created.";
    }

    // A number for each thread, from 1, given on its first Lazy it creates.
    internal static unsafe class LazyThread
    {
        // What Lazy's mark runs: argument points at the Lazy.
        internal static void RunFactory(void* argument)
            => Unsafe.As<ILazyFactory>(Unsafe.AsRef<object>(argument)).RunFactory();

        private static int s_last;

        [ThreadStatic]
        private static int t_id;

        internal static int Id
        {
            get
            {
                int id = t_id;
                if (id == 0)
                    t_id = id = Interlocked.Increment(ref s_last);
                return id;
            }
        }
    }
}
