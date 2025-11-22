using Sundouleia.DrawSystem.Selector;
using ZstdSharp.Unsafe;

namespace Sundouleia.DrawSystem;

public class DynamicSorter<T> : IReadOnlyList<ISortMethod<T>> where T : class
{
    private readonly List<ISortMethod<T>> _sortSteps = new();

    /// <summary>
    ///     If the sorter does the first step in descending order.
    /// </summary>
    public bool FirstDescending = false;

    // Expose read-only access.
    public int Count => _sortSteps.Count;
    public ISortMethod<T> this[int index] => _sortSteps[index];

    public IEnumerator<ISortMethod<T>> GetEnumerator()
        => _sortSteps.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()
        => _sortSteps.GetEnumerator();

    // Mutating API.
    /// <summary>
    ///     Adds a sort step to the sorter. Duplicate methods are voided.
    /// </summary>
    public void Add(ISortMethod<T> sortMethod)
    {
        if (!_sortSteps.Contains(sortMethod))
            return;
        _sortSteps.Add(sortMethod);
    }

    /// <summary>
    ///     Removes a sort step.
    /// </summary>
    public void Remove(ISortMethod<T> sortMethod)
        => _sortSteps.Remove(sortMethod);

    /// <summary>
    ///     Clears all sort steps.
    /// </summary>
    public void Clear()
        => _sortSteps.Clear();

    public void Move(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= _sortSteps.Count) return;
        if (newIndex < 0 || newIndex >= _sortSteps.Count) return;

        var m = _sortSteps[oldIndex];
        _sortSteps.RemoveAt(oldIndex);
        _sortSteps.Insert(newIndex, m);
    }

    public IEnumerable<TItem> SortItems<TItem>(IEnumerable<TItem> items, Func<TItem, IComparable?> defaultKey)
    {
        if (_sortSteps.Count == 0)
            return items.OrderBy(defaultKey);

        IOrderedEnumerable<TItem>? ordered = null;

        for (int i = 0; i < _sortSteps.Count; i++)
        {
            var method = _sortSteps[i];

            Func<TItem, IComparable?> selector = it =>
                method.KeySelector((T)(object)it)!;

            if (ordered == null)
            {
                ordered = FirstDescending
                    ? items.OrderByDescending(selector)
                    : items.OrderBy(selector);
            }
            else
            {
                ordered = ordered.ThenBy(selector);
            }
        }

        return ordered!;
    }
}

public interface ISortMethod<T> : IComparable<ISortMethod<T>> where T : class
{
    /// <summary>
    ///     The name of this sorter step. (Used for comparison and identification)
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Icon used for display, if any.
    /// </summary>
    FAI Icon { get; }

    /// <summary>
    ///     For the CkGui.AttachTooltip
    /// </summary>
    string Tooltip { get; }

    /// <summary>
    ///     Depicts what part of <typeparamref name="T"/> is used for sorting.
    /// </summary>
    Func<T, IComparable?> KeySelector { get; }

    /// <summary>
    ///     Compare to other sorter steps by name.
    /// </summary>
    int IComparable<ISortMethod<T>>.CompareTo(ISortMethod<T>? other)
        => string.Compare(Name, other?.Name, StringComparison.Ordinal);
}