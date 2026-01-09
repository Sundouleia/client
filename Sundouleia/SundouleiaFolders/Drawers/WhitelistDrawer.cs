using CkCommons;
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
using Sundouleia.Gui.MainWindow;
using Sundouleia.Localization;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;

namespace Sundouleia.DrawSystem;

public sealed class WhitelistDrawer : DynamicDrawer<Sundesmo>
{
    // Static tooltips for leaves.
    private static readonly string DragDropTooltip =
        "--COL--[L-CLICK & DRAG]--COL-- Drag-Drop this User to another Folder." +
        "--NL----COL--[CTRL + L-CLICK]--COL-- Single-Select this item for multi-select Drag-Drop" +
        "--NL----COL--[SHIFT + L-CLICK]--COL-- Select/Deselect all from last selected";
    private static readonly string NormalTooltip =
        "--COL--[L-CLICK]--COL-- Swap Between Name/Nick/Alias & UID." +
        "--NL----COL--[M-CLICK]--COL-- Open Profile" +
        "--NL----COL--[R-CLICK]--COL-- Edit Groups" +
        "--NL----COL--[SHIFT + R-CLICK]--COL-- Edit Nickname";

    private readonly SundouleiaMediator _mediator;
    private readonly MainConfig _config;
    private readonly FolderConfig _folderConfig;
    private readonly FavoritesConfig _favoritesConfig;
    private readonly NicksConfig _nicks;
    private readonly SundesmoManager _sundesmos;
    private readonly SidePanelService _sidePanel;
    private readonly WhitelistDrawSystem _drawSystem;

    // If the FilterRow is to be expanded.
    private bool _configExpanded = false;

    // private vars for renaming items.
    private HashSet<IDynamicNode<Sundesmo>> _showingUID = new(); // Nodes in here show UID.
    private IDynamicNode<Sundesmo>?         _renaming   = null;
    private string                          _nameEditStr= string.Empty; // temp nick text.

    // Popout Tracking.
    private IDynamicNode? _hoveredTextNode;     // From last frame.
    private IDynamicNode? _newHoveredTextNode;  // Tracked each frame.
    private bool          _profileShown = false;// If currently displaying a popout profile.
    private DateTime?     _lastHoverTime;       // time until we should show the profile.

    public WhitelistDrawer(SundouleiaMediator mediator,MainConfig config, FolderConfig folders, 
        FavoritesConfig favorites, NicksConfig nicks, SundesmoManager sundesmos,
        SidePanelService sidePanel, WhitelistDrawSystem ds)
        : base("##WhitelistDrawer", Svc.Logger.Logger, ds, new SundesmoCache(ds))
    {
        _mediator = mediator;
        _config = config;
        _folderConfig = folders;
        _favoritesConfig = favorites;
        _nicks = nicks;
        _sundesmos = sundesmos;
        _sidePanel = sidePanel;
        _drawSystem = ds;
    }

    #region Search
    protected override void DrawSearchBar(float width, int length)
    {
        var tmp = FilterCache.Filter;
        var buttonsWidth = CkGui.IconButtonSize(FAI.Cog).X + CkGui.IconTextButtonSize(FAI.Globe, "Basic");
        // Update the search bar if things change, like normal.
        if (FancySearchBar.Draw("Filter", width, ref tmp, "filter..", length, buttonsWidth, DrawButtons))
            FilterCache.Filter = tmp;

        // If the config is expanded, draw that.
        if (_configExpanded)
            DrawFilterConfig(width);
    }

    // Draws the grey line around the filtered content when expanded and stuff.
    protected override void PostSearchBar()
    {
        if (_configExpanded)
            ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(ImGuiCol.Button), 5f);
    }

    private void DrawButtons()
    {
        if (CkGui.IconTextButton(FAI.Globe, "Basic", null, true, _configExpanded))
        {
            _configExpanded = false;
            _folderConfig.Current.ViewingGroups = true;
            _folderConfig.Save();
        }
        CkGui.AttachToolTip("Switch to Groups View");

        ImGui.SameLine(0, 0);
        if (CkGui.IconButton(FAI.Cog, inPopup: !_configExpanded))
            _configExpanded = !_configExpanded;
        CkGui.AttachToolTip("Configure preferences for default folders.");
    }
    #endregion Search

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

    // Look further into luna for how to cache the runtime type to remove any nessisary casting.
    // AKA Creation of "CachedNodes" of defined types.
    // For now this will do.
    protected override void DrawFolderBannerInner(IDynamicFolder<Sundesmo> folder, Vector2 region, DynamicFlags flags)
        => DrawFolderInner((DefaultFolder)folder, region, flags);

    private void DrawFolderInner(DefaultFolder folder, Vector2 region, DynamicFlags flags)
    {
        var pos = ImGui.GetCursorPos();
        if (ImGui.InvisibleButton($"{Label}_node_{folder.ID}", region))
            HandleClick(folder, flags);
        HandleDetections(folder, flags);

        // Back to the start, then draw.
        ImGui.SameLine(pos.X);
        CkGui.FramedIconText(folder.IsOpen ? FAI.CaretDown : FAI.CaretRight);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        CkGui.IconText(folder.Icon, folder.IconColor);
        CkGui.ColorTextFrameAlignedInline(folder.Name, folder.NameColor);
        // Total Context.
        CkGui.ColorTextFrameAlignedInline(folder.BracketText, ImGuiColors.DalamudGrey2);
        CkGui.AttachToolTip(folder.BracketTooltip);
    }

    #region SundesmoLeaf
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
        DrawLeftSide(leaf.Data, flags);
        ImGui.SameLine();

        // Store current position, then draw the right side.
        var posX = ImGui.GetCursorPosX();
        var rightSide = DrawRightButtons(leaf, flags);
        // Bounce back to the start position.
        ImGui.SameLine(posX);
        // If we are editing the name, draw that, otherwise, draw the name area.
        if (editing)
            DrawNameEditor(leaf, region.X);
        else
            DrawNameDisplay(leaf, new(rightSide - posX, region.Y), flags);
    }

    private void DrawLeftSide(Sundesmo s, DynamicFlags flags)
    {
        var icon = s.IsRendered ? FAI.Eye : FAI.User;
        var color = s.IsOnline ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        ImGui.AlignTextToFramePadding();
        CkGui.IconText(icon, color);
        CkGui.AttachToolTip(TooltipText(s));
        if (!flags.HasAny(DynamicFlags.DragDropLeaves) && s.IsRendered && ImGui.IsItemClicked())
            _mediator.Publish(new TargetSundesmoMessage(s));
    }

    private float DrawRightButtons(IDynamicLeaf<Sundesmo> leaf, DynamicFlags flags)
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
        SundouleiaEx.DrawFavoriteStar(_favoritesConfig, leaf.Data.UserData.UID, true);
        return currentRightSide;
    }

    private void DrawNameEditor(IDynamicLeaf<Sundesmo> leaf, float width)
    {
        ImGui.SetNextItemWidth(width);
        if (ImGui.InputTextWithHint($"##{leaf.FullPath}-nick", "Give a nickname..", ref _nameEditStr, 45, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            _nicks.SetNickname(leaf.Data.UserData.UID, _nameEditStr);
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

        if (ImGui.InvisibleButton($"{leaf.FullPath}-name-area", region))
            HandleClick(leaf, flags);
        HandleDetections(leaf, flags);

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
                if (!_showingUID.Remove(node))
                    _showingUID.Add(node);
            }
            else if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
            {
                _mediator.Publish(new ProfileOpenMessage(node.Data.UserData));
            }
            else if (ImGui.GetIO().KeyShift && ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _renaming = node;
                _nameEditStr = node.Data.GetNickname() ?? string.Empty;
            }
            else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {

            }
        }
    }

    private string TooltipText(Sundesmo s)
    {
        var str = $"{s.GetNickAliasOrUid()} is ";
        if (s.IsRendered) str += $"visible ({s.PlayerName})--SEP--Click to target this player";
        else if (s.IsOnline) str += "online";
        else str += "offline";
        return str;
    }

    #endregion SundesmoLeaf

    #region Utility
    private void DrawFilterConfig(float width)
    {
        var bgCol = _configExpanded ? ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.FrameBg), 0.4f) : 0;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImUtf8.ItemSpacing.Y);
        using var s = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, ImGui.GetStyle().CellPadding with { Y = 0 });
        using var child = CkRaii.ChildPaddedW("BasicExpandedChild", width, CkStyle.ThreeRowHeight(), bgCol, 5f);
        using var _ = ImRaii.Table("BasicExpandedTable", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV);
        if (!_)
            return;

        ImGui.TableSetupColumn("Displays");
        ImGui.TableSetupColumn("Preferences");
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        var showVisible = _folderConfig.Current.VisibleFolder;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.ShowVisibleSeparateLabel, ref showVisible))
        {
            _folderConfig.Current.VisibleFolder = showVisible;
            _folderConfig.Save();
            Log.Information("Regenerating Basic Folders due to Visible Folder setting change.");
            // Update the folder structure to reflect this change.
            _drawSystem.UpdateVisibleFolderState(showVisible);
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.ShowVisibleSeparateTT);

        var showOffline = _folderConfig.Current.OfflineFolder;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.ShowOfflineSeparateLabel, ref showOffline))
        {
            _folderConfig.Current.OfflineFolder = showOffline;
            _folderConfig.Save();
            _drawSystem.UpdateOfflineFolderState(showOffline);
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.ShowOfflineSeparateTT);

        var favoritesFirst = _folderConfig.Current.FavoritesFirst;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.FavoritesFirstLabel, ref favoritesFirst))
        {
            _folderConfig.Current.FavoritesFirst = favoritesFirst;
            _folderConfig.Save();
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.FavoritesFirstTT);

        ImGui.TableNextColumn();

        var nickOverName = _folderConfig.Current.NickOverPlayerName;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.PreferNicknamesLabel, ref nickOverName))
        {
            _folderConfig.Current.NickOverPlayerName = nickOverName;
            _folderConfig.Save();
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.PreferNicknamesTT);

        var useFocusTarget = _folderConfig.Current.TargetWithFocus;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.FocusTargetLabel, ref useFocusTarget))
        {
            _folderConfig.Current.TargetWithFocus = useFocusTarget;
            _folderConfig.Save();
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.FocusTargetTT);
    }

    public bool DrawPopup(string popupId, GroupFolder folder, float width)
    {
        // Set next popup position, style, color, and display.
        ImGui.SetNextWindowPos(ImGui.GetItemRectMin() + new Vector2(ImGui.GetItemRectSize().X, 0));
        using var s = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f)
            .Push(ImGuiStyleVar.PopupRounding, 5f)
            .Push(ImGuiStyleVar.WindowPadding, ImGuiHelpers.ScaledVector2(4f, 1f));
        using var c = ImRaii.PushColor(ImGuiCol.Border, ImGuiColors.ParsedGold);
        using var popup = ImRaii.Popup(popupId, WFlags.NoMove | WFlags.NoResize | WFlags.NoCollapse | WFlags.NoScrollbar);
        if (!popup)
            return false;

        // Display the filter editor inside, after drawing the filter popup display.
        CkGui.InlineSpacingInner();
        CkGui.ColorTextFrameAligned("Filters:", ImGuiColors.ParsedGold);
        ImGui.Separator();
        return false;
    }

    #endregion Utility
}

