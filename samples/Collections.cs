// Example: Generic List<T> and Dict<K,V> (lib/List.cs, lib/Dict.cs) in --stdlib:zero.
// Build: ./build.sh Collections
//
// Both collections take their memory from the Arena and work with any
// unmanaged element type.

namespace CollectionsSample;

public struct Point
{
    public int X;
    public int Y;
}

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
        var scores = Dict<int, int>.Create(arena);
        scores.Set(1, 100);
        scores.Set(2, 250);
        scores.Set(3, 175);
        scores.Set(-42, 999);
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
        Out.Line("--- Clone ---");
        Arena keep = Arena.Create(4096);
        List<int> snapshot = numbers.Clone(keep);
        Dict<int, int> scoresCopy = scores.Clone(keep);
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
