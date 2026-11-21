// Seq: LINQ-style query operators (Where, Select, Sum, Count, ...) over spans,
// built so that a query allocates nothing while it runs.
//
// .NET's LINQ allocates an iterator object per operator and a closure per
// capturing lambda. Without a garbage collector that memory is never given
// back, so a query in a loop would grow the program without limit. Here:
//
// - Each operator returns a ref struct that wraps the one before it, so a
//   whole query is one value on the stack. Nothing is allocated to build it
//   or to run it, and `foreach` runs it directly.
// - Lambdas that capture nothing are allocated once, the first time they run,
//   and then reused by the compiler. Mark them `static` so a capture by
//   mistake is a compile error.
// - A lambda that needs a value from outside takes it as a state argument
//   instead of capturing it: Where(min, static (min, x) => x > min).
//
// Queries start from a span (arrays included), a List<T> (AsQuery) or
// Seq.Range. Query syntax works too: `from x in numbers where x > 2 select x`.
// ToList(arena) is the way to keep results.
//
// GroupBy has to collect every item before it can answer, so it takes an
// arena, like ToList, and builds the groups there: a list of (key, items)
// pairs, in the order the keys first appear. It's freed with the arena. Keys
// are compared by their bytes, as in Dict. Query syntax's `group ... by` can't
// pass an arena, so call GroupBy as a method.
//
// A query reads its span as it runs, so the span must stay valid until it
// ends, and a query can't be stored in a field (it's a ref struct).

using System;

/// <summary>A source of items for a <see cref="Query{T, TSource}"/>, read one at a time.</summary>
public interface ISeqSource<T>
{
    /// <summary>Moves to the next item; false at the end.</summary>
    bool MoveNext();

    /// <summary>The item MoveNext moved to.</summary>
    T Current { get; }
}

/// <summary>A query: a chain of operators over a source. Build one with <see cref="Seq"/>.</summary>
public ref struct Query<T, TSource> where TSource : ISeqSource<T>, allows ref struct
{
    TSource _source;

    internal Query(TSource source)
    {
        _source = source;
    }

    /// <summary>The items <paramref name="predicate"/> accepts.</summary>
    public Query<T, WhereSource<T, TSource>> Where(Func<T, bool> predicate)
        => new(new WhereSource<T, TSource>(_source, predicate));

    /// <summary>The items <paramref name="predicate"/> accepts, with <paramref name="state"/> passed to it instead of captured.</summary>
    public Query<T, WhereSource<T, TState, TSource>> Where<TState>(TState state, Func<TState, T, bool> predicate)
        => new(new WhereSource<T, TState, TSource>(_source, state, predicate));

    /// <summary>Each item turned into another by <paramref name="selector"/>.</summary>
    public Query<TResult, SelectSource<T, TResult, TSource>> Select<TResult>(Func<T, TResult> selector)
        => new(new SelectSource<T, TResult, TSource>(_source, selector));

    /// <summary>Each item turned into another by <paramref name="selector"/>, with <paramref name="state"/> passed to it instead of captured.</summary>
    public Query<TResult, SelectSource<T, TResult, TState, TSource>> Select<TState, TResult>(TState state, Func<TState, T, TResult> selector)
        => new(new SelectSource<T, TResult, TState, TSource>(_source, state, selector));

    /// <summary>The first <paramref name="count"/> items.</summary>
    public Query<T, TakeSource<T, TSource>> Take(int count)
        => new(new TakeSource<T, TSource>(_source, count));

    /// <summary>All but the first <paramref name="count"/> items.</summary>
    public Query<T, SkipSource<T, TSource>> Skip(int count)
        => new(new SkipSource<T, TSource>(_source, count));

    /// <summary>The number of items.</summary>
    public int Count()
    {
        int count = 0;
        while (_source.MoveNext())
            count++;
        return count;
    }

    /// <summary>Whether there is at least one item.</summary>
    public bool Any() => _source.MoveNext();

    /// <summary>Whether <paramref name="predicate"/> accepts at least one item.</summary>
    public bool Any(Func<T, bool> predicate)
    {
        while (_source.MoveNext())
        {
            if (predicate(_source.Current))
                return true;
        }
        return false;
    }

    /// <summary>Whether <paramref name="predicate"/> accepts every item (true when there are none).</summary>
    public bool All(Func<T, bool> predicate)
    {
        while (_source.MoveNext())
        {
            if (!predicate(_source.Current))
                return false;
        }
        return true;
    }

    /// <summary>The first item, if there is one.</summary>
    public bool TryFirst(out T item)
    {
        if (_source.MoveNext())
        {
            item = _source.Current;
            return true;
        }
        item = default;
        return false;
    }

    /// <summary>Combines the items, starting from <paramref name="seed"/>: Aggregate(0, static (sum, x) => sum + x).</summary>
    public TAccumulate Aggregate<TAccumulate>(TAccumulate seed, Func<TAccumulate, T, TAccumulate> combine)
    {
        TAccumulate result = seed;
        while (_source.MoveNext())
            result = combine(result, _source.Current);
        return result;
    }

    /// <summary>Runs the query in a foreach loop.</summary>
    public Query<T, TSource> GetEnumerator() => this;

    /// <summary>For foreach.</summary>
    public bool MoveNext() => _source.MoveNext();

    /// <summary>For foreach.</summary>
    public T Current => _source.Current;
}

/// <summary>Starting points for queries, and the operators that need a particular item type.</summary>
public static class Seq
{
    /// <summary>A query over <paramref name="span"/>.</summary>
    public static Query<T, SpanSource<T>> AsQuery<T>(this ReadOnlySpan<T> span)
        => new(new SpanSource<T>(span));

    /// <summary>The items of <paramref name="span"/> that <paramref name="predicate"/> accepts.</summary>
    public static Query<T, WhereSource<T, SpanSource<T>>> Where<T>(this ReadOnlySpan<T> span, Func<T, bool> predicate)
        => span.AsQuery().Where(predicate);

    /// <summary>The items of <paramref name="span"/> that <paramref name="predicate"/> accepts, with <paramref name="state"/> passed to it.</summary>
    public static Query<T, WhereSource<T, TState, SpanSource<T>>> Where<T, TState>(this ReadOnlySpan<T> span, TState state, Func<TState, T, bool> predicate)
        => span.AsQuery().Where(state, predicate);

    /// <summary>Each item of <paramref name="span"/> turned into another by <paramref name="selector"/>.</summary>
    public static Query<TResult, SelectSource<T, TResult, SpanSource<T>>> Select<T, TResult>(this ReadOnlySpan<T> span, Func<T, TResult> selector)
        => span.AsQuery().Select(selector);

    /// <summary>Each item of <paramref name="span"/> turned into another by <paramref name="selector"/>, with <paramref name="state"/> passed to it.</summary>
    public static Query<TResult, SelectSource<T, TResult, TState, SpanSource<T>>> Select<T, TState, TResult>(this ReadOnlySpan<T> span, TState state, Func<TState, T, TResult> selector)
        => span.AsQuery().Select(state, selector);

    /// <summary>A query over the items of <paramref name="list"/>, read with Get, so adding to the list while the query runs is safe.</summary>
    public static Query<T, ListSource<T>> AsQuery<T>(this List<T> list) where T : unmanaged
        => new(new ListSource<T>(list));

    /// <summary>The <paramref name="count"/> numbers from <paramref name="start"/> up.</summary>
    public static Query<int, RangeSource> Range(int start, int count)
        => new(new RangeSource(start, count));

    /// <summary>The sum of the items.</summary>
    public static int Sum<TSource>(this Query<int, TSource> query) where TSource : ISeqSource<int>, allows ref struct
    {
        int sum = 0;
        foreach (int item in query)
            sum += item;
        return sum;
    }

    /// <summary>The sum of the items.</summary>
    public static long Sum<TSource>(this Query<long, TSource> query) where TSource : ISeqSource<long>, allows ref struct
    {
        long sum = 0;
        foreach (long item in query)
            sum += item;
        return sum;
    }

    /// <summary>The sum of the items.</summary>
    public static double Sum<TSource>(this Query<double, TSource> query) where TSource : ISeqSource<double>, allows ref struct
    {
        double sum = 0;
        foreach (double item in query)
            sum += item;
        return sum;
    }

    /// <summary>
    /// The items grouped by the key <paramref name="keySelector"/> gives each,
    /// as a list in <paramref name="arena"/> with one <see cref="Group{TKey, T}"/>
    /// per key, in the order the keys first appear. A not-created list if the
    /// arena is out of memory.
    /// </summary>
    public static List<Group<TKey, T>> GroupBy<T, TKey, TSource>(this Query<T, TSource> query, Arena arena, Func<T, TKey> keySelector)
        where T : unmanaged where TKey : unmanaged where TSource : ISeqSource<T>, allows ref struct
    {
        List<Group<TKey, T>> groups = List<Group<TKey, T>>.Create(arena);
        Dict<TKey, int> indexOf = Dict<TKey, int>.Create(arena);
        foreach (T item in query)
        {
            TKey key = keySelector(item);
            if (!indexOf.TryGet(key, out int index))
            {
                index = groups.Count;
                Group<TKey, T> group = new() { Key = key, Items = List<T>.Create(arena, 4) };
                if (!groups.Add(group) || !indexOf.Set(key, index))
                    return default;
            }
            if (!groups.Get(index).Items.Add(item))
                return default;
        }
        return groups;
    }

    /// <summary>The items of <paramref name="span"/> grouped by key; see the other GroupBy.</summary>
    public static List<Group<TKey, T>> GroupBy<T, TKey>(this ReadOnlySpan<T> span, Arena arena, Func<T, TKey> keySelector)
        where T : unmanaged where TKey : unmanaged
        => span.AsQuery().GroupBy(arena, keySelector);

    /// <summary>The items in a new list in <paramref name="arena"/> (a not-created list if it is out of memory).</summary>
    public static List<T> ToList<T, TSource>(this Query<T, TSource> query, Arena arena) where T : unmanaged where TSource : ISeqSource<T>, allows ref struct
    {
        List<T> list = List<T>.Create(arena);
        foreach (T item in query)
        {
            if (!list.Add(item))
                return default;
        }
        return list;
    }
}

/// <summary>One group from GroupBy: a key, and the items that have it, in their original order.</summary>
public struct Group<TKey, T> where TKey : unmanaged where T : unmanaged
{
    /// <summary>The key these items share.</summary>
    public TKey Key;

    /// <summary>The items, in the arena GroupBy was given.</summary>
    public List<T> Items;
}

/// <summary>The items of a List, in order.</summary>
public struct ListSource<T> : ISeqSource<T> where T : unmanaged
{
    readonly List<T> _list;
    int _index;

    internal ListSource(List<T> list)
    {
        _list = list;
        _index = -1;
    }

    public bool MoveNext() => ++_index < _list.Count;
    public T Current => _list.Get(_index);
}

/// <summary>The items of a span, in order.</summary>
public ref struct SpanSource<T> : ISeqSource<T>
{
    readonly ReadOnlySpan<T> _span;
    int _index;

    internal SpanSource(ReadOnlySpan<T> span)
    {
        _span = span;
        _index = -1;
    }

    public bool MoveNext() => ++_index < _span.Length;
    public T Current => _span[_index];
}

/// <summary>Consecutive numbers.</summary>
public struct RangeSource : ISeqSource<int>
{
    int _current;
    readonly int _end;

    internal RangeSource(int start, int count)
    {
        _current = start - 1;
        _end = start + (count < 0 ? 0 : count);
    }

    public bool MoveNext() => ++_current < _end;
    public int Current => _current;
}

/// <summary>Where's source.</summary>
public ref struct WhereSource<T, TSource> : ISeqSource<T> where TSource : ISeqSource<T>, allows ref struct
{
    TSource _source;
    readonly Func<T, bool> _predicate;

    internal WhereSource(TSource source, Func<T, bool> predicate)
    {
        _source = source;
        _predicate = predicate;
    }

    public bool MoveNext()
    {
        while (_source.MoveNext())
        {
            if (_predicate(_source.Current))
                return true;
        }
        return false;
    }

    public T Current => _source.Current;
}

/// <summary>Where's source, with state.</summary>
public ref struct WhereSource<T, TState, TSource> : ISeqSource<T> where TSource : ISeqSource<T>, allows ref struct
{
    TSource _source;
    readonly TState _state;
    readonly Func<TState, T, bool> _predicate;

    internal WhereSource(TSource source, TState state, Func<TState, T, bool> predicate)
    {
        _source = source;
        _state = state;
        _predicate = predicate;
    }

    public bool MoveNext()
    {
        while (_source.MoveNext())
        {
            if (_predicate(_state, _source.Current))
                return true;
        }
        return false;
    }

    public T Current => _source.Current;
}

/// <summary>Select's source.</summary>
public ref struct SelectSource<T, TResult, TSource> : ISeqSource<TResult> where TSource : ISeqSource<T>, allows ref struct
{
    TSource _source;
    readonly Func<T, TResult> _selector;
    TResult _current;

    internal SelectSource(TSource source, Func<T, TResult> selector)
    {
        _source = source;
        _selector = selector;
        _current = default;
    }

    public bool MoveNext()
    {
        if (!_source.MoveNext())
            return false;
        _current = _selector(_source.Current);
        return true;
    }

    public TResult Current => _current;
}

/// <summary>Select's source, with state.</summary>
public ref struct SelectSource<T, TResult, TState, TSource> : ISeqSource<TResult> where TSource : ISeqSource<T>, allows ref struct
{
    TSource _source;
    readonly TState _state;
    readonly Func<TState, T, TResult> _selector;
    TResult _current;

    internal SelectSource(TSource source, TState state, Func<TState, T, TResult> selector)
    {
        _source = source;
        _state = state;
        _selector = selector;
        _current = default;
    }

    public bool MoveNext()
    {
        if (!_source.MoveNext())
            return false;
        _current = _selector(_state, _source.Current);
        return true;
    }

    public TResult Current => _current;
}

/// <summary>Take's source.</summary>
public ref struct TakeSource<T, TSource> : ISeqSource<T> where TSource : ISeqSource<T>, allows ref struct
{
    TSource _source;
    int _left;

    internal TakeSource(TSource source, int count)
    {
        _source = source;
        _left = count;
    }

    public bool MoveNext()
    {
        if (_left <= 0)
            return false;
        _left--;
        return _source.MoveNext();
    }

    public T Current => _source.Current;
}

/// <summary>Skip's source.</summary>
public ref struct SkipSource<T, TSource> : ISeqSource<T> where TSource : ISeqSource<T>, allows ref struct
{
    TSource _source;
    int _skip;

    internal SkipSource(TSource source, int count)
    {
        _source = source;
        _skip = count;
    }

    public bool MoveNext()
    {
        while (_skip > 0)
        {
            _skip--;
            if (!_source.MoveNext())
                return false;
        }
        return _source.MoveNext();
    }

    public T Current => _source.Current;
}
