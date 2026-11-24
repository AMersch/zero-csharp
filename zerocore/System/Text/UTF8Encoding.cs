namespace System.Text
{
    // UTF-8. Encoding.UTF8 is new UTF8Encoding(true): it has a byte order mark
    // for GetPreamble; new UTF8Encoding() and Encoding.Default don't. GetBytes
    // writes no mark either way.
    public unsafe class UTF8Encoding : Encoding
    {
        private readonly bool _emitIdentifier;

        public UTF8Encoding() : this(false, false) { }

        public UTF8Encoding(bool encoderShouldEmitUTF8Identifier) : this(encoderShouldEmitUTF8Identifier, false) { }

        public UTF8Encoding(bool encoderShouldEmitUTF8Identifier, bool throwOnInvalidBytes) : base(throwOnInvalidBytes)
            => _emitIdentifier = encoderShouldEmitUTF8Identifier;

        public override int CodePage => 65001;
        public override string WebName => "utf-8";
        public override string EncodingName => "Unicode (UTF-8)";

        public override ReadOnlySpan<byte> Preamble => _emitIdentifier ? Bom : default;

        private static ReadOnlySpan<byte> Bom => [0xEF, 0xBB, 0xBF];

        public override int GetMaxByteCount(int charCount)
        {
            if (charCount < 0 || charCount >= 0x7FFFFFFF / 3)
                Environment.FailFast("ArgumentOutOfRangeException: charCount");
            return (charCount + 1) * 3;
        }

        public override int GetMaxCharCount(int byteCount)
        {
            if (byteCount < 0 || byteCount == 0x7FFFFFFF)
                Environment.FailFast("ArgumentOutOfRangeException: byteCount");
            return byteCount + 1;
        }

        internal override int Encode(char* chars, int charCount, byte* bytes, int capacity)
        {
            int written = 0;
            for (int i = 0; i < charCount; i++)
            {
                int c = chars[i];
                if (c < 0x80)
                {
                    if (capacity >= 0)
                    {
                        if (written == capacity)
                            return -1;
                        bytes[written] = (byte)c;
                    }
                    written++;
                    continue;
                }
                if (c >= 0xD800 && c <= 0xDFFF)
                {
                    if (c <= 0xDBFF && i + 1 < charCount && chars[i + 1] >= 0xDC00 && chars[i + 1] <= 0xDFFF)
                    {
                        c = ((c - 0xD800) << 10) + (chars[i + 1] - 0xDC00) + 0x10000;
                        i++;
                    }
                    else
                    {
                        Invalid("EncoderFallbackException: a lone surrogate can't be encoded as UTF-8");
                        c = 0xFFFD;
                    }
                }
                int length = c < 0x800 ? 2 : c < 0x10000 ? 3 : 4;
                if (capacity >= 0)
                {
                    if (capacity - written < length)
                        return -1;
                    byte* b = bytes + written;
                    if (length == 2)
                    {
                        b[0] = (byte)(0xC0 | (c >> 6));
                        b[1] = (byte)(0x80 | (c & 0x3F));
                    }
                    else if (length == 3)
                    {
                        b[0] = (byte)(0xE0 | (c >> 12));
                        b[1] = (byte)(0x80 | ((c >> 6) & 0x3F));
                        b[2] = (byte)(0x80 | (c & 0x3F));
                    }
                    else
                    {
                        b[0] = (byte)(0xF0 | (c >> 18));
                        b[1] = (byte)(0x80 | ((c >> 12) & 0x3F));
                        b[2] = (byte)(0x80 | ((c >> 6) & 0x3F));
                        b[3] = (byte)(0x80 | (c & 0x3F));
                    }
                }
                written += length;
            }
            return written;
        }

        // Each maximal sequence of bytes that starts a character but doesn't
        // finish it becomes one U+FFFD, as in .NET (and the Unicode standard's
        // recommendation): E0 80 is two, F0 9F 98 at the end is one.
        internal override int Decode(byte* bytes, int byteCount, char* chars, int capacity)
        {
            int written = 0;
            int i = 0;
            while (i < byteCount)
            {
                int b = bytes[i];
                int c;
                if (b < 0x80)
                {
                    c = b;
                    i++;
                }
                else
                {
                    int needed, low = 0x80, high = 0xBF;
                    if (b >= 0xC2 && b <= 0xDF)
                    {
                        needed = 1;
                        c = b & 0x1F;
                    }
                    else if (b >= 0xE0 && b <= 0xEF)
                    {
                        needed = 2;
                        c = b & 0x0F;
                        if (b == 0xE0)
                            low = 0xA0; // shorter forms
                        else if (b == 0xED)
                            high = 0x9F; // surrogates
                    }
                    else if (b >= 0xF0 && b <= 0xF4)
                    {
                        needed = 3;
                        c = b & 0x07;
                        if (b == 0xF0)
                            low = 0x90; // shorter forms
                        else if (b == 0xF4)
                            high = 0x8F; // above U+10FFFF
                    }
                    else
                    {
                        needed = 0;
                        c = -1;
                    }
                    i++;
                    for (; needed > 0 && i < byteCount; needed--)
                    {
                        int next = bytes[i];
                        if (next < low || next > high)
                            break;
                        c = (c << 6) | (next & 0x3F);
                        low = 0x80;
                        high = 0xBF;
                        i++;
                    }
                    if (needed > 0 || c < 0)
                    {
                        Invalid("DecoderFallbackException: the bytes aren't valid UTF-8");
                        c = 0xFFFD;
                    }
                }
                if (c < 0x10000)
                {
                    if (capacity >= 0)
                    {
                        if (written == capacity)
                            return -1;
                        chars[written] = (char)c;
                    }
                    written++;
                }
                else
                {
                    if (capacity >= 0)
                    {
                        if (capacity - written < 2)
                            return -1;
                        chars[written] = (char)(((c - 0x10000) >> 10) + 0xD800);
                        chars[written + 1] = (char)(((c - 0x10000) & 0x3FF) + 0xDC00);
                    }
                    written += 2;
                }
            }
            return written;
        }
    }
}
