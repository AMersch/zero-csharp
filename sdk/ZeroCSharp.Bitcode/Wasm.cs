// Rewrites a WebAssembly object file as sources for another processor.
//
// NativeAOT-LLVM writes a program's code as LLVM bitcode, which clang can
// compile for any processor, but its data (type descriptions, string
// literals, static fields) as a WebAssembly object file. This turns such a
// file into:
//
//   OUT.s   the data, as assembly: the same bytes, with each address the
//           WebAssembly linker would have filled in written as a symbol
//           reference
//   OUT.c   the few functions the file contains, translated to C (they are
//           tiny accessors for static fields; anything else is refused)
//
// Target says what differs between processors. As it is, it is the 386 in a
// 32-bit Windows object file (DOS): symbols get the leading underscore that C
// symbols have there, so the output links with clang's i386-pc-windows-gnu
// code. Wasm2Mos.cs has the 6502's.
using System.Text;

namespace ZeroBitcode;

/// <summary>The 386, in a 32-bit Windows object file.</summary>
class Target
{
    public virtual string Tool => "zero-bitcode wasm2i386";
    public virtual string Comment => "#";           // starts a comment in assembly
    public virtual string Prefix => "_";            // before every C symbol
    public virtual string U32 => "unsigned int";    // C's 32-bit integer

    /// <summary>The assembly for a 4-byte slot holding an address.</summary>
    public virtual string Address(string expression, string section) => $"\t.long {expression}";

    /// <summary>A function's real C types, or null when every one of them can
    /// be passed as a 32-bit integer, as here.</summary>
    public virtual (string Result, List<string> Parameters)? Signature(string name) => null;
}

sealed class Reader(byte[] data, int pos = 0, int? end = null)
{
    public readonly byte[] Data = data;
    public int Pos = pos;
    public int End = end ?? data.Length;

    public bool More => Pos < End;

    public byte Byte() => Data[Pos++];

    public long Uleb()
    {
        long result = 0;
        int shift = 0;
        while (true)
        {
            byte b = Byte();
            result |= (long)(b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0)
                return result;
        }
    }

    public long Sleb()
    {
        long result = 0;
        int shift = 0;
        while (true)
        {
            byte b = Byte();
            result |= (long)(b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0)
            {
                if ((b & 0x40) != 0)
                    result -= 1L << shift;
                return result;
            }
        }
    }

    public byte[] Take(int n)
    {
        Pos += n;
        return Data[(Pos - n)..Pos];
    }

    public string Name() => Encoding.UTF8.GetString(Take((int)Uleb()));
}

sealed class Symbol
{
    public int Kind;
    public long Flags;
    public bool Undefined;
    public string Name = "";
    public long Index, Segment, Offset, Size, Section;
}

record struct Relocation(int Kind, long Offset, long Symbol, long Addend);

sealed class Module
{
    // Relocation types (WebAssembly tool conventions, Linking.md).
    public const int FunctionIndexLeb = 0, TableIndexSleb = 1, TableIndexI32 = 2;
    public const int MemoryAddrLeb = 3, MemoryAddrSleb = 4, MemoryAddrI32 = 5;
    static readonly int[] WithAddend = [MemoryAddrLeb, MemoryAddrSleb, MemoryAddrI32];

    public const int SymFunction = 0, SymData = 1, SymGlobal = 2, SymSection = 3;
    public const long FlagBindingLocal = 0x02, FlagUndefined = 0x10, FlagExplicitName = 0x40;

    public readonly byte[] Data;
    public readonly List<(List<byte> Params, List<byte> Results)> Types = [];
    public readonly List<(string Name, long Type)> FuncImports = [];
    public List<long> FuncTypes = [];                               // type index of each function defined here
    public readonly List<(List<byte> Locals, int Start, int End, int Entry)> Bodies = [];
    public readonly List<(int Start, byte[] Bytes)> Segments = [];  // start within the DATA section
    public readonly List<(string Name, long Align)> SegmentInfo = [];   // alignment as a power of two
    public readonly List<Symbol> Symbols = [];
    public readonly Dictionary<long, List<Relocation>> Relocs = []; // by target section index
    public readonly Dictionary<int, long> SectionIndex = [];        // section id -> index among all sections
    public int CodeStart, DataStart;

    public static ToolFailure Fail(string message) => new("wasm2i386", message);

    public Module(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual("\0asm\u0001\0\0\0"u8))
            throw Fail("not a WebAssembly object file");
        Data = data;

        var r = new Reader(data, 8);
        long index = 0;
        while (r.More)
        {
            byte kind = r.Byte();
            int size = (int)r.Uleb();
            var body = new Reader(data, r.Pos, r.Pos + size);
            r.Pos += size;
            if (kind == 0)
            {
                string name = body.Name();
                if (name == "linking")
                    ReadLinking(body);
                else if (name.StartsWith("reloc.", StringComparison.Ordinal))
                    ReadRelocs(body);
            }
            else
            {
                SectionIndex[kind] = index;
                switch (kind)
                {
                    case 1: ReadTypes(body); break;
                    case 2: ReadImports(body); break;
                    case 3:
                        long count = body.Uleb();
                        FuncTypes = [];
                        for (long i = 0; i < count; i++)
                            FuncTypes.Add(body.Uleb());
                        break;
                    case 10: ReadCode(body); break;
                    case 11: ReadData(body); break;
                }
            }
            index++;
        }
    }

    void ReadTypes(Reader r)
    {
        for (long n = r.Uleb(); n > 0; n--)
        {
            if (r.Byte() != 0x60)
                throw Fail("unsupported type section entry");
            var parameters = new List<byte>();
            for (long i = r.Uleb(); i > 0; i--)
                parameters.Add(r.Byte());
            var results = new List<byte>();
            for (long i = r.Uleb(); i > 0; i--)
                results.Add(r.Byte());
            Types.Add((parameters, results));
        }
    }

    void ReadImports(Reader r)
    {
        for (long n = r.Uleb(); n > 0; n--)
        {
            r.Name();
            string field = r.Name();
            byte kind = r.Byte();
            switch (kind)
            {
                case 0: FuncImports.Add((field, r.Uleb())); break;
                case 1: r.Byte(); Limits(r); break;     // table: element type, limits
                case 2: Limits(r); break;               // memory: limits
                case 3: r.Byte(); r.Byte(); break;      // global: type, mutability
                default: throw Fail($"unsupported import kind {kind}");
            }
        }
    }

    static void Limits(Reader r)
    {
        byte flags = r.Byte();
        r.Uleb();
        if ((flags & 1) != 0)
            r.Uleb();
    }

    void ReadCode(Reader r)
    {
        CodeStart = r.Pos;
        for (long n = r.Uleb(); n > 0; n--)
        {
            int entry = r.Pos;
            int size = (int)r.Uleb();
            int end = r.Pos + size;
            var localTypes = new List<byte>();
            for (long groups = r.Uleb(); groups > 0; groups--)
            {
                long count = r.Uleb();
                byte type = r.Byte();
                for (long i = 0; i < count; i++)
                    localTypes.Add(type);
            }
            Bodies.Add((localTypes, r.Pos, end, entry));
            r.Pos = end;
        }
    }

    void ReadData(Reader r)
    {
        DataStart = r.Pos;
        for (long n = r.Uleb(); n > 0; n--)
        {
            long flags = r.Uleb();
            if ((flags & 2) != 0)
                r.Uleb();                // memory index
            if ((flags & 1) == 0)        // active segment: skip its offset expression
            {
                while (r.Byte() != 0x0B)
                {
                }
            }
            int size = (int)r.Uleb();
            Segments.Add((r.Pos - DataStart, r.Take(size)));
        }
    }

    void ReadLinking(Reader r)
    {
        if (r.Uleb() != 2)
            throw Fail("unsupported linking section version");
        while (r.More)
        {
            byte kind = r.Byte();
            var sub = new Reader(r.Data, r.Pos);
            int size = (int)sub.Uleb();
            sub.End = sub.Pos + size;
            r.Pos = sub.End;
            if (kind == 5)          // segment info
            {
                for (long n = sub.Uleb(); n > 0; n--)
                {
                    string name = sub.Name();
                    long align = sub.Uleb();
                    sub.Uleb();
                    SegmentInfo.Add((name, align));
                }
            }
            else if (kind == 8)     // symbol table
            {
                for (long n = sub.Uleb(); n > 0; n--)
                    Symbols.Add(ReadSymbol(sub));
            }
        }
    }

    Symbol ReadSymbol(Reader r)
    {
        int kind = r.Byte();
        long flags = r.Uleb();
        var sym = new Symbol { Kind = kind, Flags = flags, Undefined = (flags & FlagUndefined) != 0 };
        switch (kind)
        {
            case SymFunction:
                sym.Index = r.Uleb();
                sym.Name = !sym.Undefined || (flags & FlagExplicitName) != 0
                    ? r.Name()
                    : FuncImports[(int)sym.Index].Name;
                break;
            case SymData:
                sym.Name = r.Name();
                if (!sym.Undefined)
                {
                    sym.Segment = r.Uleb();
                    sym.Offset = r.Uleb();
                    sym.Size = r.Uleb();
                }
                break;
            case SymSection:
                sym.Section = r.Uleb();
                break;
            default:
                throw Fail($"unsupported symbol kind {kind}");
        }
        return sym;
    }

    void ReadRelocs(Reader r)
    {
        long target = r.Uleb();
        if (!Relocs.TryGetValue(target, out var entries))
            Relocs[target] = entries = [];
        for (long n = r.Uleb(); n > 0; n--)
        {
            int kind = r.Byte();
            long offset = r.Uleb();
            long symbol = r.Uleb();
            long addend = WithAddend.Contains(kind) ? r.Sleb() : 0;
            entries.Add(new Relocation(kind, offset, symbol, addend));
        }
    }
}

static class Wasm
{
    /// <summary>Writes OUT.s and, if the file has functions, OUT.c.</summary>
    public static void Convert(string input, string output, Target target)
    {
        var module = new Module(File.ReadAllBytes(input));
        WriteData(module, output + ".s", target);
        if (module.Bodies.Count > 0)
            WriteCode(module, output + ".c", target);
    }

    static string AsmSymbol(string name, Target target) => "\"" + target.Prefix + name + "\"";

    // ---- the data ------------------------------------------------------------------

    static void WriteData(Module m, string output, Target target)
    {
        var lines = new List<string> { $"{target.Comment} Generated by {target.Tool} from a WebAssembly object file.", "" };
        long dataSection = m.SectionIndex.GetValueOrDefault(11, -1);
        List<Relocation> relocs = m.Relocs.GetValueOrDefault(dataSection, []).OrderBy(e => e.Offset).ToList();
        var defined = new Dictionary<long, List<(long Offset, string Name, bool Local)>>();
        foreach (Symbol sym in m.Symbols)
        {
            if (sym.Kind == Module.SymData && !sym.Undefined)
            {
                if (!defined.TryGetValue(sym.Segment, out var list))
                    defined[sym.Segment] = list = [];
                list.Add((sym.Offset, sym.Name, (sym.Flags & Module.FlagBindingLocal) != 0));
            }
        }

        for (int index = 0; index < m.Segments.Count; index++)
        {
            var (start, data) = m.Segments[index];
            var (name, align) = index < m.SegmentInfo.Count ? m.SegmentInfo[index] : (".data", 2L);
            var labels = new Dictionary<long, List<(string Name, bool Local)>>();
            foreach (var (offset, sym, local) in defined.GetValueOrDefault(index, []))
            {
                if (!labels.TryGetValue(offset, out var list))
                    labels[offset] = list = [];
                list.Add((sym, local));
            }
            var mine = new Dictionary<long, Relocation>();
            foreach (Relocation e in relocs)
            {
                if (start <= e.Offset && e.Offset < start + data.Length)
                    mine[e.Offset - start] = e;
            }

            // Everything goes into .data: the startup code writes to some of
            // it (static field storage), and one section keeps the layout
            // simple.
            lines.Add($"{target.Comment} segment {index}: {name}");
            lines.Add("\t.data");
            lines.Add($"\t.p2align {Math.Max(align, 2)}");
            int pos = 0;
            var run = new List<byte>();

            void Flush()
            {
                for (int i = 0; i < run.Count; i += 16)
                    lines.Add("\t.byte " + string.Join(",", run.Skip(i).Take(16)));
                run.Clear();
            }

            while (pos <= data.Length)
            {
                if (labels.TryGetValue(pos, out var here))
                {
                    Flush();
                    foreach (var (sym, local) in here)
                    {
                        if (!local)
                            lines.Add($"\t.globl {AsmSymbol(sym, target)}");
                        lines.Add($"{AsmSymbol(sym, target)}:");
                    }
                }
                if (pos == data.Length)
                    break;
                if (mine.TryGetValue(pos, out Relocation reloc))
                {
                    Flush();
                    if (reloc.Kind != Module.MemoryAddrI32 && reloc.Kind != Module.TableIndexI32)
                        throw Module.Fail($"unsupported relocation type {reloc.Kind} in data");
                    int at = pos;
                    if (labels.Keys.Any(o => at < o && o < at + 4))
                        throw Module.Fail("a symbol starts inside a relocated word");
                    string expression = AsmSymbol(m.Symbols[(int)reloc.Symbol].Name, target);
                    if (reloc.Addend != 0)
                        expression += reloc.Addend > 0 ? $"+{reloc.Addend}" : $"{reloc.Addend}";
                    lines.Add(target.Address(expression, name));
                    pos += 4;
                }
                else
                {
                    run.Add(data[pos]);
                    pos++;
                }
            }
            Flush();
            lines.Add("");
        }

        Files.WriteText(output, string.Join("\n", lines));
    }

    // ---- the functions, translated to C ------------------------------------------------

    const byte I32 = 0x7F;

    // Symbol names can hold characters C identifiers can't ("Func_2<Int32>"),
    // so the C code uses made-up names and gives the real one as an assembler
    // label.
    static string CLabel(string name, Target target)
    {
        if (name.Contains('"') || name.Contains('\\'))
            throw Module.Fail($"symbol '{name}' can't be written as an assembler label");
        return $"__asm__(\"{target.Prefix}{name}\")";
    }

    // Where a pointer and a 32-bit integer aren't passed the same way
    // (Target.Signature), the translated code still computes with 32-bit
    // integers, in static functions, and these convert at the edges.
    static string ToU32(string ctype, string expression) =>
        ctype.EndsWith('*') ? $"(u32)(__UINTPTR_TYPE__){expression}" : $"(u32){expression}";

    static string FromU32(string ctype, string expression) =>
        ctype.EndsWith('*') ? $"({ctype})(__UINTPTR_TYPE__){expression}" : $"({ctype}){expression}";

    static string TypedParameters(List<string> types) =>
        types.Count == 0 ? "void" : string.Join(", ", types.Select((t, i) => $"{t} a{i}"));

    static string Indent(int depth) => new(' ', 4 * depth);

    static void WriteCode(Module m, string output, Target target)
    {
        long codeSection = m.SectionIndex.TryGetValue(10, out long found) ? found : long.MinValue;
        var relocs = new Dictionary<long, Relocation>();
        foreach (Relocation e in m.Relocs.GetValueOrDefault(codeSection, []))
            relocs[e.Offset] = e;
        var names = new Dictionary<long, string>();     // function index -> name
        foreach (Symbol sym in m.Symbols)
        {
            if (sym.Kind == Module.SymFunction)
                names[sym.Index] = sym.Name;
        }
        int imports = m.FuncImports.Count;

        (int Parameters, int Results) Signature(long index)
        {
            long typeIndex = index < imports ? m.FuncImports[(int)index].Type : m.FuncTypes[(int)(index - imports)];
            var (parameters, results) = m.Types[(int)typeIndex];
            if (parameters.Concat(results).Any(t => t != I32) || results.Count > 1)
                throw Module.Fail("only functions of 32-bit integers are supported");
            return (parameters.Count, results.Count);
        }

        var decls = new List<string>();
        var bodies = new List<string>();
        var dataDecls = new HashSet<string>(StringComparer.Ordinal);

        (string Result, List<string> Parameters)? TypedSignature(string name, int count, int results)
        {
            var typed = target.Signature(name);
            if (typed is { } t && (t.Parameters.Count != count || (t.Result != "void") != (results != 0)))
                throw Module.Fail($"{name} has {count} parameters here and {t.Parameters.Count} where it is called or defined");
            return typed;
        }

        // Declares a function with its real signature, so that calling it and
        // taking its address agree. Returns the name to call it by, its number
        // of parameters, whether it has a result, and the name to take its
        // address by.
        (string Name, int Count, int Results, string Real) Declare(long symbol)
        {
            Symbol function = m.Symbols[(int)symbol];
            var (count, results) = Signature(function.Index);
            string name = $"f{function.Index}";
            string returns = results != 0 ? "u32" : "void";
            var typed = TypedSignature(function.Name, count, results);
            if (typed is not { } t)
            {
                string plain = $"{returns} {name}({(count == 0 ? "void" : string.Join(", ", Enumerable.Repeat("u32", count)))})"
                               + $" {CLabel(function.Name, target)};";
                if (!decls.Contains(plain))
                    decls.Add(plain);
                return (name, count, results, name);
            }
            string real = $"r{function.Index}";
            string decl = $"{t.Result} {real}({TypedParameters(t.Parameters)}) {CLabel(function.Name, target)};";
            if (!decls.Contains(decl))
            {
                decls.Add(decl);
                string parameters = count == 0 ? "void" : string.Join(", ", Enumerable.Range(0, count).Select(i => $"u32 a{i}"));
                if (function.Index < imports)
                {
                    // Defined in the compiled code: a way to call it with integers.
                    string call = $"{real}({string.Join(", ", t.Parameters.Select((p, i) => FromU32(p, $"a{i}")))})";
                    string body = results != 0 ? $"return {ToU32(t.Result, call)};" : $"{call};";
                    // (Unused when only the function's address is taken.)
                    decls.Add($"__attribute__((unused)) static {returns} {name}({parameters}) {{ {body} }}");
                }
                else
                {
                    decls.Add($"static {returns} {name}({parameters});");
                }
            }
            return (name, count, results, real);
        }

        string DeclareData(long symbol)
        {
            string name = $"d{symbol}";
            dataDecls.Add($"extern char {name}[] {CLabel(m.Symbols[(int)symbol].Name, target)};");
            return name;
        }

        for (int number = 0; number < m.Bodies.Count; number++)
        {
            var (localTypes, start, end, _) = m.Bodies[number];
            long index = imports + number;
            var (nparams, nresults) = Signature(index);
            if (localTypes.Any(t => t != I32))
                throw Module.Fail("only 32-bit integer locals are supported");
            int nlocals = nparams + localTypes.Count;
            var code = new List<string>();
            var stack = new List<string>();
            int temp = 0;
            int depth = 1;
            var r = new Reader(m.Data, start, end);

            void Push(string expression)
            {
                code.Add(Indent(depth) + $"u32 t{temp} = {expression};");
                stack.Add($"t{temp}");
                temp++;
            }

            string Pop()
            {
                string top = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                return top;
            }

            Relocation RelocAt(int pos, int expected)
            {
                if (!relocs.TryGetValue(pos - m.CodeStart, out Relocation entry) || entry.Kind != expected)
                    throw Module.Fail("expected a relocation in a function body");
                return entry;
            }

            while (r.More)
            {
                byte op = r.Byte();
                switch (op)
                {
                    case 0x41:                      // i32.const
                    {
                        int at = r.Pos;
                        long value = r.Sleb();
                        if (!relocs.TryGetValue(at - m.CodeStart, out Relocation entry))
                        {
                            Push($"{value & 0xFFFFFFFFL}u");
                        }
                        else if (entry.Kind == Module.MemoryAddrSleb)     // the address of data
                        {
                            string data = DeclareData(entry.Symbol);
                            Push($"(u32){data} + {entry.Addend & 0xFFFFFFFFL}u");
                        }
                        else if (entry.Kind == Module.TableIndexSleb)     // the address of a function
                        {
                            string real = Declare(entry.Symbol).Real;
                            Push(target.Signature(m.Symbols[(int)entry.Symbol].Name) is null
                                ? $"(u32)&{real}"
                                : $"(u32)(__UINTPTR_TYPE__)&{real}");
                        }
                        else
                        {
                            throw Module.Fail($"unsupported relocation type {entry.Kind} in code");
                        }
                        break;
                    }
                    case 0x20:                      // local.get
                        Push($"l{r.Uleb()}");
                        break;
                    case 0x21:                      // local.set
                    {
                        long local = r.Uleb();
                        code.Add(Indent(depth) + $"l{local} = {Pop()};");
                        break;
                    }
                    case 0x22:                      // local.tee
                    {
                        long local = r.Uleb();
                        code.Add(Indent(depth) + $"l{local} = {stack[^1]};");
                        break;
                    }
                    case 0x6A:                      // i32.add
                    {
                        string b = Pop(), a = Pop();
                        Push($"{a} + {b}");
                        break;
                    }
                    case 0x28:                      // i32.load
                    {
                        r.Uleb();
                        long offset = r.Uleb();
                        Push($"*(u32 *)({Pop()} + {offset}u)");
                        break;
                    }
                    case 0x04:                      // if (no result)
                        if (r.Byte() != 0x40)
                            throw Module.Fail("only 'if' blocks without a result are supported");
                        code.Add(Indent(depth) + $"if ({Pop()}) {{");
                        depth++;
                        break;
                    case 0x10:                      // call
                    {
                        int at = r.Pos;
                        r.Uleb();
                        Relocation entry = RelocAt(at, Module.FunctionIndexLeb);
                        var (name, count, results, _) = Declare(entry.Symbol);
                        var args = new List<string>();
                        for (int i = 0; i < count; i++)
                            args.Add(Pop());
                        args.Reverse();
                        string call = $"{name}({string.Join(", ", args)})";
                        if (results != 0)
                            Push(call);
                        else
                            code.Add(Indent(depth) + call + ";");
                        break;
                    }
                    case 0x1A:                      // drop
                        Pop();
                        break;
                    case 0x0B:                      // end
                        depth--;
                        if (depth > 0)
                            code.Add(Indent(depth) + "}");
                        break;
                    default:
                        throw Module.Fail($"unsupported instruction 0x{op:x2} in function {names.GetValueOrDefault(index, "None")}");
                }
            }
            if (depth != 0)
                throw Module.Fail("unbalanced blocks in a function body");

            string parametersText = nparams == 0 ? "void" : string.Join(", ", Enumerable.Range(0, nparams).Select(i => $"u32 l{i}"));
            string head = $"{(nresults != 0 ? "u32" : "void")} f{index}({parametersText})";
            string functionName = names[index];
            var typed = TypedSignature(functionName, nparams, nresults);
            var text = typed is null
                ? new List<string> { $"// {functionName}", $"{head} {CLabel(functionName, target)};", head, "{" }
                : new List<string> { $"// {functionName}", $"static {head};", $"static {head}", "{" };
            for (int i = nparams; i < nlocals; i++)
                text.Add($"    u32 l{i} = 0;");
            text.AddRange(code);
            if (nresults != 0)
                text.Add($"    return {Pop()};");
            text.Add("}");
            if (typed is { } t)
            {
                // The function under its real name and with its real types.
                string real = $"{t.Result} r{index}({TypedParameters(t.Parameters)})";
                string call = $"f{index}({string.Join(", ", t.Parameters.Select((p, i) => ToU32(p, $"a{i}")))})";
                text.AddRange(["", $"{real} {CLabel(functionName, target)};", real, "{",
                               nresults != 0 ? $"    return {FromU32(t.Result, call)};" : $"    {call};", "}"]);
            }
            bodies.Add(string.Join("\n", text));
        }

        var all = dataDecls.Order(StringComparer.Ordinal).Concat(decls);
        Files.WriteText(output,
            $"// Generated by {target.Tool} from a WebAssembly object file.\n"
            + $"typedef {target.U32} u32;\n\n"
            + string.Join("\n", all) + "\n\n" + string.Join("\n\n", bodies) + "\n");
    }
}
