using System.Runtime.CompilerServices;
using Internal.Runtime;

namespace System.Runtime
{
    // Calling an interface method on an object whose type isn't known at build
    // time. (When the compiler can tell which class is behind the interface, it
    // calls the method directly and none of this is involved.)
    //
    // Each such call site has a "dispatch cell": two words, the address of the
    // code to call and a description of the interface method. The compiler
    // points every cell at RhpInitialDynamicInterfaceDispatch (native/*.S),
    // which asks Resolve for the method and jumps to it.
    //
    // .NET then caches the answer in the cell; this doesn't, so each call costs
    // a search of the type's dispatch map. That's short for typical types, but
    // it isn't the two instructions of a virtual call.
    internal static unsafe class InterfaceDispatch
    {
        // The low bits of a cell's second word say how it names the interface.
        private const nint EncodingMask = 3;
        private const nint FullPointer = 1;
        private const nint IndirectRelativePointer = 2;
        private const nint RelativePointer = 3;

        // Implementation slots that mean "no usable implementation".
        private const int ReabstractedSlot = 0xFFFF;
        private const int DiamondSlot = 0xFFFE;

        [RuntimeExport("RhpResolveInterfaceMethod")]
        internal static nint Resolve(object obj, nint* cell)
        {
            if (obj == null)
                Environment.FailFast("NullReferenceException");

            // Cells for the same interface method sit together, followed by an
            // entry with no code address that carries the method's slot number.
            nint* last = cell;
            while (last[0] != 0)
                last += 2;
            if ((last[1] >> 16) != 0)
                Environment.FailFast("Unsupported kind of interface call");
            int slot = (ushort)last[1];

            MethodTable* type = obj._methodTable;
            nint target = FindImplementation(type, InterfaceOf(cell), slot);
            if (target == 0)
                Environment.FailFast("Interface method not implemented (variance isn't supported)");
            return target;
        }

#if WASM
        // WebAssembly has no way to jump to a method with the caller's
        // arguments still in place, so compiled code there asks for the
        // method's address and calls it itself.
        [RuntimeExport("RhpResolveInterfaceDispatch")]
        internal static nint ResolveForCall(object obj, nint* cell) => Resolve(obj, cell);
#endif

        private static MethodTable* InterfaceOf(nint* cell)
        {
            nint value = cell[1];
            nint encoding = value & EncodingMask;
            if (encoding == FullPointer)
                return (MethodTable*)(value & ~EncodingMask);
            if (encoding == 0)
                Environment.FailFast("Unsupported kind of interface call");

            // Relative to the word itself; only its low 32 bits are meaningful.
            nint address = ((nint)(cell + 1) + (int)value) & ~EncodingMask;
            return encoding == RelativePointer ? (MethodTable*)address : *(MethodTable**)address;
        }

        // Looks for the method in the object's type, then its base types: first
        // among the methods classes implement, then among the default
        // implementations interfaces provide.
        private static nint FindImplementation(MethodTable* objectType, MethodTable* interfaceType, int slot)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                for (MethodTable* type = objectType; type != null; type = type->BaseType)
                {
                    DispatchMap* map = type->DispatchMap;
                    if (map == null)
                        continue;

                    DispatchMap.Entry* entry = map->Entries + (pass == 0 ? 0 : map->StandardEntryCount);
                    int count = pass == 0 ? map->StandardEntryCount : map->DefaultEntryCount;
                    for (int i = 0; i < count; i++, entry++)
                    {
                        if (entry->InterfaceMethodSlot != slot || type->InterfaceMap[entry->InterfaceIndex] != interfaceType)
                            continue;

                        int implSlot = entry->ImplMethodSlot;
                        // An ordinary virtual method: take it from the object's
                        // own type, which may override it.
                        if (implSlot < type->NumVtableSlots)
                            return objectType->VTable[implSlot];
                        if (implSlot == ReabstractedSlot || implSlot == DiamondSlot)
                            Environment.FailFast("Interface method has no single implementation");
                        // A sealed one: it lives with the type that declares it.
                        return type->SealedVirtualSlot(implSlot - type->NumVtableSlots);
                    }
                }
            }
            return 0;
        }
    }
}
