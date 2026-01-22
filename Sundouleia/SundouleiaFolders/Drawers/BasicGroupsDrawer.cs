using CkCommons.DrawSystem;
using CkCommons.DrawSystem.Selector;
using CkCommons.Gui;
using CkCommons.Raii;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.CustomCombos;
using Sundouleia.Gui.MainWindow;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using TerraFX.Interop.Windows;

namespace Sundouleia.DrawSystem;

// Because this is a seperate drawer, we need to ensure that the FilterValue
// remains up to date with whitelist drawer.
public class BasicGroupsDrawer : DynamicDrawer<Sundesmo>
{
    private static readonly string Tooltip =
        "--COL--[L-CLICK]--COL-- Swap Between Name/Nick/Alias & UID." +
        "--NL----COL--[M-CLICK]--COL-- Open Profile" +
        "--NL----COL--[SHIFT + R-CLICK]--COL-- Edit Nickname";

    private readonly SundouleiaMediator _mediator;
    private readonly MainConfig _config;
    private readonly FolderConfig _folders;
    private readonly FavoritesConfig _favorites;
    private readonly NicksConfig _nicks;
    private readonly GroupsManager _groups;
    private readonly SundesmoManager _sundesmos;
    private readonly SidePanelService _sidePanel;

    // Widgets
    private GroupFilterEditor _filterEditor;
    private FAIconCombo       _iconSelector;

    private BasicGroupCache _cache => (BasicGroupCache)FilterCache;

    // Popout Tracking.
    private IDynamicNode? _hoveredTextNode;     // From last frame.
    private IDynamicNode? _newHoveredTextNode;  // Tracked each frame.
    private bool _profileShown = false;         // If currently displaying a popout profile.
    private DateTime? _lastHoverTime;           // time until we should show the profile.

    public BasicGroupsDrawer(ILogger<GroupsDrawer> logger, SundouleiaMediator mediator,
        MainConfig config, FolderConfig folders, FavoritesConfig favorites, NicksConfig nicks,
        GroupsManager groups, SundesmoManager sundesmos, SidePanelService sp, GroupsDrawSystem ds)
        : base("##BasicGroupsDrawer", Svc.Logger.Logger, ds, new BasicGroupCache(ds))
    {
        _mediator = mediator;
        _config = config;
        _folders = folders;
        _favorites = favorites;
        _nicks = nicks;
        _groups = groups;
        _sundesmos = sundesmos;
        _sidePanel = sp;
        // Init the filter editor.
        _filterEditor = new GroupFilterEditor(groups);
        _iconSelector = new FAIconCombo(logger);
    }
    public void UpdateFilter(string newStr)
        => FilterCache.Filter = newStr;

    public void DrawFoldersOnly(float width, DynamicFlags flags = DynamicFlags.None)
    {
        FilterCache.UpdateCache();
        DrawFolderGroupChildren(FilterCache.RootCache, ImUtf8.FrameHeight * .65f, ImUtf8.FrameHeight + ImUtf8.ItemInnerSpacing.X, flags);
        PostDraw();
    }

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
                _mediator.Publish(new CloseProfilePopout());
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
    // Will always only ever be the groups.
    protected override void DrawFolderBanner(IDynamicFolder<Sundesmo> f, DynamicFlags flags, bool selected)
    {
        var width = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        var isRenaming = _cache.RenamingNode == f;
        var bgCol = (!isRenaming && selected) ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : f.BgColor;
        using var _ = CkRaii.FramedChildPaddedW($"df_{Label}_{f.ID}", width, ImUtf8.FrameHeight, bgCol, f.BorderColor, 5f, 1f);

        var rWidth = CkGui.IconButtonSize(FAI.Edit).X + CkGui.IconButtonSize(FAI.Filter).X;
        if (_cache.RenamingNode == f)
            FolderBannerEditor((GroupFolder)f, rWidth, flags);
        else
            FolderBannerInner((GroupFolder)f, width, rWidth, flags, selected);
    }

    // Normal Folder
    private void FolderBannerInner(GroupFolder f, float width, float rWidth, DynamicFlags flags, bool selected)
    {
        var isDragDrop = flags.HasAny(DynamicFlags.DragDropFolders);
        var pos = ImGui.GetCursorPos();
        if (ImGui.InvisibleButton($"{Label}_node_{f.ID}", new(width - rWidth, ImUtf8.FrameHeight)))
            HandleLeftClick(f, flags);
        HandleDetections(f, flags);
        
        // Back to the start, then draw.
        ImGui.SameLine(pos.X);
        CkGui.FramedIconText(f.IsOpen ? FAI.CaretDown : FAI.CaretRight);
        
        ImGui.SameLine();
        CkGui.IconTextAligned(f.Icon, f.IconColor);
        CkGui.ColorTextFrameAlignedInline(f.Name, f.NameColor);
        // Total Context.
        CkGui.ColorTextFrameAlignedInline($"[{f.Online}]", ImGuiColors.DalamudGrey2);
        CkGui.AttachToolTip($"{f.Online} online\n{f.TotalChildren} total");
        // Draw right options.
        if (!isDragDrop)
            DrawFolderOptions(f, _cache.GroupInEditor == f);
        // Ensure context menus remain handled.
        HandleContext(f);
    }

    // Editing FolderGroup
    private void FolderBannerEditor(GroupFolder f, float rWidth, DynamicFlags flags)
    {
        CkGui.FramedIconText(f.IsOpen ? FAI.CaretDown : FAI.CaretRight);
        ImGui.SameLine();
        // Display the editor version of this groups icon, allowing it to be changed.
        if (_iconSelector.Draw("IconSel", f.Icon, 10))
        {
            f.Group.Icon = _iconSelector.Current;
            _groups.Save();
            f.ApplyLatestStyle();
        }
        CkGui.AttachToolTip("Edit the icon for your group.");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - rWidth);
        var nameTmp = f.Name;
        ImGui.InputTextWithHint("##GroupNameEdit", "Set Name..", ref nameTmp, 30);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            AddPostDrawLogic(() =>
            {
                // Clear renaming node regardless.
                _cache.RenamingNode = null;                
                // Do nothing for empty names.
                if (string.IsNullOrWhiteSpace(nameTmp))
                    return;
                // If this is caught, then another item with the same name exists, and we should not process it.
                try
                {
                    DrawSystem.Rename(f, nameTmp);
                }
                catch (Bagagwa)
                {
                    Log.Warning($"Another Group or Folder already has the name '{nameTmp}'");
                }
                // Was successful, so rename the group, and mark the filtercache for reload.
                _groups.TryRename(f.Group, nameTmp);
                FilterCache.MarkForReload(f, true);
            });
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            _cache.RenamingNode = null;
        CkGui.AttachToolTip("--COL--[ENTER]--COL-- To save" +
            "--NL----COL--[R-CLICK]--COL-- Cancel edits.", ImGuiColors.DalamudOrange);

        ImGui.SameLine();
        var endX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        endX -= CkGui.IconButtonSize(FAI.Edit).X;
        ImGui.SameLine(endX);
        CkGui.IconButton(FAI.Edit, disabled: true, inPopup: true);

        endX -= CkGui.IconButtonSize(FAI.Filter).X;
        ImGui.SameLine(endX);
        CkGui.IconButton(FAI.Filter, id: f.ID.ToString(), disabled: true, inPopup: true);
    }

    private float DrawFolderOptions(GroupFolder folder, bool inEditor)
    {
        var inFilterEditor = ImGui.IsPopupOpen($"{folder.ID}_filterEdit");
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        
        var currentRightSide = windowEndX - CkGui.IconButtonSize(FAI.Edit).X;
        ImGui.SameLine(currentRightSide);
        if (CkGui.IconButton(FAI.Edit, inPopup: !inEditor))
            _sidePanel.ForGroupEditor(folder, _cache);
        CkGui.AttachToolTip("Edit Group");

        currentRightSide -= CkGui.IconButtonSize(FAI.Filter).X;
        ImGui.SameLine(currentRightSide);
        if (CkGui.IconButton(FAI.Filter, null, folder.ID.ToString(), false, !inFilterEditor))
            ImGui.OpenPopup($"{folder.ID}_filterEdit");
        CkGui.AttachToolTip("Change sort order.--SEP----COL--[R-CLICK]--COL-- Clear Filters", ImGuiColors.DalamudOrange);

        // If right clicked, we should clear the folders filters and refresh.
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            _groups.ClearFilters(folder.Group);
            folder.ApplyLatestSorter();
            FilterCache.MarkForSortUpdate(folder);
        }

        // Early exit if the popup is not open.
        if (!inFilterEditor)
            return currentRightSide;

        // Otherwise process the popup logic.
        if (_filterEditor.DrawPopup($"{folder.ID}_filterEdit", folder, 150f * ImGuiHelpers.GlobalScale))
        {
            folder.ApplyLatestSorter();
            FilterCache.MarkForSortUpdate(folder);
        }

        return currentRightSide;
    }

    protected override void DrawContextMenu(IDynamicFolder<Sundesmo> node)
    {
        if (node is not GroupFolder groupFolder)
        {   
            base.DrawContextMenu(node);
            return;
        }

        if (ImGui.MenuItem("Rename Group"))
        {
            _cache.RenamingNode = node;
            _cache.NameEditStr = node.Name;
        }

        if (groupFolder.Group.InBasicView && ImGui.MenuItem("Remove from Basic View"))
        {
            AddPostDrawLogic(() =>
            {
                groupFolder.Group.InBasicView = false;
                _folders.Save();
                _cache.MarkCacheDirty();
            });
        }

        // Add / Remove from Basic view button.  
        if (ImGui.MenuItem("Delete Group", enabled: ImGui.GetIO().KeyShift))
        {
            if (node is not GroupFolder)
                return;
            AddPostDrawLogic(() =>
            {
                _groups.DeleteGroup(((GroupFolder)node).Group);
                DrawSystem.Delete(node);
            });
        }
        CkGui.AttachToolTip("Perminantly Delete this Group." +
            "--SEP----COL--Must hold SHIFT to delete.--COL--", ImGuiColors.DalamudYellow);
    }

    protected override void HandleDetections(IDynamicCollection<Sundesmo> node, DynamicFlags flags)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.RectOnly))
            _newHoveredNode = node;

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup(node.FullPath);
    }
    #endregion Folders

    #region Leaves
    // This override intentionally prevents the inner method from being called so that we can call our own inner method.
    protected override void DrawLeaf(IDynamicLeaf<Sundesmo> leaf, DynamicFlags flags, bool selected)
    {
        var cursorPos = ImGui.GetCursorPos();
        var size = new Vector2(CkGui.GetWindowContentRegionWidth() - cursorPos.X, ImUtf8.FrameHeight);
        var editing = _cache.RenamingNode == leaf;
        var bgCol = (!editing && selected) ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0;
        using (var _ = CkRaii.Child(Label + leaf.Name, size, bgCol, 5f))
        {
            DrawLeafInner(leaf, _.InnerRegion, flags, editing);
            HandleContext(leaf);
        }
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
            DrawNameEditor(leaf, rightSide - posX);
        else
            DrawNameDisplay(leaf, new(rightSide - posX, region.Y), flags);
    }

    private void DrawLeafIcon(Sundesmo s, DynamicFlags flags)
    {
        var icon = s.IsRendered ? FAI.Eye : FAI.User;
        var color = s.IsOnline ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        CkGui.IconTextAligned(icon, color);
        CkGui.AttachToolTip(TooltipText(s));
        if (!flags.HasAny(DynamicFlags.DragDropLeaves) && s.IsRendered && ImGui.IsItemClicked())
            _mediator.Publish(new TargetSundesmoMessage(s));

        string TooltipText(Sundesmo s)
        {
            var str = $"{s.GetNickAliasOrUid()} is ";
            if (s.IsRendered) str += $"visible ({s.PlayerName})--SEP--Click to target this player";
            else if (s.IsOnline) str += "online";
            else str += "offline";
            return str;
        }
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
                _sidePanel.ForInteractions(leaf.Data);

            currentRightSide -= interactionsSize.X;
            ImGui.SameLine(currentRightSide);
        }

        ImGui.AlignTextToFramePadding();
        SundouleiaEx.DrawFavoriteStar(_favorites, leaf.Data.UserData.UID, true);
        return currentRightSide;
    }

    private void DrawNameEditor(IDynamicLeaf<Sundesmo> leaf, float width)
    {
        ImGui.SetNextItemWidth(width);
        if (ImGui.InputTextWithHint($"##{leaf.FullPath}-nick", "Give a nickname..", ref _cache.NameEditStr, 45, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            _nicks.SetNickname(leaf.Data.UserData.UID, _cache.NameEditStr);
            _cache.RenamingNode = null;
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            _cache.RenamingNode = null;
        // Helper tooltip.
        CkGui.AttachToolTip("--COL--[ENTER]--COL-- To save" +
            "--NL----COL--[R-CLICK]--COL-- Cancel edits.", ImGuiColors.DalamudOrange);
    }

    private void DrawNameDisplay(IDynamicLeaf<Sundesmo> leaf, Vector2 region, DynamicFlags flags)
    {
        // For handling Interactions.
        var isDragDrop = flags.HasAny(DynamicFlags.DragDropLeaves);
        var pos = ImGui.GetCursorPos();
        if (ImGui.InvisibleButton($"{leaf.FullPath}-name-area", region))
            HandleLeftClick(leaf, flags);
        HandleDetections(leaf, flags);

        // Then return to the start position and draw out the text.
        ImGui.SameLine(pos.X);

        // Push the monofont if we should show the UID, otherwise dont.
        DrawSundesmoName(leaf);
        CkGui.AttachToolTip(Tooltip, ImGuiColors.DalamudOrange);
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
                _mediator.Publish(new OpenProfilePopout(leaf.Data.UserData));
            }
        }
    }

    protected override void HandleDetections(IDynamicLeaf<Sundesmo> node, DynamicFlags flags)
    {
        if (ImGui.IsItemHovered())
            _newHoveredNode = node;

        // Additional, SundesmoLeaf-Specific interaction handles.
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            if (!_cache.ShowingUID.Remove(node))
                _cache.ShowingUID.Add(node);
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
        {
            _mediator.Publish(new ProfileOpenMessage(node.Data.UserData));
        }
        if (ImGui.GetIO().KeyShift && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            _cache.RenamingNode = node;
            _cache.NameEditStr = node.Data.GetNickname() ?? string.Empty;
        }
    }

    /// <summary>
    ///     The Leaf Context Menu.
    /// </summary>
    protected override void DrawContextMenu(IDynamicLeaf<Sundesmo> leaf)
    {
        if (ImGui.MenuItem("Edit Nickname"))
        {
            _cache.RenamingNode = leaf;
            _cache.NameEditStr = leaf.Data.GetNickname() ?? string.Empty;
        }
        if (ImGui.MenuItem("Open Profile"))
        {
            _mediator.Publish(new ProfileOpenMessage(leaf.Data.UserData));
        }

        if (leaf.Parent is GroupFolder groupFolder)
        {
            if (ImGui.MenuItem("Remove from Group"))
            {

                _groups.UnlinkFromGroup(leaf.Data.UserData.UID, groupFolder.Group);
                FilterCache.MarkForReload(groupFolder);
            }
        }
        CkGui.AttachToolTip("Removes this Sundesmo from the Group.");
    }

    private void DrawSundesmoName(IDynamicLeaf<Sundesmo> s)
    {
        // Assume we use mono font initially.
        var useMono = true;
        // Get if we are set to show the UID over the name.
        var showUidOverName = _cache.ShowingUID.Contains(s);
        // obtain the DisplayName (Player || Nick > Alias/UID).
        var dispName = string.Empty;
        // If we should be showing the uid, then set the display name to it.
        if (_cache.ShowingUID.Contains(s))
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
    #endregion Leaves
}
