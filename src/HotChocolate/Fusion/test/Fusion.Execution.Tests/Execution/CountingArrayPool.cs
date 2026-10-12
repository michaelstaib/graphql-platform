using System.Buffers;

namespace HotChocolate.Fusion.Execution;

/// <summary>
/// An <see cref="ArrayPool{T}"/> that records every rented and returned array and hands a returned array
/// out again without clearing it.
/// </summary>
internal sealed class CountingArrayPool : ArrayPool<ulong>
{
    private readonly object _sync = new();
    private readonly List<ulong[]> _available = [];
    private readonly List<ulong[]> _rented = [];
    private readonly List<ulong[]> _returned = [];

    /// <summary>
    /// Gets the arrays handed out so far, in rent order.
    /// </summary>
    public ulong[][] Rented
    {
        get
        {
            lock (_sync)
            {
                return [.. _rented];
            }
        }
    }

    /// <summary>
    /// Gets the arrays returned so far, in return order.
    /// </summary>
    public ulong[][] Returned
    {
        get
        {
            lock (_sync)
            {
                return [.. _returned];
            }
        }
    }

    /// <summary>
    /// Gets the number of arrays rented and not yet returned.
    /// </summary>
    public int Outstanding
    {
        get
        {
            lock (_sync)
            {
                return _rented.Count - _returned.Count;
            }
        }
    }

    /// <summary>
    /// Makes <paramref name="array"/> the next array a rent receives, with its content untouched.
    /// </summary>
    public void Seed(ulong[] array)
    {
        lock (_sync)
        {
            _available.Add(array);
        }
    }

    public override ulong[] Rent(int minimumLength)
    {
        lock (_sync)
        {
            var index = _available.FindIndex(array => array.Length >= minimumLength);
            ulong[] array;

            if (index >= 0)
            {
                array = _available[index];
                _available.RemoveAt(index);
            }
            else
            {
                array = new ulong[minimumLength];
            }

            _rented.Add(array);
            return array;
        }
    }

    public override void Return(ulong[] array, bool clearArray = false)
    {
        lock (_sync)
        {
            _returned.Add(array);
            _available.Add(array);
        }
    }
}
