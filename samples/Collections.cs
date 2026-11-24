// Example: Generic List<T> and Dict<K,V> (lib/List.cs, lib/Dict.cs) in --stdlib:zero,
// and their versions for references: RefList<T>, StrDict<TValue> (string keys
// compared by text) and RefDict<TKey, TValue> (lib/RefList.cs, StrDict.cs, RefDict.cs).
// Build: ./build.sh Collections
//
// All of them take their memory from the Arena. List and Dict work with any
// unmanaged element type.

using System;
using System.Collections.Generic;

namespace CollectionsSample;

public struct Point
{
    public int X;
    public int Y;
}

// docs:descending
// A comparer as a struct: Sort is compiled for it; Compare is a direct call.
public struct Descending : IComparer<int>
{
    public int Compare(int a, int b) => b.CompareTo(a);
}
// docs:end

public static class Program
{
    public static int Main()
    {
        Arena arena = Arena.Create(64 * 1024);
        if (!arena.IsCreated)
            return 1;

        Out.Line("=== Collections Demo ===");
        Out.Line();

        Out.Line("--- List<int> ---");
        var numbers = List<int>.Create(arena, 0); // capacity is clamped to a minimum of 4
        for (int i = 1; i <= 10; i++)
            numbers.Add(i * 10);
        Out.Line("Count: ", numbers.Count, " | Capacity: ", numbers.Capacity);
        PrintList(numbers);

        numbers.RemoveAt(5);
        Out.Line("After RemoveAt(5):");
        PrintList(numbers);

        // List is a handle: the callee's Adds are visible to the caller.
        AddMore(numbers, 3);
        Out.Line("After AddMore(numbers, 3) in another method, Count: ", numbers.Count);

        List<int> notCreated = default;
        Out.Write("Add on default(List) returns ");
        Out.Write(notCreated.Add(1));
        Out.Line();
        Out.Line();

        Out.Line("--- List<Point> ---");
        var points = List<Point>.Create(arena);
        points.Add(new Point { X = 1, Y = 2 });
        points.Add(new Point { X = 10, Y = 20 });
        points.Add(new Point { X = 100, Y = 200 });
        for (int i = 0; i < points.Count; i++)
        {
            Point p = points.Get(i);
            Out.Write("[");
            Out.Write(i);
            Out.Write("] (");
            Out.Write(p.X);
            Out.Write(", ");
            Out.Write(p.Y);
            Out.Line(")");
        }
        Out.Line();

        Out.Line("--- Dict<int, int> ---");
        // docs:dict
        var scores = Dict<int, int>.Create(arena);
        scores.Set(1, 100);
        scores.Set(2, 250);
        scores.Set(3, 175);
        scores.Set(-42, 999);
        // docs:end
        Out.Line("Count: ", scores.Count);

        if (scores.TryGet(2, out int value))
            Out.Line("scores[2] = ", value);
        if (scores.TryGet(-42, out value))
            Out.Line("scores[-42] = ", value);
        if (!scores.TryGet(99, out value))
            Out.Line("scores[99] not found");

        // Growth: 1000 inserts into a map created with capacity 8.
        var squares = Dict<int, long>.Create(arena, 8);
        for (int i = 0; i < 1000; i++)
            squares.Set(i, (long)i * i);
        int found = 0;
        for (int i = 0; i < 1000; i++)
        {
            if (squares.TryGet(i, out long sq) && sq == (long)i * i)
                found++;
        }
        Out.Line("1000 inserts after growth, found: ", found);

        Dict<int, int> notCreatedMap = default;
        Out.Write("Lookup in default(Dict) misses instead of crashing: ");
        Out.Write(!notCreatedMap.ContainsKey(1));
        Out.Line();
        Out.Line();

        Out.Line("--- Dict<Point, int> ---");
        var pointIds = Dict<Point, int>.Create(arena);
        pointIds.Set(new Point { X = 0, Y = 0 }, 1);
        pointIds.Set(new Point { X = 10, Y = 10 }, 2);
        pointIds.Set(new Point { X = 5, Y = 5 }, 3);
        if (pointIds.TryGet(new Point { X = 10, Y = 10 }, out value))
            Out.Line("ID for (10, 10): ", value);

        Out.Line();

        Out.Line("--- Sort, Remove, foreach ---");
        var unsorted = List<int>.Create(arena);
        int[] values = { 42, 7, 19, 3, 88, 7, 61 };
        foreach (int v in values)
            unsorted.Add(v);
        unsorted.Sort(&Ascending); // any comparison: pass a static method's address
        Out.Write("Ascending:  ");
        PrintList(unsorted);
        unsorted.Sort(&ByLastDigit);
        Out.Write("By last digit, then value: ");
        PrintList(unsorted);
        // docs:sort-comparers
        unsorted.Sort();                  // by int's IComparable<int>
        Out.Write("Sort():     ");
        PrintList(unsorted);
        unsorted.Sort(new Descending());  // by a struct: an IComparer<int>
        Out.Write("Descending: ");
        PrintList(unsorted);

        Span<int> span = values;          // spans sort in place too
        span.Sort();
        Out.Line($"BinarySearch(19): {span.BinarySearch(19)}, "
            + $"(20) would go at {~span.BinarySearch(20)}");
        // docs:end
        span.Reverse();
        Out.Line($"Reversed: {span[0]} ... {span[^1]}");
        span.Sort((a, b) => a % 10 - b % 10);
        Out.Line($"By a lambda, last digit first: {span[0]}, then {span[1]}");

        Out.Line($"Remove(3): {scores.Remove(3)}, again: {scores.Remove(3)}, Count: {scores.Count}");
        long total = 0;
        int pairs = 0;
        foreach (var pair in scores) // no particular order
        {
            total += pair.Value;
            pairs++;
        }
        Out.Line($"foreach over scores: {pairs} pairs, values sum to {total}");
        for (int i = 0; i < 1000; i += 2)
            squares.Remove(i);
        int odd = 0;
        foreach (var pair in squares)
        {
            if (pair.Key % 2 == 1 && pair.Value == (long)pair.Key * pair.Key)
                odd++;
        }
        Out.Line($"After removing the 500 even keys: Count {squares.Count}, odd keys intact: {odd}");
        Out.Line();

        // Collections are handles, so `var b = a` shares contents. Clone makes a
        // real copy, in any arena: here one that outlives the original's.
        // List<T> takes only unmanaged types; RefList<T> holds references.
        Out.Line("--- RefList<string> ---");
        var names = RefList<string>.Create(arena, 0);
        string[] words = { "delta", "alpha", "charlie", "bravo" };
        foreach (string word in words)
            names.Add(Str.Format(arena, $"{word}:{word.Length}")); // strings built in the arena
        names.Add(null);
        names.Sort(&ByName);
        Out.Write("Sorted (null first): ");
        PrintNames(names);
        names.RemoveAt(0);
        names.Set(0, "ALPHA");
        Out.Write("RemoveAt(0), Set(0): ");
        PrintNames(names);
        var codes = RefList<string>.Create(arena, 0);
        codes.Add("b");
        codes.Add("C");
        codes.Add("a");
        codes.Sort(); // string's CompareTo: by UTF-16 code unit, so "C" first
        Out.Write("Sort(): ");
        PrintNames(codes);
        RefList<string> noNames = default;
        Out.Line($"default(RefList): Count {noNames.Count}, Add returns {noNames.Add("x")}");
        Out.Line();

        // Keys compared by their text; each new key is copied into the map.
        Out.Line("--- StrDict ---");
        var counts = StrDict<int>.Create(arena, 0);
        Span<char> wordBuffer = stackalloc char[8]; // every word is read into this buffer
        // docs:word-counts
        foreach (ReadOnlySpan<char> w in "the cat and the hat and the bat".Split(' '))
        {
            for (int i = 0; i < w.Length; i++)
                wordBuffer[i] = w[i];
            ReadOnlySpan<char> key = wordBuffer.Slice(0, w.Length);
            counts.TryGet(key, out int n);
            counts.Set(key, n + 1);
        }
        // docs:end
        Out.Line($"Word counts: {counts.Count} distinct, the = {Count(counts, "the")}, and = {Count(counts, "and")}, bat = {Count(counts, "bat")}, dog = {Count(counts, "dog")}");

        // Keys made in a scratch arena that is reset every time: the map has its own copies.
        var squareMap = StrDict<long>.Create(arena, 0);
        Arena scratch = Arena.Create(256);
        for (int i = 0; i < 500; i++)
        {
            unsafe { scratch.Reset(); } // SAFETY: nothing from scratch is used after this.
            squareMap.Set(Str.Format(scratch, $"key{i}"), (long)i * i);
        }
        int removed = 0;
        for (int i = 0; i < 500; i += 2)
        {
            unsafe { scratch.Reset(); } // SAFETY: as above.
            if (squareMap.Remove(Str.Format(scratch, $"key{i}")))
                removed++;
        }
        int oddFound = 0;
        for (int i = 1; i < 500; i += 2)
        {
            unsafe { scratch.Reset(); } // SAFETY: as above.
            if (squareMap.TryGet(Str.Format(scratch, $"key{i}"), out long square) && square == (long)i * i)
                oddFound++;
        }
        long squareSum = 0;
        int squarePairs = 0;
        foreach (var pair in squareMap)
        {
            squareSum += pair.Value;
            squarePairs++;
        }
        unsafe { scratch.Destroy(); } // SAFETY: nothing from scratch is used after this.
        Out.Line($"500 keys, {removed} even ones removed: Count {squareMap.Count}, odd keys intact: {oddFound}, foreach: {squarePairs} pairs summing to {squareSum}");
        Out.Line($"key0 gone: {!squareMap.ContainsKey("key0")}, key499 = {(squareMap.TryGet("key499", out long last) ? last : -1)}");

        // docs:labels
        var labels = StrDict<string>.Create(arena); // values can be references too
        labels.Set("ok", "fine");
        labels.Set("ok", Str.Format(arena, $"fine x{2}"));
        // docs:end
        Out.Line($"StrDict<string>: ok = {(labels.TryGet("ok", out string label) ? label : "missing")}, Count {labels.Count}");
        StrDict<int> noCounts = default;
        Out.Line($"default(StrDict): Count {noCounts.Count}, Set returns {noCounts.Set("x", 1)}, found: {noCounts.ContainsKey("x")}");
        Out.Line();

        // Unmanaged keys, reference values.
        Out.Line("--- RefDict<int, string> ---");
        // docs:refdict
        var byId = RefDict<int, string>.Create(arena);
        byId.Set(7, "seven");
        byId.Set(3, Str.Format(arena, $"three is {3}"));
        byId.Set(9, null);
        // docs:end
        bool nineFound = byId.TryGet(9, out string nine);
        bool fourFound = byId.TryGet(4, out string four);
        Out.Line($"3 = {(byId.TryGet(3, out string three) ? three : "missing")}, 9 found: {nineFound} and null: {nine is null}, 4 found: {fourFound} and null: {four is null}");
        Out.Line($"Remove(7): {byId.Remove(7)}, again: {byId.Remove(7)}, Count: {byId.Count}");
        int ids = 0;
        foreach (var pair in byId)
            ids += pair.Key;
        Out.Line($"foreach: keys sum to {ids}");
        Out.Line();

        Out.Line("--- Clone ---");
        // docs:clone
        Arena keep = Arena.Create(4096);
        StrDict<int> countsCopy = counts.Clone(keep); // keys are copied into keep too
        List<int> snapshot = numbers.Clone(keep);
        Dict<int, int> scoresCopy = scores.Clone(keep);
        // docs:end
        numbers.Set(0, -1);
        scores.Set(2, 0);
        Out.Line($"Original changed: numbers[0] = {numbers.Get(0)}, scores[2] = {(scores.TryGet(2, out value) ? value : -1)}");
        Out.Line($"Clones unchanged: snapshot[0] = {snapshot.Get(0)}, scoresCopy[2] = {(scoresCopy.TryGet(2, out value) ? value : -1)}");
        Out.Line($"Clone capacity fits its {snapshot.Count} items: {snapshot.Capacity}");
        Out.Write("Clone of default(List) is not created: ");
        Out.Write(!notCreated.Clone(keep).IsCreated);
        Out.Line();

        Out.Line();
        Out.Line("Arena used: ", (long)arena.Used, " bytes");

        unsafe { arena.Destroy(); } // SAFETY: only the clones, which live in `keep`, are used after this.

        snapshot.Add(1234); // the clone grows in its own arena
        countsCopy.Set("dog", 1);
        Out.Line($"StrDict clone, keys copied: the = {Count(countsCopy, "the")}, dog = {Count(countsCopy, "dog")}, Count {countsCopy.Count}");
        Out.Line($"After destroying the first arena, clones still work: {snapshot.Count} items, last = {snapshot.Get(snapshot.Count - 1)}, scoresCopy[-42] = {(scoresCopy.TryGet(-42, out value) ? value : -1)}");
        unsafe { keep.Destroy(); } // SAFETY: the clones are not used after this.
        Out.Line("Done!");
        return 0;
    }

    static void AddMore(List<int> list, int n)
    {
        for (int i = 0; i < n; i++)
            list.Add(1000 + i);
    }

    static int Ascending(int a, int b) => a < b ? -1 : a > b ? 1 : 0;

    static int ByLastDigit(int a, int b) => a % 10 != b % 10 ? a % 10 - b % 10 : Ascending(a, b);

    static int ByName(string a, string b) => Str.Compare(a, b);

    static int Count(StrDict<int> counts, string word) => counts.TryGet(word, out int n) ? n : 0;

    static void PrintNames(RefList<string> names)
    {
        Out.Write("[");
        bool first = true;
        foreach (string name in names)
        {
            if (!first)
                Out.Write(", ");
            Out.Write(name is null ? "null" : name);
            first = false;
        }
        Out.Line($"] ({names.Count} items)");
    }

    static void PrintList(List<int> list)
    {
        Out.Write("[");
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0)
                Out.Write(", ");
            Out.Write(list.Get(i));
        }
        Out.Line("]");
    }
}
