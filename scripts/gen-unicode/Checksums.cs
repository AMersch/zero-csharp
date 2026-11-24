using System.Globalization;

// What samples/Chars.cs computes in zero mode, computed here by .NET: one hash
// per char method over every char (and for categories every code point).
// The sample holds these numbers; they change when the Unicode data does.
static class Checksums
{
    static uint Mix(uint hash, uint value) => (hash ^ value) * 16777619;

    static uint OverChars(Func<char, uint> f)
    {
        uint hash = 2166136261;
        for (int c = 0; c <= 0xFFFF; c++)
            hash = Mix(hash, f((char)c));
        return hash;
    }

    static uint B(bool b) => b ? 1u : 0u;

    public static void Print()
    {
        var lines = new (string Name, uint Hash)[]
        {
            ("IsLetter", OverChars(c => B(char.IsLetter(c)))),
            ("IsDigit", OverChars(c => B(char.IsDigit(c)))),
            ("IsNumber", OverChars(c => B(char.IsNumber(c)))),
            ("IsLetterOrDigit", OverChars(c => B(char.IsLetterOrDigit(c)))),
            ("IsUpper", OverChars(c => B(char.IsUpper(c)))),
            ("IsLower", OverChars(c => B(char.IsLower(c)))),
            ("IsPunctuation", OverChars(c => B(char.IsPunctuation(c)))),
            ("IsSymbol", OverChars(c => B(char.IsSymbol(c)))),
            ("IsSeparator", OverChars(c => B(char.IsSeparator(c)))),
            ("IsWhiteSpace", OverChars(c => B(char.IsWhiteSpace(c)))),
            ("IsControl", OverChars(c => B(char.IsControl(c)))),
            ("Category", OverChars(c => (uint)char.GetUnicodeCategory(c))),
            ("ToUpper", OverChars(c => char.ToUpper(c))),
            ("ToLower", OverChars(c => char.ToLower(c))),
            ("ToUpperInvariant", OverChars(c => char.ToUpperInvariant(c))),
            ("ToLowerInvariant", OverChars(c => char.ToLowerInvariant(c))),
            ("NumericValue", OverChars(c => (uint)(long)(char.GetNumericValue(c) * 1000000))),
            ("CodePointCategory", CodePoints()),
        };
        foreach (var (name, hash) in lines)
            Console.WriteLine($"{name} {hash:x8}");
    }

    // Every code point's category, and the numeric value of every supplementary
    // one that has one (through a string, as .NET offers it).
    static uint CodePoints()
    {
        uint hash = 2166136261;
        for (int cp = 0; cp < 0x110000; cp++)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(cp);
            hash = Mix(hash, (uint)category);
            if (cp > 0xFFFF && category is UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber)
                hash = Mix(hash, (uint)(long)(char.GetNumericValue(char.ConvertFromUtf32(cp), 0) * 1000000));
        }
        return hash;
    }
}
