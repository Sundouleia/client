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
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;

namespace Sundouleia.DrawSystem;

public sealed class WhitelistDrawer : DynamicDrawer<Sundesmo>
{
    // Static tooltips for leaves.
    private static readonly string Tooltip =
        "--COL--[L-CLICK]--COL-- Swap Between Name/Nick/Alias & UID." +
        "--NL----COL--[M-CLICK]--COL-- Open Profile" +
        "--NL----COL--[R-CLICK]--COL-- Open Context Menu";

    private readonly SundouleiaMediator _mediator;
    private readonly MainHub _hub;
    private readonly MainConfig _config;
    private readonly FolderConfig _folders;
    private readonly FavoritesConfig _favorites;
    private readonly NicksConfig _nicks;
    private readonly GroupsManager _groups;
    private readonly SundesmoManager _sundesmos;
    private readonly SidePanelService _sidePanel;
    private readonly WhitelistDrawSystem _drawSystem;

    private WhitelistCache _cache => (WhitelistCache)FilterCache;

    // Popout Tracking.
    private IDynamicNode? _hoveredTextNode;     // From last frame.
    private IDynamicNode? _newHoveredTextNode;  // Tracked each frame.
    private bool          _profileShown = false;// If currently displaying a popout profile.
    private DateTime?     _lastHoverTime;       // time until we should show the profile.

    public WhitelistDrawer(SundouleiaMediator mediator, MainHub hub, MainConfig config,
        FolderConfig folders, FavoritesConfig favorites, NicksConfig nicks, GroupsManager groups, 
        SundesmoManager sundesmos, SidePanelService sidePanel, WhitelistDrawSystem ds)
        : base("##WhitelistDrawer", Svc.Logger.Logger, ds, new WhitelistCache(ds))
    {
        _mediator = mediator;
        _hub = hub;
        _config = config;
        _folders = folders;
        _favorites = favorites;
        _nicks = nicks;
        _groups = groups;
        _sundesmos = sundesmos;
        _sidePanel = sidePanel;
        _drawSystem = ds;
    }

    public string SearchFilter => FilterCache.Filter;

    public void DrawFoldersOnly(float width, DynamicFlags flags = DynamicFlags.None)
    {
        HandleMainContext();
        FilterCache.UpdateCache();
        DrawFolderGroupChildren(FilterCache.RootCache, ImUtf8.FrameHeight * .65f, ImUtf8.FrameHeight + ImUtf8.ItemInnerSpacing.X, flags);
        PostDraw();
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
        CkGui.IconTextAligned(folder.Icon, folder.IconColor);
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
        var endX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        
        if (!flags.HasAny(DynamicFlags.DragDrop))
        {
            endX -= interactionsSize.X;
            ImGui.SameLine(endX);
            ImGui.AlignTextToFramePadding();
            if (CkGui.IconButton(FAI.ChevronRight, inPopup: true))
                _sidePanel.ForInteractions(leaf.Data);
            CkGui.AttachToolTip("Toggle Interactions View");
        }

        // Now the favorites
        endX -= ImUtf8.FrameHeight;
        ImGui.SameLine(endX);
        SundouleiaEx.DrawFavoriteStar(_favorites, leaf.Data.UserData.UID, true);

        // If they are temporary, draw the interaction area for this as well.
        if (leaf.Data.IsTemporary)
        {
            endX -= ImUtf8.FrameHeight;
            ImGui.SameLine(endX);
            if (SundouleiaEx.DrawTempUserLink(leaf.Data, UiService.DisableUI))
                ConvertUserToPermanent(leaf);
        }
        // Return the remaining width
        return endX;
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
        {
            if (ImGui.GetIO().KeyShift)
            {
                _cache.RenamingNode = node;
                _cache.NameEditStr = node.Data.GetNickname() ?? string.Empty;
            }
            else
            {
                ImGui.OpenPopup(node.FullPath);
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

        if (_groups.Groups.Count is 0)
            return;

        // Obtain the list of groups (Maybe find a better way to handle this later, but for now this works)
        var groups = _groups.GroupsList;
        var inGroups = groups.Where(g => g.LinkedUids.Contains(leaf.Data.UserData.UID)).ToList();
        var notInGroups = groups.Except(inGroups).ToList();

        if (notInGroups.Count is not 0)
        {
            if (ImGui.BeginMenu("Add to Group"))
            {
                foreach (var g in notInGroups)
                {
                    var ret = ImGui.Selectable($"##{g.Label}", false);
                    ImGui.SameLine(0, ImUtf8.ItemInnerSpacing.X);
                    CkGui.IconTextAligned(g.Icon, g.IconColor);
                    CkGui.ColorTextFrameAlignedInline(g.Label, g.LabelColor);
                    if (ret)
                    {
                        _groups.LinkToGroup(leaf.Data.UserData.UID, g);
                        _mediator.Publish(new FolderUpdateGroup(g.Label));
                    }
                }
                ImGui.EndMenu();
            }
        }

        if (inGroups.Count is not 0)
        {
            if (ImGui.BeginMenu("Remove from Group"))
            {
                foreach (var g in inGroups)
                {
                    var ret = ImGui.Selectable($"##{g.Label}", false);
                    ImGui.SameLine(0, ImUtf8.ItemInnerSpacing.X);
                    CkGui.IconTextAligned(g.Icon, g.IconColor);
                    CkGui.ColorTextFrameAlignedInline(g.Label, g.LabelColor);
                    if (ret)
                    {
                        _groups.UnlinkFromGroup(leaf.Data.UserData.UID, g);
                        _mediator.Publish(new FolderUpdateGroup(g.Label));
                    }
                }
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

        var useFocusTarget = _folders.Current.TargetWithFocus;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.FocusTargetLabel, ref useFocusTarget))
        {
            _folders.Current.TargetWithFocus = useFocusTarget;
            _folders.Save();
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.FocusTargetTT);

        ImGui.TableNextColumn();
        var nickOverName = _folders.Current.NickOverPlayerName;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.PreferNicknamesLabel, ref nickOverName))
        {
            _folders.Current.NickOverPlayerName = nickOverName;
            _folders.Save();
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.PreferNicknamesTT);

        var prioFavorites = _folders.Current.PrioritizeFavorites;
        var prioTemps = _folders.Current.PrioritizeTemps;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.PrioritizeFavoritesLabel, ref prioFavorites))
        {
            _folders.Current.PrioritizeFavorites = prioFavorites;
            _folders.Save();
            _drawSystem.UpdateFilters();
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.PrioritizeFavoritesTT);

        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.PrioritizeTempLabel, ref prioTemps))
        {
            _folders.Current.PrioritizeTemps = prioTemps;
            _folders.Save();
            _drawSystem.UpdateFilters();
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.PrioritizeTempTT);
    }

    private void ConvertUserToPermanent(IDynamicLeaf<Sundesmo> leaf)
    {
        UiService.SetUITask(async () =>
        {
            var res = await _hub.UserPersistPair(new(leaf.Data.UserData));
            if (res.ErrorCode is not SundouleiaApiEc.Success)
                Log.Warning($"Failed to make pairing with {leaf.Data.GetDisplayName()} permanent. Reason: {res.ErrorCode}");
            else
            {
                Log.Warning($"Successfully made pairing with {leaf.Data.GetDisplayName()} permanent.");
                leaf.Data.MarkAsPermanent();
            }
        });
    }

    #endregion Utility


}

