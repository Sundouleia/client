
using Dalamud.Interface;

namespace Sundouleia.DrawSystem;

/// <summary> A cheap struct to avoid unnecessary allocations for comparison with nodes. </summary>
/// <param name="comparer"> The comparer to use. </param>
/// <param name="name"> The name to compare. </param>
internal readonly ref struct SearchNode(NameComparer comparer, ReadOnlySpan<char> name) : IComparable<IDynamicNode>
{
    private readonly IComparer<ReadOnlySpan<char>> _comparer = comparer.BaseComparer;
    private readonly ReadOnlySpan<char> _name = name;

    /// <inheritdoc/>
    public int CompareTo(IDynamicNode? other)
    {
        if (other is null)
            return _comparer.Compare(_name, []);

        return _comparer.Compare(_name, other.Name);
    }
}