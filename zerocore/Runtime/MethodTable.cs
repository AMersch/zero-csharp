using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Internal.Runtime
{
    // What the compiler emits for every type, and what an object's first field
    // points to. The layout is the compiler's:
    //
    //   [this struct] [virtual method slots: pointers] [interface map: MethodTable*]
    //   [optional fields: type manager, writable data, dispatch map, finalizer,
    //    sealed virtual slots, ...]
    //
    // An optional field is a 32-bit relative pointer where the compiler uses
    // those (Linux), a full pointer otherwise (WebAssembly).
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MethodTable
    {
        private const uint KindMask = 0x00030000;
        private const uint ParameterizedKind = 0x00020000; // arrays, pointers, byrefs
        private const uint HasDispatchMapFlag = 0x00040000;
        private const uint HasFinalizerFlag = 0x00100000;
        private const uint HasSealedVTableEntriesFlag = 0x00400000;
        private const uint GenericVarianceFlag = 0x00800000;
        private const uint ElementTypeMask = 0x7C000000;
        private const int ElementTypeShift = 26;
        private const uint ElementTypeNullable = 0x12;
        private const uint ElementTypeInterface = 0x15;
        private const uint ValueTypePaddingMask = 0x00E0; // in the low 16 bits, for types without a component size
        private const int ValueTypePaddingShift = 5;
        private const uint HasComponentSizeFlag = 0x80000000;

        private uint _flags; // the low 16 bits are the component size, for arrays and strings
        private uint _baseSize;
        private MethodTable* _relatedType; // base type; for an array, its element type
        private ushort _numVtableSlots;
        private ushort _numInterfaces;
        private uint _hashCode;

        // The method table of T, resolved by the compiler.
        [Intrinsic]
        internal static MethodTable* Of<T>() => Of<T>();

        // Whether the compiler emitted 32-bit relative pointers in its data
        // structures (Linux) or full ones (WebAssembly).
        internal static bool SupportsRelativePointers
        {
            [Intrinsic]
            get => SupportsRelativePointers;
        }

        internal uint BaseSize => _baseSize;

        internal uint ComponentSize => (_flags & HasComponentSizeFlag) != 0 ? (ushort)_flags : 0u;

        internal bool IsParameterized => (_flags & KindMask) == ParameterizedKind;

        internal bool IsInterface => !IsParameterized && ((_flags & ElementTypeMask) >> ElementTypeShift) == ElementTypeInterface;

        // For an enum, its underlying type's: 4 sbyte, 5 byte, 6 short,
        // 7 ushort, 8 int, 9 uint, 10 long, 11 ulong (.NET's EETypeElementType).
        internal uint ElementTypeKind => (_flags & ElementTypeMask) >> ElementTypeShift;

        internal bool IsNullable => !IsParameterized && ((_flags & ElementTypeMask) >> ElementTypeShift) == ElementTypeNullable;

        // The size of a struct's fields: its boxed size without the object
        // header, the method table pointer and the padding added for boxing.
        internal uint ValueTypeSize => _baseSize - (uint)(2 * sizeof(nint)) - ((_flags & ValueTypePaddingMask) >> ValueTypePaddingShift);

        internal bool HasGenericVariance => (_flags & GenericVarianceFlag) != 0;

        internal MethodTable* BaseType => IsParameterized ? null : _relatedType;

        internal MethodTable* ElementType => _relatedType;

        internal int NumInterfaces => _numInterfaces;

        internal int NumVtableSlots => _numVtableSlots;

        private byte* Start => (byte*)Unsafe.AsPointer(ref this);

        // The addresses of the type's virtual methods.
        internal nint* VTable => (nint*)(Start + sizeof(MethodTable));

        // Every interface the type implements, inherited ones included.
        internal MethodTable** InterfaceMap => (MethodTable**)(VTable + _numVtableSlots);

        private static int OptionalFieldSize => SupportsRelativePointers ? sizeof(int) : sizeof(nint);

        private static byte* ReadOptionalField(byte* field)
            => SupportsRelativePointers ? field + *(int*)field : *(byte**)field;

        // The dispatch map comes third, after the type manager and writable data.
        private byte* DispatchMapField => (byte*)(InterfaceMap + _numInterfaces) + 2 * OptionalFieldSize;

        // Which of the type's methods implements which interface method; null
        // if the type itself implements none.
        internal DispatchMap* DispatchMap
            => (_flags & HasDispatchMapFlag) != 0 ? (DispatchMap*)ReadOptionalField(DispatchMapField) : null;

        // Virtual methods that can't be overridden any further aren't in the
        // vtable; they're in a table of their own, of relative or full pointers
        // like the optional fields.
        internal nint SealedVirtualSlot(int index)
        {
            if ((_flags & HasSealedVTableEntriesFlag) == 0)
                return 0;
            byte* field = DispatchMapField;
            if ((_flags & HasDispatchMapFlag) != 0)
                field += OptionalFieldSize;
            if ((_flags & HasFinalizerFlag) != 0)
                field += OptionalFieldSize;
            byte* table = ReadOptionalField(field);
            return (nint)ReadOptionalField(table + index * OptionalFieldSize);
        }
    }

    // Layout defined by the compiler: four counts, then the entries, standard
    // ones first.
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct DispatchMap
    {
        internal struct Entry
        {
            public ushort InterfaceIndex;      // into the type's interface map
            public ushort InterfaceMethodSlot; // which method of that interface
            public ushort ImplMethodSlot;      // the implementing method's slot on the type
        }

        public ushort StandardEntryCount;      // methods implemented by the class
        public ushort DefaultEntryCount;       // default implementations from interfaces
        public ushort StandardStaticEntryCount;
        public ushort DefaultStaticEntryCount;

        internal Entry* Entries => (Entry*)((byte*)Unsafe.AsPointer(ref this) + 4 * sizeof(ushort));
    }

    // Gives access to an object's memory after its method table pointer.
    internal class RawData
    {
        public byte Data;
    }
}
