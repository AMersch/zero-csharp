using System.Runtime.CompilerServices;
using Internal.Runtime;

namespace System.Runtime
{
    // The compiler calls these for `is`, `as`, casts, and for storing into
    // arrays of reference types. IsInstanceOf* return the object or null;
    // CheckCast* end the process where .NET would throw InvalidCastException.
    //
    // Supported: casts to classes and to interfaces, and to an array's exact
    // type. Not supported, and reported as such instead of giving a wrong
    // answer: variance (IEnumerable<string> as IEnumerable<object>, string[]
    // as object[]) and the generic interfaces of arrays (IList<T>).
    internal static unsafe class TypeCast
    {
        private static object IsInstanceOfClass(MethodTable* target, object obj)
        {
            if (obj == null || IsDerivedFrom(obj.m_pEEType, target))
                return obj;
            return null;
        }

        private static object IsInstanceOfInterface(MethodTable* target, object obj)
        {
            if (obj == null || Implements(obj.m_pEEType, target))
                return obj;
            if (target->HasGenericVariance)
                Environment.FailFast("Casts that need generic variance aren't supported");
            return null;
        }

        private static object IsInstanceOfAny(MethodTable* target, object obj)
        {
            if (obj == null)
                return null;
            if (target->IsInterface)
                return IsInstanceOfInterface(target, obj);
            if (!target->IsParameterized)
                return IsInstanceOfClass(target, obj);

            // An array type: only an array of exactly that type matches.
            MethodTable* type = obj.m_pEEType;
            if (type == target)
                return obj;
            if (type->IsParameterized && type->ElementType != target->ElementType)
            {
                // An array of structs matches no other array type (.NET also
                // lets int[] pass as uint[], and an enum's array as its
                // underlying type's; not here). Arrays of classes could match
                // by covariance (string[] as object[]), which isn't supported.
                if (IsValueType(type->ElementType) || IsValueType(target->ElementType))
                    return null;
                Environment.FailFast("Casts between array types aren't supported");
            }
            return null;
        }

        private static bool IsInstanceOfException(MethodTable* target, object obj)
            => IsInstanceOfClass(target, obj) != null;

        private static object CheckCastClass(MethodTable* target, object obj)
            => Checked(IsInstanceOfClass(target, obj), obj);

        private static object CheckCastClassSpecial(MethodTable* target, object obj)
            => Checked(IsInstanceOfClass(target, obj), obj);

        private static object CheckCastInterface(MethodTable* target, object obj)
            => Checked(IsInstanceOfInterface(target, obj), obj);

        private static object CheckCastAny(MethodTable* target, object obj)
            => Checked(IsInstanceOfAny(target, obj), obj);

        private static object Checked(object result, object obj)
        {
            if (result == null && obj != null)
                Environment.FailFast("InvalidCastException");
            return obj;
        }

        private static bool IsDerivedFrom(MethodTable* type, MethodTable* target)
        {
            for (; type != null; type = type->BaseType)
            {
                if (type == target)
                    return true;
            }
            return false;
        }

        // .NET's EETypeElementType: Boolean (2) up to Nullable (18) are structs.
        private static bool IsValueType(MethodTable* type)
            => !type->IsParameterized && type->ElementTypeKind >= 2 && type->ElementTypeKind <= 18;

        // A type's interface map lists everything it implements, including what
        // its base types and its interfaces bring in, so one pass is enough.
        private static bool Implements(MethodTable* type, MethodTable* target)
        {
            MethodTable** map = type->InterfaceMap;
            for (int i = 0; i < type->NumInterfaces; i++)
            {
                if (map[i] == target)
                    return true;
            }
            return false;
        }

        // array[index] = obj, for arrays of reference types. The element type
        // check is what lets a string[] be passed as an object[] safely.
        private static void StelemRef(object[] array, nint index, object obj)
        {
            ref object element = ref ElementAt(array, index);
            if (obj != null)
            {
                MethodTable* elementType = array.m_pEEType->ElementType;
                MethodTable* type = obj.m_pEEType;
                bool fits = elementType->IsInterface ? Implements(type, elementType)
                    : elementType->IsParameterized ? type == elementType
                    : IsDerivedFrom(type, elementType);
                if (!fits)
                    Environment.FailFast("ArrayTypeMismatchException");
            }
            element = obj;
        }

        // ref array[index], for arrays of reference types.
        private static ref object LdelemaRef(object[] array, nint index, MethodTable* elementType)
        {
            if (array.m_pEEType->ElementType != elementType)
                Environment.FailFast("ArrayTypeMismatchException");
            return ref ElementAt(array, index);
        }

        private static ref object ElementAt(object[] array, nint index)
        {
            if ((nuint)index >= (nuint)(uint)array.Length)
                Environment.FailFast("IndexOutOfRangeException");
            ref byte first = ref Unsafe.AddByteOffset(ref Unsafe.As<RawData>(array).Data, (nint)sizeof(nint));
            return ref Unsafe.Add(ref Unsafe.As<byte, object>(ref first), index);
        }
    }
}
