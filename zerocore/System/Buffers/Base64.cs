using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Buffers
{
    // What a span operation that may stop early says about how far it got.
    public enum OperationStatus
    {
        Done,
        DestinationTooSmall,
        NeedMoreData,
        InvalidData,
    }
}

namespace System.Buffers.Text
{
    // Base64 to and from UTF-8 bytes, with .NET's names. Convert has the same
    // for strings and chars. Decoding skips whitespace (space, tab, CR, LF),
    // as .NET does.
    public static class Base64
    {
        public static int GetMaxEncodedToUtf8Length(int length)
        {
            if ((uint)length > 1610612733u) // the largest whose encoding fits an int
                Environment.FailFast("Base64.GetMaxEncodedToUtf8Length: length out of range");
            return (length + 2) / 3 * 4;
        }

        public static int GetMaxDecodedFromUtf8Length(int length)
        {
            if (length < 0)
                Environment.FailFast("Base64.GetMaxDecodedFromUtf8Length: length out of range");
            return (length >> 2) * 3;
        }

        // With isFinalBlock false, a last group of fewer than three bytes is
        // left for the next call (NeedMoreData) instead of being padded.
        public static OperationStatus EncodeToUtf8(ReadOnlySpan<byte> bytes, Span<byte> utf8, out int bytesConsumed, out int bytesWritten, bool isFinalBlock = true)
            => Base64Core.Encode(bytes, utf8, out bytesConsumed, out bytesWritten, isFinalBlock);

        // With isFinalBlock false, a last group of fewer than four characters
        // is left for the next call (NeedMoreData).
        public static OperationStatus DecodeFromUtf8(ReadOnlySpan<byte> utf8, Span<byte> bytes, out int bytesConsumed, out int bytesWritten, bool isFinalBlock = true)
            => Base64Core.Decode(utf8, bytes, out bytesConsumed, out bytesWritten, isFinalBlock);
    }

    // The encoder and decoder, for text in bytes (UTF-8) or chars: TChar is
    // byte or char, and the compiler keeps only the branch for it.
    internal static class Base64Core
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        public static OperationStatus Encode<TChar>(ReadOnlySpan<byte> source, Span<TChar> destination, out int consumed, out int written, bool isFinalBlock)
            where TChar : unmanaged
        {
            int i = 0, o = 0;
            OperationStatus status = OperationStatus.Done;
            while (source.Length - i >= 3)
            {
                if (destination.Length - o < 4)
                {
                    status = OperationStatus.DestinationTooSmall;
                    goto End;
                }
                uint group = (uint)(source[i] << 16 | source[i + 1] << 8 | source[i + 2]);
                Put(destination, o, Alphabet[(int)(group >> 18)]);
                Put(destination, o + 1, Alphabet[(int)(group >> 12) & 63]);
                Put(destination, o + 2, Alphabet[(int)(group >> 6) & 63]);
                Put(destination, o + 3, Alphabet[(int)group & 63]);
                i += 3;
                o += 4;
            }
            int left = source.Length - i;
            if (left > 0)
            {
                if (!isFinalBlock)
                {
                    status = OperationStatus.NeedMoreData;
                    goto End;
                }
                if (destination.Length - o < 4)
                {
                    status = OperationStatus.DestinationTooSmall;
                    goto End;
                }
                uint group = (uint)(source[i] << 16 | (left == 2 ? source[i + 1] << 8 : 0));
                Put(destination, o, Alphabet[(int)(group >> 18)]);
                Put(destination, o + 1, Alphabet[(int)(group >> 12) & 63]);
                Put(destination, o + 2, left == 2 ? Alphabet[(int)(group >> 6) & 63] : '=');
                Put(destination, o + 3, '=');
                i += left;
                o += 4;
            }
        End:
            consumed = i;
            written = o;
            return status;
        }

        public static OperationStatus Decode<TChar>(ReadOnlySpan<TChar> source, Span<byte> destination, out int consumed, out int written, bool isFinalBlock)
            where TChar : unmanaged
        {
            consumed = 0;
            written = 0;
            int i = 0;
            while (true)
            {
                // Gather four characters, skipping whitespace.
                int count = 0, padding = 0;
                uint group = 0;
                while (count < 4 && i < source.Length)
                {
                    int c = Get(source, i++);
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                        continue;
                    int value;
                    if (c == '=')
                    {
                        if (count < 2)
                            return OperationStatus.InvalidData;
                        padding++;
                        value = 0;
                    }
                    else
                    {
                        value = Sextet(c);
                        if (value < 0 || padding > 0) // not Base64, or data after '='
                            return OperationStatus.InvalidData;
                    }
                    group = group << 6 | (uint)value;
                    count++;
                }
                if (count == 0)
                {
                    consumed = i; // trailing whitespace
                    return OperationStatus.Done;
                }
                if (count < 4)
                    return isFinalBlock ? OperationStatus.InvalidData : OperationStatus.NeedMoreData;

                int bytes = 3 - padding;
                if (destination.Length - written < bytes)
                    return OperationStatus.DestinationTooSmall;
                destination[written] = (byte)(group >> 16);
                if (bytes > 1)
                    destination[written + 1] = (byte)(group >> 8);
                if (bytes > 2)
                    destination[written + 2] = (byte)group;
                written += bytes;
                consumed = i;

                if (padding > 0)
                {
                    // Padding ends the data: only whitespace may follow.
                    for (; i < source.Length; i++)
                    {
                        int c = Get(source, i);
                        if (c != ' ' && c != '\t' && c != '\r' && c != '\n')
                            return OperationStatus.InvalidData;
                    }
                    consumed = i;
                    return OperationStatus.Done;
                }
            }
        }

        // How many bytes source decodes to, or -1 if it isn't valid Base64.
        public static int DecodedLength<TChar>(ReadOnlySpan<TChar> source) where TChar : unmanaged
        {
            int count = 0, padding = 0;
            for (int i = 0; i < source.Length; i++)
            {
                int c = Get(source, i);
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    continue;
                count++;
                if (c == '=')
                    padding++;
            }
            if (count % 4 != 0 || padding > 2)
                return -1;
            return count / 4 * 3 - padding;
        }

        private static int Sextet(int c)
        {
            if (c >= 'A' && c <= 'Z') return c - 'A';
            if (c >= 'a' && c <= 'z') return c - 'a' + 26;
            if (c >= '0' && c <= '9') return c - '0' + 52;
            if (c == '+') return 62;
            if (c == '/') return 63;
            return -1;
        }

        private static void Put<TChar>(Span<TChar> span, int index, char c) where TChar : unmanaged
        {
            if (Unsafe.SizeOf<TChar>() == 1)
                Unsafe.As<TChar, byte>(ref span[index]) = (byte)c;
            else
                Unsafe.As<TChar, char>(ref span[index]) = c;
        }

        private static int Get<TChar>(ReadOnlySpan<TChar> span, int index) where TChar : unmanaged
        {
            // The callers keep index below span.Length.
            ref TChar item = ref Unsafe.Add(ref MemoryMarshal.GetReference(span), index);
            return Unsafe.SizeOf<TChar>() == 1 ? Unsafe.As<TChar, byte>(ref item) : Unsafe.As<TChar, char>(ref item);
        }
    }
}
