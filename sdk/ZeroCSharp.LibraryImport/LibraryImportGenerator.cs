// Writes each [LibraryImport] method as a [DllImport] of the same function, in
// one file. Zero mode has no marshalling: a signature that needs it is an error.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace System.Runtime.CompilerServices
{
    static class IsExternalInit { }
}

namespace ZeroCSharp.LibraryImport
{
    [Generator(LanguageNames.CSharp)]
    public sealed class LibraryImportGenerator : IIncrementalGenerator
    {
        const string Interop = "global::System.Runtime.InteropServices.";

        static readonly DiagnosticDescriptor Shape = new(
            "ZERO012", "[LibraryImport] method can't be implemented",
            "'{0}': {1}", "ZeroCSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

        static readonly DiagnosticDescriptor Marshalling = new(
            "ZERO013", "[LibraryImport] needs marshalling, which zero mode doesn't have",
            "'{0}': {1}", "ZeroCSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

        sealed record Import(string Scope, string Member, Diagnostic? Error);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<Import> imports = context.SyntaxProvider.ForAttributeWithMetadataName(
                "System.Runtime.InteropServices.LibraryImportAttribute",
                static (node, _) => node is MethodDeclarationSyntax,
                static (ctx, _) => Read((IMethodSymbol)ctx.TargetSymbol, (MethodDeclarationSyntax)ctx.TargetNode, ctx.Attributes[0]));
            context.RegisterSourceOutput(imports.Collect(), static (spc, all) => Write(spc, all));
        }

        static Import Read(IMethodSymbol method, MethodDeclarationSyntax syntax, AttributeData attribute)
        {
            string? error = Check(method, syntax, attribute, out DiagnosticDescriptor descriptor);
            Diagnostic? diagnostic = null;
            if (error != null)
            {
                string name = method.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
                diagnostic = Diagnostic.Create(descriptor, syntax.Identifier.GetLocation(), name, error);
                // A method of the wrong shape can't be implemented at all.
                if (descriptor == Shape)
                    return new Import("", "", diagnostic);
            }

            var scope = new List<string>();
            for (SyntaxNode? node = syntax.Parent; node is TypeDeclarationSyntax type; node = node.Parent)
            {
                string keyword = type is RecordDeclarationSyntax { ClassOrStructKeyword.Text: { Length: > 0 } kind } ? $"record {kind}" : type.Keyword.Text;
                string modifiers = string.Concat(type.Modifiers
                    .Where(m => m.IsKind(SyntaxKind.UnsafeKeyword) || m.IsKind(SyntaxKind.RefKeyword) || m.IsKind(SyntaxKind.ReadOnlyKeyword))
                    .Select(m => m.Text + " "));
                scope.Insert(0, $"{modifiers}partial {keyword} {type.Identifier.Text}");
            }
            if (!method.ContainingNamespace.IsGlobalNamespace)
                scope.Insert(0, $"namespace {method.ContainingNamespace.ToDisplayString()}");

            string entryPoint = attribute.NamedArguments.FirstOrDefault(a => a.Key == "EntryPoint").Value.Value as string ?? method.Name;
            string callingConvention = "";
            foreach (TypedConstant callConv in CallConvs(method))
            {
                if (callConv.Value is ITypeSymbol { Name: "CallConvCdecl" })
                    callingConvention = $", CallingConvention = {Interop}CallingConvention.Cdecl";
                else if (callConv.Value is ITypeSymbol { Name: "CallConvStdcall" })
                    callingConvention = $", CallingConvention = {Interop}CallingConvention.StdCall";
            }
            string dllImport = $"[{Interop}DllImport({Literal(attribute.ConstructorArguments.FirstOrDefault().Value as string ?? "")}, "
                + $"EntryPoint = {Literal(entryPoint)}, ExactSpelling = true{callingConvention})]";

            string modifiersOfMethod = string.Concat(syntax.Modifiers.Where(m => !m.IsKind(SyntaxKind.PartialKeyword)).Select(m => m.Text + " "));
            string returnType = (method.ReturnsByRefReadonly ? "ref readonly " : method.ReturnsByRef ? "ref " : "")
                + (method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            string[] names = method.Parameters.Select(p => SyntaxFacts.GetKeywordKind(p.Name) != SyntaxKind.None ? "@" + p.Name : p.Name).ToArray();
            string parameters = string.Join(", ", method.Parameters.Select((p, i) => $"{RefKindText(p.RefKind)}{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {names[i]}"));

            // C# 15's memory safety rules want an extern marked safe or unsafe, the
            // same on both parts. Unmarked, the method calls an extern of its own.
            bool unmarked = syntax.SyntaxTree.Options.Features.ContainsKey("updated-memory-safety-rules")
                && !syntax.Modifiers.Any(m => m.IsKind(SyntaxKind.UnsafeKeyword) || m.Text == "safe");
            string member = diagnostic != null
                // Refused: a body that keeps the compiler from also reporting a
                // partial method with no implementation. The build fails anyway.
                ? (unmarked
                    ? $"{modifiersOfMethod}partial {returnType} {syntax.Identifier.Text}({parameters}) => throw null;"
                    : $"{modifiersOfMethod}extern partial {returnType} {syntax.Identifier.Text}({parameters});")
                : unmarked
                ? $"{modifiersOfMethod}partial {returnType} {syntax.Identifier.Text}({parameters})\n{{\n"
                    + $"    unsafe {{ {(method.ReturnsVoid ? "" : "return ")}__PInvoke({string.Join(", ", names)}); }}\n\n"
                    + $"    {dllImport}\n    static extern unsafe {returnType} __PInvoke({parameters});\n}}"
                : $"{dllImport}\n{modifiersOfMethod}extern partial {returnType} {syntax.Identifier.Text}({parameters});";
            return new Import(string.Join("\n", scope), member, diagnostic);
        }

        static string RefKindText(RefKind kind) => kind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            RefKind.RefReadOnlyParameter => "ref readonly ",
            _ => "",
        };

        static string? Check(IMethodSymbol method, MethodDeclarationSyntax syntax, AttributeData attribute, out DiagnosticDescriptor descriptor)
        {
            descriptor = Shape;
            if (!method.IsStatic || !method.IsPartialDefinition || method.PartialImplementationPart != null)
                return "a [LibraryImport] method is 'static partial', with no body";
            if (method.IsGenericMethod)
                return "a [LibraryImport] method can't be generic";
            for (SyntaxNode? node = syntax.Parent; node is TypeDeclarationSyntax type; node = node.Parent)
            {
                if (!type.Modifiers.Any(SyntaxKind.PartialKeyword))
                    return $"its type '{type.Identifier.Text}' must be partial";
                if (type.TypeParameterList != null)
                    return $"its type '{type.Identifier.Text}' can't be generic";
            }
            foreach (TypedConstant callConv in CallConvs(method))
            {
                if (callConv.Value is not ITypeSymbol { Name: "CallConvCdecl" or "CallConvStdcall" or "CallConvSuppressGCTransition" })
                    return $"the calling convention '{callConv.Value}' isn't supported";
            }

            descriptor = Marshalling;
            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            {
                if (argument.Key == "SetLastError" && argument.Value.Value is true)
                    return "SetLastError isn't supported: read errno or GetLastError() yourself";
                if (argument.Key.StartsWith("StringMarshalling") && argument.Value.Value is not (null or 0))
                    return "strings aren't marshalled: pass them as byte* (UTF-8) or ushort* (UTF-16)";
            }
            if (method.GetReturnTypeAttributes().Concat(method.Parameters.SelectMany(p => p.GetAttributes())).Any(IsMarshalAs))
                return "[MarshalAs] isn't supported";
            if (method.ReturnsByRef || method.ReturnsByRefReadonly)
                return "it returns by reference: return a pointer instead";
            if (!method.ReturnsVoid && Unsupported(method.ReturnType) is string returned)
                return $"it returns {returned}";
            foreach (IParameterSymbol parameter in method.Parameters)
            {
                if (parameter.RefKind != RefKind.None)
                    return $"parameter '{parameter.Name}' is {parameter.RefKind.ToString().ToLowerInvariant()}: pass a pointer instead";
                if (Unsupported(parameter.Type) is string reason)
                    return $"parameter '{parameter.Name}' is {reason}";
            }
            return null;
        }

        static IEnumerable<TypedConstant> CallConvs(IMethodSymbol method) => method.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.UnmanagedCallConvAttribute")
            .SelectMany(a => a.NamedArguments)
            .Where(a => a.Key == "CallConvs" && !a.Value.IsNull)
            .SelectMany(a => a.Value.Values);

        static bool IsMarshalAs(AttributeData attribute) =>
            attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.MarshalAsAttribute";

        // Why a type can't cross to C as it is, or null.
        static string? Unsupported(ITypeSymbol type)
        {
            switch (type)
            {
                case IFunctionPointerTypeSymbol { Signature.CallingConvention: System.Reflection.Metadata.SignatureCallingConvention.Default }:
                    return $"{type.ToDisplayString()}, a managed function pointer: use delegate* unmanaged";
                case IPointerTypeSymbol or IFunctionPointerTypeSymbol:
                    return null;
                case { SpecialType: SpecialType.System_Boolean }:
                    return "bool, whose size C doesn't fix: use byte or int";
                case { SpecialType: SpecialType.System_Char }:
                    return "char: use ushort";
                case { SpecialType: SpecialType.System_Decimal }:
                    return "decimal, which C doesn't have";
                case { SpecialType: SpecialType.System_String }:
                    return "string: pass a byte* (UTF-8) or ushort* (UTF-16)";
                case { SpecialType: SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16
                    or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
                    or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_IntPtr
                    or SpecialType.System_UIntPtr or SpecialType.System_Single or SpecialType.System_Double }:
                case INamedTypeSymbol { TypeKind: TypeKind.Enum }:
                    return null;
                case INamedTypeSymbol { TypeKind: TypeKind.Struct, IsRefLikeType: false } structure:
                    foreach (IFieldSymbol field in structure.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
                    {
                        ITypeSymbol fieldType = field.IsFixedSizeBuffer ? ((IPointerTypeSymbol)field.Type).PointedAtType : field.Type;
                        if (Unsupported(fieldType) is string inner)
                            // An auto-property's field is named after the property.
                            return $"{structure.Name}, whose field '{field.AssociatedSymbol?.Name ?? field.Name}' is {inner}";
                    }
                    return null;
                default:
                    return $"{type.ToDisplayString()}, which isn't a C type: use pointers, numbers, enums or structs of them";
            }
        }

        static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

        static void Write(SourceProductionContext context, ImmutableArray<Import> imports)
        {
            var text = new StringBuilder("// <auto-generated/>\n");
            foreach (Import import in imports.Where(i => i.Error != null))
                context.ReportDiagnostic(import.Error!);
            foreach (IGrouping<string, Import> group in imports.Where(i => i.Member.Length > 0).GroupBy(i => i.Scope))
            {
                string[] scope = group.Key.Split('\n');
                string indent = new(' ', 4 * scope.Length);
                text.Append('\n');
                for (int i = 0; i < scope.Length; i++)
                    text.Append(' ', 4 * i).Append(scope[i]).Append('\n').Append(' ', 4 * i).Append("{\n");
                text.Append(string.Join("\n\n", group.Select(i => indent + i.Member.Replace("\n", "\n" + indent).Replace(indent + "\n", "\n"))));
                text.Append('\n');
                for (int i = scope.Length - 1; i >= 0; i--)
                    text.Append(' ', 4 * i).Append("}\n");
            }
            context.AddSource("LibraryImports.g.cs", text.ToString());
        }
    }
}
