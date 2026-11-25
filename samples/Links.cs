// Example: Ptr<T> (lib/Ptr.cs), pointers between items in an arena, for the
// structures a struct can't hold by itself: a doubly linked list, which is
// full of cycles, and a tree. Then Pool<T> and Handle<T> (lib/Pool.cs), for
// items removed one by one, handed over with Move, and kept from a
// ScratchPool, and LinkedList<T> (lib/LinkedList.cs), which is built on them.
// Exit code = number of failed checks.
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

// docs:task
// A task that waits for another: items in a pool point at each other with
// handles, which are plain structs too.
public struct Job
{
    public int Id;
    public Handle<Job> WaitsFor;
}
// docs:end

public static class Program
{
    static int s_failures;
    static int s_sectionChecks;
    static int s_sectionFailures;

    static void Section(string section)
    {
        Console.WriteLine($"{(s_sectionFailures == 0 ? "PASS" : "FAIL")} {section} ({s_sectionChecks} checks)");
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
            Console.WriteLine($"  FAIL: {name}");
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

    // The values as pairs of digits, first to last.
    static int Digits(LinkedList<int> list)
    {
        int digits = 0;
        foreach (int item in list)
            digits = digits * 100 + item;
        return digits;
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

        // docs:pool
        var jobs = Pool<Job>.Create(arena);
        Handle<Job> build = jobs.Add(new Job { Id = 1 });
        Handle<Job> test = jobs.Add(new Job { Id = 2, WaitsFor = build });
        Handle<Job> ship = jobs.Add(new Job { Id = 3, WaitsFor = test });
        jobs.Remove(test);
        // ship still holds the handle, but it no longer finds anything,
        // even once the slot is used again for another job.
        Handle<Job> docs = jobs.Add(new Job { Id = 4 });
        bool waiting = jobs.Contains(jobs[ship].WaitsFor);   // false
        // docs:end
        Check("a removed item is refused", !waiting && !jobs.TryGet(test, out _) && jobs.Count == 3);
        Check("its slot was reused", docs != test && jobs[docs].Id == 4);
        jobs[build].Id += 10;
        int ids = 0;
        foreach (Handle<Job> job in jobs)
            ids += jobs[job].Id;
        Check("change in place, walk the handles", ids == 11 + 3 + 4);
        var growing = Pool<int>.Create(arena, 8);
        Handle<int> firstNumber = growing.Add(1);
        ref int kept = ref growing[firstNumber];
        for (int i = 0; i < 100; i++)
            growing.Add(i);
        kept = 42;
        Check("items don't move as the pool grows", growing[firstNumber] == 42 && growing.Count == 101);
        growing.Clear();
        Check("Clear refuses every handle", growing.Count == 0 && !growing.Contains(firstNumber));
        Section("a pool");

        // docs:move
        Handle<Job> mine = jobs.Add(new Job { Id = 7 });
        Handle<Job> copy = mine;
        Handle<Job> theirs = jobs.Move(ref mine);
        // mine is now null, and every copy of it is refused: only the new
        // owner reaches the job.
        // docs:end
        Check("Move: the new handle finds the item", mine.IsNull && jobs[theirs].Id == 7);
        Check("Move: old copies are refused", !jobs.Contains(copy));
        var done = Pool<Job>.Create(arena);
        Handle<Job> finished = jobs.MoveTo(done, ref theirs);
        Check("MoveTo: into the other pool", theirs.IsNull && done[finished].Id == 7 && done.Count == 1);
        Check("MoveTo: gone from the first", jobs.Count == 3);
        Check("another pool's handles are refused", !done.Contains(build) && !jobs.Contains(finished));
        Section("moving");

        // docs:scratch-pool
        // Work in a scratch pool, then move the one result to keep into a
        // pool of yours; the rest goes with the scratch memory.
        var results = Pool<Job>.Create(arena);
        Handle<Job> best = Arena.Scratch(ref results,
            static (scoped ScratchScope scratch, scoped ref Pool<Job> keep) =>
            {
                ScratchPool<Job> tries = scratch.Pool<Job>();
                Handle<Job> top = default;
                for (int id = 1; id <= 20; id++)
                {
                    Handle<Job> next = tries.Add(new Job { Id = id * 7 % 20 });
                    if (top.IsNull || tries[next].Id > tries[top].Id)
                        top = next;
                }
                return tries.MoveTo(keep, ref top);
            });
        // docs:end
        Check("the result was kept", results.Count == 1 && results[best].Id == 19);
        Section("a scratch pool");

        // docs:linked-list
        var list = LinkedList<int>.Create(arena);
        var two = list.AddLast(2);
        list.AddFirst(1);
        var four = list.AddAfter(two, 4);
        list.AddBefore(four, 3);
        list.AddLast(5);
        list.Remove(two);
        foreach (ref int item in list)
            item *= 10;                         // 10, 30, 40, 50
        // docs:end
        Check("Add and Remove", Digits(list) == 10304050 && list.Count == 4);
        Check("a removed node is refused", !list.Remove(two) && !list.Contains(two));
        int backward = 0;
        for (var node = list.Last; !node.IsNull; node = list[node].Previous)
            backward = backward * 100 + list[node].Value;
        Check("walks backward", backward == 50403010);
        Check("Find", list[list.Find(40)].Value == 40 && list.Find(20).IsNull && list.Contains(30));
        list.RemoveFirst();
        list.RemoveLast();
        Check("RemoveFirst, RemoveLast", Digits(list) == 3040 && list[list.First].Next == list.Last);
        list.Clear();
        Check("Clear", list.Count == 0 && list.First.IsNull && !list.Contains(four));
        Section("LinkedList");

        Console.WriteLine();
        if (s_failures == 0)
            Console.WriteLine("All checks passed.");
        else
            Console.WriteLine($"{s_failures} checks FAILED.");
        return s_failures;
    }
}
