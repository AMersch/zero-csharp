namespace System.Text
{
    // The small StringBuilder that records' ToString and string.Format use:
    // Append and ToString. It grows by doubling, with new, so it allocates;
    // lib's StrBuilder builds strings in an arena instead.
    public sealed class StringBuilder
    {
        private char[] _chars = new char[16];
        private int _length;

        public int Length => _length;

        public StringBuilder Append(char value)
        {
            if (_length == _chars.Length)
            {
                char[] bigger = new char[_chars.Length * 2];
                for (int i = 0; i < _length; i++)
                    bigger[i] = _chars[i];
                _chars = bigger;
            }
            _chars[_length++] = value;
            return this;
        }

        public StringBuilder Append(string value)
        {
            if (value != null)
            {
                for (int i = 0; i < value.Length; i++)
                    Append(value[i]);
            }
            return this;
        }

        public StringBuilder Append(object value) => value == null ? this : Append(value.ToString());

        public StringBuilder Append(int value) => Append(Number.FormatInt64(value));

        public StringBuilder Append(long value) => Append(Number.FormatInt64(value));

        public StringBuilder Append(bool value) => Append(value ? "True" : "False");

        public StringBuilder Append(double value) => Append(Number.FormatDouble(value));

        public override string ToString()
        {
            string result = string.Allocate(_length);
            ref char target = ref result.FirstChar;
            for (int i = 0; i < _length; i++)
                System.Runtime.CompilerServices.Unsafe.Add(ref target, i) = _chars[i];
            return result;
        }
    }
}
