namespace CsLib;

public sealed class IntStack
{
    private readonly int[] _items;
    private int _count;

    public IntStack(int capacity) => _items = new int[capacity];

    public void Push(int value) => _items[_count++] = value;

    public int Pop() => _items[--_count];
}
