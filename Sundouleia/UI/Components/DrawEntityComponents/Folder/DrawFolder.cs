using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using OtterGui.Text.EndObjects;
using Sundouleia.Gui.Handlers;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using System.Collections.Immutable;

namespace Sundouleia.Gui.Components;

/// <summary>
///     A DrawFolder that has implementable logic for regeneration, search filter updates, and reorders. <para />
///     Comes includes with <see cref="FolderOptions"/>, drag-drop support, and multi-selection support. <para />
///     Generated Dynamically as needed by updates, for DrawTime performance.
/// </summary>
public abstract class DrawFolder : DisposableMediatorSubscriberBase, ISundesmoFolder
{
    protected readonly MainConfig _config;
    protected readonly GroupsManager _manager;
    protected readonly SundesmoManager _sundesmos;
    protected readonly DrawEntityFactory _factory;

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

    // A lazily evaluated list of all sundesmos 
    protected IImmutableList<Sundesmo> _allItems { get; private set; } = ImmutableList<Sundesmo>.Empty;
    
    // Internal Evaluation of selected items.
    protected HashSet<DrawEntitySundesmo> _selectedItems = [];
    protected DrawEntitySundesmo? _selected;
    protected DrawEntitySundesmo? _lastAnchor;

    internal readonly Queue<Action> DragDropActions = new();

    protected DrawFolder(string label, FolderOptions options, ILogger log, SundouleiaMediator mediator,
        MainConfig config, SharedFolderMemory memory, DrawEntityFactory factory, GroupsManager manager, 
        SundesmoManager sundesmos)
        : base(log, mediator)
    {
        Label = label;
        Options = options;

        _config = config;
        _factory = factory;
        _manager = manager;
        _sundesmos = sundesmos;
        SharedMemory = memory;
        RegenerateItems(string.Empty);

        Mediator.Subscribe<FolderDragDropComplete>(this, _ => OnDragDropFinish(_.Source, _.Dest, _.Payload));
    }

    protected DrawFolder(SundesmoGroup group, FolderOptions options, ILogger<DrawFolder> log, 
        SundouleiaMediator mediator, MainConfig config, SharedFolderMemory memory, 
        DrawEntityFactory factory, GroupsManager manager, SundesmoManager sundesmos)
        : base(log, mediator)
    {
        Label = group.Label;
        Options = options;

        _config = config;
        _factory = factory;
        _manager = manager;
        _sundesmos = sundesmos;
        SharedMemory = memory;

        Mediator.Subscribe<FolderDragDropComplete>(this, _ => OnDragDropFinish(_.Source, _.Dest, _.Payload));
    }

    public int Total => _allItems.Count;
    public int Rendered => _allItems.Count(s => s.IsRendered);
    public int Online => _allItems.Count(s => s.IsOnline);
    public ImmutableList<DrawEntitySundesmo> DrawEntities { get; private set; }

    protected bool ShouldShowFolder()
        => (DrawEntities.Count > 0) || (ShowIfEmpty || Options.ShowIfEmpty);

    protected virtual IImmutableList<Sundesmo> GetAllItems()
        => _sundesmos.DirectPairs.ToImmutableList();

    /// <summary>
    ///     Convert the sorted sundesmo collection into draw-entities. 
    ///     Concrete subclasses must implement.
    /// </summary>
    protected virtual ImmutableList<DrawEntitySundesmo> CreateDrawEntities(IEnumerable<Sundesmo> sorted)
        => sorted.Select(u => _factory.CreateDrawEntity(this, u)).ToImmutableList();

    /// <summary>
    ///     Default sort order for folders. Override in subclasses to provide folder-specific defaults.
    /// </summary>
    protected virtual IEnumerable<FolderSortFilter> GetSortOrder()
        => _config.Current.FavoritesFirst
        ? [FolderSortFilter.Rendered, FolderSortFilter.Online, FolderSortFilter.Favorite, FolderSortFilter.Alphabetical]
        : [FolderSortFilter.Rendered, FolderSortFilter.Online, FolderSortFilter.Alphabetical];

    // Regenerate the list of contents. (usually on sundesmo add / removal)
    public virtual void RegenerateItems(string filter)
    {
        // Regenerate the items.
        var newItems = GetAllItems();
        // Update _allItems.
        _allItems = newItems;

        // Cleanup empty selections here if any exist.
        CleanupEntities(newItems.Except(_allItems));

        // Update the filtered list.
        UpdateItemsForFilter(filter);

        // 
    }

    protected virtual void CleanupEntities(IEnumerable<Sundesmo> removedItems) { }

    // Update the filtered display from the current items.
    public void UpdateItemsForFilter(string filter)
    {
        // Filter the new items by the search filter.
        var filteredItems = _allItems.Where(i => CheckFilter(i, filter));
        // Perform the desired filter sort order to this new result.
        var finalSorted = ApplySortOrder(filteredItems);
        // build it.
        DrawEntities = CreateDrawEntities(finalSorted);
    }

    /// <summary>
    ///     Functionalized sort application.
    ///     Accepts the source set and an optional explicit sort-order sequence.
    ///     Returns a concrete, ordered List&lt;Sundesmo&gt;.
    /// </summary>
    protected List<Sundesmo> ApplySortOrder(IEnumerable<Sundesmo> source)
    {
        var builder = new FolderSortBuilder(source);
        foreach (var f in GetSortOrder())
            builder.Add(f);

        return builder.Build();
    }

    private bool CheckFilter(Sundesmo u, string filter)
    {
        if (filter.IsNullOrEmpty()) return true;
        // return a user if the filter matches their alias or ID, playerChara name, or the nickname we set.
        return u.UserData.AliasOrUID.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            (u.GetNickname()?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (u.PlayerName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    public void DrawContents()
    {
        // If we have opted to not render the folder if empty, and we have nothing to draw, return.
        if (!ShouldShowFolder())
            return;

        // Give the folder a id for the contents.
        using var id = ImRaii.PushId($"sundouleia_folder_{Label}");
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One);
        // pre-determine the size of the folder.
        var folderWidth = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        var bgCol = _hovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : ColorBG;
        var rightWidth = CkGui.IconButtonSize(FAI.Cog).X + CkGui.IconButtonSize(FAI.Filter).X + ImUtf8.ItemInnerSpacing.X * 2;

        // Draw framed child via CkRaii with background based on hover state 
        using (var _ = CkRaii.FramedChildPaddedW($"sundouleia_folder_ {Label}", folderWidth, ImUtf8.FrameHeight, bgCol, ColorBorder, 5f, 1f))
        {
            var pos = ImGui.GetCursorPos();
            ImGui.InvisibleButton($"folder_click_area_{Label}", new Vector2(folderWidth - rightWidth, _.InnerRegion.Y));
            if (ImGui.IsItemClicked())
                _manager.ToggleState(Label);

            // Back to start and then draw.
            ImGui.SameLine(pos.X);
            CkGui.FramedIconText(_manager.IsOpen(Label) ? FAI.CaretDown : FAI.CaretRight);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            CkGui.IconText(Icon, IconColor);
            CkGui.ColorTextFrameAlignedInline(Label, LabelColor);
            DrawOtherInfo();
            if (rightWidth > 0)
            {
                ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth() - rightWidth);
                DrawRightOptions();
            }
        }
        _hovered = ImGui.IsItemHovered();

        // Handle as a drag-drop target.
        AsDragDropTarget();

        if (!_manager.IsOpen(Label))
            return;

        DrawItems();
    }

    protected virtual void DrawOtherInfo()
    {
        CkGui.ColorTextFrameAlignedInline($"[{Online}]", ImGuiColors.DalamudGrey2);
        CkGui.AttachToolTip($"{Online} online\n{Total} total");
    }

    protected virtual void DrawRightOptions()
    { }

    private void DrawItems()
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

    protected void DrawEntity(DrawEntitySundesmo item)
    {
        var clicked = item.DrawItem(_selected == item || _selectedItems.Contains(item));

        // Do not process click detection if we are not doing selections.
        if (!Options.MultiSelect)
            return;

        if (clicked)
            SelectItem(item);
    }

    public void AsDragDropSource(DrawEntitySundesmo item)
    {
        if (!Options.DragDropItems)
            return;

        using var source = ImUtf8.DragDropSource();
        if (!source)
            return;

        if (!DragDropSource.SetPayload(SharedFolderMemory.MoveLabel))
        {
            UpdateSelectedCache(item);
            SharedMemory.UpdateSourceCache(this, _selectedItems.Select(s => s.Sundesmo).ToList());
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

    protected abstract void OnDragDropFinish(DrawFolder Source, DrawFolder Finish, List<Sundesmo> items);

    protected void UpdateSelectedCache(DrawEntitySundesmo item)
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

    protected void SelectItem(DrawEntitySundesmo item)
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
