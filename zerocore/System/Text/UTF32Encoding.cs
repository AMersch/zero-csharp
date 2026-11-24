namespace System.Text
{
    // UTF-32: four bytes per code point. Encoding.UTF32 is little-endian, with
    // a byte order mark for GetPreamble; GetBytes writes none.
    public sealed unsafe class UTF32Encoding : Encoding
    {
        private readonly bool _bigEndian;
        private readonly bool _byteOrderMark;

        public UTF32Encoding() : this(false, true, false) { }

        public UTF32Encoding(bool bigEndian, bool byteOrderMark) : this(bigEndian, byteOrderMark, false) { }

        public UTF32Encoding(bool bigEndian, bool byteOrderMark, bool throwOnInvalidCharacters) : base(throwOnInvalidCharacters)
        {
            _bigEndian = bigEndian;
            _byteOrderMark = byteOrderMark;
        }

        public override int CodePage => _bigEndian ? 12001 : 12000;
        public override string WebName => _bigEndian ? "utf-32BE" : "utf-32";
        public override string EncodingName => _bigEndian ? "Unicode (UTF-32 Big-Endian)" : "Unicode (UTF-32)";

        public override ReadOnlySpan<byte> Preamble => !_byteOrderMark ? default : _bigEndian ? BigEndianBom : LittleEndianBom;

        private static ReadOnlySpan<byte> LittleEndianBom => [0xFF, 0xFE, 0x00, 0x00];
        private static ReadOnlySpan<byte> BigEndianBom => [0x00, 0x00, 0xFE, 0xFF];

        public override int GetMaxByteCount(int charCount)
        {
            if (charCount < 0 || charCount >= 0x7FFFFFFF / 4)
                Environment.FailFast("ArgumentOutOfRangeException: charCount");
            return (charCount + 1) * 4;
        }

        public override int GetMaxCharCount(int byteCount)
        {
            if (byteCount < 0)
                Environment.FailFast("ArgumentOutOfRangeException: byteCount");
            return byteCount / 2 + 2;
        }

        internal override int Encode(char* chars, int charCount, byte* bytes, int capacity)
        {
            int written = 0;
            for (int i = 0; i < charCount; i++)
            {
                int c = chars[i];
                if (c >= 0xD800 && c <= 0xDFFF)
                {
                    if (c <= 0xDBFF && i + 1 < charCount && chars[i + 1] >= 0xDC00 && chars[i + 1] <= 0xDFFF)
                    {
                        c = ((c - 0xD800) << 10) + (chars[i + 1] - 0xDC00) + 0x10000;
                        i++;
                    }
                    else
                    {
                        Invalid("EncoderFallbackException: a lone surrogate can't be encoded as UTF-32");
                        c = 0xFFFD;
                    }
                }
                if (capacity >= 0)
                {
                    if (capacity - written < 4)
                        return -1;
                    byte* b = bytes + written;
                    for (int k = 0; k < 4; k++)
                        b[_bigEndian ? 3 - k : k] = (byte)(c >> (8 * k));
                }
                written += 4;
            }
            return written;
        }

        // A value that isn't a code point (a surrogate, or above U+10FFFF),
        // and 1 to 3 bytes left at the end, become U+FFFD.
        internal override int Decode(byte* bytes, int byteCount, char* chars, int capacity)
        {
            int written = 0;
            for (int i = 0; i < byteCount; i += 4)
            {
                int c;
                if (byteCount - i < 4)
                {
                    c = -1;
                }
                else
                {
                    uint value = 0;
                    for (int k = 0; k < 4; k++)
                        value |= (uint)bytes[i + (_bigEndian ? 3 - k : k)] << (8 * k);
                    c = value > 0x10FFFF || value >= 0xD800 && value <= 0xDFFF ? -1 : (int)value;
                }
                if (c < 0)
                {
                    Invalid("DecoderFallbackException: the bytes aren't valid UTF-32");
                    c = 0xFFFD;
                }
                int length = c < 0x10000 ? 1 : 2;
                if (capacity >= 0)
                {
                    if (capacity - written < length)
                        return -1;
                    if (length == 1)
                    {
                        chars[written] = (char)c;
                    }
                    else
                    {
                        chars[written] = (char)(((c - 0x10000) >> 10) + 0xD800);
                        chars[written + 1] = (char)(((c - 0x10000) & 0x3FF) + 0xDC00);
                    }
                }
                written += length;
            }
            return written;
        }
    }
}
