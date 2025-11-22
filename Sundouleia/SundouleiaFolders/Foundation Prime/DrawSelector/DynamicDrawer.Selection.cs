using Dalamud.Bindings.ImGui;
using OtterGui.Extensions;

namespace Sundouleia.DrawSystem.Selector;

// Focuses on Selections.
public partial class DynamicDrawer<T>
{
    // Helps prevent flooding SundouleiaMediator while only having parent classes listen on changes.
    // Alternatively we could make abstract after splitting from partial and having the parent handle it.
    // But that's a future thing.
    public delegate void SelectionChangeDelegate(T? oldSelection, T? newSelection);
    public event SelectionChangeDelegate SelectionChanged;

    // Quick-Lookup HashSet of all selected entities regardless of type.
    protected readonly HashSet<IDynamicNode<T>> _selected = [];
    
    // Filtered Selection Caches (can remove later if not needed)
    protected readonly List<DynamicFolderGroup<T>> _selectedFolderGroups = [];
    
    protected readonly List<DynamicFolder<T>> _selectedFolders = [];
    
    protected readonly List<IDynamicCollection<T>> _selectedFoldersAll = [];
    
    protected readonly List<DynamicLeaf<T>> _selectedLeaves = [];

    // Track history of latest selection to know how to perform CTRL+SHIFT Multi-Selection Jumps.
    protected IDynamicNode<T>? _lastSelected;
    protected IDynamicNode<T>? _lastAnchor;

    // Public Accessor for selection.
    public IReadOnlySet<IDynamicNode<T>> SelectedEntities
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
    protected void SelectItem(IDynamicNode<T> entity, bool canAnchorSelect, bool canRangeSelect)
    {
        bool ctrl = ImGui.GetIO().KeyCtrl;
        bool shift = ImGui.GetIO().KeyShift;

        // SHIFT Range select / deselect.
        //  - LastAnchor must be valid.
        //  - LastAnchor cannot be the same as the current entity.
        if (shift && _lastAnchor != null && canRangeSelect && _lastAnchor != entity)
        { 
            var idxTo = _nodeCacheFlat.IndexOf(entity);

            // obtain the idx of the last anchor.
            var idxFrom = _nodeCacheFlat.IndexOf(_lastAnchor);

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
                    SelectInternal(_nodeCacheFlat[i]);
                else
                    DeselectInternal(_nodeCacheFlat[i]);
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
                SelectInternal(entity);
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
                SelectInternal(entity);
                _lastAnchor = entity;
                _lastSelected = entity;
            }
        }
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

    private void SelectInternal(IDynamicNode<T> entity)
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

    private void SelectInternal(IEnumerable<IDynamicNode<T>> entities)
    {
        foreach (var entity in entities)
            SelectInternal(entity);
    }
}
