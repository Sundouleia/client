using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using OtterGui.Classes;
using OtterGui.Raii;
using OtterGui.Text;

// Credit to OtterGui for the original implementation.
namespace Sundouleia.CustomCombos;

public abstract class CkFilterComboGallery<T>
{
    private const CFlags heightMask = CFlags.HeightSmall | CFlags.HeightRegular | CFlags.HeightLarge | CFlags.HeightLargest;

    private readonly HashSet<uint> _popupState = [];

    public readonly IReadOnlyList<T> Items;
    public readonly Vector2 ItemSize;

    private LowerString _filter = LowerString.Empty;
    private string[] _filterParts = [];

    protected readonly ILogger Log;
    protected int ItemsPerRow = 4;
    protected bool SearchByParts;
    protected int? NewSelection;

    private int _lastSelection = -1;
    private bool _filterDirty = true;
    private bool _setScroll;
    private bool _closePopup;

    /// <summary> The stored filtered indexes available for display so we can avoid iterating the Items. </summary>
    private readonly List<int> _available;

    public LowerString Filter 
        => _filter;

    protected CkFilterComboGallery(IReadOnlyList<T> items, Vector2 itemSize, ILogger log)
    {
        Items = items;
        ItemSize = itemSize;
        Log = log;
        _available = [];
    }

    /// <summary> Cleans up the storage for the combo item list. </summary>
    /// <param name="label"> The label of the combo to clear the storage of.. </param>
    private void ClearStorage(string label)
    {
        // Log.LogTrace($"Cleaning up Filter Combo Cache for {label}");
        _filter = LowerString.Empty;
        _filterParts = [];
        _lastSelection = -1;

        Cleanup();
        _filterDirty = true;
        _available.Clear();
        _available.TrimExcess();
    }

    /// <summary> Determines if the item is visible based on the filter. </summary>
    protected virtual bool IsVisible(int globalIndex, LowerString filter)
    {
        if (!SearchByParts)
            return filter.IsContained(ToString(Items[globalIndex]));

        if (_filterParts.Length == 0)
            return true;

        var name = ToString(Items[globalIndex]).ToLowerInvariant();
        return _filterParts.All(name.Contains);
    }

    /// <summary>
    ///     May faze out.
    /// </summary>
    protected virtual string ToString(T obj)
        => obj?.ToString() ?? string.Empty;

    /// <summary>
    ///     Width of the popup displayed by the combo/popup call.
    /// </summary>
    protected virtual float GetInnerWidth()
        => ItemSize.X * ItemsPerRow + ImUtf8.ItemInnerSpacing.X * (ItemsPerRow - 1);

    protected void RefreshCombo()
    {
        Cleanup(); // Instruct the CachingList to regenerate its items.
        _filterDirty = true; // Force the filter to be updated (even when opened to make immediate).
    }

    /// <summary>
    ///     Called upon a storage clear. Should be overridden by Filter Combo Cache to ensure its caches are cleared.
    /// </summary>
    protected virtual void Cleanup() 
    { }

    // Maybe remove later, unsure what purpose this serves.
    protected virtual void PostCombo(float previewWidth)
    { }

    // Draws the popup variant of the combo display.
    private void DrawComboPopup(string label, Vector2 openPos, int currentSelected, uint? customSearchBg = null)
    {
        // Begin the popup thingy.
        ImGui.SetNextWindowPos(openPos);
        using var popup = ImRaii.Popup(label, ImGuiWindowFlags.AlwaysAutoResize);
        var id = ImGui.GetID(label);
        if (popup)
        {
            // Appends the popup to display the window of items when opened.
            _popupState.Add(id);

            // Updates the filter to have the correct _available indexes.
            UpdateFilter();
            var innerWidth = GetInnerWidth();
            // Draws the filter and updates the scroll to the selected items.
            DrawFilter(currentSelected, innerWidth, customSearchBg);
            // grab the filter height for reference incase the list uses custom height.
            var resHeight = ItemSize.Y * 12;
            // If any items are selected, they are stored in `NewSelection`.
            // `NewSelection` is cleared at the end of the parent DrawFunction.
            DrawGallery(innerWidth, resHeight);
            // If we should close the popup (after selection), do so.
            ClosePopup(id, label);
        }
        else if (_popupState.Remove(id))
        {
            // Clear the storage if the popup state can be removed. (We closed it)
            ClearStorage(label);
        }
    }


    /// <summary> Called by the filter combo base Draw() call. Handles updates and changed items. </summary>
    private void DrawCombo(string label, string preview, float comboWidth, int curSelected, CFlags flags, uint? customSearchBg = null)
    {
        // Ensure a height flag is set.
        if ((flags & heightMask) == 0)
            flags |= CFlags.HeightLarge;

        // Give this an id and begin the combo.
        var id = ImGui.GetID(label);
        ImGui.SetNextItemWidth(comboWidth);

        using var combo = ImRaii.Combo(label, preview, flags);
        PostCombo(comboWidth);

        if (combo)
        {
            // Appends the popup to display the window of items when opened.
            _popupState.Add(id);
            // Updates the filter to have the correct _available indexes.
            UpdateFilter();

            // Width of the popup window and text input field.
            var innerWidth = GetInnerWidth();
            var galleryHeight = (ItemSize.Y + ImUtf8.ItemSpacing.Y) * (((flags & CFlags.HeightLargest) != 0) ? 20 : 12);

            // Draws the filter and updates the scroll to the selected items.
            DrawFilter(curSelected, innerWidth, customSearchBg);

            // If any items are selected, they are stored in `NewSelection`.
            // `NewSelection` is cleared at the end of the parent DrawFunction.
            DrawGallery(innerWidth, galleryHeight);

            // If we should close the popup (after selection), do so.
            ClosePopup(id, label);
        }
        else if (_popupState.Remove(id))
        {
            // Clear the storage if the popup state can be removed. (We closed it)
            ClearStorage(label);
        }
    }

    /// <summary> Updates the current selection to be used in the DrawList function. </summary>
    /// <remarks> Do not ever override this unless you know what you're doing. If you do, you must call the base. </remarks>
    protected virtual int UpdateCurrentSelected(int currentSelected)
    {
        _lastSelection = currentSelected;
        return currentSelected;
    }

    /// <summary> Updates the last selection with the currently selected item. This is then updated to the proper index in _available[] </summary>
    /// <remarks> Additionally scrolls the list to the last selected item, if any, and displays the filter. </remarks>
    protected virtual void DrawFilter(int currentSelected, float width, uint? searchBg)
    {
        if(searchBg.HasValue)
            ImGui.PushStyleColor(ImGuiCol.FrameBg, searchBg.Value);

        _setScroll = false;
        // Scroll to current selected when opened if any, and set keyboard focus to the filter field.
        if (ImGui.IsWindowAppearing())
        {
            currentSelected = UpdateCurrentSelected(currentSelected);
            _lastSelection = _available.IndexOf(currentSelected);
            _setScroll = true;
            ImGui.SetKeyboardFocusHere();
        }

        // Draw the text input.
        ImGui.SetNextItemWidth(width);
        if (LowerString.InputWithHint("##filter", "Filter...", ref _filter))
        {
            _filterDirty = true;
            if (SearchByParts)
                _filterParts = _filter.Lower.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }

        if (searchBg.HasValue)
            ImGui.PopStyleColor();
    }

    /// <summary> 
    ///     Draws the Gallery result of items.
    /// </summary>
    protected virtual void DrawGallery(float innerWidth, float galleryHeight)
    {
        // A child for the items, so that the filter remains visible.
        // Height is based on default combo height minus the filter input.
        var finalHeight = galleryHeight - ImGui.GetFrameHeight() - ImGui.GetStyle().WindowPadding.Y;
        using var _ = CkRaii.Child("ChildGallery", new Vector2(innerWidth, finalHeight), wFlags: WFlags.NoScrollbar);      
        // Shift the scroll to the location of the item.
        if (_setScroll)
            ImGui.SetScrollFromPosY(_lastSelection * ItemSize.Y - ImGui.GetScrollY());

        CkGuiClip.DynamicClippedGalleryDraw(_available, DrawItemInternal, ItemsPerRow, ItemSize.X);
    }

    protected virtual bool DrawSelectable(int globalIdx, bool selected)
    {
        var obj = Items[globalIdx];
        var name = ToString(obj);
        return ImGui.Button (name, ItemSize);
    }

    private void DrawItemInternal(int globalIdx, int localIdx)
    {
        using var id = ImRaii.PushId(globalIdx);
        if (DrawSelectable(globalIdx, _lastSelection == localIdx))
        {
            NewSelection = globalIdx;
            _closePopup = true;
        }
    }

    /// <summary> To execute any additional logic upon the popup closing prior to clearing the storage. Override this. </summary>
    protected virtual void OnClosePopup() { }

    /// <summary> The action to take upon a popup closing. </summary>
    private void ClosePopup(uint id, string label)
    {
        if (!_closePopup)
            return;

        // Close the popup and reset state.
        Log.LogTrace("Closing popup for {Label}.", label);
        ImGui.CloseCurrentPopup();
        _popupState.Remove(id);
        OnClosePopup();
        ClearStorage(label);
        // Reset the close popup state.
        _closePopup = false;
    }

    /// <summary> MAIN DRAW CALL LOGIC FUNC. Should ALWAYS be called by the Filter Combo Cache unless instructed otherwise. </summary>
    /// <returns> True if anything was selected, false otherwise. </returns>
    /// <remarks> This will return the index of the `ref` currentSelection, meaning Filter Combo Cache handles the selected item. </remarks>
    public virtual bool Draw(string label, string preview, float comboWidth, ref int currentSelection, CFlags flags = CFlags.None, uint? customSearchBg = null)
    {
        DrawCombo(label, preview, comboWidth, currentSelection, flags, customSearchBg);
        if (NewSelection is null)
            return false;
        // if the selection is the same, do not return true. (maybe revert, experimental)
        var changed = (currentSelection != NewSelection.Value);
        // Update these regardless.
        currentSelection = NewSelection.Value;
        NewSelection = null;
        // Return if the new  selection changed.
        return changed;
    }

    public virtual bool DrawPopup(string label, Vector2 openPos, ref int currentSelection, uint? customSearchBg = null)
    {
        using var s = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var c = ImRaii.PushColor(ImGuiCol.Border, ImGuiColors.ParsedPink);

        DrawComboPopup(label, openPos, currentSelection, customSearchBg);
        if (NewSelection is null)
            return false;
        // if the selection is the same, do not return true. (maybe revert, experimental)
        var changed = (currentSelection != NewSelection.Value);
        // Update these regardless.
        currentSelection = NewSelection.Value;
        NewSelection = null;
        // Return if the new  selection changed.
        return changed;
    }


    /// <summary> Be stateful and update the filter whenever it gets dirty. </summary>
    private void UpdateFilter()
    {
        if (!_filterDirty)
            return;

        _filterDirty = false;
        _available.EnsureCapacity(Items.Count);

        // Keep the selected key if possible.
        var lastSelection = _lastSelection == -1 ? -1 : _available[_lastSelection];
        _lastSelection = -1;

        _available.Clear();
        for (var idx = 0; idx < Items.Count; ++idx)
        {
            if (!IsVisible(idx, _filter))
                continue;

            if (lastSelection == idx)
                _lastSelection = _available.Count;
            _available.Add(idx);
        }
    }
}
