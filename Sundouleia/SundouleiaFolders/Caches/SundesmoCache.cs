using CkCommons.DrawSystem;
using CkCommons.DrawSystem.Selector;
using Sundouleia.Pairs;

namespace Sundouleia.DrawSystem;

// Cache for DDS's using Sundesmo items.
public class SundesmoCache(DynamicDrawSystem<Sundesmo> parent) : DynamicFilterCache<Sundesmo>(parent)
{
    /// <summary>
    ///     If the config options under the filter bar should show.
    /// </summary>
    public bool FilterConfigOpen = false;

    /// <summary>
    ///     The Nodes that display MonoFont UIDs instead of DisplayName.
    /// </summary>
    public HashSet<IDynamicNode<Sundesmo>> ShowingUID = new();

    /// <summary>
    ///     The node currently being renamed, if any.
    /// </summary>
    public IDynamicNode<Sundesmo>? RenamingNode = null;

    /// <summary>
    ///     Temp nick text.
    /// </summary>
    public string NameEditStr = string.Empty;

    protected override bool IsVisible(IDynamicNode<Sundesmo> node)
    {
        if (Filter.Length is 0)
            return true;

        if (node is DynamicLeaf<Sundesmo> leaf)
            return leaf.Data.UserData.AliasOrUID.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || (leaf.Data.GetNickname()?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false)
                || (leaf.Data.PlayerName?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false);

        return base.IsVisible(node);
    }
}