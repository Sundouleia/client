namespace Sundouleia.DrawSystem.Selector;

/// <summary>
///     A Filterable Cache for a <see cref="DynamicDrawSystem{T}"/>. <para />
///     Note that IsVisible can be overwritten by parent classes, allowing you to 
///     set your own filter overrides.
/// </summary>
/// <remarks> Considering allowing custom funcs in other constructors later maybe. </remarks>
public class DynamicFilterCache<T>(DynamicDrawSystem<T> parent) where T : class
{
    private bool    _cacheDirty = true;
    private string  _filter     = string.Empty;

    private List<IDynamicNode<T>>                               _flatNodeCache   = [];
    private Dictionary<IDynamicCollection<T>, IDynamicCache<T>> _cachedFolderMap = new();

    /// <summary>
    ///     The Filter string used to generate the DynamicCache. <para />
    ///     Automatically marks the cache as dirty when updated.
    /// </summary>
    /// <remarks> _cacheDirty doesn't update if Filter is set to the same as current. </remarks>
    public string Filter
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

    /// <summary>
    ///     The cached root node, with all sorting and filters applied. <para />
    ///     Used in most common shared draw functions that display in hierarchical form.
    /// </summary>
    /// <remarks> Use as public until we find a way to make a Read-Only version. </remarks>
    public DynamicFolderGroupCache<T> RootCache { get; private set; }

    /// <summary>
    ///     The flattened list of RootNodeCache. <para />
    ///     Used primarily for multi-selection, but not much elsewhere. <para />
    /// </summary>
    public IReadOnlyList<IDynamicNode<T>> FlatList => _flatNodeCache;

    /// <summary>
    ///     Maps all collections from the parent <see cref="DynamicDrawSystem{T}"/>'s Root Folder. <para />
    ///     Unlike RootNodeCache, this includes folders that were fully filtered out and have no children. <para />
    /// </summary>
    /// <remarks> Used when we are drawing individual folders, or a group of select folders that could have been filtered out. </remarks>
    public IReadOnlyDictionary<IDynamicCollection<T>, IDynamicCache<T>> CacheMap => _cachedFolderMap;

    /// <summary>
    ///     Generic call to mark the entire cache as dirty, requiring a full recalculation.
    /// </summary>
    public void MarkCacheDirty()
        => _cacheDirty = true;

    /// <summary>
    ///     Marks a single cached folder as dirty, requiring the cache update to
    ///     only recalculate one folder and its children over the entire cache.
    /// </summary>
    public void MarkFolderDirty(IDynamicCollection<T> folder)
    {
        // for now mark the whole thing.
        _cacheDirty = true;
    }

    /// <summary>
    ///     Updates the current cache if the filter is marked as dirty, 
    ///     or handles per-folder recalclations where nessisary. <para />
    ///     <b> Fairly safe to call every drawframe. Only calculates on dirty filter. </b>
    /// </summary>
    public void UpdateCache()
    {
        if (!_cacheDirty)
            return;

        // If we ever add per-folder dirty handling,
        // we can clear it here since we would be doing a full rebuild.

        // Recreate the entire root cache.
        RootCache = new DynamicFolderGroupCache<T>(parent.Root);
        // Recreate the map.
        _cachedFolderMap.Clear();
        // Recursively build the cached folder from the root.
        BuildCachedFolder(RootCache);
        // Rebuild the flat cache.
        _flatNodeCache = [ RootCache.Folder, ..RootCache.GetChildren() ];

        // Maybe something here to ensure correct selections idk anymore lol.
        // Add as time goes on.

        // Mark cache as no longer dirty.
        _cacheDirty = false;
    }

    /// <summary>
    ///     Recursively constructs a <paramref name="cachedNode"/>, filtering
    ///     out non-visible nodes, sorting remaining, and updating the map.
    /// </summary>
    private bool BuildCachedFolder(IDynamicCache<T> cachedNode)
    {
        // Assume visibility fails until proven otherwise.
        bool visible = false;

        // Add or Update to the map regardless of if it has children or not.
        _cachedFolderMap[cachedNode.Folder] = cachedNode;

        // If the cached group is a FolderCollection
        if (cachedNode is DynamicFolderGroupCache<T> fc)
        {
            // Firstly, get if the folder itself is visible.
            visible |= IsVisible(fc.Folder);
            // If the folder is opened, recursively process all children.
            if (fc.Folder.IsOpen)
            {
                // Pre-sort the children until we find a better solution for this.
                // Computation is minimal performance impact due to it only updating on dirty filters.
                // Assume we are going to be appending child nodes.
                var childNodes = new List<IDynamicCache<T>>();
                // Iterate through each one, recursively building the caches.
                foreach (var child in fc.Folder.Children)
                {
                    IDynamicCache<T> innerCache = child switch
                    {
                        IDynamicFolderGroup<T> c => new DynamicFolderGroupCache<T>(c),
                        IDynamicFolder<T> f => new DynamicFolderCache<T>(f),
                        _ => throw new InvalidOperationException("UNK CachedNodeType"),
                    };
                    // Build another CachedFolderNode for the child.
                    if (BuildCachedFolder(innerCache))
                    {
                        visible = true;
                        childNodes.Add(innerCache);
                    }
                }

                // Sorts the remaining child nodes by their folder's, and then selects the nodes for output.
                fc.Children = fc.Folder.Sorter
                    .SortItems(childNodes.Select(c => c.Folder))
                    .Select(sorted => childNodes.First(c => c.Folder == sorted)).ToList();
            }
            // Otherwise, if closed, scan to see if any sub-nodes are visible.
            else
            {
                // Scan to see if any sub-nodes are visible.
                visible |= IsCollapsedNodeVisible(fc.Folder);
            }
        }
        // If the cached group is a Folder
        else if (cachedNode is DynamicFolderCache<T> folder)
        {
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

    /// <summary>
    ///     Scan for any descendants of a collapsed folder to see if any match. <para />
    ///     Check all sub-folders recursively, if ANY match, the nodes parent should be visible.
    /// </summary>
    protected bool IsCollapsedNodeVisible(IDynamicNode<T> node)
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
    ///     Used when obtaining the filtered results of an IDynamicNode. <para />
    ///     If you desire custom filter logic, override this in a parent class.
    /// </summary>
    protected virtual bool IsVisible(IDynamicNode<T> node)
        => Filter.Length is 0 || node.FullPath.Contains(Filter, StringComparison.OrdinalIgnoreCase);
}