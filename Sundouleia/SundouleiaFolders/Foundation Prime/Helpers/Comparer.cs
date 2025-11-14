
using Dalamud.Interface;

namespace Sundouleia.DrawSystem;

/// <summary>
///     A comparer that compares two DynamicDrawSystem nodes by name.
/// </summary>
internal readonly struct NameComparer(IComparer<ReadOnlySpan<char>> baseComparer) : IComparer<IDynamicNode>
{
    /// <summary> The base comparer used to compare the strings. </summary>
    public IComparer<ReadOnlySpan<char>> BaseComparer
        => baseComparer;

    /// <inheritdoc/>
    public int Compare(IDynamicNode? x, IDynamicNode? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (y is null)
            return 1;
        if (x is null)
            return -1;

        return baseComparer.Compare(x.Name, y.Name);
    }
}

/// <summary>
///     The default comparer used when no other comparer is specified for the DDS.
/// </summary>
internal sealed class OrdinalSpanComparer : IComparer<ReadOnlySpan<char>>
{
    /// <inheritdoc/>
    public int Compare(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
        => x.CompareTo(y, StringComparison.OrdinalIgnoreCase);
}