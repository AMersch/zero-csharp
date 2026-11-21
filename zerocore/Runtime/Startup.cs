using System;
using System.Runtime;
using System.Runtime.CompilerServices;

namespace Internal.Runtime.CompilerHelpers
{
    // The compiler's generated entry point calls InitializeCommandLineArgs
    // before Main. That's where static fields get set up.
    internal static unsafe class StartupCodeHelpers
    {
        private const int GCStaticRegionSection = 201;

        // The low bits of a static base's address, as the compiler emits it.
        private const nint Uninitialized = 1;
        private const nint HasPreInitializedData = 2;
        private const nint FlagsMask = 3;

#if WASM
        // WebAssembly can't take a data symbol's address from C#, so
        // wasm/runtime.c hands over the module header.
        [RuntimeImport("*", "ZeroGetModuleHeader"), MethodImpl(MethodImplOptions.InternalCall)]
        private static extern ModuleHeader* GetModuleHeader();
#else
        // The linker defines these two symbols around the __modules section,
        // which holds a pointer to each module's header. They're data, declared
        // as functions only to get at their addresses.
        [RuntimeImport("*", "__start___modules"), MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void ModulesStart();

        [RuntimeImport("*", "__stop___modules"), MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void ModulesStop();

        // Not inlined on purpose: the compiler can't know where the linker will
        // put these symbols, and when it sees both addresses in one method it
        // decides the loop between them never runs.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ModuleHeader** ModulesBegin() => (ModuleHeader**)(delegate*<void>)&ModulesStart;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ModuleHeader** ModulesEnd() => (ModuleHeader**)(delegate*<void>)&ModulesStop;
#endif

        private static int s_argc;
        private static byte** s_argv;

        private static void InitializeCommandLineArgs(int argc, byte** argv)
        {
            s_argc = argc;
            s_argv = argv;
#if WASM
            InitializeModule(GetModuleHeader());
#else
            ModuleHeader** end = ModulesEnd();
            for (ModuleHeader** module = ModulesBegin(); module < end; module++)
            {
                if (*module != null)
                    InitializeModule(*module);
            }
#endif
        }

        // What the compiler calls instead on Windows, where .NET's arguments
        // are UTF-16. win/runtime.c's entry point converts them to UTF-8 before
        // calling Main, so they arrive as on Linux under another type.
        private static void InitializeCommandLineArgsW(int argc, char** argv)
            => InitializeCommandLineArgs(argc, (byte**)argv);

        // For Main(string[] args) and top-level statements: the arguments after
        // the program's name, converted from UTF-8.
        private static string[] GetMainMethodArguments()
        {
            int count = s_argc > 1 ? s_argc - 1 : 0;
            string[] args = new string[count];
            for (int i = 0; i < count; i++)
                args[i] = FromUtf8(s_argv[i + 1]);
            return args;
        }

        private static string FromUtf8(byte* text)
        {
            // First pass counts UTF-16 units, second pass writes them.
            int length = DecodeUtf8(text, ref Unsafe.As<byte, char>(ref *text), write: false);
            string result = string.Allocate(length);
            DecodeUtf8(text, ref result.FirstChar, write: true);
            return result;
        }

        private static int DecodeUtf8(byte* text, ref char output, bool write)
        {
            int length = 0;
            while (*text != 0)
            {
                uint lead = *text++;
                int extra = lead < 0x80 ? 0 : lead < 0xC0 ? -1 : lead < 0xE0 ? 1 : lead < 0xF0 ? 2 : lead < 0xF8 ? 3 : -1;
                uint value = extra <= 0 ? lead : lead & (0x3Fu >> extra);
                bool valid = extra >= 0;
                for (int i = 0; i < extra && valid; i++)
                {
                    if ((*text & 0xC0) != 0x80)
                        valid = false; // cut short: the next byte starts a new character
                    else
                        value = (value << 6) | (*text++ & 0x3Fu);
                }
                if (!valid || value > 0x10FFFF || (value >= 0xD800 && value < 0xE000))
                    value = 0xFFFD; // the replacement character

                if (value < 0x10000)
                {
                    if (write)
                        Unsafe.Add(ref output, length) = (char)value;
                    length++;
                }
                else
                {
                    if (write)
                    {
                        value -= 0x10000;
                        Unsafe.Add(ref output, length) = (char)(0xD800 + (value >> 10));
                        Unsafe.Add(ref output, length + 1) = (char)(0xDC00 + (value & 0x3FF));
                    }
                    length += 2;
                }
            }
            return length;
        }

        private struct ModuleHeader
        {
            public uint Signature;
            public ushort MajorVersion;
            public ushort MinorVersion;
            public uint Flags;
            public ushort NumberOfSections;
            public byte EntrySize;
            public byte EntryType;
        }

        private struct ModuleSection
        {
            public int Id;
            public int Flags;
            public byte* Start;
            public byte* End;
        }

        private static void InitializeModule(ModuleHeader* header)
        {
            ModuleSection* sections = (ModuleSection*)(header + 1);
            for (int i = 0; i < header->NumberOfSections; i++)
            {
                if (sections[i].Id == GCStaticRegionSection)
                {
                    InitializeStatics(sections[i].Start, sections[i].End);
                    if (s_regionStart == null)
                    {
                        s_regionStart = sections[i].Start;
                        s_regionEnd = sections[i].End;
                    }
                }
            }
        }

        // The GC static region, kept for StaticsReferInto. A bflat program is
        // one module, so it has one.
        private static byte* s_regionStart;
        private static byte* s_regionEnd;

        // Whether any static field holds a reference to an object in
        // [start, end). Lib's arena uses it, with debug checks on, to catch a
        // static field left pointing into memory about to be freed. It reads
        // every word of every static base, so a non-reference field whose
        // value happens to be such an address counts too; static bases hold
        // little else.
        internal static bool StaticsReferInto(byte* start, byte* end)
        {
            bool relative = MethodTable.SupportsRelativePointers;
            for (byte* entry = s_regionStart; entry < s_regionEnd; entry += relative ? sizeof(int) : sizeof(nint))
            {
                nint* slot = relative ? (nint*)ReadRelative(entry) : *(nint**)entry;
                byte* statics = (byte*)*slot;
                if (((nint)statics & FlagsMask) != 0)
                    continue; // never initialized
                nuint size = (*(MethodTable**)statics)->BaseSize;
                for (nuint offset = (nuint)sizeof(nint); offset + (nuint)sizeof(nint) <= size; offset += (nuint)sizeof(nint))
                {
                    byte* value = *(byte**)(statics + offset);
                    if (value >= start && value < end)
                        return true;
                }
            }
            return false;
        }

        // Static fields that hold object references live in one object per type
        // (its "GC static base"), which the garbage collector would normally
        // allocate. The region lists a slot for each; a slot starts out holding
        // the base's method table, tagged Uninitialized, and ends up holding the
        // object, which is what compiled code reads.
        private static void InitializeStatics(byte* region, byte* regionEnd)
        {
            bool relative = MethodTable.SupportsRelativePointers;
            for (byte* entry = region; entry < regionEnd; entry += relative ? sizeof(int) : sizeof(nint))
            {
                nint* slot = relative ? (nint*)ReadRelative(entry) : *(nint**)entry;
                nint value = relative ? (nint)ReadRelative(slot) : *slot;
                if ((value & Uninitialized) == 0)
                    continue;

                MethodTable* type = (MethodTable*)(value & ~FlagsMask);
                object statics = RuntimeExports.RhpNewFast(type);
                if ((value & HasPreInitializedData) != 0)
                {
                    // Fields the compiler evaluated at build time (string
                    // literals, for instance): copy them over the new object.
                    byte* source = relative ? ReadRelative((int*)slot + 1) : (byte*)slot[1];
                    ref byte target = ref Unsafe.As<RawData>(statics).Data;
                    nint size = (nint)type->BaseSize - 2 * sizeof(nint);
                    for (nint i = 0; i < size; i++)
                        Unsafe.Add(ref target, i) = source[i];
                }
                *slot = Unsafe.As<object, nint>(ref statics);
            }
        }

        private static byte* ReadRelative(void* address) => (byte*)address + *(int*)address;
    }

    // What compiled code calls to turn a type, method or field named in the
    // source (typeof, an array initializer's data) into a handle: the handle is
    // the address the compiler passes.
    internal static unsafe class LdTokenHelpers
    {
        // typeof(T): the compiler skips the handle and asks for the Type itself.
        private static Type GetRuntimeType(MethodTable* type) => Type.Of(type);

        private static RuntimeTypeHandle GetRuntimeTypeHandle(MethodTable* type)
        {
            RuntimeTypeHandle handle;
            *(nint*)&handle = (nint)type;
            return handle;
        }

        private static RuntimeMethodHandle GetRuntimeMethodHandle(nint signature)
        {
            RuntimeMethodHandle handle;
            *(nint*)&handle = signature;
            return handle;
        }

        private static RuntimeFieldHandle GetRuntimeFieldHandle(nint signature)
        {
            RuntimeFieldHandle handle;
            *(nint*)&handle = signature;
            return handle;
        }
    }

    // What compiled code calls where .NET would throw.
    internal static class ThrowHelpers
    {
        private static void ThrowNullReferenceException() => Environment.FailFast("NullReferenceException");
        private static void ThrowIndexOutOfRangeException() => Environment.FailFast("IndexOutOfRangeException");
        private static void ThrowOverflowException() => Environment.FailFast("OverflowException");
        private static void ThrowDivideByZeroException() => Environment.FailFast("DivideByZeroException");
        private static void ThrowArrayTypeMismatchException() => Environment.FailFast("ArrayTypeMismatchException");
        private static void ThrowInvalidProgramException() => Environment.FailFast("InvalidProgramException");
        private static void ThrowPlatformNotSupportedException() => Environment.FailFast("PlatformNotSupportedException");
        private static void ThrowArgumentException() => Environment.FailFast("ArgumentException");
        private static void ThrowArgumentOutOfRangeException() => Environment.FailFast("ArgumentOutOfRangeException");
        private static void ThrowDataMisalignedException() => Environment.FailFast("DataMisalignedException");
    }
}

namespace System.Runtime.CompilerServices
{
    // Compiled code calls these before touching the static fields of a type
    // whose static constructor hasn't run yet.
    internal static unsafe class ClassConstructorRunner
    {
        private static object CheckStaticClassConstructionReturnGCStaticBase(ref StaticClassConstructionContext context, object gcStaticBase)
        {
            CheckStaticClassConstruction(ref context);
            return gcStaticBase;
        }

        private static nint CheckStaticClassConstructionReturnNonGCStaticBase(ref StaticClassConstructionContext context, nint nonGcStaticBase)
        {
            CheckStaticClassConstruction(ref context);
            return nonGcStaticBase;
        }

        // Single-threaded: clearing the address first also covers a constructor
        // that touches its own type's statics.
        private static void CheckStaticClassConstruction(ref StaticClassConstructionContext context)
        {
            nint constructor = context.cctorMethodAddress;
            if (constructor != 0)
            {
                context.cctorMethodAddress = 0;
                ((delegate*<void>)constructor)();
            }
        }
    }

    // The address of a type's static constructor; zero once it has run.
    public struct StaticClassConstructionContext
    {
        public nint cctorMethodAddress;
    }
}
