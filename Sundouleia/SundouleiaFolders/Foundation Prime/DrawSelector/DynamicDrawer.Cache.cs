using System.Diagnostics.CodeAnalysis;

namespace Sundouleia.DrawSystem.Selector;

public partial class DynamicDrawer<T>
{
    // As much as you want to split these into seperate classes for a filter and cache,
    // keep them together for now to save your sanity and get something thatis functional first.
    private bool _cacheDirty = true;
    private string _filter = string.Empty;
    protected string Filter
    {
        get => _filter;
        set
        {
            if (_filter != value)
            {
                _filter = value;
                _cacheDirty = true;
            }
        }
    }

    // Initially assume an empty root cache.
    private CachedFolderGroup<T>      _nodeCache;
    private IReadOnlyList<IDynamicNode<T>> _nodeCacheFlat;

    public void MarkCacheDirty()
        => _cacheDirty = true;

    /// <summary>
    ///     Reconstructs the Cache whenever the filter is marked as dirty. <para />
    ///     This currently performs a full recalculation of the cache, and done recursively, which,
    ///     in many ways, can be improved upon in the future. But for now focus on getting things functional.
    /// </summary>
    private void ApplyFilters()
    {
        // If nothing changed in the filters, nothing to do.
        if (!_cacheDirty)
            return;

        // Recreate the entire root cache.
        _nodeCache = new CachedFolderGroup<T>(DrawSystem.Root);
        BuildCachedFolder(_nodeCache);

        // Rebuild the flat cache from the root cache for selection help.
        _nodeCacheFlat = [ .._nodeCache.GetChildren() ];

        // Used to be something here about single selection leaves, but we can avoid this for now,
        // as we do not need to auto-select single actors or anything. Additionally we should be able to 
        // only need to recalculate caches for individual groups now too.
        
        // Mark cache as no longer dirty.
        _cacheDirty = false;
    }

    // 'Recursively' builds the cached folder collection for the root.
    // Returns false if the folder, and all sub-nodes, are not visible.
    private bool BuildCachedFolder(ICachedFolderNode<T> cachedGroup)
    {
        // Assume visibility fails until proven otherwise.
        bool visible = false;

        // If the cached group is a FolderCollection
        if (cachedGroup is CachedFolderGroup<T> fc)
        {
            // Firstly, get if the folder itself is visible.
            visible |= IsVisible(fc.Folder);

            // If the folder is opened, recursively process all children.
            if (fc.Folder.IsOpen)
            {
                // Assume we are going to be appending child nodes.
                var childNodes = new List<ICachedFolderNode<T>>();
                // Iterate through each one, recursively building the caches.
                foreach (var child in fc.Folder.Children)
                {
                    ICachedFolderNode<T> innerCache = child switch
                    {
                        IDynamicFolderGroup<T> c => new CachedFolderGroup<T>(c),
                        IDynamicFolder<T> f => new CachedFolder<T>(f),
                        _ => throw new InvalidOperationException("UNK CachedNodeType"),
                    };
                    // Build another CachedFolderNode for the child.
                    if (BuildCachedFolder(innerCache))
                    {
                        visible = true;
                        childNodes.Add(innerCache);
                    }
                }
                // Once processed, apply the sorter & update the cached children.
                fc.Children = [.. ApplySorter(childNodes, fc.Folder.Sorter)];
            }
            // Otherwise, if closed, scan to see if any sub-nodes are visible.
            else
            {
                // Scan to see if any sub-nodes are visible.
                visible |= IsCollapsedNodeVisible(fc.Folder);
            }
        }
        // If the cached group is a Folder
        else if (cachedGroup is CachedFolder<T> folder)
        {
            // Firstly, get if the folder itself is visible.
            visible |= IsVisible(folder.Folder);
            // If opened, recursively process all children.
            if (folder.Folder.IsOpen)
            {
                // Update the cached folder's children.
                folder.Children = [..ApplySorter(folder.Folder.Children.Where(IsVisible), folder.Folder.Sorter)];
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

    // Scan for any descendants of a collapsed folder to see if any match the filter.
    // Check all sub-folders recursively, if ANY match, the nodes parent should be visible.
    public bool IsCollapsedNodeVisible(IDynamicNode<T> node)
    {
        // If the folder itself is visible, display it.
        // Check if this entity itself matches visibility criteria.
        if (IsVisible(node))
            return true;

        // Otherwise, return true if ANY of the children inside of the closed folder are visible,
        if (node is DynamicFolderGroup<T> fc)
            return fc.Children.Any(IsCollapsedNodeVisible);
        else if (node is DynamicFolder<T> f)
            return f.Children.Any(IsCollapsedNodeVisible);

        // No visibility found at this node or deeper.
        return false;
    }

    /// <summary>
    ///     Used when obtaining the filtered results of an IDynamicNode
    /// </summary>
    protected virtual bool IsVisible(IDynamicNode<T> node)
        => Filter.Length is 0 || node.FullPath.Contains(Filter);


    // This could be abstracted into a generic type function but unless things get very messy just stick with these 3 for now.
    // Sorter logic for folder groups.
    protected IEnumerable<IDynamicLeaf<T>> ApplySorter(IEnumerable<IDynamicLeaf<T>> leaves, IReadOnlyList<ISortMethod<T>> sorter)
    {
        if (sorter.Count is 0)
            return leaves.OrderBy(l => l.Name); // default alphabetical

        IOrderedEnumerable<IDynamicLeaf<T>>? ordered = null;
        foreach (var step in sorter)
        {
            if (ordered is null)
                ordered = leaves.OrderBy(c => step.KeySelector(c.Data));
            else
                ordered = ordered.ThenBy(c => step.KeySelector(c.Data));
        }
        // The ?? case is a safety net for a wild edge case where a sort method exists but isn't processed?
        return ordered ?? leaves.OrderBy(l => l.Name);
    }

    // Sorter logic for cached folders
    protected IEnumerable<IDynamicCollection<T>> ApplySorter(IEnumerable<IDynamicCollection<T>> groups, IReadOnlyList<ISortMethod<IDynamicCollection<T>>> sorter)
    {
        if (sorter.Count is 0)
            return groups.OrderBy(g => g.Name); // default alphabetical

        IOrderedEnumerable<IDynamicCollection<T>>? ordered = null;
        foreach (var step in sorter)
        {
            if (ordered is null)
                ordered = groups.OrderBy(c => step.KeySelector(c));
            else
                ordered = ordered.ThenBy(c => step.KeySelector(c));
        }
        // The ?? case is a safety net for a wild edge case where a sort method exists but isn't processed?
        return ordered ?? groups.OrderBy(g => g.Name);
    }

    // Sorter logic for cached folders
    protected IEnumerable<ICachedFolderNode<T>> ApplySorter(IEnumerable<ICachedFolderNode<T>> groups, IReadOnlyList<ISortMethod<IDynamicCollection<T>>> sorter)
    {
        if (sorter.Count is 0)
            return groups.OrderBy(g => g.Folder.Name); // default alphabetical

        IOrderedEnumerable<ICachedFolderNode<T>>? ordered = null;
        foreach (var step in sorter)
        {
            if (ordered is null)
                ordered = groups.OrderBy(c => step.KeySelector(c.Folder));
            else
                ordered = ordered.ThenBy(c => step.KeySelector(c.Folder));
        }
        // The ?? case is a safety net for a wild edge case where a sort method exists but isn't processed?
        return ordered ?? groups.OrderBy(g => g.Folder.Name);
    }
}