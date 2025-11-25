using Dalamud.Bindings.ImGui;
using OtterGui.Extensions;

namespace Sundouleia.DrawSystem.Selector;

/// <summary>
///     A Manager for all selections in a <see cref="DynamicDrawer{T}"/>. <para />
///     Pulls info from <see cref="DynamicFilterCache{T}"/> and <see cref="DynamicDrawSystem{T}"/>
/// </summary>
public class DynamicSelections<T> : IDisposable where T : class
{
    private readonly DynamicDrawSystem<T> _parent;
    private readonly DynamicFilterCache<T> _cache;

    // Quick-Lookup HashSet of all selected entities regardless of type.
    private HashSet<IDynamicNode<T>> _selected = [];
    
    // Filtered Selection Caches (can remove later if not needed)
    private List<DynamicFolderGroup<T>> _selectedFolderGroups = [];
    private List<DynamicFolder<T>>      _selectedFolders      = [];
    private List<IDynamicCollection<T>> _selectedFoldersAll   = [];
    private List<DynamicLeaf<T>>        _selectedLeaves       = [];

    // Track history of latest selection to know how to perform CTRL+SHIFT Multi-Selection Jumps.
    protected IDynamicNode<T>? _lastSelected;
    protected IDynamicNode<T>? _lastAnchor;

    public DynamicSelections(DynamicDrawSystem<T> parent, DynamicFilterCache<T> cache)
    {
        _parent = parent;
        _cache = cache;

        // Monitor the changes from the parents events.
        _parent.DDSChanged += OnDrawSystemChange;
        _parent.CollectionUpdated += OnCollectionChange;
    }

    public void Dispose()
    {
        _parent.DDSChanged -= OnDrawSystemChange;
        _parent.CollectionUpdated -= OnCollectionChange;
    }

    // Public Accessor for selection.
    public IReadOnlyList<DynamicLeaf<T>> SelectedLeaves
        => _selectedLeaves;
    public IReadOnlySet<IDynamicNode<T>> Selected
        => _selected;
    
    public DynamicLeaf<T>? SelectedLeaf
        => _selectedLeaves.Count is 1 ? _selectedLeaves[0] : null;

    // Obtain if a particular node is selected.
    public bool IsSelected(IDynamicNode<T> entity)
        => _selected.Contains(entity);

    /// <summary>
    ///     Perform a full clear of all selected items in the DDS.
    /// </summary>
    public void ClearSelected()
    {
        _selected.Clear();
        _selectedFolderGroups.Clear();
        _selectedFolders.Clear();
        _selectedFoldersAll.Clear();
        _selectedLeaves.Clear();
        _lastSelected = null;
        _lastAnchor = null;
    }

    // Deselect the entity, and remove it from all tracked selection lists.
    public void Deselect(IDynamicNode<T> entity)
        => DeselectInternal(entity);

    /// <summary>
    ///     Selects an entity in the DDS. <para /> 
    ///     Supports CTRL / SHIFT multi-selection behavior and updates accordingly. <para />
    ///     <b>NOTICE:</b>
    ///     This is very much pulled directly from Sundouleia's prototype model. It could be very prone to errors.
    /// </summary>
    /// <param name="entity"> The entity being selected. </param>
    /// <param name="canAnchorSelect"> If we allow CTRL based multi-selection. </param>
    /// <param name="canRangeSelect"> If we allow SHIFT based range selection. </param>
    public void SelectItem(IDynamicNode<T> entity, bool canAnchorSelect, bool canRangeSelect)
    {
        bool ctrl = ImGui.GetIO().KeyCtrl;
        bool shift = ImGui.GetIO().KeyShift;

        // SHIFT Range select / deselect.
        //  - LastAnchor must be valid.
        //  - LastAnchor cannot be the same as the current entity.
        if (shift && _lastAnchor != null && canRangeSelect && _lastAnchor != entity)
        { 
            var idxTo = _cache.FlatList.IndexOf(entity);

            // obtain the idx of the last anchor.
            var idxFrom = _cache.FlatList.IndexOf(_lastAnchor);

            // Ensure correct selection order (top to bottom / bottom to top)
            (idxFrom, idxTo) = idxFrom > idxTo ? (idxTo, idxFrom) : (idxFrom, idxTo);

            // Determine if bulk selecting or deselecting.
            // NOTICE: This causes behavior where outcome depends on what's shift selected and not anchored
            // when performing a shift select multiple times back to back.
            // It's nothing to worry about, and can be easily changed from entity to _lastAnchor if more prefer that.
            bool selecting = !_selected.Contains(entity);

            // Perform bulk select/deselect.
            for (int i = idxFrom; i <= idxTo; i++)
            {
                if (selecting)
                    SelectSingle(_cache.FlatList[i]);
                else
                    DeselectInternal(_cache.FlatList[i]);
            }
            // Update last interacted.
            _lastSelected = selecting ? entity : null;
        }
        // Single-Selection toggle
        else if (ctrl && canAnchorSelect)
        {
            var wasSelected = _selected.Contains(entity);
            if (wasSelected)
                DeselectInternal(entity);
            else
                SelectSingle(entity);
            // Update interactions.
            _lastAnchor = entity;
            _lastSelected = wasSelected ? null : entity;
        }
        // Normal interaction should immediately clear all other selections.
        else
        {
            // we can single select if nothing else is selected.
            if (_selected.Count is 1 && _lastSelected == entity)
            {
                ClearSelected();
                _lastAnchor = null;
                _lastSelected = null;
            }
            else
            {
                ClearSelected();
                SelectSingle(entity);
                _lastAnchor = entity;
                _lastSelected = entity;
            }
        }
    }

    public void SelectSingle(IDynamicNode<T> entity)
    {
        _selected.Add(entity);
        if (entity is DynamicFolderGroup<T> fc)
        {
            _selectedFolderGroups.Add(fc);
            _selectedFoldersAll.Add(fc);
        }
        else if (entity is DynamicFolder<T> f)
        {
            _selectedFolders.Add(f);
            _selectedFoldersAll.Add(f);
        }
        else if (entity is DynamicLeaf<T> l)
        {
            _selectedLeaves.Add(l);
        }
    }

    public void SelectMultiple(IEnumerable<IDynamicNode<T>> entities)
    {
        foreach (var entity in entities)
            SelectSingle(entity);
    }

    private void DeselectInternal(IDynamicNode<T> entity)
    {
        _selected.Remove(entity);
        if (entity is DynamicFolderGroup<T> fc)
        {
            _selectedFolderGroups.Remove(fc);
            _selectedFoldersAll.Remove(fc);
        }
        else if (entity is DynamicFolder<T> f)
        {
            _selectedFolders.Remove(f);
            _selectedFoldersAll.Remove(f);
        }
        else if (entity is DynamicLeaf<T> l)
        {
            _selectedLeaves.Remove(l);
        }
    }

    private void DeselectInternal(IEnumerable<IDynamicNode<T>> entities)
    {
        _selected.RemoveWhere(e => entities.Contains(e));
        _selectedFolderGroups.RemoveAll(fc => entities.Contains(fc));
        _selectedFolders.RemoveAll(f => entities.Contains(f));
        _selectedFoldersAll.RemoveAll(f => entities.Contains(f));
        _selectedLeaves.RemoveAll(l => entities.Contains(l));
    }

    /// <summary>
    ///     For ensuring that selections are cached between 
    ///     reloads to restore selections.
    /// </summary>
    private void OnDrawSystemChange(DDSChange type, IDynamicNode<T> _, IDynamicCollection<T>? __, IDynamicCollection<T>? ___)
    {
        switch (type)
        {
            case DDSChange.FullReloadStarting:
                // Do stuff.
                break;

            case DDSChange.FullReloadFinished:
                // Do other stuff.
                break;
        }
    }

    /// <summary>
    ///     FolderUpdated events can output removed paths, useful for deslecting nodes no longer present. <para />
    /// </summary>
    private void OnCollectionChange(CollectionUpdate kind, IDynamicCollection<T> collection, IEnumerable<DynamicLeaf<T>>? removed)
    {
        if (kind is CollectionUpdate.FolderUpdated)
            DeselectInternal(removed ?? []);
    }
}
