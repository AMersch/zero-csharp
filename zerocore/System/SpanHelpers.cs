// Adapted from dotnet/runtime (src/coreclr/nativeaot/Test.CoreLib), which the
// .NET Foundation licenses under the MIT license.

using System.Runtime.CompilerServices;

namespace System
{
    // The compiler calls these to clear and copy blocks of memory: zeroing a
    // large stackalloc, copying a large struct.
    internal static unsafe class SpanHelpers
    {
        [Intrinsic]
        public static void ClearWithoutReferences(ref byte dest, nuint len)
        {
            Fill(ref dest, 0, len);
        }

        // The ranges may overlap, so the direction depends on which starts first.
        [Intrinsic]
        internal static void Memmove(ref byte dest, ref byte src, nuint len)
        {
            if ((nuint)Unsafe.AsPointer(ref dest) - (nuint)Unsafe.AsPointer(ref src) >= len)
            {
                for (nuint i = 0; i < len; i++)
                    Unsafe.Add(ref dest, (nint)i) = Unsafe.Add(ref src, (nint)i);
            }
            else
            {
                for (nuint i = len; i > 0; i--)
                    Unsafe.Add(ref dest, (nint)(i - 1)) = Unsafe.Add(ref src, (nint)(i - 1));
            }
        }

        internal static void Fill(ref byte dest, byte value, nuint len)
        {
            for (nuint i = 0; i < len; i++)
                Unsafe.Add(ref dest, (nint)i) = value;
        }
    }
}
