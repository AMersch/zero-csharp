// A WebAssembly object file's data and functions, for the 6502.
//
// Wasm.cs turns such a file into assembly and C for the 386; this is the same
// for the 6502, where three things differ:
//
//   - An address is 2 bytes. The data keeps its 32-bit layout (see
//     Ll2Mos.cs), so each 4-byte slot that holds one gets the address and two
//     zero bytes.
//   - A pointer and a 32-bit integer are passed in different registers, and
//     WebAssembly calls both i32. The functions' real types are read from the
//     program's LLVM IR, where each of them is declared or defined.
//   - C's int is 16 bits, but the functions C# imports ([DllImport]) are
//     called with C#'s 32-bit ints. So each import is bound to cs_<name>,
//     which c64/runtime.c defines with 32-bit parameters: C#'s calloc is
//     cs_calloc there, and the C library's own calloc keeps its name and its
//     types.
namespace ZeroBitcode;

sealed class Mos(IEnumerable<string> modules) : Target
{
    static readonly Dictionary<string, string> CTypes = new()
    {
        ["ptr"] = "void *", ["i32"] = "u32", ["i16"] = "unsigned short", ["i8"] = "unsigned char", ["void"] = "void",
    };

    readonly (Dictionary<string, Ll2Mos.Signature> Defined, Dictionary<string, HashSet<Ll2Mos.Signature>> Declared)
        _signatures = Ll2Mos.Signatures(modules);

    public override string Tool => "zero-bitcode wasm2mos";
    public override string Comment => ";";
    public override string Prefix => "";
    public override string U32 => "unsigned long";

    public override string Address(string expression, string section)
    {
        // An import's "cell": the slot compiled code reads the function's
        // address from.
        if (section.StartsWith(".rodata.cell.", StringComparison.Ordinal))
            expression = "\"cs_" + expression[1..];
        return $"\t.short {expression}\n\t.short 0";
    }

    public override (string Result, List<string> Parameters)? Signature(string name)
    {
        Ll2Mos.Signature signature;
        if (_signatures.Defined.TryGetValue(name, out var defined))
            signature = defined;
        else if (_signatures.Declared.TryGetValue(name, out var declared))
            signature = declared.Order().First();
        else if (name.StartsWith("unbox_", StringComparison.Ordinal))
            // Called only through a method table, so not declared anywhere:
            // the method of a struct, for a boxed one. Same types.
            return Signature(name["unbox_".Length..]);
        else
            throw Module.Fail($"{name} isn't declared in the program's IR, so its types are unknown");

        string CType(string irType) => CTypes.TryGetValue(irType, out string? c)
            ? c
            : throw Module.Fail($"{name} has a parameter or result of type '{irType}', which isn't supported");
        return (CType(signature.Result), signature.Parameters.Select(CType).ToList());
    }
}

static class Wasm2Mos
{
    public static void Run(string input, string output, string[] modules) =>
        Wasm.Convert(input, output, new Mos(modules));
}
