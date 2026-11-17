// Attributes the C# compiler, or the native compiler, looks for by name.

namespace System
{
    public abstract class Attribute
    {
        protected Attribute() { }
    }

    public enum AttributeTargets
    {
        Assembly = 0x1,
        Module = 0x2,
        Class = 0x4,
        Struct = 0x8,
        Enum = 0x10,
        Constructor = 0x20,
        Method = 0x40,
        Property = 0x80,
        Field = 0x100,
        Event = 0x200,
        Interface = 0x400,
        Parameter = 0x800,
        Delegate = 0x1000,
        ReturnValue = 0x2000,
        GenericParameter = 0x4000,
        All = 0x7FFF,
    }

    public sealed class AttributeUsageAttribute : Attribute
    {
        public AttributeUsageAttribute(AttributeTargets validOn) { }
        public bool AllowMultiple { get; set; }
        public bool Inherited { get; set; }
    }

    public sealed class FlagsAttribute : Attribute { }

    public sealed class ObsoleteAttribute : Attribute
    {
        public ObsoleteAttribute() { }
        public ObsoleteAttribute(string message) { }
        public ObsoleteAttribute(string message, bool error) { }
    }

    public sealed class ParamArrayAttribute : Attribute { }

    public sealed class ThreadStaticAttribute : Attribute { }
}

namespace System.Reflection
{
    public sealed class DefaultMemberAttribute : Attribute
    {
        public DefaultMemberAttribute(string memberName) { }
    }
}

namespace System.Diagnostics
{
    public sealed class ConditionalAttribute : Attribute
    {
        public ConditionalAttribute(string conditionString) { }
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    public sealed class DoesNotReturnAttribute : Attribute { }
}

namespace System.Runtime
{
    // Binds a method to a native symbol, called directly (no marshalling).
    internal sealed class RuntimeImportAttribute : Attribute
    {
        public RuntimeImportAttribute(string entry) { }
        public RuntimeImportAttribute(string dllName, string entry) { }
    }

    // Exports a method under a native symbol name, for the compiler's generated
    // code to call.
    internal sealed class RuntimeExportAttribute : Attribute
    {
        public RuntimeExportAttribute(string entry) { }
    }
}

namespace System.Runtime.CompilerServices
{
    public enum MethodImplOptions
    {
        Unmanaged = 0x4,
        NoInlining = 0x8,
        ForwardRef = 0x10,
        Synchronized = 0x20,
        NoOptimization = 0x40,
        PreserveSig = 0x80,
        AggressiveInlining = 0x100,
        AggressiveOptimization = 0x200,
        InternalCall = 0x1000,
    }

    public sealed class MethodImplAttribute : Attribute
    {
        public MethodImplAttribute(MethodImplOptions methodImplOptions) { }
    }

    internal sealed class IntrinsicAttribute : Attribute { }

    public sealed class ExtensionAttribute : Attribute { }

    public sealed class CompilerGeneratedAttribute : Attribute { }

    public sealed class IsReadOnlyAttribute : Attribute { }

    public sealed class IsByRefLikeAttribute : Attribute { }

    public sealed class IsUnmanagedAttribute : Attribute { }

    public sealed class ScopedRefAttribute : Attribute { }

    public sealed class RequiresLocationAttribute : Attribute { }

    public sealed class ParamCollectionAttribute : Attribute { }

    public sealed class ModuleInitializerAttribute : Attribute { }

    public sealed class SkipLocalsInitAttribute : Attribute { }

    public sealed class UnscopedRefAttribute : Attribute { }

    public sealed class RequiredMemberAttribute : Attribute { }

    public sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) { }
        public bool IsOptional { get; set; }
    }

    public sealed class RefSafetyRulesAttribute : Attribute
    {
        public RefSafetyRulesAttribute(int version) { }
    }

    public sealed class MemorySafetyRulesAttribute : Attribute
    {
        public MemorySafetyRulesAttribute(int version) { }
    }

    public sealed class InlineArrayAttribute : Attribute
    {
        public InlineArrayAttribute(int length) { }
    }

    public sealed class FixedBufferAttribute : Attribute
    {
        public FixedBufferAttribute(Type elementType, int length) { }
    }

    public sealed class UnsafeValueTypeAttribute : Attribute { }

    public sealed class TupleElementNamesAttribute : Attribute
    {
        public TupleElementNamesAttribute(string[] transformNames) { }
    }

    public static class IsVolatile { }

    public static class IsExternalInit { }

    public static class RuntimeFeature
    {
        public const string PortablePdb = nameof(PortablePdb);
        public const string DefaultImplementationsOfInterfaces = nameof(DefaultImplementationsOfInterfaces);
        public const string UnmanagedSignatureCallingConvention = nameof(UnmanagedSignatureCallingConvention);
        public const string CovariantReturnsOfClasses = nameof(CovariantReturnsOfClasses);
        public const string ByRefFields = nameof(ByRefFields);
        public const string ByRefLikeGenerics = nameof(ByRefLikeGenerics);
        public const string VirtualStaticsInInterfaces = nameof(VirtualStaticsInInterfaces);
        public const string NumericIntPtr = nameof(NumericIntPtr);
    }

    public class CallConvCdecl { }

    public class CallConvStdcall { }

    public class CallConvSuppressGCTransition { }
}

namespace System.Runtime.InteropServices
{
    public enum LayoutKind
    {
        Sequential = 0,
        Explicit = 2,
        Auto = 3,
    }

    public enum CharSet
    {
        None = 1,
        Ansi = 2,
        Unicode = 3,
        Auto = 4,
    }

    public enum CallingConvention
    {
        Winapi = 1,
        Cdecl = 2,
        StdCall = 3,
        ThisCall = 4,
        FastCall = 5,
    }

    public enum UnmanagedType
    {
        Bool = 0x2,
        I1 = 0x3,
        U1 = 0x4,
        I2 = 0x5,
        U2 = 0x6,
        I4 = 0x7,
        U4 = 0x8,
        I8 = 0x9,
        U8 = 0xA,
        R4 = 0xB,
        R8 = 0xC,
        LPStr = 0x14,
        LPWStr = 0x15,
        SysInt = 0x1F,
        SysUInt = 0x20,
        FunctionPtr = 0x26,
    }

    public sealed class StructLayoutAttribute : Attribute
    {
        public StructLayoutAttribute(LayoutKind layoutKind) { }
        public int Pack;
        public int Size;
        public CharSet CharSet;
    }

    public sealed class FieldOffsetAttribute : Attribute
    {
        public FieldOffsetAttribute(int offset) { }
    }

    public sealed class DllImportAttribute : Attribute
    {
        public DllImportAttribute(string dllName) { }
        public string EntryPoint;
        public CharSet CharSet;
        public bool SetLastError;
        public bool ExactSpelling;
        public CallingConvention CallingConvention;
    }

    public sealed class UnmanagedCallersOnlyAttribute : Attribute
    {
        public UnmanagedCallersOnlyAttribute() { }
        public string EntryPoint;
        public Type[] CallConvs;
    }

    public sealed class SuppressGCTransitionAttribute : Attribute { }

    public sealed class MarshalAsAttribute : Attribute
    {
        public MarshalAsAttribute(UnmanagedType unmanagedType) { }
    }

    public sealed class InAttribute : Attribute { }

    public sealed class OutAttribute : Attribute { }
}
