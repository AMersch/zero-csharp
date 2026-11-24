// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

#nowarn "25" // Incomplete match expressions
#nowarn "35" // This construct is deprecated: the treatment of this operator is now handled directly by the F# compiler and its meaning may not be redefined.
#nowarn "44" // This construct is deprecated. This function is for use by compiled F# code and should not be used directly
#nowarn "52" // The value has been copied to ensure the original is not mutated by this operation
#nowarn "60" // Override implementations in augmentations are now deprecated. Override implementations should be given as part of the initial declaration of a type.
#nowarn "61" // The containing type can use 'null' as a representation value for its nullary union case. This member will be compiled as a static member.
#nowarn "69" // Interface implementations should normally be given on the initial declaration of a type. Interface implementations in augmentations may lead to accessing static bindings before they are initialized, though only if the interface implementation is invoked during initialization of the static data, and in turn access the static data. You may remove this warning using #nowarn "69" if you have checked this is not the case.
#nowarn "77" // Member constraints with the name 'Exp' are given special status by the F# compiler...
#nowarn "3218" // mismatch of parameter name for 'fst' and 'snd'
#nowarn "3244" // no nullness checking
#nowarn "3245" // no nullness checking

namespace Microsoft.FSharp.Core

    open System
    open System.Collections.Generic

    type Unit() =
        override _.GetHashCode() = 0

        override _.Equals(obj:objnull) =
            match obj with null -> true | _ -> (# "isinst Microsoft.FSharp.Core.Unit ldnull cgt.un" obj : bool #)

        interface IComparable with
            member _.CompareTo(_obj:objnull) = 0

    and unit = Unit

    type SourceConstructFlags =
       | None = 0
       | SumType = 1
       | RecordType = 2
       | ObjectType = 3
       | Field = 4
       | Exception = 5
       | Closure = 6
       | Module = 7
       | UnionCase = 8
       | Value = 9
       | KindMask = 31
       | NonPublicRepresentation = 32

    [<Flags>]
    type CompilationRepresentationFlags =
       | None = 0
       | Static = 1
       | Instance = 2
       /// append 'Module' to the end of a non-unique module
       | ModuleSuffix = 4
       | UseNullAsTrueValue = 8
       | Event = 16

    [<AttributeUsage(AttributeTargets.Class, AllowMultiple=false)>]
    type SealedAttribute(value:bool) =
        inherit Attribute()
        member _.Value = value
        new() = SealedAttribute(true)

    [<AttributeUsage(AttributeTargets.Class, AllowMultiple=false)>]
    [<Sealed>]
    type AbstractClassAttribute() =
        inherit Attribute()

    [<AttributeUsage(AttributeTargets.GenericParameter, AllowMultiple=false)>]
    [<Sealed>]
    type EqualityConditionalOnAttribute() =
        inherit Attribute()

    [<AttributeUsage(AttributeTargets.GenericParameter, AllowMultiple=false)>]
    [<Sealed>]
    type ComparisonConditionalOnAttribute() =
        inherit Attribute()

    [<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Interface, AllowMultiple=false)>]
    [<Sealed>]
    type AllowNullLiteralAttribute(value: bool) =
        inherit Attribute()
        member _.Value = value
        new () = AllowNullLiteralAttribute(true)

    [<AttributeUsage(AttributeTargets.Field, AllowMultiple=false)>]
    [<Sealed>]
    type VolatileFieldAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Class ||| AttributeTargets.Struct, AllowMultiple=false)>]
    [<Sealed>]
    type DefaultAugmentationAttribute(value:bool) =
        inherit Attribute()
        member _.Value = value

    [<AttributeUsage (AttributeTargets.Method, AllowMultiple=false)>]
    [<Sealed>]
    type AllowOverloadOnReturnTypeAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Property, AllowMultiple=false)>]
    [<Sealed>]
    type CLIEventAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Class ||| AttributeTargets.Struct, AllowMultiple=false)>]
    [<Sealed>]
    type CLIMutableAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Class ||| AttributeTargets.Struct, AllowMultiple=false)>]
    [<Sealed>]
    type AutoSerializableAttribute(value:bool) =
        inherit Attribute()
        member _.Value = value

    [<AttributeUsage (AttributeTargets.Field ||| AttributeTargets.Method, AllowMultiple=false)>]
    [<Sealed>]
    type DefaultValueAttribute(check:bool) =
        inherit Attribute()
        member _.Check = check
        new() = DefaultValueAttribute(true)

    [<AttributeUsage (AttributeTargets.Method, AllowMultiple=false)>]
    [<Sealed>]
    type EntryPointAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Class, AllowMultiple=false)>]
    [<Sealed>]
    type ReferenceEqualityAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Class ||| AttributeTargets.Struct, AllowMultiple=false)>]
    [<Sealed>]
    type StructuralComparisonAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Class ||| AttributeTargets.Struct, AllowMultiple=false)>]
    [<Sealed>]
    type StructuralEqualityAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Class ||| AttributeTargets.Interface |||
                      AttributeTargets.Delegate ||| AttributeTargets.Struct |||
                      AttributeTargets.Enum, AllowMultiple=false)>]
    [<Sealed>]
    type NoEqualityAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Class ||| AttributeTargets.Struct, AllowMultiple=false)>]
    [<Sealed>]
    type CustomEqualityAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Class ||| AttributeTargets.Struct, AllowMultiple=false)>]
    [<Sealed>]
    type CustomComparisonAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Class ||| AttributeTargets.Interface |||
                      AttributeTargets.Delegate ||| AttributeTargets.Struct |||
                      AttributeTargets.Enum, AllowMultiple=false)>]
    [<Sealed>]
    type NoComparisonAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Class ||| AttributeTargets.Struct |||
                      AttributeTargets.Parameter ||| AttributeTargets.Method |||
                      AttributeTargets.Property ||| AttributeTargets.Constructor |||
                      AttributeTargets.Delegate, AllowMultiple=false)>]

    [<Sealed>]
    type ReflectedDefinitionAttribute(includeValue: bool) =
        inherit Attribute()
        new() = ReflectedDefinitionAttribute(false)
        member _.IncludeValue = includeValue

    [<AttributeUsage (AttributeTargets.Method ||| AttributeTargets.Class |||
                      AttributeTargets.Field ||| AttributeTargets.Interface |||
                      AttributeTargets.Struct ||| AttributeTargets.Delegate |||
                      AttributeTargets.Enum ||| AttributeTargets.Property, AllowMultiple=false)>]
    [<Sealed>]
    type CompiledNameAttribute(compiledName:string) =
        inherit Attribute()
        member _.CompiledName = compiledName

    [<AttributeUsage (AttributeTargets.Class ||| AttributeTargets.Struct ||| AttributeTargets.ReturnValue ||| AttributeTargets.Parameter ,AllowMultiple=false)>]
    [<Sealed>]
    type StructAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.GenericParameter ||| AttributeTargets.Class, AllowMultiple=false)>]
    [<Sealed>]
    type MeasureAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Class, AllowMultiple=false)>]
    [<Sealed>]
    type MeasureAnnotatedAbbreviationAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Interface, AllowMultiple=false)>]
    [<Sealed>]
    type InterfaceAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Class, AllowMultiple=false)>]
    [<Sealed>]
    type ClassAttribute() =
        inherit Attribute()

    [<AttributeUsage(AttributeTargets.Field, AllowMultiple=false)>]
    [<Sealed>]
    type LiteralAttribute() =
        inherit Attribute()

    [<AttributeUsage(AttributeTargets.Assembly, AllowMultiple=false)>]
    [<Sealed>]
    type FSharpInterfaceDataVersionAttribute(major:int,minor:int,release:int) =
        inherit Attribute()
        member _.Major = major
        member _.Minor = minor
        member _.Release = release

    [<AttributeUsage(AttributeTargets.All, AllowMultiple=true)>]
    [<Sealed>]
    type CompilationMappingAttribute(sourceConstructFlags:SourceConstructFlags,
                                     variantNumber:int,
                                     sequenceNumber:int,
                                     resourceName:string,
                                     typeDefinitions:Type array) =
        inherit Attribute()
        member _.SourceConstructFlags = sourceConstructFlags
        member _.SequenceNumber = sequenceNumber
        member _.VariantNumber = variantNumber
        new(sourceConstructFlags) = CompilationMappingAttribute(sourceConstructFlags,0,0)
        new(sourceConstructFlags,sequenceNumber) = CompilationMappingAttribute(sourceConstructFlags,0,sequenceNumber)
        new(sourceConstructFlags,variantNumber,sequenceNumber) = CompilationMappingAttribute(sourceConstructFlags,variantNumber,sequenceNumber,null,null)
        new(resourceName, typeDefinitions) = CompilationMappingAttribute(SourceConstructFlags.None,0,0,resourceName, typeDefinitions)
        member _.TypeDefinitions = typeDefinitions
        member _.ResourceName = resourceName

    [<AttributeUsage(AttributeTargets.All, AllowMultiple=false)>]
    [<Sealed>]
    type CompilationSourceNameAttribute(sourceName:string) =
        inherit Attribute()
        member _.SourceName = sourceName

    //-------------------------------------------------------------------------
    [<AttributeUsage(AttributeTargets.All, AllowMultiple=false)>]
    [<Sealed>]
    type CompilationRepresentationAttribute (flags : CompilationRepresentationFlags) =
        inherit Attribute()
        member _.Flags = flags


    module internal ExperimentalAttributeMessages =
        [<Literal>]
        let RequiresPreview: string = "Experimental library feature, requires '--langversion:preview'"

        [<Literal>]
        let NotSupportedYet: string = "This construct is not supported by your version of the F# compiler"

    [<AttributeUsage(AttributeTargets.All, AllowMultiple=false)>]
    [<Sealed>]
    type ExperimentalAttribute(message:string) =
        inherit Attribute()

        member _.Message = message

    [<AttributeUsage (AttributeTargets.Parameter,AllowMultiple=false)>]
    [<Sealed>]
    type InlineIfLambdaAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Parameter,AllowMultiple=false)>]  
    [<Sealed>]
    type OptimizeClosureIfNotInlinedAttribute() = 
        inherit Attribute()

    [<AttributeUsage(AttributeTargets.Method, AllowMultiple=false)>]
    [<Sealed>]
    type CompilationArgumentCountsAttribute(counts:int array) =
        inherit Attribute()
        member _.Counts = (counts :> IEnumerable<int>)

    [<AttributeUsage(AttributeTargets.Method, AllowMultiple=false)>]
    [<Sealed>]
    type CustomOperationAttribute(name:string) =
        inherit Attribute()
        let mutable isBinary = false
        let mutable allowInto = false
        let mutable isJoin = false
        let mutable isGroupJoin = false
        let mutable maintainsVarSpace = false
        let mutable maintainsVarSpaceWithBind = false
        let mutable joinOnWord = ""
        new() = CustomOperationAttribute("")
        member _.Name = name
        member _.AllowIntoPattern with get() = allowInto and set v = allowInto <- v
        member _.IsLikeZip with get() = isBinary and set v = isBinary <- v
        member _.IsLikeJoin with get() = isJoin and set v = isJoin <- v
        member _.IsLikeGroupJoin with get() = isGroupJoin and set v = isGroupJoin <- v
        member _.JoinConditionWord with get() = joinOnWord and set v = joinOnWord <- v

        member _.MaintainsVariableSpace with get() = maintainsVarSpace and set v = maintainsVarSpace <- v
        member _.MaintainsVariableSpaceUsingBind with get() = maintainsVarSpaceWithBind and set v = maintainsVarSpaceWithBind <- v

    [<AttributeUsage(AttributeTargets.Parameter, AllowMultiple=false)>]
    [<Sealed>]
    type ProjectionParameterAttribute() =
        inherit Attribute()

    [<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Interface ||| AttributeTargets.Struct ||| AttributeTargets.Delegate ||| AttributeTargets.Enum, AllowMultiple=false)>]
    [<Sealed>]
    type StructuredFormatDisplayAttribute(value:string) =
        inherit Attribute()
        member _.Value = value

    [<AttributeUsage(AttributeTargets.All, AllowMultiple=false)>]
    [<Sealed>]
    type CompilerMessageAttribute(message:string, messageNumber : int) =
        inherit Attribute()
        let mutable isError = false
        let mutable isHidden = false
        member _.Message = message
        member _.MessageNumber = messageNumber
        member _.IsError with get() = isError and set v = isError <- v
        member _.IsHidden with get() = isHidden and set v = isHidden <- v

    [<AttributeUsage(AttributeTargets.Method ||| AttributeTargets.Property, AllowMultiple=false)>]
    [<Sealed>]
    type UnverifiableAttribute() =
        inherit Attribute()

    [<AttributeUsage(AttributeTargets.Method ||| AttributeTargets.Property, AllowMultiple=false)>]
    [<Sealed>]
    type NoDynamicInvocationAttribute(isLegacy: bool) =

        inherit Attribute()

        new () = NoDynamicInvocationAttribute(false)

        member _.IsLegacy = isLegacy

    [<AttributeUsage(AttributeTargets.Parameter,AllowMultiple=false)>]
    [<Sealed>]
    type OptionalArgumentAttribute() =
        inherit Attribute()

    [<AttributeUsage(AttributeTargets.Method, AllowMultiple=false)>]
    [<Sealed>]
    type GeneralizableValueAttribute() =
        inherit Attribute()

    [<AttributeUsage(AttributeTargets.Method, AllowMultiple=false)>]
    [<Sealed>]
    type RequiresExplicitTypeArgumentsAttribute() =
        inherit Attribute()

    [<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct ||| AttributeTargets.Enum, AllowMultiple=false)>]
    [<Sealed>]
    type RequireQualifiedAccessAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Class ||| AttributeTargets.Struct ||| AttributeTargets.Assembly, AllowMultiple=true)>]
    [<Sealed>]
    type AutoOpenAttribute(path:string) =
        inherit Attribute()
        member _.Path = path
        new() =  AutoOpenAttribute("")

    /// This Attribute is used to make Value bindings like
    ///      let x = some code
    /// operate like static properties.
    [<AttributeUsage(AttributeTargets.Property, AllowMultiple=false)>]
    [<Sealed>]
    type ValueAsStaticPropertyAttribute() =
        inherit Attribute()

    [<AttributeUsage(AttributeTargets.Method ||| AttributeTargets.Property, AllowMultiple=false)>]
    [<Sealed>]
    type NoCompilerInliningAttribute() =
        inherit Attribute()

    [<AttributeUsage (AttributeTargets.Method, AllowMultiple=false)>]
    [<Sealed>]
    type WarnOnWithoutNullArgumentAttribute(warningMessage:string) =
        inherit Attribute()
        member _.WarningMessage = warningMessage
        member val internal Localize = false with get, set

    [<AttributeUsage(AttributeTargets.Method,AllowMultiple=false)>]
    [<Sealed>]
    type TailCallAttribute() =
        inherit Attribute()

namespace Microsoft.FSharp.Core.CompilerServices

    open System.ComponentModel
    open Microsoft.FSharp.Core

    /// <summary>
    /// A marker type that only compilers that support the <c>when 'T : Enum</c>
    /// library-only static optimization constraint will recognize.
    /// </summary>
    [<Sealed; AbstractClass>]
    [<EditorBrowsable(EditorBrowsableState.Never)>]
    [<CompilerMessage("This type is for compiler use and should not be used directly", 1204, IsHidden = true)>]
    type SupportsWhenTEnum = class end

