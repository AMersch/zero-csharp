using System.Runtime.CompilerServices;
using Internal.Runtime;

namespace System.Runtime
{
    // The functions compiled code calls to allocate objects and to store object
    // references. Memory comes from calloc and is never freed: there is no
    // garbage collector.
    //
    // Storing an object reference also goes through runtime functions, with a
    // calling convention C# can't express; those are in native/*.S.
    //
    // On WebAssembly those are in wasm/runtime.c.
    internal static unsafe class RuntimeExports
    {
        [RuntimeExport("RhpNewFast")]
        internal static object RhpNewFast(MethodTable* type)
        {
            return Allocate(type, type->BaseSize);
        }

        // Arrays and strings: the element count follows the method table.
        [RuntimeExport("RhpNewArray")]
        internal static object RhpNewArray(MethodTable* type, int length)
        {
            if (length < 0)
                Environment.FailFast("Negative array length");
            object array = Allocate(type, type->BaseSize + (nuint)(uint)length * type->ComponentSize);
            Unsafe.As<byte, int>(ref Unsafe.As<RawData>(array).Data) = length;
            return array;
        }

        [RuntimeExport("RhpNewArrayFast")]
        internal static object RhpNewArrayFast(MethodTable* type, int length) => RhpNewArray(type, length);

        [RuntimeExport("RhpNewPtrArrayFast")]
        internal static object RhpNewPtrArrayFast(MethodTable* type, int length) => RhpNewArray(type, length);

        // On 32-bit targets (WebAssembly, DOS), objects holding a long or a
        // double ask for 8-byte alignment. calloc and ObjectAllocator give it,
        // so these allocate as usual.
        [RuntimeExport("RhpNewFastAlign8")]
        internal static object RhpNewFastAlign8(MethodTable* type) => RhpNewFast(type);

        [RuntimeExport("RhpNewArrayFastAlign8")]
        internal static object RhpNewArrayFastAlign8(MethodTable* type, int length) => RhpNewArray(type, length);

        // A boxed long or double on a 32-bit target: its value follows the
        // 4-byte method table pointer, so the object must start 4 bytes past
        // an 8-byte boundary for the value to be aligned.
        [RuntimeExport("RhpNewFastMisalign")]
        internal static object RhpNewFastMisalign(MethodTable* type)
        {
            nint memory = AllocateZeroed(type->BaseSize + 4);
            if ((memory & 7) == 0)
                memory += 4;
            *(MethodTable**)memory = type;
            return Unsafe.As<nint, object>(ref memory);
        }

        // Boxing: a struct converted to object or to an interface becomes an
        // object holding a copy of it.
        internal static object RhBox(MethodTable* type, ref byte data)
        {
            if (type->IsNullable)
                Environment.FailFast("Boxing a nullable struct isn't supported");
            object box = RhpNewFast(type);
            ref byte target = ref Unsafe.As<RawData>(box).Data;
            for (uint i = 0; i < type->ValueTypeSize; i++)
                Unsafe.Add(ref target, (nint)i) = Unsafe.Add(ref data, (nint)i);
            return box;
        }

        // Unboxing: `(Point)obj` gets a reference to the copy inside the box.
        internal static ref byte RhUnbox2(MethodTable* type, object obj)
        {
            if (obj == null)
                Environment.FailFast("NullReferenceException");
            RhUnboxTypeTest(type, obj.m_pEEType);
            return ref Unsafe.As<RawData>(obj).Data;
        }

        internal static void RhUnboxTypeTest(MethodTable* type, MethodTable* boxType)
        {
            if (type != boxType)
                Environment.FailFast("InvalidCastException");
        }

        internal static void RhUnboxNullable(ref byte data, MethodTable* type, object obj)
            => Environment.FailFast("Unboxing to a nullable struct isn't supported");

#if !WASM
        // Calls into native code that aren't marked SuppressGCTransition are
        // bracketed by these, so a garbage collector could run meanwhile.
        // (WebAssembly's versions are in wasm/runtime.c: compiled code there
        // calls them without the hidden argument every C# method takes.)
        [RuntimeExport("RhpPInvoke")]
        internal static void RhpPInvoke(void* frame) { }

        [RuntimeExport("RhpPInvokeReturn")]
        internal static void RhpPInvokeReturn(void* frame) { }

        // The same in the other direction: native code calling managed code
        // (the program's entry point, [UnmanagedCallersOnly] callbacks).
        [RuntimeExport("RhpReversePInvoke")]
        internal static void RhpReversePInvoke(void* frame) { }

        [RuntimeExport("RhpReversePInvokeReturn")]
        internal static void RhpReversePInvokeReturn(void* frame) { }
#endif

        // Compiled code polls here so a garbage collector could suspend it.
        [RuntimeExport("RhpGcPoll")]
        internal static void RhpGcPoll() { }

        // `throw`: a panic. Print the message and end the program; nothing
        // unwinds (see System/Exceptions.cs). `throw null` throws a
        // NullReferenceException in .NET, so it reports that.
        [RuntimeExport("RhpThrowEx")]
        internal static void RhpThrowEx(object exception)
        {
            Exception e = exception as Exception;
            if (e == null)
                Environment.FailFast("Unhandled exception: NullReferenceException");
            string message = e.Message;
            Environment.FailFast(message == null ? "Unhandled exception" : "Unhandled exception: " + message);
        }

        // Where compiled code ends up when it would throw and has nowhere to go.
        [RuntimeExport("RhpFallbackFailFast")]
        internal static void RhpFallbackFailFast() => Environment.FailFast("Unhandled exception");

        // An object straight from calloc, ignoring any ObjectAllocator: for
        // objects zerocore keeps for the rest of the run (see Type).
        internal static T AllocateCalloc<T>() where T : class
            => Unsafe.As<T>(AllocateCalloc(MethodTable.Of<T>()));

        internal static object AllocateCalloc(MethodTable* type)
        {
            System.Threading.Interlocked.Add(ref s_allocatedBytes, (long)type->BaseSize);
            nint memory = (nint)Platform.calloc(1, type->BaseSize);
            if (memory == 0)
                Environment.FailFast("Out of memory");
            *(MethodTable**)memory = type;
            return Unsafe.As<nint, object>(ref memory);
        }

        // Every byte allocated with calloc so far, for GC.GetTotalAllocatedBytes.
        internal static long s_allocatedBytes;

        // C# requires new objects to be zeroed, which calloc guarantees, and
        // so must an ObjectAllocator.
        private static object Allocate(MethodTable* type, nuint size)
        {
            nint memory = AllocateZeroed(size);
            *(MethodTable**)memory = type;
            return Unsafe.As<nint, object>(ref memory);
        }

        // size zeroed bytes, 8-byte aligned, from this thread's ObjectAllocator
        // or calloc.
        private static nint AllocateZeroed(nuint size)
        {
            nint memory;
            void* setting = System.Threading.Volatile.Read(ref ObjectAllocator.s_threadsWithSetting) != 0
                ? ObjectAllocator.CurrentSetting() : null;
            if (ObjectAllocator.HasAllocate(setting))
            {
                memory = (nint)ObjectAllocator.Allocate(setting, size);
            }
            else
            {
                System.Threading.Interlocked.Add(ref s_allocatedBytes, (long)size);
                memory = (nint)Platform.calloc(1, size);
            }
            if (memory == 0)
                Environment.FailFast("Out of memory");
            return memory;
        }
    }
}
