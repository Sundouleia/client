using CkCommons;
using CkCommons.Gui;
using CkCommons.Helpers;
using CkCommons.Raii;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.DrawSystem.Selector;
using Sundouleia.Localization;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using TerraFX.Interop.Windows;

namespace Sundouleia.DrawSystem;

public class WhitelistDrawer : DynamicDrawer<Sundesmo>
{
    // Static tooltips for leaves.
    private static readonly string DragDropTooltip =
        "--COL--[L-CLICK & DRAG]--COL-- Drag-Drop this User to another Folder." +
        "--NL----COL--[CTRL + L-CLICK]--COL-- Single-Select this item for multi-select Drag-Drop" +
        "--NL----COL--[SHIFT + L-CLICK]--COL-- Select/Deselect all users between current and last selection";
    private static readonly string NormalTooltip =
        "--COL--[L-CLICK]--COL-- Swap Between Name/Nick/Alias & UID." +
        "--NL----COL--[M-CLICK]--COL-- Open Profile" +
        "--NL----COL--[R-CLICK]--COL-- Edit Nickname";


    private readonly SundouleiaMediator _mediator;
    private readonly MainConfig _config;
    private readonly FolderConfig _folderConfig;
    private readonly FavoritesConfig _favoritesConfig;
    // maybe make seperate config for nicks idk.
    private readonly ServerConfigManager _serverConfigs;
    private readonly SundesmoManager _sundesmos;
    private readonly WhitelistDrawSystem _drawSystem;

    // If the FilterRow is to be expanded.
    private bool _configExpanded = false;

    // private vars for renaming items.
    private HashSet<IDynamicNode<Sundesmo>> _showingUID = new(); // Nodes in here show UID.
    private IDynamicNode<Sundesmo>?         _renaming   = null;
    private string    _nameEditStr = string.Empty; // temp nick text.
    private bool      _profileShown = false;
    private DateTime? _lastHoverTime;

    public WhitelistDrawer(ILogger<WhitelistDrawer> logger, SundouleiaMediator mediator,
        MainConfig config, FolderConfig folderConfig, FavoritesConfig favoritesConfig,
        ServerConfigManager serverConfigs, SundesmoManager sundesmos, WhitelistDrawSystem ds) 
        : base("##WhitelistDrawer", logger, ds)
    {
        _mediator = mediator;
        _config = config;
        _folderConfig = folderConfig;
        _favoritesConfig = favoritesConfig;
        _serverConfigs = serverConfigs;
        _sundesmos = sundesmos;
        _drawSystem = ds;
        // We can handle interaction stuff via customizable buttons later that we will figure out as things go on.
    }

    // SearchBar functionality will be revised later, as all filters contain a
    // config of some kind, so the below logic will be duplicated over all drawers.
    #region Search
    protected override void DrawSearchBar(float width, int length)
    {
        string tmp = Filter;
        var buttonsWidth = CkGui.IconButtonSize(FAI.Cog).X + CkGui.IconTextButtonSize(FAI.Globe, "Basic");
        // Update the search bar if things change, like normal.
        if (FancySearchBar.Draw("Filter", width, ref tmp, string.Empty, length, buttonsWidth, DrawButtons))
        {
            if (string.Equals(tmp, Filter, StringComparison.Ordinal))
                Filter = tmp; // Auto-Marks as dirty.
        }

        // If the config is expanded, draw that.
        if (_configExpanded)
            DrawFilterConfig(width);
    }

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
            _mediator.Publish(new SwapWhitelistDDS());
        }
        CkGui.AttachToolTip("Switch to Groups View");

        ImGui.SameLine(0, 0);
        if (CkGui.IconButton(FAI.Cog, inPopup: !_configExpanded))
            _configExpanded = !_configExpanded;
        CkGui.AttachToolTip("Configure preferences for default folders.");
    }
    #endregion Search

    // Look further into luna for how to cache the runtime type to remove any nessisary casting.
    // AKA Creation of "CachedNodes" of defined types.
    // For now this will do.
    protected override void DrawFolderBannerInner(IDynamicFolder<Sundesmo> folder, Vector2 region, DynamicFlags flags)
        => DrawFolderInner((WhitelistFolder)folder, region, flags);

    private void DrawFolderInner(WhitelistFolder folder, Vector2 region, DynamicFlags flags)
    {
        var pos = ImGui.GetCursorPos();
        ImGui.InvisibleButton($"{Label}_node_{folder.ID}", region);
        HandleInteraction(folder, flags);

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
        var size = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImUtf8.FrameHeight);
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
        // Icon display and tooltips.
        var icon = leaf.Data.IsRendered ? FAI.Eye : FAI.User;
        var col = leaf.Data.IsOnline ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        ImGui.AlignTextToFramePadding();
        CkGui.IconText(icon, col);
        CkGui.AttachToolTip($"{leaf.Data.GetNickAliasOrUid()} is " +
            (leaf.Data.IsRendered ? $"visible ({leaf.Data.PlayerName})--SEP--Click to target this player"
                          : leaf.Data.IsOnline ? "online" : "offline"));
        // Target action if not for dragdrop and rendered.
        if (!flags.HasAny(DynamicFlags.DragDropLeaves) && leaf.Data.IsRendered && ImGui.IsItemClicked())
            _mediator.Publish(new TargetSundesmoMessage(leaf.Data));
        
        // Proceed to next space.
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
        var pos = ImGui.GetCursorPos();
        var pressed = ImGui.InvisibleButton($"{leaf.FullPath}-interactable", region);
        HandleInteraction(leaf, flags);

        // Then return to the start position and draw out the text.
        ImGui.SameLine(pos.X);

        // Push the monofont if we should show the UID, otherwise dont.
        var showUid = _showingUID.Contains(leaf);
        using (ImRaii.PushFont(UiBuilder.MonoFont, showUid))
            CkGui.TextFrameAligned(showUid ? leaf.Data.UserData.UID : leaf.Data.GetDrawEntityName());
        // Based on if the leaf is a drag-drop leaf, handle post-text draw differently.
        if (flags.HasAny(DynamicFlags.DragDropLeaves))
        {
            CkGui.AttachToolTip(DragDropTooltip, ImGuiColors.DalamudOrange);
        }
        else
        {
            CkGui.AttachToolTip(NormalTooltip, ImGuiColors.DalamudOrange);
            // If not a drag-drop item, and hovered, monitor profile update.
            if (ImGui.IsItemHovered())
            {
                // If the profile is not shown, start the timer.
                if (!_profileShown && _lastHoverTime is null)
                    _lastHoverTime = DateTime.UtcNow.AddSeconds(_config.Current.ProfileDelay);
                // If the time has elapsed and we are not showing the profile, show it.
                if (!_profileShown && _lastHoverTime < DateTime.UtcNow && _config.Current.ShowProfiles)
                {
                    _profileShown = true;
                    _mediator.Publish(new OpenProfilePopout(leaf.Data.UserData));
                }
            }
            else
            {
                if (_profileShown)
                {
                    // Reset the hover time and close the popup.
                    _profileShown = false;
                    _lastHoverTime = null;
                    _mediator.Publish(new CloseProfilePopout());
                }
            }
        }
    }

    protected override void HandleInteraction(IDynamicLeaf<Sundesmo> node, DynamicFlags flags)
    {
        if (ImGui.IsItemHovered())
            _hoveredNode = node;
        // Handle Selection.
        if (flags.HasAny(DynamicFlags.SelectableLeaves) && ImGui.IsItemClicked())
            SelectItem(node, flags.HasFlag(DynamicFlags.MultiSelect), flags.HasFlag(DynamicFlags.RangeSelect));
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
            if (KeyMonitor.ShiftPressed() && ImGui.IsItemClicked(ImGuiMouseButton.Right))
                _renaming = node;
        }
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
            Log.LogInformation("Regenerating Basic Folders due to Visible Folder setting change.");
            // Update the folder structure to reflect this change.
            _drawSystem.VisibleFolderStateUpdate(showVisible);
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.ShowVisibleSeparateTT);

        var showOffline = _folderConfig.Current.OfflineFolder;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.ShowOfflineSeparateLabel, ref showOffline))
        {
            _folderConfig.Current.OfflineFolder = showOffline;
            _folderConfig.Save();
            _drawSystem.OfflineFolderStateUpdate(showOffline);
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
    #endregion Utility
}

