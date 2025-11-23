namespace Sundouleia.DrawSystem;

// Read-Only access to the dynamic sorter, without mutable methods.
public interface IReadOnlyDynamicSorter<TItem> : IReadOnlyList<ISortMethod<TItem>> where TItem : class
{
    /// <summary>
    ///     If the first step is in descending order.
    /// </summary>
    public bool FirstDescending { get; }

    /// <summary>
    ///     The steps of the dynamic sorter.
    /// </summary>
    IReadOnlyList<ISortMethod<TItem>> Steps { get; }

    /// <summary>
    ///     Sorts all passed in items with the sorter's steps.
    ///     Items must match the defined type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="items"> The items to sort. </param>
    /// <param name="fallback"> The fallback to use. </param>
    IEnumerable<TItem> SortItems(IEnumerable<TItem> items);
}

public interface IDynamicSorter<TItem> where TItem : class
{
    /// <summary>
    ///     Adds a sort step to the sorter. Duplicate methods are voided.
    /// </summary>
    void Add(ISortMethod<TItem> step);

    /// <summary>
    ///     Appends a series of steps to the sorter.
    /// </summary>
    void AddRange(IEnumerable<ISortMethod<TItem>> steps);

    /// <summary>
    ///     Assigns a series of steps to the sorter, replacing any existing ones.
    /// </summary>
    void SetSteps(IEnumerable<ISortMethod<TItem>> steps);

    /// <summary>
    ///     Removes a sort step.
    /// </summary>
    void Remove(ISortMethod<TItem> step);
    
    /// <summary>
    ///     Clears all sort steps.
    /// </summary>
    void Clear();

    /// <summary>
    ///     Reorders a sort step from oldIndex to newIndex.
    /// </summary>
    void Move(int oldIndex, int newIndex);
}


public class DynamicSorter<T> : IDynamicSorter<T>, IReadOnlyDynamicSorter<T> where T : class
{
    private readonly List<ISortMethod<T>> _sortSteps = [];

    /// <summary>
    ///     Constructor for optional paramaters, defaulting to nothing being assigned. <para />
    /// </summary>
    public DynamicSorter(IEnumerable<ISortMethod<T>>? steps = null)
    {
        if (steps is not null)
            _sortSteps.AddRange(steps);
    }
    // Satisfy ReadOnly
    public bool FirstDescending { get; set; } = false;
    public int Count => _sortSteps.Count;
    public ISortMethod<T> this[int index] => _sortSteps[index];
    public IReadOnlyList<ISortMethod<T>> Steps => _sortSteps;

    public IEnumerator<ISortMethod<T>> GetEnumerator()
        => _sortSteps.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()
        => _sortSteps.GetEnumerator();

    // Satify IDynamicSorter
    public void Add(ISortMethod<T> step)
    {
        if (!_sortSteps.Contains(step))
            _sortSteps.Add(step);
    }

    public void AddRange(IEnumerable<ISortMethod<T>> steps)
    {
        foreach (var step in steps)
            Add(step);
    }

    public void SetSteps(IEnumerable<ISortMethod<T>> steps)
    {
        _sortSteps.Clear();
        _sortSteps.AddRange(steps);
    }

    public void Remove(ISortMethod<T> sortMethod)
        => _sortSteps.Remove(sortMethod);

    public void Clear() => _sortSteps.Clear();

    public void Move(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= _sortSteps.Count) return;
        if (newIndex < 0 || newIndex >= _sortSteps.Count) return;

        var m = _sortSteps[oldIndex];
        _sortSteps.RemoveAt(oldIndex);
        _sortSteps.Insert(newIndex, m);
    }

    public IEnumerable<T> SortItems(IEnumerable<T> items)
    {
        if (_sortSteps.Count is 0)
            return items;

        IOrderedEnumerable<T>? ordered = null;

        for (int i = 0; i < _sortSteps.Count; i++)
        {
            var key = _sortSteps[i].KeySelector;

            if (ordered == null)
                ordered = FirstDescending ? items.OrderByDescending(key) : items.OrderBy(key);
            else
                ordered = ordered.ThenBy(key);
        }

        return ordered ?? items;
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