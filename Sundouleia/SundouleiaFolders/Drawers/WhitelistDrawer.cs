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
    private static readonly string Tooltip =
        "--COL--[L-CLICK]--COL-- Swap Between Name/Nick/Alias & UID." +
        "--NL----COL--[M-CLICK]--COL-- Open Profile" +
        "--NL----COL--[R-CLICK]--COL-- Open Context Menu";

    private readonly SundouleiaMediator _mediator;
    private readonly MainConfig _config;
    private readonly FolderConfig _folders;
    private readonly FavoritesConfig _favorites;
    private readonly NicksConfig _nicks;
    private readonly SundesmoManager _sundesmos;
    private readonly SidePanelService _sidePanel;
    private readonly WhitelistDrawSystem _drawSystem;

    private WhitelistCache _cache => (WhitelistCache)FilterCache;

    // Popout Tracking.
    private IDynamicNode? _hoveredTextNode;     // From last frame.
    private IDynamicNode? _newHoveredTextNode;  // Tracked each frame.
    private bool          _profileShown = false;// If currently displaying a popout profile.
    private DateTime?     _lastHoverTime;       // time until we should show the profile.

    public WhitelistDrawer(SundouleiaMediator mediator,MainConfig config, FolderConfig folders, 
        FavoritesConfig favorites, NicksConfig nicks, SundesmoManager sundesmos,
        SidePanelService sidePanel, WhitelistDrawSystem ds)
        : base("##WhitelistDrawer", Svc.Logger.Logger, ds, new WhitelistCache(ds))
    {
        _mediator = mediator;
        _config = config;
        _folders = folders;
        _favorites = favorites;
        _nicks = nicks;
        _sundesmos = sundesmos;
        _sidePanel = sidePanel;
        _drawSystem = ds;
    }

    #region Search
    protected override void DrawSearchBar(float width, int length)
    {
        var tmp = FilterCache.Filter;
        // Update the search bar if things change, like normal.
        if (FancySearchBar.Draw("Filter", width, ref tmp, "filter..", length, CkGui.IconTextButtonSize(FAI.Cog, "Settings"), DrawButtons))
            FilterCache.Filter = tmp;

        // If the config is expanded, draw that.
        if (_cache.FilterConfigOpen)
            DrawFilterConfig(width);

        void DrawButtons()
        {
            if (CkGui.IconTextButton(FAI.Cog, "Settings", isInPopup: !_cache.FilterConfigOpen))
                _cache.FilterConfigOpen = !_cache.FilterConfigOpen;
            CkGui.AttachToolTip("Configure preferences for default folders.");
        }
    }

    // Draws the grey line around the filtered content when expanded and stuff.
    protected override void PostSearchBar()
    {
        if (_cache.FilterConfigOpen)
            ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(ImGuiCol.Button), 5f);
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
                Log.Information($"Current TextNode was {_hoveredTextNode?.Name ?? "UNK"}, new is {_newHoveredTextNode?.Name ?? "UNK"}");
                _lastHoverTime = null;
                _profileShown = false;
                _mediator.Publish(new CloseProfilePopout());
            }
        }

        if (_hoveredNode != _newHoveredNode)
        {
            // Ignore the update if it was in a popup.
            if (_newHoveredNode is null && _popupNodes.Contains(_hoveredNode))
                _newHoveredNode = _hoveredNode;
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
            HandleLeftClick(folder, flags);
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

        string TooltipText(Sundesmo s)
        {
            var str = $"{s.GetNickAliasOrUid()} is ";
            if (s.IsRendered) str += $"visible ({s.PlayerName})--SEP--Click to target this player";
            else if (s.IsOnline) str += "online";
            else str += "offline";
            return str;
        }
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
        var pos = ImGui.GetCursorPos();

        if (ImGui.InvisibleButton($"{leaf.FullPath}-name-area", region))
            HandleLeftClick(leaf, flags);
        HandleDetections(leaf, flags);

        // Then return to the start position and draw out the text.
        ImGui.SameLine(pos.X);

        // Push the monofont if we should show the UID, otherwise dont.
        DrawSundesmoName(leaf);
        CkGui.AttachToolTip(Tooltip, ImGuiColors.DalamudOrange);
        // Handle hover state.
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.RectOnly | ImGuiHoveredFlags.NoNavOverride))
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
    #endregion SundesmoLeaf

    #region Interaction
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
        else if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
            _mediator.Publish(new ProfileOpenMessage(node.Data.UserData));

        // Handle Context Menus. (Maybe make a flag later. Would save on some drawtime.)
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup(node.FullPath);
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

    #endregion Interaction

    #region Utility
    private void DrawFilterConfig(float width)
    {
        var bgCol = _cache.FilterConfigOpen ? ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.FrameBg), 0.4f) : 0;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImUtf8.ItemSpacing.Y);
        using var s = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, ImGui.GetStyle().CellPadding with { Y = 0 });
        using var child = CkRaii.ChildPaddedW("BasicExpandedChild", width, CkStyle.ThreeRowHeight(), bgCol, 5f);
        
        using var _ = ImRaii.Table("BasicExpandedTable", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV);
        if (!_) return;

        ImGui.TableSetupColumn("Displays");
        ImGui.TableSetupColumn("Preferences");
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        var showVisible = _folders.Current.VisibleFolder;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.ShowVisibleSeparateLabel, ref showVisible))
        {
            _folders.Current.VisibleFolder = showVisible;
            _folders.Save();
            Log.Information("Regenerating Basic Folders due to Visible Folder setting change.");
            // Update the folder structure to reflect this change.
            _drawSystem.UpdateVisibleFolderState(showVisible);
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.ShowVisibleSeparateTT);

        var showOffline = _folders.Current.OfflineFolder;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.ShowOfflineSeparateLabel, ref showOffline))
        {
            _folders.Current.OfflineFolder = showOffline;
            _folders.Save();
            _drawSystem.UpdateOfflineFolderState(showOffline);
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.ShowOfflineSeparateTT);

        var favoritesFirst = _folders.Current.FavoritesFirst;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.FavoritesFirstLabel, ref favoritesFirst))
        {
            _folders.Current.FavoritesFirst = favoritesFirst;
            _folders.Save();
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.FavoritesFirstTT);

        ImGui.TableNextColumn();

        var nickOverName = _folders.Current.NickOverPlayerName;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.PreferNicknamesLabel, ref nickOverName))
        {
            _folders.Current.NickOverPlayerName = nickOverName;
            _folders.Save();
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.PreferNicknamesTT);

        var useFocusTarget = _folders.Current.TargetWithFocus;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.FocusTargetLabel, ref useFocusTarget))
        {
            _folders.Current.TargetWithFocus = useFocusTarget;
            _folders.Save();
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

