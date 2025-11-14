using CkCommons.Widgets;
using Dalamud.Interface.Utility.Raii;
using System.Collections.ObjectModel;

namespace Sundouleia.DrawSystem.Selector;

// CACHE RESTRUCTURE NOTES:
// =========================
//
// - Reference Sundouleia's DynamicFolder UpdateItemsForFilter function.
//     - Inside this we process all items, obtaining the subset where check filter is valid.
// - See if we can find a way to re-use this logic within the drawer's cache.
// - Remove the flat list, if possible, it will make drawing a lot smoother.
//
// Thoughts:
// - Can we embed filtered state into DrawSystems folders? It is
//   a _drawSystem_, so is it that out of place?
//
// - Could create a lightweight, cached hierarchy, containing filtered items.
//
// - Currently, we grab the sorted list of all items in a folder, before even
//   filtering them. This leads to unnecessary allocations and processing.
//
// - Potentially re-route how filtering works, and how the hierarchy is built?



// Would recommend heavily referencing Luna's Cache structure for this if possible,
// but that is a heavy maybe, pulling structures outside the class is going to cause
// a world of pain.
public partial class DynamicDrawer<T> where T : class
{
    // Internal helper for state cache, migrate to other structure when possible.
    private struct EntityState
    {
        public DynamicDrawSystem<T>.IDynamicEntity Entity;
        public byte Depth;
    }

    // The flat list of all cached entities.
    private readonly List<EntityState> _cachedState;

    // Filtered result information.
    // Informs us how many are displayed, and if only a single leaf matches.
    private DynamicDrawSystem<T>.Leaf? _singleLeaf = null;
    private int _leafCount  = 0;

    // The filter search string.
    protected string FilterValue { get; private set; } = string.Empty;

    // If we need an update.
    private bool _filterDirty = true;

    /// <summary>
    ///     Manually flag the filter as dirty externally.
    /// </summary>
    public void SetFilterDirty()
        => _filterDirty = true;

    /// <summary>
    ///     Potentially rework this function, but effectively is what we can override 
    ///     to determine if a patch matches the filter.
    /// </summary>
    /// <returns> True if the item should be displayed, false otherwise. </returns>
    /// <remarks> By default, this is compared against the FullPath of the entity. </remarks>
    protected virtual bool IsVisible(DynamicDrawSystem<T>.IDynamicEntity path)
        => FilterValue.Length is 0 || path.FullPath.Contains(FilterValue);

    /// <summary>
    ///     Recursively iterate through <paramref name="entity"/>, constructing the <see cref="_cachedState"/>. <para />
    ///     Enters with the current depth and cached state index. <para />
    ///     
    ///     NOTE:
    ///     This is not very efficient to recursively iterate over a flat-list approach, which is the main thing Luna
    ///     Handles better, so maybe look into it later.
    /// </summary>
    /// <param name="entity"> The entity to apply filters to. </param>
    /// <param name="idx"> The current index in the displayed state cache. </param>
    /// <param name="currentDepth"> The current depth in the hierarchy. </param>
    /// <returns> If ANY item inside of the the 
    private bool ApplyFiltersAddInternal(DynamicDrawSystem<T>.IDynamicEntity entity, ref int idx, byte currentDepth)
    {
        bool visible = IsVisible(entity);
        // Append the entity to the state if it is visible.
        // We append it here so that if we recursively iterate
        // through children they are inserted at the right position.
        _cachedState.Insert(idx, new EntityState()
        {
            Depth  = currentDepth,
            Entity = entity,
        });

        // Recursively apply the filters throughout all the entities children.
        if (entity is DynamicDrawSystem<T>.FolderCollection fc)
        {
            // If opened, append the visible state of all children too.
            if (fc.IsOpen)
            {
                foreach (DynamicDrawSystem<T>.IDynamicFolder folder in fc.GetChildren())
                {
                    ++idx;
                    visible |= ApplyFiltersAddInternal(folder, ref idx, (byte)(currentDepth + 1));
                }
            }
            else
                visible |= ApplyFiltersScanInternal(entity);
        }
        // If a folder, recursively iterate through its leaves.
        else if (entity is DynamicDrawSystem<T>.Folder f)
        {
            // If the folder is opened, grab the leaves of the folder too.
            if (f.IsOpen)
            {
                foreach (DynamicDrawSystem<T>.IDynamicLeaf leaf in f.GetChildren())
                {
                    ++idx;
                    visible |= ApplyFiltersAddInternal(leaf, ref idx, (byte)(currentDepth + 1));
                }
            }
            else
                visible |= ApplyFiltersScanInternal(entity);
        }
        else if (entity is DynamicDrawSystem<T>.Leaf l)
        {
            l.Children

            // Leaf, nothing to do here.
        }
        // Otherwise, if a leaf, and it is visible, and no other leaves are found, mark it.
        else if (visible && _leafCount++ == 0)
        {
            _singleLeaf = entity as DynamicDrawSystem<T>.Leaf;
        }

        // Remove completely invisible folders unless they explicitly show empty ones.
        // (Maybe want to revisit this for ShowIfEmpty folders...)
        if (!visible)
            _cachedState.RemoveAt(idx--);

        // Return if anything inside was visible.
        return visible;
    }

    /// <summary>
    ///     Helper function to scan a collapsed folder's children to see if
    ///     any of them match the current Filter.
    /// </summary>
    /// <returns> If any child entities matched the current filter. </returns>
    private bool ApplyFiltersScanInternal(DynamicDrawSystem<T>.IDynamicEntity path)
    {
        // Check if this entity itself matches visibility criteria.
        if (IsVisible(path))
        {
            if (path is DynamicDrawSystem<T>.Leaf l && _leafCount++ == 0)
                _singleLeaf = l;
            return true;
        }

        // Otherwise, recursively check its children for visibility.
        if (path is DynamicDrawSystem<T>.FolderCollection fc)
            return fc.GetChildren().Any(ApplyFiltersScanInternal);
        else if (path is DynamicDrawSystem<T>.Folder f)
            return f.GetChildren().Any(ApplyFiltersScanInternal);

        // No visibility found at this node or deeper.
        return false;
    }

    /// <summary>
    ///     Reconstructs the Cache whenever the filter is marked as dirty. <para />
    ///     This currently performs a full recalculation of the cache, and done recursively, which,
    ///     in many ways, can be improved upon in the future. But for now focus on getting things functional.
    /// </summary>
    private void ApplyFilters()
    {
        // If nothing changed in the filters, nothing to do.
        if (!_filterDirty)
            return;

        // Completely recalculate the flat list cache.
        _leafCount = 0;
        _cachedState.Clear();
        int idx = 0;

        // Iterate through all of root's children recursively.
        foreach (DynamicDrawSystem<T>.IDynamicFolder folder in DrawSystem.Root.GetChildren())
        {
            ApplyFiltersAddInternal(folder, ref idx, 0);
            ++idx;
        }

        // If there is only a single leaf after filtering, select it.
        if (_leafCount == 1 && _singleLeaf! != SelectedLeaf)
        {
            _filterDirty = ExpandAncestors(_singleLeaf!);
            Select(_singleLeaf!);
        }
        else
        {
            _filterDirty = false;
        }
    }
}
