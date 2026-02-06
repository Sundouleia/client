using CkCommons.DrawSystem;
using CkCommons.DrawSystem.Selector;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;

namespace Sundouleia.DrawSystem;

// Cache for DDS's using RadarUsers.
public class RadarCache(DynamicDrawSystem<RadarUser> parent) : DynamicFilterCache<RadarUser>(parent)
{
    /// <summary>
    ///     If the config options under the filter bar should show.
    /// </summary>
    public bool FilterConfigOpen = false;

    /// <summary>
    ///     The leaf in the drafter.
    /// </summary>
    public IDynamicNode? NodeInDrafter;

    // Override for matching the search filter.
    protected override bool IsVisible(IDynamicNode<RadarUser> node)
    {
        if (Filter.Length is 0)
            return true;

        if (node is DynamicLeaf<RadarUser> leaf)
            return leaf.Data.MatchesFilter(Filter);

        return base.IsVisible(node);
    }
}