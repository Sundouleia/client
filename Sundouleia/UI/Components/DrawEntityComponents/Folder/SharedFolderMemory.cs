using Sundouleia.Pairs;
using System.Diagnostics.CodeAnalysis;
using static FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentHousingPlant;

namespace Sundouleia.Gui.Components;

/// <summary>
///     Shared Memory container used by all created DrawFolder instances.
///     Helps properly send drag-drop data across folders properly.
/// </summary>
public class SharedFolderMemory(ILogger<SharedFolderMemory> logger) : IDisposable
{
    public static string MoveLabel = "SUNDOULEIA_DRAGDROP_FOLDER_MOVE";
    // Information about the data help in transit by the source being dragged.
    private DrawFolder? _dragDropSource;
    private List<Sundesmo>? _dragDropSelections;

    // Ensure that all selections persist across folders so no two folders can be selected simultaneously.
    private DrawFolder? _lastFolder;
    private DrawEntitySundesmo? _lastSelected;
    public readonly HashSet<DrawEntitySundesmo> Selected = [];
    private Action? _onSourceTransferred;

    public void Dispose()
    {
        // Cleanup memory.
        if (_dragDropSelections != null)
            _dragDropSelections.Clear();
    }

    //// Clears the current multi-selection.
    //public void ClearSelection(DrawFolder caller)
    //    => SelectItem(caller, null, caller.Options.MultiSelect);

    //protected void RemoveFromSelections(DrawFolder caller, DrawEntitySundesmo item)
    //{
    //    // if the caller is different from the last folder, remove all selections
    //    if (LastFolder != caller)
    //    {
    //        Selected.Clear();
    //        LastFolder = caller;
    //    }
    //    else
    //    {
    //        // remove the item from selections, and re-select last if only one remains
    //        Selected.Remove(item);
    //        if (Selected.Count == 1)
    //            SelectItem(caller, item, true);
    //    }
    //}

    //protected void SelectItem(DrawFolder caller, DrawEntitySundesmo? item, bool clear)
    //{
    //    if (clear || LastFolder != caller)
    //        Selected.Clear();

    //    if (LastSelected == item)
    //        return;

    //    logger.LogDebug($"Selected item in folder {caller.Label} " +
    //        $"[{LastSelected?.DisplayName ?? "null"} => {item?.DisplayName ?? "null"}]");
    //    LastFolder = caller;
    //    LastSelected = item;
    //}

    //protected void SelectItem(DrawFolder caller, DrawEntitySundesmo item, bool additional, bool allBetween)
    //{
    //    // Handle clearing selection.
    //    if (item is null)
    //        SelectItem(null, Options.MultiSelect);
    //    // Handle all-between selection.
    //    else if (allBetween && Options.MultiSelect && _lastSelected != item)
    //    {
    //        // Get the current index in the filtered list to go to.
    //        var idxTo = DrawEntities.IndexOf(item);
    //        if (_lastSelected != null && _selectedItems.Count == 0)
    //        {
    //            var idxFrom = DrawEntities.IndexOf(_lastSelected);
    //            (idxFrom, idxTo) = idxFrom > idxTo ? (idxTo, idxFrom) : (idxFrom, idxTo);
    //            if (DrawEntities.Skip(idxFrom).Take(idxTo - idxFrom + 1).Count() > 0)
    //            {
    //                foreach (var p in DrawEntities.Skip(idxFrom).Take(idxTo - idxFrom + 1))
    //                    _selectedItems.Add(p);
    //                SelectItem(null, false);
    //            }
    //        }
    //    }
    //    // Handle single addition/removal.
    //    else if (additional && Options.MultiSelect)
    //    {
    //        if (_lastSelected != null && _selectedItems.Count == 0)
    //            _selectedItems.Add(_lastSelected);
    //        if (!_selectedItems.Add(item))
    //            RemoveFromSelections(item);
    //        else
    //            SelectItem(null, false);
    //    }
    //    // Handle single selection.
    //    else if (item is not null)
    //        SelectItem(item, Options.MultiSelect);
    //}

    //#endregion Selecting

    #region Drag-Drop
    /// <summary>
    /// Register a source and a copy of its current selection. This makes the registering folder
    /// the single active source until the payload is claimed or cleared.
    /// </summary>
    public void UpdateSourceCache(DrawFolder sourceFolder, List<Sundesmo> selections, Action? onTransferred = null)
    {
        // Nothing changed during drag.
        if (_dragDropSource == sourceFolder && selections.Count.Equals(_dragDropSelections?.Count))
            return;

        logger.LogDebug($"Setting drag-drop source payload in folder {sourceFolder.Label} with {selections.Count} selections.");
        _dragDropSource = sourceFolder;
        _dragDropSelections = selections;
    }


    public (DrawFolder Source, List<Sundesmo> Transferred)? GetSourcePayload()
    {
        if (_dragDropSource is null || _dragDropSelections is null)
            return null;

        logger.LogDebug($"Getting drag-drop source payload from folder {_dragDropSource.Label}.");
        _onSourceTransferred?.Invoke();
        return (_dragDropSource, _dragDropSelections);
    }

    public void ClearPayloadMemory()
    {
        logger.LogDebug("Clearing drag-drop payload memory.");
        if (_dragDropSelections is not null)
            _dragDropSelections.Clear();
        _dragDropSource = null;
    }
    #endregion Drag-Drop
}
