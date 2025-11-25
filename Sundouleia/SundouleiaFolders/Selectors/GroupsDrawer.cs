using CkCommons;
using CkCommons.Classes;
using CkCommons.Gui;
using CkCommons.Raii;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using OtterGui.Text;
using Sundouleia.CustomCombos;
using Sundouleia.DrawSystem.Selector;
using Sundouleia.Gui.MainWindow;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;

namespace Sundouleia.DrawSystem;

public class GroupsDrawer : DynamicDrawer<Sundesmo>, IMediatorSubscriber, IDisposable
{
    private static readonly IconCheckboxEx CheckboxOffline = new(FAI.Unlink);
    private static readonly IconCheckboxEx CheckboxShowEmpty = new(FAI.FolderOpen);
    // Used when arranging Groups.
    private static readonly string DragDropTooltip =
        "--COL--[L-CLICK & DRAG]--COL-- Drag folder to a new place in the hierarchy." +
        "--NL----COL--[CTRL + L-CLICK]--COL-- Single-Select this folder." +
        "--NL----COL--[SHIFT + L-CLICK]--COL-- Select/Deselect all users between current and last selection";

    // Used for normal leaf interaction.
    private static readonly string NormalTooltip =
        "--COL--[L-CLICK]--COL-- Swap Between Name/Nick/Alias & UID." +
        "--NL----COL--[M-CLICK]--COL-- Open Profile" +
        "--NL----COL--[SHIFT + R-CLICK]--COL-- Edit Nickname";

    private readonly MainConfig _config;
    private readonly FolderConfig _folderConfig;
    private readonly FavoritesConfig _favoritesConfig;
    private readonly ServerConfigManager _serverConfigs;
    private readonly GroupsManager _groups;
    private readonly SundesmoManager _sundesmos;
    private readonly StickyUIService _stickyService;


    public SundouleiaMediator Mediator { get; }

    // Widgets
    private GroupFilterEditor _filterEditor;
    private FAIconCombo       _iconSelector;

    // If we have the group editor window open.
    private bool _editorShown = false;
    // Single Group editor temp name storage.
    private string _nameEditTmp = string.Empty;

    // Track which folder has its config open.
    private IDynamicCollection<Sundesmo>? _folderInEditor;

    // We want to do the same state tracking as the whitelist drawer since we draw the same node types.
    private HashSet<IDynamicNode<Sundesmo>> _showingUID = new(); // Nodes in here show UID.
    private IDynamicNode<Sundesmo>?         _renaming   = null;
    private string                          _nameEditStr= string.Empty; // temp nick text.
    
    private IDynamicNode? _hoveredTextNode;     // From last frame.
    private IDynamicNode? _newHoveredTextNode;  // Tracked each frame.
    private bool          _profileShown = false;// If currently displaying a popout profile.
    private DateTime?     _lastHoverTime;       // time until we should show the profile.

    public GroupsDrawer(ILogger<GroupsDrawer> logger, SundouleiaMediator mediator, MainConfig config, 
        FolderConfig folderConfig, FavoritesConfig favorites, ServerConfigManager serverConfig, 
        GroupsManager groups, SundesmoManager sundesmos, StickyUIService stickyService, GroupsDrawSystem ds)
        : base("##GroupsDrawer", logger, ds, new SundesmoCache(ds))
    {
        Mediator = mediator;
        _config = config;
        _folderConfig = folderConfig;
        _favoritesConfig = favorites;
        _serverConfigs = serverConfig;
        _groups = groups;
        _sundesmos = sundesmos;
        _stickyService = stickyService;
        // Init the filter editor.
        _filterEditor = new GroupFilterEditor(groups);
        _iconSelector = new FAIconCombo(logger);

        // Mediator.Subscribe<SetSidePanel>(this, _ => _editorShown = _.Mode is SidePanelMode.GroupEditor);
    }

    public override void Dispose()
    {
        base.Dispose();
        Mediator.UnsubscribeAll(this);
    }

    #region Search
    protected override void DrawSearchBar(float width, int length)
    {
        var tmp = Cache.Filter;
        var buttonsWidth = CkGui.IconButtonSize(FAI.Wrench).X + CkGui.IconTextButtonSize(FAI.PeopleGroup, "Groups");
        // Update the search bar if things change, like normal.
        if (FancySearchBar.Draw("Filter", width, ref tmp, "filter..", length, buttonsWidth, DrawButtons))
            Cache.Filter = tmp;
    }

    private void DrawButtons()
    {
        if (CkGui.IconTextButton(FAI.PeopleGroup, "Groups", null, true, _editorShown))
        {
            _editorShown = false;
            _folderConfig.Current.ViewingGroups = !_folderConfig.Current.ViewingGroups;
            _folderConfig.Save();
        }
        CkGui.AttachToolTip("Switch to Basic View");

        ImGui.SameLine(0, 0);
        if (CkGui.IconButton(FAI.Wrench, inPopup: !_editorShown))
            _stickyService.ForOrganizer();
        CkGui.AttachToolTip("Edit, Add, Rearrange, or Remove Groups.");
    }
    #endregion Search

    // Override to detect hover changes on text nodes in addition to selections.
    protected override void UpdateHoverNode()
    {
        // Before we update the nodes we should run a comparison to see if they changed.
        // If they did we should close any popup if opened.
        if (_hoveredTextNode != _newHoveredTextNode)
        {
            if (!_profileShown)
                _lastHoverTime = _newHoveredTextNode is null ? null : DateTime.UtcNow.AddSeconds(_config.Current.ProfileDelay);
            else
            {
                _lastHoverTime = null;
                _profileShown = false;
                Mediator.Publish(new CloseProfilePopout());
            }
        }

        // Update the hovered text node stuff.
        _hoveredTextNode = _newHoveredTextNode;
        _newHoveredTextNode = null;

        // Now properly update the hovered node.
        _hoveredNode = _newHoveredNode;
        _newHoveredNode = null;
    }

    #region Folders
    protected override void DrawFolderBanner(IDynamicFolder<Sundesmo> f, DynamicFlags flags, bool selected)
    {
        // Ensure we draw the base for the all folder.
        if (f is not GroupFolder gf)
        {
            base.DrawFolderBanner(f, flags, selected);
            return;
        }
        // Otherwise draw the group folder.
        var editing = _folderInEditor == f;
        var width = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        // If we are editing, draw both, otherwise, draw only the header.
        if (!editing)
            DrawFolderRow(gf, width, flags, selected);
        else
        {
            using (ImRaii.Group())
            {
                DrawFolderRowEditing(gf, width, flags, selected);
                DrawFolderEditor(gf, width, flags, editing);
            }
            ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(ImGuiCol.Button), 5f);
        }
    }

    private void DrawFolderRow(GroupFolder folder, float width, DynamicFlags flags, bool selected)
    {
        // We could likely reduce this by a lot if we had a override for this clipped draw within the dynamic draw system.
        var rWidth = CkGui.IconButtonSize(FAI.Cog).X + CkGui.IconButtonSize(FAI.Filter).X;
        var bgCol = selected ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : folder.BgColor;
        // Display a framed child with stylizations based on the folders preferences.
        using var _ = CkRaii.FramedChildPaddedW($"df_header_{folder.ID}", width, ImUtf8.FrameHeight, bgCol, folder.BorderColor, 5f, 1f);

        var pos = ImGui.GetCursorPos();
        ImGui.InvisibleButton($"{Label}_node_{folder.ID}", new(width - rWidth, ImUtf8.FrameHeight));
        HandleInteraction(folder, flags);
        CkGui.AttachToolTip(DragDropTooltip, !flags.HasAny(DynamicFlags.DragDropFolders), ImGuiColors.DalamudOrange);

        // Back to the start, then draw.
        ImGui.SameLine(pos.X);
        CkGui.FramedIconText(folder.IsOpen ? FAI.CaretDown : FAI.CaretRight);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        CkGui.IconText(folder.Icon, folder.IconColor);
        CkGui.ColorTextFrameAlignedInline(folder.Name, folder.NameColor);
        // Total Context.
        CkGui.ColorTextFrameAlignedInline($"[{folder.Online}]", ImGuiColors.DalamudGrey2);
        CkGui.AttachToolTip($"{folder.Online} online\n{folder.TotalChildren} total");

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth() - rWidth);
        DrawFolderOptions(folder);
    }

    private void DrawFolderRowEditing(GroupFolder folder, float width, DynamicFlags flags, bool selected)
    {
        // We could likely reduce this by a lot if we had a override for this clipped draw within the dynamic draw system.
        var rWidth = CkGui.IconButtonSize(FAI.Cog).X + CkGui.IconButtonSize(FAI.Filter).X;
        var bgCol = selected ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : folder.BgColor;
        // Display a framed child with stylizations based on the folders preferences.
        using var _ = CkRaii.FramedChildPaddedW($"df_header_{folder.ID}", width, ImUtf8.FrameHeight, bgCol, folder.BorderColor, 5f, 1f);

        ImUtf8.SameLineInner();
        CkGui.FramedIconText(folder.IsOpen ? FAI.CaretDown : FAI.CaretRight);
        ImGui.SameLine();
        // Display the editor version of this groups icon, allowing it to be changed.
        if (_iconSelector.Draw("IconSel", folder.Icon, 10))
        {
            // Update the icon within the group manager.
            if (_groups.TrySetIcon(folder.Name, _iconSelector.Current, folder.IconColor))
                folder.ApplyLatestStyle();
        }
        CkGui.AttachToolTip("Edit the icon for your group.");

        ImGui.SameLine();
        ImGui.SetNextItemWidth((ImGui.GetContentRegionAvail().X - rWidth) / 2);
        var nameTmp = folder.Name;
        ImGui.InputTextWithHint("##GroupNameEdit", "Set Name..", ref _nameEditTmp, 40);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            if (_groups.TryRename(folder.Name, _nameEditTmp))
            {
                DrawSystem.Rename(folder, _nameEditTmp);
                // Mark the parent for a reload, since its new name may not be filtered anymore.
                Cache.MarkForReload(folder.Parent);
            }
        }
        CkGui.AttachToolTip("The name of this group.");

        ImGui.SameLine();
        var posX = ImGui.GetCursorPosX();
        var rightX = DrawFolderOptions(folder);

        // Fill in remaining area with interactable space.
        ImGui.SameLine(posX);
        ImGui.InvisibleButton($"{Label}_node_{folder.ID}", new(rightX - posX, ImUtf8.FrameHeight));
        HandleInteraction(folder, flags);
    }

    private void DrawFolderEditor(GroupFolder f, float width, DynamicFlags flags, bool editing)
    {
        var bgCol = ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.FrameBg), 0.4f);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImUtf8.ItemSpacing.Y);
        using var s = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, ImGui.GetStyle().CellPadding with { Y = 0 })
            .Push(ImGuiStyleVar.WindowPadding, new Vector2(4f));
        using var child = CkRaii.ChildPaddedW("FolderEditView", width, CkStyle.TwoRowHeight(), bgCol, 5f);
        using var _ = ImRaii.Table("FolderEditTable", 3, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV);

        if (!_)
            return;

        ImGui.TableSetupColumn("Colors");
        ImGui.TableSetupColumn("Flags");
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        var iconCol = ImGui.ColorConvertU32ToFloat4(f.IconColor);
        if (ImGui.ColorEdit4("Icon Color", ref iconCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
        {

            if (_groups.TrySetStyle(f.Name, ImGui.ColorConvertFloat4ToU32(iconCol), f.NameColor, f.BorderColor, f.GradientColor))
                f.ApplyLatestStyle();
        }
        CkGui.AttachToolTip("Change the color of the folder icon.");


        var labelCol = ImGui.ColorConvertU32ToFloat4(f.NameColor);
        if (ImGui.ColorEdit4("Label Color", ref labelCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
        {
            if (_groups.TrySetStyle(f.Name, f.IconColor, ImGui.ColorConvertFloat4ToU32(labelCol), f.BorderColor, f.GradientColor))
                f.ApplyLatestStyle();
        }
        CkGui.AttachToolTip("Change the color of the folder label.");

        // Other two colors.
        ImGui.TableNextColumn();
        var borderCol = ImGui.ColorConvertU32ToFloat4(f.BorderColor);
        if (ImGui.ColorEdit4("Border Color", ref borderCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
        {
            if (_groups.TrySetStyle(f.Name, f.IconColor, f.NameColor, ImGui.ColorConvertFloat4ToU32(borderCol), f.GradientColor))
                f.ApplyLatestStyle();
        }
        CkGui.AttachToolTip("Change the color of the folder border.");

        var gradCol = ImGui.ColorConvertU32ToFloat4(f.GradientColor);
        if (ImGui.ColorEdit4("Gradient Color", ref gradCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
        {
            if (_groups.TrySetStyle(f.Name, f.IconColor, f.NameColor, f.BorderColor, ImGui.ColorConvertFloat4ToU32(gradCol)))
                f.ApplyLatestStyle();
        }
        CkGui.AttachToolTip("Change the color of the folder gradient.");

        ImGui.TableNextColumn();
        var showOffline = f.ShowOffline;
        if (CheckboxOffline.Draw("Show Offline"u8, ref showOffline))
        {
            if (_groups.TrySetState(f.Name, showOffline, f.ShowIfEmpty))
            {
                // Update the folder within the file system and mark things for a reload.
                DrawSystem.UpdateFolder(f);
                Cache.MarkForReload(f);
            }
        }
        CkGui.AttachToolTip("Show offline pairs in this folder.");

        var showIfEmpty = f.Flags.HasAny(FolderFlags.ShowIfEmpty);
        if (CheckboxShowEmpty.Draw("Show If Empty"u8, ref showIfEmpty))
        {
            f.SetShowEmpty(showIfEmpty);
            if (_groups.TrySetState(f.Name, f.ShowOffline, f.ShowIfEmpty))
                Cache.MarkForReload(f);
        }
        CkGui.AttachToolTip("Folder is shown even with 0 items are filtered");
    }

    private float DrawFolderOptions(GroupFolder folder)
    {
        var inFilterEditor = ImGui.IsPopupOpen($"{folder.ID}_filterEdit");
        var isFolderInEditor = _folderInEditor == folder;
        var config = CkGui.IconButtonSize(FAI.Cog);
        var filter = CkGui.IconButtonSize(FAI.Filter);
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        var currentRightSide = windowEndX - config.X;

        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        if (CkGui.IconButton(FAI.Cog, inPopup: !isFolderInEditor))
            ToggleEditor(folder);
        CkGui.AttachToolTip("Edit Group");

        currentRightSide -= filter.X;
        ImGui.SameLine(currentRightSide);
        if (CkGui.IconButton(FAI.Filter, null, folder.ID.ToString(), false, !inFilterEditor))
            ImGui.OpenPopup($"{folder.ID}_filterEdit");
        CkGui.AttachToolTip("Change filters or sort order.");

        // If right clicked, we should clear the folders filters and refresh.
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            _groups.ClearFilters(folder.Name);
            folder.ApplyLatestSorter();
            Cache.MarkForSortUpdate(folder);
        }

        // Early exit if the popup is not open.
        if (!inFilterEditor)
            return currentRightSide;

        // Otherwise process the popup logic.
        if (_filterEditor.DrawPopup($"{folder.ID}_filterEdit", folder, 150f * ImGuiHelpers.GlobalScale))
        {
            folder.ApplyLatestSorter();
            Cache.MarkForSortUpdate(folder);
        }

        return currentRightSide;
    }
    #endregion Folders

    #region Leaves
    // This override intentionally prevents the inner method from being called so that we can call our own inner method.
    protected override void DrawLeaf(IDynamicLeaf<Sundesmo> leaf, DynamicFlags flags, bool selected)
    {
        var cursorPos = ImGui.GetCursorPos();
        var size = new Vector2(CkGui.GetWindowContentRegionWidth() - cursorPos.X, ImUtf8.FrameHeight);
        var editing = _renaming == leaf;
        var bgCol = (!editing && selected) ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0;
        using (var _ = CkRaii.Child(Label + leaf.Name, size, bgCol, 5f))
            DrawLeafInner(leaf, _.InnerRegion, flags, editing);

        // Draw out the supporter icon after if needed.
        if (leaf.Data.UserData.Tier is not CkVanityTier.NoRole)
        {
            var Image = CosmeticService.GetSupporterInfo(leaf.Data.UserData);
            if (Image.SupporterWrap is { } wrap)
            {
                ImGui.SameLine(cursorPos.X);
                ImGui.SetCursorPosX(cursorPos.X - ImUtf8.FrameHeight - ImUtf8.ItemInnerSpacing.X);
                ImGui.Image(wrap.Handle, new Vector2(ImUtf8.FrameHeight));
                CkGui.AttachToolTip(Image.Tooltip);
            }
        }
    }

    // Inner leaf called by the above drawfunction, serving as a replacement for the default DrawLeafInner.
    private void DrawLeafInner(IDynamicLeaf<Sundesmo> leaf, Vector2 region, DynamicFlags flags, bool editing)
    {
        ImUtf8.SameLineInner();
        DrawLeafIcon(leaf.Data, flags);
        ImGui.SameLine();

        // Store current position, then draw the right side.
        var posX = ImGui.GetCursorPosX();
        var rightSide = DrawLeafButtons(leaf, flags);
        // Bounce back to the start position.
        ImGui.SameLine(posX);
        // If we are editing the name, draw that, otherwise, draw the name area.
        if (editing)
            DrawNameEditor(leaf, region.X);
        else
            DrawNameDisplay(leaf, new(rightSide - posX, region.Y), flags);
    }

    private void DrawLeafIcon(Sundesmo s, DynamicFlags flags)
    {
        var icon = s.IsRendered ? FAI.Eye : FAI.User;
        var color = s.IsOnline ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        ImGui.AlignTextToFramePadding();
        CkGui.IconText(icon, color);
        CkGui.AttachToolTip(TooltipText(s));
        if (!flags.HasAny(DynamicFlags.DragDropLeaves) && s.IsRendered && ImGui.IsItemClicked())
            Mediator.Publish(new TargetSundesmoMessage(s));
    }

    private float DrawLeafButtons(IDynamicLeaf<Sundesmo> leaf, DynamicFlags flags)
    {
        var interactionsSize = CkGui.IconButtonSize(FAI.ChevronRight);
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        var currentRightSide = windowEndX - interactionsSize.X;

        ImGui.SameLine(currentRightSide);
        if (!flags.HasAny(DynamicFlags.DragDrop))
        {
            ImGui.AlignTextToFramePadding();
            if (CkGui.IconButton(FAI.ChevronRight, inPopup: true))
                _stickyService.ForInteractions(leaf.Data);

            currentRightSide -= interactionsSize.X;
            ImGui.SameLine(currentRightSide);
        }

        ImGui.AlignTextToFramePadding();
        SundouleiaEx.DrawFavoriteStar(_favoritesConfig, leaf.Data.UserData.UID, true);
        return currentRightSide;
    }

    private void DrawNameEditor(IDynamicLeaf<Sundesmo> leaf, float width)
    {
        ImGui.SetNextItemWidth(width);
        if (ImGui.InputTextWithHint($"##{leaf.FullPath}-nick", "Give a nickname..", ref _nameEditStr, 45, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            _serverConfigs.SetNickname(leaf.Data.UserData.UID, _nameEditStr);
            _renaming = null;
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            _renaming = null;
        // Helper tooltip.
        CkGui.AttachToolTip("--COL--[ENTER]--COL-- To save" +
            "--NL----COL--[R-CLICK]--COL-- Cancel edits.", ImGuiColors.DalamudOrange);
    }

    private void DrawNameDisplay(IDynamicLeaf<Sundesmo> leaf, Vector2 region, DynamicFlags flags)
    {
        // For handling Interactions.
        var isDragDrop = flags.HasAny(DynamicFlags.DragDropLeaves);
        var pos = ImGui.GetCursorPos();
        ImGui.InvisibleButton($"{leaf.FullPath}-name-area", region);
        HandleInteraction(leaf, flags);

        // Then return to the start position and draw out the text.
        ImGui.SameLine(pos.X);

        // Push the monofont if we should show the UID, otherwise dont.
        DrawSundesmoName(leaf);
        CkGui.AttachToolTip(isDragDrop ? DragDropTooltip : NormalTooltip, ImGuiColors.DalamudOrange);
        if (isDragDrop)
            return;
        // Handle hover state.
        if (ImGui.IsItemHovered())
        {
            _newHoveredTextNode = leaf;

            // If we should show it, and it is not already shown, show it.
            if (!_profileShown && _lastHoverTime < DateTime.UtcNow && _config.Current.ShowProfiles)
            {
                _profileShown = true;
                Mediator.Publish(new OpenProfilePopout(leaf.Data.UserData));
            }
        }
    }

    // Override to handle the unique interactions that can be performed on leaves.
    protected override void HandleInteraction(IDynamicLeaf<Sundesmo> node, DynamicFlags flags)
    {
        if (ImGui.IsItemHovered())
            _newHoveredNode = node;
        // Handle Drag and Drop.
        if (flags.HasAny(DynamicFlags.DragDropLeaves))
        {
            AsDragDropSource(node);
            AsDragDropTarget(node);
        }
        else
        {
            // Additional, SundesmoLeaf-Spesific interaction handles.
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                // Performs a toggle of state.
                if (!_showingUID.Remove(node))
                    _showingUID.Add(node);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
                Mediator.Publish(new ProfileOpenMessage(node.Data.UserData));
            if (ImGui.GetIO().KeyShift && ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _renaming = node;
                _nameEditStr = node.Data.GetNickname() ?? string.Empty;
            }
        }

        // Handle Selection.
        if (flags.HasAny(DynamicFlags.SelectableLeaves) && ImGui.IsItemClicked())
            Selector.SelectItem(node, flags.HasFlag(DynamicFlags.MultiSelect), flags.HasFlag(DynamicFlags.RangeSelect));
    }
    #endregion Leaves

    #region Utility
    private void ToggleEditor(GroupFolder folder)
    {
        if (_folderInEditor == folder)
        {
            _folderInEditor = null;
            _nameEditTmp = string.Empty;
        }
        else
        {
            _folderInEditor = folder;
            _nameEditTmp = folder.Name;
        }
    }
    private void DrawSundesmoName(IDynamicLeaf<Sundesmo> s)
    {
        // Assume we use mono font initially.
        var useMono = true;
        // Get if we are set to show the UID over the name.
        var showUidOverName = _showingUID.Contains(s);
        // obtain the DisplayName (Player || Nick > Alias/UID).
        var dispName = string.Empty;
        // If we should be showing the uid, then set the display name to it.
        if (_showingUID.Contains(s))
        {
            // Mono Font is enabled.
            dispName = s.Data.UserData.AliasOrUID;
        }
        else
        {
            // Set it to the display name.
            dispName = s.Data.GetDisplayName();
            // Update mono to be disabled if the display name is not the alias/uid.
            useMono = s.Data.UserData.AliasOrUID.Equals(dispName, StringComparison.Ordinal);
        }

        // Display the name.
        using (ImRaii.PushFont(UiBuilder.MonoFont, useMono))
            CkGui.TextFrameAligned(dispName);
    }

    private string TooltipText(Sundesmo s)
    {
        var str = $"{s.GetNickAliasOrUid()} is ";
        if (s.IsRendered) str += $"visible ({s.PlayerName})--SEP--Click to target this player";
        else if (s.IsOnline) str += "online";
        else str += "offline";
        return str;
    }
    #endregion Utility
}
