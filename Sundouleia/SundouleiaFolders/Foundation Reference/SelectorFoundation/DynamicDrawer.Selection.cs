using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using OtterGui.Extensions;
using Sundouleia.Gui.Components;
using TerraFX.Interop.Windows;
using static FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentHousingPlant;

namespace Sundouleia.DrawSystem.Selector;

// Focuses on Selections.
public partial class DynamicDrawer<T> where T : class
{
    // Helps prevent flooding SundouleiaMediator while only having parent classes listen on changes.
    // Alternatively we could make abstract after splitting from partial and having the parent handle it.
    // But that's a future thing.
    public delegate void SelectionChangeDelegate(T? oldSelection, T? newSelection);
    public event SelectionChangeDelegate SelectionChanged;

    // Quick-Lookup HashSet of all selected entities regardless of type.
    protected readonly HashSet<DynamicDrawSystem<T>.IDynamicEntity> _selected = [];
    
    // Filtered Selection Caches (can remove later if not needed)
    protected readonly List<DynamicDrawSystem<T>.FolderCollection> _selectedFolderGroups = [];
    
    protected readonly List<DynamicDrawSystem<T>.Folder> _selectedFolders = [];
    
    protected readonly List<DynamicDrawSystem<T>.IDynamicFolder> _selectedFoldersAll = [];
    
    protected readonly List<DynamicDrawSystem<T>.Leaf> _selectedLeaves = [];

    // Track history of latest selection to know how to perform CTRL+SHIFT Multi-Selection Jumps.
    protected DynamicDrawSystem<T>.IDynamicEntity? _lastSelected;
    protected DynamicDrawSystem<T>.IDynamicEntity? _lastAnchor;

    // The Entity to jump focus to in next draw call (could potentially remove this, dunno)
    private DynamicDrawSystem<T>.Leaf? _jumpToSelection = null;

    // Public Accessor for selection.
    public IReadOnlySet<DynamicDrawSystem<T>.IDynamicEntity> SelectedEntities
        => _selected;
    
    public DynamicDrawSystem<T>.Leaf? SelectedLeaf
        => _selectedLeaves.Count is 1 ? _selectedLeaves[0] : null;

    // Obtain if a particular node is selected.
    public bool IsSelected(DynamicDrawSystem<T>.IDynamicEntity entity)
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
    public void Deselect(DynamicDrawSystem<T>.IDynamicEntity entity)
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
    private void SelectItem(DynamicDrawSystem<T>.IDynamicEntity entity, bool canAnchorSelect, bool canRangeSelect)
    {
        bool ctrl = ImGui.GetIO().KeyCtrl;
        bool shift = ImGui.GetIO().KeyShift;

        // SHIFT Range select / deselect.
        //  - LastAnchor must be valid.
        //  - LastAnchor cannot be the same as the current entity.
        if (shift && _lastAnchor != null && canRangeSelect && _lastAnchor != entity)
        { 
            var idxTo = _cachedState.IndexOf(s => s.Entity == entity);
            var depth = _cachedState[idxTo].Depth;

            // obtain the idx of the last anchor.
            var idxFrom = _cachedState.IndexOf(s => s.Entity == _lastAnchor);

            // Ensure correct selection order (top to bottom / bottom to top)
            (idxFrom, idxTo) = idxFrom > idxTo ? (idxTo, idxFrom) : (idxFrom, idxTo);

            // Determine if bulk selecting or deselecting.
            // NOTICE: This causes behavior where outcome depends on what's shift selected and not anchored
            // when performing a shift select multiple times back to back.
            // It's nothing to worry about, and can be easily changed from entity to _lastAnchor if more prefer that.
            bool selecting = !_selected.Contains(entity);

            // Perform bulk select/deselect.
            if (_cachedState.Skip(idxFrom).Take(idxTo - idxFrom + 1).All(s => s.Depth >= depth))
            {
                foreach (var e in _cachedState.Skip(idxFrom).Take(idxTo - idxFrom + 1))
                {
                    if (selecting)
                        SelectInternal(e.Entity);
                    else
                        DeselectInternal(e.Entity);
                }
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

    private void DeselectInternal(DynamicDrawSystem<T>.IDynamicEntity entity)
    {
        _selected.Remove(entity);
        if (entity is DynamicDrawSystem<T>.FolderCollection fc)
        {
            _selectedFolderGroups.Remove(fc);
            _selectedFoldersAll.Remove(fc);
        }
        else if (entity is DynamicDrawSystem<T>.Folder f)
        {
            _selectedFolders.Remove(f);
            _selectedFoldersAll.Remove(f);
        }
        else if (entity is DynamicDrawSystem<T>.Leaf l)
        {
            _selectedLeaves.Remove(l);
        }
    }

    private void DeselectInternal(IEnumerable<DynamicDrawSystem<T>.IDynamicEntity> entities)
    {
        _selected.RemoveWhere(e => entities.Contains(e));
        _selectedFolderGroups.RemoveAll(fc => entities.Contains(fc));
        _selectedFolders.RemoveAll(f => entities.Contains(f));
        _selectedFoldersAll.RemoveAll(f => entities.Contains(f));
        _selectedLeaves.RemoveAll(l => entities.Contains(l));
    }

    private void SelectInternal(DynamicDrawSystem<T>.IDynamicEntity entity)
    {
        _selected.Add(entity);
        if (entity is DynamicDrawSystem<T>.FolderCollection fc)
        {
            _selectedFolderGroups.Add(fc);
            _selectedFoldersAll.Add(fc);
        }
        else if (entity is DynamicDrawSystem<T>.Folder f)
        {
            _selectedFolders.Add(f);
            _selectedFoldersAll.Add(f);
        }
        else if (entity is DynamicDrawSystem<T>.Leaf l)
        {
            _selectedLeaves.Add(l);
        }
    }

    private void SelectInternal(IEnumerable<DynamicDrawSystem<T>.IDynamicEntity> entities)
    {
        foreach (var entity in entities)
            SelectInternal(entity);
    }
}
