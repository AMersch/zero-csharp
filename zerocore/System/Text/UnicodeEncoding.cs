namespace System.Text
{
    // UTF-16, the encoding of char and string: Encoding.Unicode (little-endian)
    // and Encoding.BigEndianUnicode. Both have a byte order mark for
    // GetPreamble; GetBytes writes none.
    public unsafe class UnicodeEncoding : Encoding
    {
        private readonly bool _bigEndian;
        private readonly bool _byteOrderMark;

        public UnicodeEncoding() : this(false, true, false) { }

        public UnicodeEncoding(bool bigEndian, bool byteOrderMark) : this(bigEndian, byteOrderMark, false) { }

        public UnicodeEncoding(bool bigEndian, bool byteOrderMark, bool throwOnInvalidBytes) : base(throwOnInvalidBytes)
        {
            _bigEndian = bigEndian;
            _byteOrderMark = byteOrderMark;
        }

        public override int CodePage => _bigEndian ? 1201 : 1200;
        public override string WebName => _bigEndian ? "utf-16BE" : "utf-16";
        public override string EncodingName => _bigEndian ? "Unicode (Big-Endian)" : "Unicode";

        public override ReadOnlySpan<byte> Preamble => !_byteOrderMark ? default : _bigEndian ? BigEndianBom : LittleEndianBom;

        private static ReadOnlySpan<byte> LittleEndianBom => [0xFF, 0xFE];
        private static ReadOnlySpan<byte> BigEndianBom => [0xFE, 0xFF];

        public override int GetMaxByteCount(int charCount)
        {
            if (charCount < 0 || charCount >= 0x7FFFFFFF / 2)
                Environment.FailFast("ArgumentOutOfRangeException: charCount");
            return (charCount + 1) * 2;
        }

        public override int GetMaxCharCount(int byteCount)
        {
            if (byteCount < 0)
                Environment.FailFast("ArgumentOutOfRangeException: byteCount");
            return (byteCount >> 1) + (byteCount & 1) + 1;
        }

        // A surrogate pair stays a pair; a lone surrogate becomes U+FFFD.
        internal override int Encode(char* chars, int charCount, byte* bytes, int capacity)
        {
            if (capacity >= 0 && capacity / 2 < charCount)
                return -1;
            for (int i = 0; i < charCount; i++)
            {
                char c = chars[i];
                if (c >= 0xD800 && c <= 0xDFFF)
                {
                    if (c <= 0xDBFF && i + 1 < charCount && chars[i + 1] >= 0xDC00 && chars[i + 1] <= 0xDFFF)
                    {
                        if (capacity >= 0)
                            Put(bytes + i * 2, c);
                        i++;
                        c = chars[i];
                    }
                    else
                    {
                        Invalid("EncoderFallbackException: a lone surrogate can't be encoded as UTF-16");
                        c = (char)0xFFFD;
                    }
                }
                if (capacity >= 0)
                    Put(bytes + i * 2, c);
            }
            return charCount * 2;
        }

        // A lone surrogate, and an odd byte at the end, become U+FFFD.
        internal override int Decode(byte* bytes, int byteCount, char* chars, int capacity)
        {
            int units = byteCount / 2;
            int length = units + (byteCount & 1);
            if (capacity >= 0 && capacity < length)
                return -1;
            for (int i = 0; i < units; i++)
            {
                char c = Get(bytes + i * 2);
                if (c >= 0xD800 && c <= 0xDFFF)
                {
                    char next = i + 1 < units ? Get(bytes + i * 2 + 2) : '\0';
                    if (c <= 0xDBFF && next >= 0xDC00 && next <= 0xDFFF)
                    {
                        if (capacity >= 0)
                        {
                            chars[i] = c;
                            chars[i + 1] = next;
                        }
                        i++;
                        continue;
                    }
                    Invalid("DecoderFallbackException: the bytes aren't valid UTF-16");
                    c = (char)0xFFFD;
                }
                if (capacity >= 0)
                    chars[i] = c;
            }
            if ((byteCount & 1) != 0)
            {
                Invalid("DecoderFallbackException: the bytes aren't valid UTF-16");
                if (capacity >= 0)
                    chars[units] = (char)0xFFFD;
            }
            return length;
        }

        private void Put(byte* at, char c)
        {
            at[_bigEndian ? 1 : 0] = (byte)c;
            at[_bigEndian ? 0 : 1] = (byte)(c >> 8);
        }

        private char Get(byte* at) => _bigEndian ? (char)((at[0] << 8) | at[1]) : (char)(at[0] | (at[1] << 8));
    }
}
