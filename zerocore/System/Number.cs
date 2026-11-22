namespace System
{
    // Numbers as text, for the primitives' ToString (and so for `"n=" + 5`,
    // string.Format and records). Each call allocates the string it returns.
    //
    // Doubles get up to 15 significant digits and floats 7, as .NET did
    // before .NET Core 3.0: always readable and right to 15 digits, but not the shortest text
    // that reads back to the same bits. lib's Fmt and Out do that.
    internal static unsafe class Number
    {
        internal static string FormatInt64(long value)
        {
            if (value >= 0)
                return FormatUInt64((ulong)value, negative: false);
            return FormatUInt64(unchecked(0 - (ulong)value), negative: true);
        }

        internal static string FormatUInt64(ulong value, bool negative = false)
        {
            char* digits = stackalloc char[21];
            int start = 21;
            do
            {
                digits[--start] = (char)('0' + (int)(value % 10));
                value /= 10;
            }
            while (value != 0);
            if (negative)
                digits[--start] = '-';
            return FromChars(digits + start, 21 - start);
        }

        // scientificFrom: the decimal exponent from which the text switches to
        // 1.5E+20 form; by default precision.
        internal static string FormatDouble(double value, int precision = 15, int scientificFrom = 0)
        {
            if (scientificFrom == 0)
                scientificFrom = precision;
            if (value != value)
                return "NaN";
            if (value == 1.0 / 0.0)
                return "∞";
            if (value == -1.0 / 0.0)
                return "-∞";
            if (value == 0)
                return (1 / value) < 0 ? "-0" : "0";

            char* text = stackalloc char[32];
            int length = 0;
            if (value < 0)
            {
                text[length++] = '-';
                value = -value;
            }

            // precision significant digits: value = digits * 10^(exponent - precision + 1).
            long low = 1;
            for (int i = 1; i < precision; i++)
                low *= 10;
            long high = low * 10;
            int exponent = (int)Math.Floor(Math.Log10(value));
            long digits = (long)Math.Round(value / Math.Pow(10, exponent - precision + 1));
            if (digits >= high)
            {
                digits /= 10;
                exponent++;
            }
            else if (digits < low)
            {
                digits = (long)Math.Round(value / Math.Pow(10, exponent - precision));
                exponent--;
            }

            char* d = stackalloc char[15];
            for (int i = precision - 1; i >= 0; i--)
            {
                d[i] = (char)('0' + (int)(digits % 10));
                digits /= 10;
            }
            int count = precision;
            while (count > 1 && d[count - 1] == '0')
                count--;

            if (exponent >= scientificFrom || exponent < -4)
            {
                // 1.5E+20, 1E-05: .NET's scientific form.
                text[length++] = d[0];
                if (count > 1)
                {
                    text[length++] = '.';
                    for (int i = 1; i < count; i++)
                        text[length++] = d[i];
                }
                text[length++] = 'E';
                text[length++] = exponent < 0 ? '-' : '+';
                int e = exponent < 0 ? -exponent : exponent;
                if (e >= 100)
                    text[length++] = (char)('0' + e / 100);
                text[length++] = (char)('0' + e / 10 % 10);
                text[length++] = (char)('0' + e % 10);
            }
            else if (exponent >= 0)
            {
                for (int i = 0; i <= exponent; i++)
                    text[length++] = i < count ? d[i] : '0';
                if (count > exponent + 1)
                {
                    text[length++] = '.';
                    for (int i = exponent + 1; i < count; i++)
                        text[length++] = d[i];
                }
            }
            else
            {
                text[length++] = '0';
                text[length++] = '.';
                for (int i = -1; i > exponent; i--)
                    text[length++] = '0';
                for (int i = 0; i < count; i++)
                    text[length++] = d[i];
            }
            return FromChars(text, length);
        }

        internal static string FromChars(char* chars, int length)
        {
            string result = string.Allocate(length);
            ref char target = ref result.FirstChar;
            for (int i = 0; i < length; i++)
                System.Runtime.CompilerServices.Unsafe.Add(ref target, i) = chars[i];
            return result;
        }

        // FNV-1a over a value's bytes, for GetHashCode.
        internal static int HashBytes(byte* bytes, int count)
        {
            uint hash = 2166136261;
            for (int i = 0; i < count; i++)
                hash = (hash ^ bytes[i]) * 16777619;
            return (int)hash;
        }
    }
}
