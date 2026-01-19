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

public class GroupsDrawer : DynamicDrawer<Sundesmo>
{
    private static readonly string Tooltip =
        "--COL--[L-CLICK]--COL-- Swap Between Name/Nick/Alias & UID." +
        "--NL----COL--[M-CLICK]--COL-- Open Profile" +
        "--NL----COL--[SHIFT + R-CLICK]--COL-- Edit Nickname";

    private static readonly string FolderDDTooltip =
        "--COL--[DRAG]:--COL-- Move the folder around, re-ordering it.--NL--" +
        "--COL--[L-CLICK]:--COL-- Add / Remove DragDrop selection.--NL--" +
        "--COL--[SHIFT + L-CLICK]: --COL-- Bulk Select/Deselect between last & current.";

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
    private FAIconCombo       _iconSelector; // For Internal Renames

    private WhitelistCache _cache => (WhitelistCache)FilterCache;

    // Popout Tracking.
    private IDynamicNode? _hoveredTextNode;     // From last frame.
    private IDynamicNode? _newHoveredTextNode;  // Tracked each frame.
    private bool _profileShown = false;         // If currently displaying a popout profile.
    private DateTime? _lastHoverTime;           // time until we should show the profile.

    public GroupsDrawer(ILogger<GroupsDrawer> logger, SundouleiaMediator mediator,
        MainConfig config, FolderConfig folders, FavoritesConfig favorites, NicksConfig nicks,
        GroupsManager groups, SundesmoManager sundesmos, SidePanelService sp, GroupsDrawSystem ds)
        : base("##GroupsDrawer", Svc.Logger.Logger, ds, new WhitelistCache(ds))
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

    public bool OrganizerMode => _cache.FilterConfigOpen;

    #region Search
    protected override void DrawSearchBar(float width, int length)
    {
        var tmp = FilterCache.Filter;
        var rWidth = CkGui.IconTextButtonSize(FAI.FolderPlus, "Group") + CkGui.IconTextButtonSize(FAI.FolderPlus, "Folder") + CkGui.IconButtonSize(FAI.LayerGroup).X;
        // Update the search bar if things change, like normal.
        if (FancySearchBar.Draw("Filter", width, ref tmp, "filter..", length, rWidth, DrawButtons))
            FilterCache.Filter = tmp;
    }

    // Draws the grey line around the filtered content when expanded and stuff.
    protected override void PostSearchBar()
    {
        if (_cache.FilterConfigOpen)
            ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(ImGuiCol.Button), 5f);
    }

    private void DrawButtons()
    {
        if (CkGui.IconTextButton(FAI.FolderPlus, "Group", null, true))
            _sidePanel.ForNewGroup((GroupsDrawSystem)DrawSystem);
        CkGui.AttachToolTip("Create a new Group");

        ImGui.SameLine(0, 0);
        if (CkGui.IconTextButton(FAI.FolderPlus, "Folder", null, true))
            _sidePanel.ForNewFolderGroup((GroupsDrawSystem)DrawSystem);
        CkGui.AttachToolTip("Create a new Folder to catagorize your groups.");

        ImGui.SameLine(0, 0);
        if (CkGui.IconButton(FAI.LayerGroup, inPopup: !_cache.FilterConfigOpen))
        {
            _cache.FilterConfigOpen = !_cache.FilterConfigOpen;
            if (!_cache.FilterConfigOpen)
                Selector.ClearSelected();
        }
        CkGui.AttachToolTip("Organizer Mode");
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

    #region FolderGroups
    // Overrides are nessisary to draw either the Renaming node or the actual node.
    protected override void DrawFolderGroupBanner(IDynamicFolderGroup<Sundesmo> fg, DynamicFlags flags, bool selected)
    {
        var width = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        if (_cache.RenamingNode == fg)
            FolderGroupBannerEditor(fg, width, flags);
        else
            FolderGroupBanner(fg, width, flags, selected);
    }

    // Normal FolderGroup
    private void FolderGroupBanner(IDynamicFolderGroup<Sundesmo> fg, float width, DynamicFlags flags, bool selected)
    {
        var bgCol = selected ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : fg.BgColor;
        using var _ = CkRaii.FramedChildPaddedW($"dfg_{Label}_{fg.ID}", width, ImUtf8.FrameHeight, bgCol, fg.BorderColor, 5f, 1f);
        DrawFolderGroupBanner(fg, _.InnerRegion, flags);
        HandleContext(fg);
    }

    // Editing FolderGroup
    private void FolderGroupBannerEditor(IDynamicFolderGroup<Sundesmo> fg, float width, DynamicFlags flags)
    {
        using var _ = CkRaii.FramedChildPaddedW($"dfg_{Label}_{fg.ID}", width, ImUtf8.FrameHeight, fg.BgColor, fg.BorderColor, 5f, 1f);        

        CkGui.FramedIconText(fg.IsOpen ? fg.IconOpen : fg.Icon);
        ImUtf8.SameLineInner();
        // Renamer (See if we can port this to the other types).
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        var nameTmp = fg.Name;
        ImGui.InputTextWithHint("##GroupsRename", "Set Name..", ref nameTmp, 40);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            if (!string.IsNullOrWhiteSpace(nameTmp))
            {
                DrawSystem.Rename(fg, nameTmp);
                FilterCache.MarkForReload(fg.Parent);
            }
            // Clear renaming node regardless.
            _cache.RenamingNode = null;
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            _cache.RenamingNode = null;
        // Helper tooltip.
        CkGui.AttachToolTip("--COL--[ENTER]--COL-- To save" +
            "--NL----COL--[R-CLICK]--COL-- Cancel edits.", ImGuiColors.DalamudOrange);
    }

    protected override void DrawContextMenu(IDynamicFolderGroup<Sundesmo> node)
    {
        if (ImGui.MenuItem("Rename Folder"))
        {
            // Initiate the renaming field for the node.
            _cache.RenamingNode = node;
            _cache.NameEditStr = node.Name;
        }

        // Maybe add a "Set Parent" here or something.
        //
        //

        // Then draw the rest of the options.
        base.DrawContextMenu(node);
    }
    #endregion FolderGroups

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
        CkGui.AttachToolTip(FolderDDTooltip, IsDragging || !isDragDrop, ImGuiColors.DalamudOrange);
        
        // Back to the start, then draw.
        ImGui.SameLine(pos.X);
        if (isDragDrop) CkGui.FramedIconText(FAI.GripLines, f.BorderColor);
        else CkGui.FramedIconText(f.IsOpen ? FAI.CaretDown : FAI.CaretRight);
        
        ImGui.SameLine();
        CkGui.IconTextAligned(f.Icon, f.IconColor);
        CkGui.ColorTextFrameAlignedInline(f.Name, f.NameColor);
        // Total Context.
        CkGui.ColorTextFrameAlignedInline($"[{f.Online}]", ImGuiColors.DalamudGrey2);
        CkGui.AttachToolTip($"{f.Online} online\n{f.TotalChildren} total");
        // Draw right options.
        DrawFolderOptions(f, _cache.GroupInEditor == f);

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
            // Update the icon within the group manager.
            _groups.SetIcon(f.Group, _iconSelector.Current, f.IconColor);
            f.ApplyLatestStyle();
        }
        CkGui.AttachToolTip("Edit the icon for your group.");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - rWidth);
        var nameTmp = f.Name;
        ImGui.InputTextWithHint("##GroupNameEdit", "Set Name..", ref nameTmp, 40);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            if (!string.IsNullOrWhiteSpace(nameTmp) && _groups.TryRename(f.Group, nameTmp))
            {
                DrawSystem.Rename(f, nameTmp);
                FilterCache.MarkForReload(f.Parent);
            }
            // Clear renaming node regardless.
            _cache.RenamingNode = null;
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            _cache.RenamingNode = null;
        CkGui.AttachToolTip("--COL--[ENTER]--COL-- To save" +
            "--NL----COL--[R-CLICK]--COL-- Cancel edits.", ImGuiColors.DalamudOrange);

        ImGui.SameLine();
        DrawFolderOptions(f, _cache.GroupInEditor == f);
    }

    private float DrawFolderOptions(GroupFolder folder, bool inEditor)
    {
        var inFilterEditor = ImGui.IsPopupOpen($"{folder.ID}_filterEdit");
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        
        var currentRightSide = windowEndX - CkGui.IconButtonSize(FAI.Edit).X;
        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        if (CkGui.IconButton(FAI.Edit, inPopup: !inEditor))
            _sidePanel.ForGroupEditor(folder, _cache, this);
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
        if (ImGui.MenuItem("Rename Group"))
        {
            _cache.RenamingNode = node;
            _cache.NameEditStr = node.Name;
        }

        // Maybe something here about changing parent... idk.
        //
        //

        // Draw out the rest of the options.
        base.DrawContextMenu(node);
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
            DrawNameEditor(leaf, rightSide - posX);
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

        // Handle Drag and Drop.
        if (flags.HasAny(DynamicFlags.DragDropLeaves))
        {
            AsDragDropSource(node);
            AsDragDropTarget(node);
        }
        else
        {
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

        if (_folders.Current.Groups.Count is not 0)
        {
            if (ImGui.BeginMenu("Add to Group.."))
            {
                ImGui.Text("I can be a checkbox list of groups or a multi-selectable list.");
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Remove from Groups.."))
            {
                ImGui.Text("I can be a checkbox list of groups or a multi-selectable list.");
                ImGui.EndMenu();
            }
        }
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

    #region DragDrop Helpers
    protected override void PostDragSourceText(IDynamicNode<Sundesmo> entity)
    {
        if (entity is not IDynamicCollection<Sundesmo> || !IsDragging || _hoveredNode == entity)
            return;

        CkGui.Separator(uint.MaxValue);
        var shiftHeld = ImGui.GetIO().KeyShift;
        string message = DragDrop switch
        {
            // CASE 1: Both FolderGroups and Folders
            { OnlyCollections: true, OnlyFolders: false, OnlyFolderGroups: false } =>
                $"Dropping collections into [{entity.Name}]",

            // CASE 2: Target is FolderGroup, Moves items were only Folders
            { OnlyFolders: true } when entity is IDynamicFolderGroup<Sundesmo> fg =>
                $"Dropping groups into: {fg.Name}",

            // CASE 3: Target is FolderGroup, we are moving only FolderGroups
            { OnlyFolderGroups: true } when entity is IDynamicFolderGroup<Sundesmo> fg =>
                shiftHeld
                    ? $"Merging folders into: {fg.Name}"
                    : $"Dropping folders into: {fg.Parent.Name}",

            // CASE 3: Target is Folder, and moving only Folders.
            { OnlyFolders: true } when entity is IDynamicFolder<Sundesmo> f =>
                shiftHeld
                    ? $"Merging all pairs from selected into: {f.Name}"
                    : $"Dropping groups into: {f.Parent.Name}",

            // CASE 4: Target is Folder, and moving only FolderGroups.
            { OnlyFolders: false } when entity is IDynamicFolder<Sundesmo> f =>
                $"Dropping groups into: {f.Parent.Name}",

            _ => string.Empty
        };
        CkGui.ColorTextFrameAligned(message, ImGuiColors.DalamudYellow);
    }

    protected override void PerformDrop(IDynamicNode<Sundesmo> target)
    {
        // Get if shifting
        bool shifting = ImGui.GetIO().KeyShift;
        var groups = DragDrop.Nodes.OfType<DynamicFolderGroup<Sundesmo>>();
        var folders = DragDrop.Nodes.OfType<GroupFolder>();

        // If the target is a folder
        if (target is GroupFolder folderTarget)
        {
            // Merge in folders if only moving folders and shifting.
            if (DragDrop.OnlyFolders && shifting)
            {
                foreach (var f in folders)
                {
                    _groups.MergeGroups(f.Group, folderTarget.Group);
                    DrawSystem.Delete(f);
                }
                DrawSystem.UpdateFolder(folderTarget);
            }
            // Mark the new target as the parent of the target folder, and migrate everything into there.
            else
            {
                // Move all of these into the target folder's parent.
                var toMove = DragDrop.Nodes.OfType<IDynamicCollection<Sundesmo>>();
                DrawSystem.BulkMove(toMove, folderTarget.Parent, folderTarget);
            }
        }
        // For FolderGroups, handle things slightly differently.
        else if (target is DynamicFolderGroup<Sundesmo> folderGroupTarget)
        {
            // If we were holding shift and only had FolderGroups, merge them.
            if (DragDrop.OnlyFolderGroups && shifting)
            {
                foreach (var g in groups)
                    DrawSystem.Merge(g, folderGroupTarget);
            }
            else
            {
                var toMove = shifting ? groups.SelectMany(g => g.GetChildren()) : groups;
                // Concat this with all of our folders.
                toMove = toMove.Concat(folders);
                // Perform a bulk move to the new location.
                DrawSystem.BulkMove(toMove, folderGroupTarget);
            }
        }
    }
    #endregion DragDrop Helpers
}
