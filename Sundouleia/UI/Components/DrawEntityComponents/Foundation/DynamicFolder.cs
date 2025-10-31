using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Extensions;
using OtterGui.Text;
using OtterGui.Text.EndObjects;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using System.Collections.Immutable;

namespace Sundouleia.Gui.Components;

/// <summary>
///     A DrawFolder that has implementable logic for regeneration, search filter updates, and reorders. <para />
///     Comes includes with <see cref="FolderOptions"/>, drag-drop support, and multi-selection support. <para />
///     Generated Dynamically as needed by updates, for DrawTime performance.
/// </summary>
public abstract class DynamicFolder<TModel, TDrawEntity> : DisposableMediatorSubscriberBase, IDynamicFolder<TDrawEntity>
    where TModel : class 
    where TDrawEntity : class, IDrawEntity
{
    protected readonly MainConfig _config;
    protected readonly DrawEntityFactory _factory;
    protected readonly GroupsManager _groups;

    public string DistinctId => Label;
    public SharedFolderMemory SharedMemory { get; init; }

    protected bool _hovered;

    public FolderOptions   Options { get; init; }
    public string          Label { get; protected set; }
    protected uint LabelColor  = uint.MaxValue;
    protected FAI  Icon        = FAI.Folder;
    protected uint IconColor   = uint.MaxValue;
    protected uint ColorBG     = uint.MinValue;
    protected uint ColorBorder = uint.MaxValue;
    protected bool ShowIfEmpty = true;
    protected bool ShowOffline = true;

    // All items known for this folder.
    protected List<TModel> _allItems { get; private set; } = new();
    // Map to properly allocate entities to models.
    // Preferably make List if possible for improvements, but leave as functional for now.
    protected Dictionary<TModel, TDrawEntity> _drawEntityMap { get; private set; } = new();
    
    // Internal Evaluation of selected items.
    protected HashSet<IDrawEntity> _selectedItems = [];
    protected IDrawEntity? _selected;
    protected IDrawEntity? _lastAnchor;

    /// <summary>
    ///     You are expected to call RegenerateItems in any derived constructor to populate the folder contents.
    /// </summary>
    protected DynamicFolder(string label, FolderOptions options, ILogger log, 
        SundouleiaMediator mediator, MainConfig config, DrawEntityFactory factory,
        GroupsManager groups, SharedFolderMemory memory)
        : base(log, mediator)
    {
        Label = label;
        Options = options;

        _config = config;
        _factory = factory;
        _groups = groups;
        SharedMemory = memory;

        Mediator.Subscribe<FolderDragDropComplete>(this, _ => OnDragDropFinish(_.Source, _.Dest, _.Transferred));
    }

    public int Total => _allItems.Count;

    // For public read use only, modification made in _drawEntities.
    public IReadOnlyList<TDrawEntity> DrawEntities { get; private set; } = [];

    protected bool ShouldShowFolder() => DrawEntities.Count > 0 || (ShowIfEmpty || Options.ShowIfEmpty);

    /// <summary>
    ///     Obtain all current items for this folder.
    /// </summary>
    /// <returns></returns>
    protected abstract List<TModel> GetAllItems();

    /// <summary>
    ///     Make a DrawEntity from the given item.
    /// </summary>
    protected abstract TDrawEntity ToDrawEntity(TModel item);

    protected virtual void OnDragDropFinish(IDynamicFolder Source, IDynamicFolder Finish, List<IDrawEntity> items)
    { /* do nothing by default */ }

    protected abstract bool CheckFilter(TModel u, string filter);

    /// <summary>
    ///     Default sort order for folders. Override in subclasses to provide folder-specific defaults.
    /// </summary>
    protected virtual IEnumerable<FolderSortFilter> GetSortOrder()
        => _config.Current.FavoritesFirst
            ? [ FolderSortFilter.Rendered, FolderSortFilter.Online, FolderSortFilter.Favorite, FolderSortFilter.Alphabetical ]
            : [ FolderSortFilter.Rendered, FolderSortFilter.Online, FolderSortFilter.Alphabetical ];

    // We dont technically need to regenerate all drawn items,
    // but rather draw what does exist, and create what doesn't.
    public virtual void RegenerateItems(string filter)
    {
        _allItems = GetAllItems();
        // Could add a cleanup filter here if desired.
        UpdateItemsForFilter(filter);
    }

    public void UpdateItemsForFilter(string filter)
    {
        // To hash for quick lookup.
        var filteredItems = _allItems.Where(i => CheckFilter(i, filter)).ToHashSet();

        // Remove items no longer in filtered.
        foreach (var item in _drawEntityMap.Keys.ToList())
            if (!filteredItems.Contains(item))
                _drawEntityMap.Remove(item);

        // Add new draw-entities for filtered items not already present.
        foreach (var item in filteredItems)
            if (!_drawEntityMap.ContainsKey(item))
                _drawEntityMap[item] = ToDrawEntity(item);

        // Sort the new filtered items.
        var sortedFilteredItems = ApplySortOrder(filteredItems.Select(i => _drawEntityMap[i]));
        DrawEntities = sortedFilteredItems;
    }

    /// <summary>
    ///     Functionalized sort application.
    ///     Accepts the source set and an optional explicit sort-order sequence.
    ///     Returns a concrete, ordered List&lt;TModel&gt;.
    /// </summary>
    protected virtual List<TDrawEntity> ApplySortOrder(IEnumerable<TDrawEntity> source)
        => source.OrderBy(u => u.DisplayName).ToList();

    // Could maybe make folders expand here or something, idk.
    public void DrawContents()
    {
        // If we have opted to not render the folder if empty, and we have nothing to draw, return.
        if (!ShouldShowFolder())
            return;

        // Give the folder a id for the contents.
        using var id = ImRaii.PushId($"sundouleia_folder_{Label}");
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One);

        // Style intended only for DragDrop selections.
        if (Options.IsDropTarget || Options.DragDropItems)
            style.Push(ImGuiStyleVar.ItemSpacing, new Vector2(ImUtf8.ItemSpacing.X, 2f * ImGuiHelpers.GlobalScale));

        DrawFolderInternal(true);
        AsDragDropTarget();

        if (!_groups.IsOpen(Label))
            return;

        DrawItems();
    }

    public void DrawFolder()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One);
        // Style intended only for DragDrop selections.
        if (Options.IsDropTarget || Options.DragDropItems)
            style.Push(ImGuiStyleVar.ItemSpacing, new Vector2(ImUtf8.ItemSpacing.X, 2f * ImGuiHelpers.GlobalScale));

        DrawFolderInternal(false);
        AsDragDropTarget();
    }

    public void DrawItems()
    {
        var folderMin = ImGui.GetItemRectMin();
        var folderMax = ImGui.GetItemRectMax();
        var wdl = ImGui.GetWindowDrawList();
        wdl.ChannelsSplit(2);
        wdl.ChannelsSetCurrent(1); // Foreground.

        using var indent = ImRaii.PushIndent(ImUtf8.FrameHeight + ImUtf8.ItemInnerSpacing.X + ImGuiHelpers.GlobalScale, false);
        ImGuiClip.ClippedDraw(DrawEntities, DrawEntity, ImUtf8.FrameHeightSpacing);

        wdl.ChannelsSetCurrent(0); // Background.
        var gradientTL = new Vector2(folderMin.X, folderMax.Y);
        var gradientTR = new Vector2(folderMax.X, ImGui.GetItemRectMax().Y);
        wdl.AddRectFilledMultiColor(gradientTL, gradientTR, ColorHelpers.Fade(ColorBorder, .9f), ColorHelpers.Fade(ColorBorder, .9f), 0, 0);
        wdl.ChannelsMerge();
    }

    protected virtual void DrawFolderInternal(bool toggles)
    {
        // pre-determine the size of the folder.
        var folderWidth = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        var bgCol = _hovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : ColorBG;
        // Draw framed child via CkRaii with background based on hover state 
        using (var _ = CkRaii.FramedChildPaddedW($"sundouleia_folder_ {Label}", folderWidth, ImUtf8.FrameHeight, bgCol, ColorBorder, 5f, 1f))
        {
            var pos = ImGui.GetCursorPos();
            ImGui.InvisibleButton($"folder_click_area_{Label}", new Vector2(folderWidth, _.InnerRegion.Y));
            if (ImGui.IsItemClicked() && toggles)
                _groups.ToggleState(Label);

            // Back to start and then draw.
            ImGui.SameLine(pos.X);
            CkGui.FramedIconText(_groups.IsOpen(Label) ? FAI.CaretDown : FAI.CaretRight);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            CkGui.IconText(Icon, IconColor);
            CkGui.ColorTextFrameAlignedInline(Label, LabelColor);
        }
        _hovered = ImGui.IsItemHovered();
    }

    protected void DrawEntity(IDrawEntity item)
    {
        var clicked = item.Draw(item.Equals(_selected) || _selectedItems.Contains(item));

        // Do not process click detection if we are not doing selections.
        if (!Options.MultiSelect)
            return;

        if (clicked)
            SelectItem(item);
    }

    public void AsDragDropSource(TDrawEntity item)
    {
        if (!Options.DragDropItems)
            return;

        using var source = ImUtf8.DragDropSource();
        if (!source)
            return;

        if (!DragDropSource.SetPayload(SharedFolderMemory.MoveLabel))
        {
            UpdateSelectedCache(item);
            SharedMemory.UpdateSourceCache(this, _selectedItems.Cast<IDrawEntity>().ToList());
        }

        // Display tooltip text.
        CkGui.TextFrameAligned(_selectedItems.Count == 1
            ? $"Moving {_selectedItems.First().DisplayName}..."
            : $"Moving ...\n\t - {string.Join("\n\t - ", _selectedItems.Select(i => i.DisplayName))}");
    }

    // Make private?
    protected void AsDragDropTarget()
    {
        if (!Options.IsDropTarget)
            return;

        using var target = ImUtf8.DragDropTarget();
        if (!target.IsDropping(SharedFolderMemory.MoveLabel))
            return;

        // Obtain the payload if available. If not, log error.
        if (SharedMemory.GetSourcePayload() is { } payload)
        {
            var (sourceFolder, selections) = payload;
            Mediator.Publish(new FolderDragDropComplete(sourceFolder, this, selections));
        }

        // Clean up any selections from this folder, if any (maybe work towards moving this to shared memory)
        _selectedItems.Clear();
    }

    protected void UpdateSelectedCache(TDrawEntity item)
    {
        if (_selectedItems.Contains(item))
            return;

        if (Options.MultiSelect)
        {
            _selectedItems.Add(item);
            _selected = item;
        }
        else
        {
            _selectedItems.Clear();
            _selectedItems.Add(item);
            _selected = item;
        }
    }

    protected void SelectItem(IDrawEntity item)
    {
        bool ctrl = ImGui.GetIO().KeyCtrl;
        bool shift = ImGui.GetIO().KeyShift;

        // SHIFT Range select / deselect
        if (shift && _lastAnchor != null && Options.MultiSelect)
        {
            int i1 = DrawEntities.IndexOf(_lastAnchor);
            int i2 = DrawEntities.IndexOf(item);
            if (i1 < 0 || i2 < 0)
                return;
            // Ensure correct selection order (top to bottom / bottom to top)
            (i1, i2) = i1 <= i2 ? (i1, i2) : (i2, i1);
            // Determine intent: are we selecting or deselecting?

            // NOTE:
            // This can produce behavior where it depends on what is shift selected, and not anchored.
            // It's nothing to worry about, other than personal preference. if more prefer the other way around, just flip this from item to _lastAnchor.
            bool selecting = !_selectedItems.Contains(item);
            // Bulk select/deselect.
            for (int i = i1; i <= i2; i++)
            {
                var e = DrawEntities[i];
                if (selecting)
                    _selectedItems.Add(e);
                else
                    _selectedItems.Remove(e);
            }
            // Update last interacted.
            _selected = selecting ? item : null;
        }
        // Single-Selection toggle
        else if (ctrl && Options.MultiSelect)
        {
            // Modify.
            var removed = _selectedItems.Remove(item);
            if (!removed)
                _selectedItems.Add(item);
            // Update interactions.
            _lastAnchor = item;
            _selected = removed ? null : item;
        }
        // Normal interaction should immediately clear all other selections.
        else
        {
            // we can single select if nothing else is selected.
            if (_selectedItems.Count is 1 && _selected == item)
            {
                _selectedItems.Clear();
                _lastAnchor = item;
                _selected = null;
            }
            else
            {
                _selectedItems.Clear();
                _selectedItems.Add(item);
                _lastAnchor = item;
                _selected = item;
            }
        }
    }
}
