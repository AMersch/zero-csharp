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
    // points every cell at RhpInitialDynamicInterfaceDispatch (native/*.S).
    //
    // The first call from a site replaces the cell's second word with a pointer
    // to a small cache: the classes seen at that site so far and the method each
    // one resolved to. From then on the stub compares the object's class with
    // the cache's entries and jumps: close to a virtual call for a site that
    // sees one class, a few compares more for each further one. A class that
    // isn't in the cache falls through to Resolve, which searches the class's
    // dispatch map and adds it. Once the cache is full, each call with yet
    // another class pays for that search.
    internal static unsafe class InterfaceDispatch
    {
        // The low bits of a cell's second word say how it names the interface.
        // Zero means it has been replaced by a pointer to a Cache.
        private const nint EncodingMask = 3;
        private const nint CachePointer = 0;
        private const nint FullPointer = 1;
        private const nint IndirectRelativePointer = 2;
        private const nint RelativePointer = 3;

        // Implementation slots that mean "no usable implementation".
        private const int ReabstractedSlot = 0xFFFF;
        private const int DiamondSlot = 0xFFFE;

        private const int CacheEntries = 4;

        // native/*.S reads the entries, so they stay first and stay four.
        //
        // Threads may fill a cache at the same time. An entry is claimed by
        // atomically setting its Type from null, and only the thread that
        // claimed it writes its Target. A reader that finds the type but no
        // target yet takes the slow path, so nobody jumps to a method that was
        // resolved for another class.
        private struct Cache
        {
            public Entry Entry0, Entry1, Entry2, Entry3;
            public MethodTable* Interface;
            public nint Slot;
        }

        private struct Entry
        {
            public nint Type;
            public nint Target;
        }

        [RuntimeExport("RhpResolveInterfaceMethod")]
        internal static nint Resolve(object obj, nint* cell)
        {
            if (obj == null)
                Environment.FailFast("NullReferenceException");

            MethodTable* type = obj._methodTable;
            Cache* cache = CacheOf(cell);

            Entry* free = null;
            Entry* entry = (Entry*)cache;
            for (int i = 0; i < CacheEntries; i++, entry++)
            {
                if (entry->Type == (nint)type)
                {
                    if (entry->Target != 0)
                        return entry->Target;
                    break; // another thread is filling it in: search without caching
                }
                if (entry->Type == 0)
                {
                    free = entry;
                    break;
                }
            }

            nint target = FindImplementation(type, cache->Interface, (int)cache->Slot);
            if (target == 0)
                Environment.FailFast("Interface method not implemented (variance isn't supported)");

            if (free != null && Threading.Interlocked.CompareExchange(ref free->Type, (nint)type, 0) == 0)
                free->Target = target;
            return target;
        }

        // The cell's cache, created on the site's first call.
        private static Cache* CacheOf(nint* cell)
        {
            nint value = cell[1];
            if ((value & EncodingMask) == CachePointer)
                return (Cache*)value;

            // Cells for the same interface method sit together, followed by an
            // entry with no code address that carries the method's slot number.
            nint* last = cell;
            while (last[0] != 0)
                last += 2;
            if ((last[1] >> 16) != 0)
                Environment.FailFast("Unsupported kind of interface call");

            Cache* cache = (Cache*)Platform.calloc(1, (nuint)sizeof(Cache));
            if (cache == null)
                Environment.FailFast("Out of memory");
            cache->Interface = InterfaceOf(cell, value);
            cache->Slot = (ushort)last[1];

            // If another thread got there first, use its cache. Ours is then
            // never used again: a few words lost, once.
            nint previous = Threading.Interlocked.CompareExchange(ref cell[1], (nint)cache, value);
            return previous == value ? cache : (Cache*)previous;
        }

#if WASM
        // WebAssembly has no way to jump to a method with the caller's
        // arguments still in place, so compiled code there asks for the
        // method's address and calls it itself. It expects a pointer back, not
        // an integer: the same thing on a 32-bit processor, but on the 6502
        // (./build.sh --c64) the two come back in different registers.
        [RuntimeExport("RhpResolveInterfaceDispatch")]
        internal static void* ResolveForCall(object obj, nint* cell) => (void*)Resolve(obj, cell);
#endif

        private static MethodTable* InterfaceOf(nint* cell, nint value)
        {
            nint encoding = value & EncodingMask;
            if (encoding == FullPointer)
                return (MethodTable*)(value & ~EncodingMask);

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
