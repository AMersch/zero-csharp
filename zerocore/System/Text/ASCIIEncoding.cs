namespace System.Text
{
    // ASCII: one byte per char. A char above 0x7F becomes '?' (so does each
    // half of a surrogate pair), and so does a byte above 0x7F, as in .NET.
    public unsafe class ASCIIEncoding : Encoding
    {
        public ASCIIEncoding() : base(false) { }

        public override int CodePage => 20127;
        public override string WebName => "us-ascii";
        public override string EncodingName => "US-ASCII";
        public override bool IsSingleByte => true;

        public override int GetMaxByteCount(int charCount)
        {
            if (charCount < 0 || charCount == 0x7FFFFFFF)
                Environment.FailFast("ArgumentOutOfRangeException: charCount");
            return charCount + 1;
        }

        public override int GetMaxCharCount(int byteCount)
        {
            if (byteCount < 0)
                Environment.FailFast("ArgumentOutOfRangeException: byteCount");
            return byteCount;
        }

        internal override int Encode(char* chars, int charCount, byte* bytes, int capacity)
        {
            if (capacity >= 0)
            {
                if (capacity < charCount)
                    return -1;
                for (int i = 0; i < charCount; i++)
                    bytes[i] = chars[i] < 0x80 ? (byte)chars[i] : (byte)'?';
            }
            return charCount;
        }

        internal override int Decode(byte* bytes, int byteCount, char* chars, int capacity)
        {
            if (capacity >= 0)
            {
                if (capacity < byteCount)
                    return -1;
                for (int i = 0; i < byteCount; i++)
                    chars[i] = bytes[i] < 0x80 ? (char)bytes[i] : '?';
            }
            return byteCount;
        }
    }

    // ISO-8859-1, Encoding.Latin1: the first 256 chars, one byte each. A char
    // above U+00FF becomes the Latin-1 character .NET's best fit gives it ('Ā'
    // becomes 'A'), or '?'.
    internal sealed unsafe class Latin1Encoding : Encoding
    {
        public Latin1Encoding() : base(false) { }

        public override int CodePage => 28591;
        public override string WebName => "iso-8859-1";
        public override string EncodingName => "Western European (ISO)";
        public override bool IsSingleByte => true;

        public override int GetMaxByteCount(int charCount)
        {
            if (charCount < 0 || charCount == 0x7FFFFFFF)
                Environment.FailFast("ArgumentOutOfRangeException: charCount");
            return charCount + 1;
        }

        public override int GetMaxCharCount(int byteCount)
        {
            if (byteCount < 0)
                Environment.FailFast("ArgumentOutOfRangeException: byteCount");
            return byteCount;
        }

        internal override int Encode(char* chars, int charCount, byte* bytes, int capacity)
        {
            if (capacity >= 0)
            {
                if (capacity < charCount)
                    return -1;
                for (int i = 0; i < charCount; i++)
                {
                    char c = chars[i];
                    bytes[i] = c <= 0xFF ? (byte)c : System.Globalization.UnicodeData.Latin1BestFitFor(c);
                }
            }
            return charCount;
        }

        internal override int Decode(byte* bytes, int byteCount, char* chars, int capacity)
        {
            if (capacity >= 0)
            {
                if (capacity < byteCount)
                    return -1;
                for (int i = 0; i < byteCount; i++)
                    chars[i] = (char)bytes[i];
            }
            return byteCount;
        }
    }
}
