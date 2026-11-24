using System.Buffers;
using System.Buffers.Text;
using System.Runtime.InteropServices;

namespace System
{
    // Hex and Base64, with .NET's names. The methods that return a string or
    // an array allocate it, and in zero mode it is never freed (unless an
    // object scope is open): in a loop, use the Try ones, which write into a
    // span you provide. Invalid text ends the program where .NET would throw
    // FormatException; the Try methods return false instead.
    public static class Convert
    {
        // ---- hex ----------------------------------------------------------

        public static string ToHexString(byte[] inArray) => ToHexString(new ReadOnlySpan<byte>(inArray));

        /// <summary>Two uppercase hex digits per byte: "0A1B".</summary>
        public static string ToHexString(ReadOnlySpan<byte> bytes) => ToHex(bytes, "0123456789ABCDEF");

        public static string ToHexStringLower(byte[] inArray) => ToHexStringLower(new ReadOnlySpan<byte>(inArray));

        /// <summary>Two lowercase hex digits per byte: "0a1b".</summary>
        public static string ToHexStringLower(ReadOnlySpan<byte> bytes) => ToHex(bytes, "0123456789abcdef");

        public static bool TryToHexString(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten)
            => TryToHex(source, destination, out charsWritten, "0123456789ABCDEF");

        public static bool TryToHexStringLower(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten)
            => TryToHex(source, destination, out charsWritten, "0123456789abcdef");

        public static byte[] FromHexString(string s)
        {
            if (s == null)
                Environment.FailFast("Convert.FromHexString: s is null");
            return FromHexString(MemoryMarshal.CreateSpan(ref s.FirstChar, s.Length));
        }

        /// <summary>The bytes that pairs of hex digits (either case) stand for.</summary>
        public static byte[] FromHexString(ReadOnlySpan<char> chars)
        {
            if (chars.Length % 2 != 0)
                Environment.FailFast("Convert.FromHexString: odd number of hex digits");
            byte[] result = new byte[chars.Length / 2];
            if (FromHexString(chars, result, out _, out _) != OperationStatus.Done)
                Environment.FailFast("Convert.FromHexString: not a hex digit");
            return result;
        }

        /// <summary>
        /// Decodes pairs of hex digits into <paramref name="destination"/>, as far
        /// as it can: InvalidData at a character that isn't a hex digit,
        /// NeedMoreData for an odd last digit, DestinationTooSmall when it's full.
        /// </summary>
        public static OperationStatus FromHexString(ReadOnlySpan<char> source, Span<byte> destination, out int charsConsumed, out int bytesWritten)
        {
            int i = 0, o = 0;
            OperationStatus status = OperationStatus.Done;
            while (source.Length - i >= 2)
            {
                if (o == destination.Length)
                {
                    status = OperationStatus.DestinationTooSmall;
                    break;
                }
                int high = HexValue(source[i]), low = HexValue(source[i + 1]);
                if ((high | low) < 0)
                {
                    status = OperationStatus.InvalidData;
                    break;
                }
                destination[o++] = (byte)(high << 4 | low);
                i += 2;
            }
            if (status == OperationStatus.Done && i < source.Length)
                status = HexValue(source[i]) < 0 ? OperationStatus.InvalidData : OperationStatus.NeedMoreData;
            charsConsumed = i;
            bytesWritten = o;
            return status;
        }

        private static string ToHex(ReadOnlySpan<byte> bytes, string digits)
        {
            string result = string.Allocate(bytes.Length * 2);
            TryToHex(bytes, MemoryMarshal.CreateSpan(ref result.FirstChar, result.Length), out _, digits);
            return result;
        }

        private static bool TryToHex(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten, string digits)
        {
            if (destination.Length / 2 < source.Length)
            {
                charsWritten = 0;
                return false;
            }
            for (int i = 0; i < source.Length; i++)
            {
                destination[2 * i] = digits[source[i] >> 4];
                destination[2 * i + 1] = digits[source[i] & 15];
            }
            charsWritten = source.Length * 2;
            return true;
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return -1;
        }

        // ---- Base64 -------------------------------------------------------

        public static string ToBase64String(byte[] inArray) => ToBase64String(new ReadOnlySpan<byte>(inArray));

        public static string ToBase64String(ReadOnlySpan<byte> bytes)
        {
            string result = string.Allocate(Base64.GetMaxEncodedToUtf8Length(bytes.Length));
            Base64Core.Encode(bytes, MemoryMarshal.CreateSpan(ref result.FirstChar, result.Length), out _, out _, true);
            return result;
        }

        public static bool TryToBase64Chars(ReadOnlySpan<byte> bytes, Span<char> chars, out int charsWritten)
        {
            if (Base64Core.Encode(bytes, chars, out _, out charsWritten, true) == OperationStatus.Done)
                return true;
            charsWritten = 0;
            return false;
        }

        public static byte[] FromBase64String(string s)
        {
            if (s == null)
                Environment.FailFast("Convert.FromBase64String: s is null");
            return FromBase64CharArray(MemoryMarshal.CreateSpan(ref s.FirstChar, s.Length));
        }

        public static bool TryFromBase64String(string s, Span<byte> bytes, out int bytesWritten)
        {
            if (s == null)
            {
                bytesWritten = 0;
                return false;
            }
            return TryFromBase64Chars(MemoryMarshal.CreateSpan(ref s.FirstChar, s.Length), bytes, out bytesWritten);
        }

        public static bool TryFromBase64Chars(ReadOnlySpan<char> chars, Span<byte> bytes, out int bytesWritten)
        {
            if (Base64Core.Decode(chars, bytes, out _, out bytesWritten, true) == OperationStatus.Done)
                return true;
            bytesWritten = 0;
            return false;
        }

        private static byte[] FromBase64CharArray(ReadOnlySpan<char> chars)
        {
            int length = Base64Core.DecodedLength(chars);
            if (length < 0)
                Environment.FailFast("Convert.FromBase64String: not valid Base64");
            byte[] result = new byte[length];
            if (Base64Core.Decode(chars, result, out _, out _, true) != OperationStatus.Done)
                Environment.FailFast("Convert.FromBase64String: not valid Base64");
            return result;
        }
    }
}
