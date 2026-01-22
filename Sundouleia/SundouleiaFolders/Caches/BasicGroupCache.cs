using CkCommons.DrawSystem;
using CkCommons.DrawSystem.Selector;
using Sundouleia.Pairs;

namespace Sundouleia.DrawSystem;

// Cache for the basic Groups view that constructs the dynamic caches in a manner where
// all FolderGroups are removed entirely, except for Root.
public class BasicGroupCache(DynamicDrawSystem<Sundesmo> dds) : WhitelistCache(dds)
{
    /// <summary>
    ///     Because everything is flattened for BasicGroups, 
    ///     any reloads requesting parent should reload ROOT.
    /// </summary>
    public override void MarkForReload(IDynamicCollection<Sundesmo> folder, bool reloadParent = false)
    {
        if (reloadParent)
            MarkCacheDirty();
        else if (cachedFolderMap.TryGetValue(folder, out var cachedNode))
            toReload.Add(cachedNode);
    }

    /// <summary>
    ///     Recursively constructs a <paramref name="cachedNode"/>, filtering
    ///     out non-visible nodes, sorting remaining, and updating the map.
    /// </summary>
    protected override bool BuildDynamicCache(IDynamicCache<Sundesmo> cachedNode)
    {
        // Assume visibility fails until proven otherwise.
        bool visible = false;

        // Add or Update to the map regardless of if it has children or not.
        cachedFolderMap[cachedNode.Folder] = cachedNode;

        // If the cached group is a FolderCollection
        if (cachedNode is DynamicFolderGroupCache<Sundesmo> fc)
        {
            // Firstly, get if the folder itself is visible.
            visible |= IsVisible(fc.Folder);
            // Recursively process all children regardless of open state.
            
            // Computation is minimal performance impact due to it only updating on dirty filters.
            // Assume we are going to be appending child nodes.
            var childNodes = new List<IDynamicCache<Sundesmo>>();
            // Iterate through each one, recursively building the caches.
            foreach (var child in fc.Folder.Children)
            {
                if (child is IDynamicFolderGroup<Sundesmo> c)
                {
                    var innerCacheFG = new DynamicFolderGroupCache<Sundesmo>(c);
                    if (BuildDynamicCache(innerCacheFG))
                    {
                        visible = true;
                        childNodes.AddRange(innerCacheFG.Children.OfType<DynamicFolderCache<Sundesmo>>());
                    }
                }
                else if (child is IDynamicFolder<Sundesmo> f)
                {
                    var innerCacheF = new DynamicFolderCache<Sundesmo>(f);
                    if (BuildDynamicCache(innerCacheF))
                    {
                        visible = true;
                        childNodes.Add(innerCacheF);
                    }
                }
            }
            // Sorts the remaining child nodes by their folder's, and then selects the nodes for output.
            fc.Children = fc.Folder.Sorter
                .SortItems(childNodes.Select(c => c.Folder))
                .Select(sorted => childNodes.First(c => c.Folder == sorted)).ToList();
        }
        // If the cached group is a Folder
        else if (cachedNode is DynamicFolderCache<Sundesmo> folder)
        {
            if (folder.Folder is GroupFolder gf && !gf.Group.InBasicView)
                return false;

            // Firstly, get if the folder itself is visible.
            visible |= IsVisible(folder.Folder);
            // If opened, recursively process all children.
            if (folder.Folder.IsOpen)
            {
                // Update the cached folder's children.
                folder.Children = folder.Folder.Sorter.SortItems(folder.Folder.Children.Where(IsVisible)).ToList();
                // If any children were added, the folder is visible.
                visible |= folder.Children.Any();
            }
            // Otherwise, if closed, scan to check for any possible visible leaves, setting them as single leaves if found.
            else
                visible |= IsCollapsedNodeVisible(folder.Folder);
        }
        // If after all of this, return if this node should be visible or not. If it shouldnt, we shouldnt append it.
        return visible;
    }

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