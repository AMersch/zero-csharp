using System.Runtime;
using Internal.Runtime;

namespace System
{
    // typeof(T) and GetType(), for comparing types: records compare their
    // EqualityContract this way. With no reflection a Type knows nothing but
    // which type it is: no name, no members.
    //
    // Each type gets one Type, made the first time it's asked for and kept in
    // a list here. Those objects come straight from calloc, never from an
    // arena's object scope, so a record compared inside a scope can't leave
    // the list pointing into the arena.
    public abstract unsafe class Type
    {
        private protected MethodTable* _type;

        private struct Entry
        {
            public MethodTable* Key;
            public Type Value;
            public Entry* Next;
        }

        private static nint s_entries; // Entry*, the list's head

        public static Type GetTypeFromHandle(RuntimeTypeHandle handle) => Of((MethodTable*)handle._value);

        internal static Type Of(MethodTable* type)
        {
            for (Entry* e = (Entry*)System.Threading.Volatile.Read(ref s_entries); e != null; e = e->Next)
            {
                if (e->Key == type)
                    return e->Value;
            }

            Type result = RuntimeExports.AllocateCalloc<RuntimeType>();
            result._type = type;
            Entry* entry = (Entry*)Platform.calloc(1, (nuint)sizeof(Entry));
            if (entry == null)
                Environment.FailFast("Out of memory");
            entry->Key = type;
            entry->Value = result;
            // Two threads may add the same type; == compares the method tables,
            // so either Type is right.
            nint head;
            do
            {
                head = System.Threading.Volatile.Read(ref s_entries);
                entry->Next = (Entry*)head;
            }
            while (System.Threading.Interlocked.CompareExchange(ref s_entries, (nint)entry, head) != head);
            return result;
        }

        public static bool operator ==(Type a, Type b)
            => (object)a == (object)b || ((object)a != null && (object)b != null && a._type == b._type);

        public static bool operator !=(Type a, Type b) => !(a == b);

        public override bool Equals(object obj) => obj is Type other && this == other;

        public override int GetHashCode() => unchecked((int)(nint)_type ^ (int)((long)(nint)_type >> 32));

        public override string ToString() => "type";
    }

    // The Type every type actually gets. The compiler asks for it by name.
    internal sealed class RuntimeType : Type { }
}
