using Sundouleia.DrawSystem.Selector;
using Sundouleia.PlayerClient;

namespace Sundouleia.DrawSystem;

// Cache for DDS's using RequestEntries.
public class RequestCache(DynamicDrawSystem<RequestEntry> parent) : DynamicFilterCache<RequestEntry>(parent)
{
    protected override bool IsVisible(IDynamicNode<RequestEntry> node)
    {
        if (Filter.Length is 0)
            return true;

        if (node is DynamicLeaf<RequestEntry> leaf)
            return leaf.Data.FromClient
                ? leaf.Data.RecipientAnonName.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                : leaf.Data.SenderAnonName.Contains(Filter, StringComparison.OrdinalIgnoreCase);

        return base.IsVisible(node);
    }
}