using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.DrawSystem.Selector;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using System.Runtime.CompilerServices;

namespace Sundouleia.DrawSystem;

public class GroupsDrawer : DynamicDrawer<Sundesmo>
{
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

    private readonly SundouleiaMediator _mediator;
    private readonly MainConfig _config;
    private readonly FolderConfig _folderConfig;
    private readonly FavoritesConfig _favoritesConfig;
    // maybe make seperate config for nicks idk.
    private readonly ServerConfigManager _serverConfigs;
    private readonly GroupsManager _groups;
    private readonly SundesmoManager _sundesmos;

    // If we have the group editor window open.
    private bool _editorShown = false;

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

    public GroupsDrawer(ILogger<GroupsDrawer> logger, SundouleiaMediator mediator,
        MainConfig config, FolderConfig folderConfig, FavoritesConfig favorites, 
        ServerConfigManager serverConfig, GroupsManager groups, 
        SundesmoManager sundesmos, GroupsDrawSystem ds)
        : base("##GroupsDrawer", logger, ds)
    {
        _mediator = mediator;
        _config = config;
        _folderConfig = folderConfig;
        _favoritesConfig = favorites;
        _serverConfigs = serverConfig;
        _groups = groups;
        _sundesmos = sundesmos;
    }

    #region Search
    protected override void DrawSearchBar(float width, int length)
    {
        var tmp = Filter;
        var buttonsWidth = CkGui.IconButtonSize(FAI.Wrench).X + CkGui.IconTextButtonSize(FAI.PeopleGroup, "Groups");
        // Update the search bar if things change, like normal.
        if (FancySearchBar.Draw("Filter", width, ref tmp, "filter..", length, buttonsWidth, DrawButtons))
        {
            if (!string.Equals(tmp, Filter, StringComparison.Ordinal))
                Filter = tmp; // Auto-Marks as dirty.
        }
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
            _editorShown = !_editorShown;
        CkGui.AttachToolTip("Edit, Add, Rearrange, or Remove Groups.");
    }
    #endregion Search

    // Override to check for a match based on the current leaf's data.
    protected override bool IsVisible(IDynamicNode<Sundesmo> node)
    {
        // Save on extra work by just returning true if nothing is in the filter.
        if (Filter.Length is 0)
            return true;

        // Check leaves for the sundesmo display name with all possible displays.
        if (node is DynamicLeaf<Sundesmo> leaf)
        {
            return leaf.Data.UserData.AliasOrUID.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || (leaf.Data.GetNickname()?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false)
                || (leaf.Data.PlayerName?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false);
        }
        // Otherwise just check the base.
        return base.IsVisible(node);
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
        // Encapsulate within a group.
        using (ImRaii.Group())
        {
            DrawFolderHeaderRow(gf, width, flags, selected);
            DrawFolderEditor(gf, width, flags, editing);
        }
        // If editing, border everything.
        if (editing)
            ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(ImGuiCol.Button), 5f);
    }

    // Group folders are very special in the way that they function.
    // Unlike other draw folders, groups are fully customizable even after initial creation.
    // Must allow filtering and configuration, similar to how the whitelist folder config functions.
    private void DrawFolderHeaderRow(GroupFolder folder, float width, DynamicFlags flags, bool selected)
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

    private void DrawFolderEditor(GroupFolder folder, float width, DynamicFlags flags, bool editing)
    {
        if (!editing)
            return;
        var bgCol = ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.FrameBg), 0.4f);
        using var child = CkRaii.ChildPaddedW("BasicExpandedChild", width, CkStyle.TwoRowHeight(), bgCol, 5f);

        // Options stuff would go here.
        CkGui.ColorText("Placeholder Text", ImGuiColors.DalamudYellow);
    }

    private void DrawFolderOptions(GroupFolder folder)
    {
        var isFolderInEditor = _folderInEditor == folder;
        var config = CkGui.IconButtonSize(FAI.Cog);
        var filter = CkGui.IconButtonSize(FAI.Filter);
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        var currentRightSide = windowEndX - config.X;

        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        if (CkGui.IconButton(FAI.Cog, inPopup: !isFolderInEditor))
            _folderInEditor = isFolderInEditor ? null : folder;
        CkGui.AttachToolTip("Edit Group");

        currentRightSide -= filter.X;
        ImGui.SameLine(currentRightSide);
        if (CkGui.IconButton(FAI.Filter, inPopup: true))
        {
            // Did something like a combo but it wouldnt really let us change the state
            // of the icon or anything.
            // If possible to work around see, but otherwise just stick with what we know works.
        }
        CkGui.AttachToolTip("Change filters or sort order.");
    }

    private bool DrawFilterEditor(GroupFolder folder)
    {
        // Button Combo for editing the filters of a folder. is Vector2.FrameHeight in size.
        using var combo = ImUtf8.Combo($"##{folder.ID}_filterEdit", ""u8, CFlags.NoPreview | CFlags.PopupAlignLeft | CFlags.HeightRegular);

        // Clears all applied filters
        var cleared = ImGui.IsItemClicked(ImGuiMouseButton.Right);

        // If the combo is not open, just return if we right clicked it or not.
        if (!combo)
            return cleared;

        // We need to draw the row of rearrangable checkbox selectables.
        // Penumbra does this in its mod editor, refere3nce that.



        // Draw the filter display here when the combo is open of the checkboxes and selectables.

        return cleared;
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
            _mediator.Publish(new TargetSundesmoMessage(s));
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
                _mediator.Publish(new ToggleSundesmoInteractionUI(leaf.Data, ToggleType.Toggle));

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
                _mediator.Publish(new OpenProfilePopout(leaf.Data.UserData));
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
                _mediator.Publish(new ProfileOpenMessage(node.Data.UserData));
            if (ImGui.GetIO().KeyShift && ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _renaming = node;
                _nameEditStr = node.Data.GetNickname() ?? string.Empty;
            }
        }

        // Handle Selection.
        if (flags.HasAny(DynamicFlags.SelectableLeaves) && ImGui.IsItemClicked())
            SelectItem(node, flags.HasFlag(DynamicFlags.MultiSelect), flags.HasFlag(DynamicFlags.RangeSelect));
    }
    #endregion Leaves

    #region Utility
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

public class GroupFolderFilterEditor
{
    // For Tracking.
    private GroupFolder _dragDropGroup;
    private ISortMethod<DynamicLeaf<Sundesmo>> _dragDropStep;
    private List<ISortMethod<DynamicLeaf<Sundesmo>>> _dragDropSteps;
    private readonly HashSet<ISortMethod<DynamicLeaf<Sundesmo>>> _selectedSteps = new();




    private void DrawFilterOption(GroupFolder group, ISortMethod<DynamicLeaf<Sundesmo>> step)
    {

    }

    private void DrawOption


}


