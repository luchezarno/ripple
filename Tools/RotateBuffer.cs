using System.Collections;

namespace Ripple.Tools;

/// <summary>
/// Fixed-capacity rotating buffer. Old elements are automatically overwritten.
/// Ported from PowerShell.MCP (Cmdlets/RotateBuffer.cs) — used by FileTools.EditFile
/// to keep the most recent N lines as pre-match context without buffering the
/// entire file. Iteration order is oldest → newest.
/// </summary>
internal sealed class RotateBuffer<T> : IEnumerable<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;

    public RotateBuffer(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");
        _buffer = new T[capacity];
    }

    public int Count => _count;

    public void Add(T item)
    {
        _buffer[_head] = item;
        _head = (_head + 1) % _buffer.Length;
        if (_count < _buffer.Length) _count++;
    }

    public void Clear()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _head = 0;
        _count = 0;
    }

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index));
            int start = (_head - _count + _buffer.Length) % _buffer.Length;
            return _buffer[(start + index) % _buffer.Length];
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _count; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
