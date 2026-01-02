using CkCommons.DrawSystem;
using CkCommons.DrawSystem.Selector;
using Sundouleia.Radar;

namespace Sundouleia.DrawSystem;

// Cache for DDS's using RadarUsers.
public class RadarCache(DynamicDrawSystem<RadarUser> parent) : DynamicFilterCache<RadarUser>(parent)
{
    protected override bool IsVisible(IDynamicNode<RadarUser> node)
    {
        if (Filter.Length is 0)
            return true;

        if (node is DynamicLeaf<RadarUser> leaf)
            return leaf.Data.MatchesFilter(Filter);

        return base.IsVisible(node);
    }
}