// Example: Ptr<T> (lib/Ptr.cs), pointers between items in an arena, for the
// structures a struct can't hold by itself: a doubly linked list, which is
// full of cycles, and a tree. Exit code = number of failed checks.
// Build: ./build.sh Links
//
// No unsafe anywhere: Ptr.Value gives a ref to the item, a null Ptr stops
// the program when used, and the items live until the arena is reset.

using System;

namespace LinksSample;

// docs:node
// One item of a doubly linked list: a struct can't contain itself, but it
// can hold pointers to others of its kind.
public struct Node
{
    public int Number;
    public Ptr<Node> Prev;
    public Ptr<Node> Next;
}
// docs:end

// docs:chain
// The list itself: its two ends, and the arena its items live in.
public struct Chain
{
    public Arena Arena;
    public Ptr<Node> First;
    public Ptr<Node> Last;

    public void AddLast(int number)
    {
        Ptr<Node> node = Arena.New(new Node { Number = number, Prev = Last });
        if (Last.IsNull)
            First = node;
        else
            Last.Value.Next = node;
        Last = node;
    }

    // Takes the node out by joining its neighbours to each other.
    public void Remove(Ptr<Node> node)
    {
        ref Node n = ref node.Value;
        if (n.Prev.IsNull)
            First = n.Next;
        else
            n.Prev.Value.Next = n.Next;
        if (n.Next.IsNull)
            Last = n.Prev;
        else
            n.Next.Value.Prev = n.Prev;
        n.Prev = default;
        n.Next = default;
    }
}
// docs:end

// docs:tree
public struct TreeNode
{
    public int Key;
    public Ptr<TreeNode> Left;
    public Ptr<TreeNode> Right;
}
// docs:end

public static class Program
{
    static int s_failures;
    static int s_sectionChecks;
    static int s_sectionFailures;

    static void Section(string section)
    {
        Out.Line($"{(s_sectionFailures == 0 ? "PASS" : "FAIL")} {section} ({s_sectionChecks} checks)");
        s_sectionChecks = 0;
        s_sectionFailures = 0;
    }

    static void Check(string name, bool ok)
    {
        s_sectionChecks++;
        if (!ok)
        {
            s_failures++;
            s_sectionFailures++;
            Out.Line($"  FAIL: {name}");
        }
    }

    // The numbers front to back, then back to front, as one number each.
    static int Forward(Chain chain)
    {
        int digits = 0;
        for (Ptr<Node> p = chain.First; !p.IsNull; p = p.Value.Next)
            digits = digits * 10 + p.Value.Number;
        return digits;
    }

    static int Backward(Chain chain)
    {
        int digits = 0;
        for (Ptr<Node> p = chain.Last; !p.IsNull; p = p.Value.Prev)
            digits = digits * 10 + p.Value.Number;
        return digits;
    }

    // docs:insert
    // Recursion works as it would with classes.
    static Ptr<TreeNode> Insert(Arena arena, Ptr<TreeNode> node, int key)
    {
        if (node.IsNull)
            return arena.New(new TreeNode { Key = key });
        ref TreeNode n = ref node.Value;
        if (key < n.Key)
            n.Left = Insert(arena, n.Left, key);
        else
            n.Right = Insert(arena, n.Right, key);
        return node;
    }
    // docs:end

    // The keys in order, as digits of one number.
    static long InOrder(Ptr<TreeNode> node, long digits)
    {
        if (node.IsNull)
            return digits;
        digits = InOrder(node.Value.Left, digits);
        digits = digits * 10 + node.Value.Key;
        return InOrder(node.Value.Right, digits);
    }

    static int Height(Ptr<TreeNode> node)
        => node.IsNull ? 0 : 1 + Math.Max(Height(node.Value.Left), Height(node.Value.Right));

    public static int Main()
    {
        Arena arena = Arena.Create(4096);

        // docs:use
        var chain = new Chain { Arena = arena };
        for (int i = 1; i <= 5; i++)
            chain.AddLast(i);
        chain.Remove(chain.First.Value.Next);   // takes out the 2
        // docs:end
        Check("walks forward", Forward(chain) == 1345);
        Check("walks backward", Backward(chain) == 5431);
        chain.Remove(chain.First);
        chain.Remove(chain.Last);
        Check("ends removed", Forward(chain) == 34 && chain.First.Value.Prev.IsNull);
        Section("a doubly linked list");

        // A ring: the last node points back at the first.
        chain.Last.Value.Next = chain.First;
        chain.First.Value.Prev = chain.Last;
        int steps = 0;
        int sum = 0;
        Ptr<Node> p = chain.First;
        do
        {
            sum += p.Value.Number;
            p = p.Value.Next;
            steps++;
        } while (p != chain.First && steps < 10);
        Check("goes round once", steps == 2 && sum == 7);
        Check("same node, same Ptr", chain.First.Value.Next.Value.Next == chain.First);
        Section("a ring");

        Ptr<TreeNode> root = default;
        foreach (int key in new[] { 5, 3, 8, 1, 4, 7, 9 })
            root = Insert(arena, root, key);
        Check("keys in order", InOrder(root, 0) == 1345789);
        Check("balanced as inserted", Height(root) == 3);
        Section("a tree");

        var zeroed = arena.New<Node>();
        Check("New<T>() gives a zeroed item", zeroed.Value.Number == 0 && zeroed.Value.Next.IsNull);
        Ptr<Node> none = default;
        Check("default is null", none.IsNull && none == default(Ptr<Node>) && none != chain.First);
        Arena.DebugChecks = true;
        Arena checkedArena = Arena.Create(256);
        Ptr<int> counter = checkedArena.New(41);
        counter.Value++;
        Check("DebugChecks lets a live item through", counter.Value == 42);
        Arena.DebugChecks = false;
        Section("Ptr itself");

        Out.Line();
        if (s_failures == 0)
            Out.Line("All checks passed.");
        else
            Out.Line($"{s_failures} checks FAILED.");
        return s_failures;
    }
}
