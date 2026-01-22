using CkCommons;
using CkCommons.DrawSystem;
using CkCommons.Gui;
using CkCommons.Gui.Utility;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using OtterGui.Text;
using Sundouleia.CustomCombos;
using Sundouleia.CustomCombos.Editor;
using Sundouleia.DrawSystem;
using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Gui.MainWindow;

/// <summary>
///     Used for SidePanel display when creating new groups, or editing existing ones. <para />
///     Potentially revise this, if we ever want style edits to be accessible via context menus. 
/// </summary>
public class SidePanelGroups
{
    private readonly ILogger<SidePanelInteractions> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly GroupsManager _groups;
    private readonly SundesmoManager _sundesmos;

    // Maybe a pair combo here or something, idk.
    private bool _previewOpen = false;
    private DDSFolderGroupCombo _folderGroupCombo;
    private FAIconCombo _icons;
    private DataCenterCombo _dataCenters;
    private WorldCombo _worlds;
    private TerritoryCombo _territories;
    private SundesmoForGroupCombo _usersCombo;

    public SidePanelGroups(ILogger<SidePanelInteractions> logger, SundouleiaMediator mediator,
        FavoritesConfig favorites, GroupsManager groups,  SundesmoManager sundesmos,
        GroupsDrawSystem groupsDDS)
    {
        _logger = logger;
        _mediator = mediator;
        _groups = groups;
        _sundesmos = sundesmos;

        _folderGroupCombo = new DDSFolderGroupCombo(logger, groupsDDS);
        _icons = new FAIconCombo(logger);
        _dataCenters = new DataCenterCombo(logger);
        _worlds = new WorldCombo(logger);
        _territories = new TerritoryCombo(logger);
        _usersCombo = new SundesmoForGroupCombo(logger, mediator, favorites, () => [
            ..sundesmos.DirectPairs
                .OrderByDescending(p => favorites.SundesmoUids.Contains(p.UserData.UID))
                .ThenByDescending(u => u.IsRendered)
                .ThenByDescending(u => u.IsOnline)
                .ThenBy(pair => pair.GetDisplayName())
        ]);
    }

    public void DrawFolderPreview(SundesmoGroup g)
    {
        using var s = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One);
        var width = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        using (var _ = CkRaii.FramedChildPaddedW(g.Label, width, ImUtf8.FrameHeight, 0, g.BorderColor, 5f, 1f))
        {
            CkGui.FramedIconText(_previewOpen ? FAI.CaretDown : FAI.CaretRight);
            ImGui.SameLine();
            CkGui.IconTextAligned(g.Icon, g.IconColor);
            CkGui.ColorTextFrameAlignedInline(g.Label, g.LabelColor);
        }
        if (ImGui.IsItemClicked())
            _previewOpen = !_previewOpen;
        CkGui.AttachToolTip("Click to toggle open and closed states");

        if (!_previewOpen)
            return;
        // Bump for child nodes.  
        using var ident = ImRaii.PushIndent(ImUtf8.FrameHeight);

        var folderMin = ImGui.GetItemRectMin();
        var folderMax = ImGui.GetItemRectMax();
        var wdl = ImGui.GetWindowDrawList();
        wdl.ChannelsSplit(2);
        wdl.ChannelsSetCurrent(1);

        // Should make this have variable heights later.
        var size = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImUtf8.FrameHeight);
        for (var i = 0; i < 2; i++)
        {
            using (CkRaii.Child($"DummyLeaf{i}", size, 0, 5f))
            {
                ImUtf8.SameLineInner();
                CkGui.IconTextAligned(FAI.User, ImGuiColors.ParsedGreen);
                ImGui.SameLine();
                using (ImRaii.PushFont(UiBuilder.MonoFont))
                    CkGui.TextFrameAligned($"DUMMY-UID-{i+1}");
            }
        }

        wdl.ChannelsSetCurrent(0); // Background.
        var gradientTL = new Vector2(folderMin.X, folderMax.Y);
        var gradientTR = new Vector2(folderMax.X, ImGui.GetItemRectMax().Y);
        wdl.AddRectFilledMultiColor(gradientTL, gradientTR, ColorHelpers.Fade(g.GradientColor, .9f), ColorHelpers.Fade(g.GradientColor, .9f), 0, 0);
        wdl.ChannelsMerge();
    }

    public void DrawFolderCreator(NewFolderGroupCache cache, float width)
    {
        // get the width of the cell.
        CkGui.FramedIconText(FAI.FolderTree);
        ImUtf8.SameLineInner();
        var cellWidth = ImGui.GetContentRegionAvail().X;
        if (_folderGroupCombo.Draw(cache.ParentNode, cellWidth, 1f))
            cache.ParentNode = _folderGroupCombo.Current;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            _folderGroupCombo.ClearSelected();
            cache.ParentNode = null;
        }

        CkGui.FramedIconText(FAI.Tag);
        ImUtf8.SameLineInner();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        var tmpName = cache.NewFolderName;
        if (ImGui.InputTextWithHint("##NewGroupLabel", "Folder Name..", ref tmpName, 40))
            cache.NewFolderName = tmpName;
        CkGui.AttachToolTip("Set the name of the Folder.");
    }

    public void DrawCreator(NewGroupCache cache, float width)
    {
        var drawHeight = ImGui.GetContentRegionAvail().Y - ImUtf8.FrameHeight - ImUtf8.ItemSpacing.Y * 2;
        using var _ = CkRaii.Child("GroupCreator", new(width, drawHeight), wFlags: WFlags.NoScrollbar);

        CkGui.FramedIconText(FAI.FolderTree);
        ImUtf8.SameLineInner();
        var cellWidth = ImGui.GetContentRegionAvail().X;
        if (_folderGroupCombo.Draw(cache.ParentNode, cellWidth, 1f))
            cache.ParentNode = _folderGroupCombo.Current;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            _folderGroupCombo.ClearSelected();
            cache.ParentNode = null;
        }

        // The Icon
        CkGui.FramedIconText(FAI.Tag);
        ImUtf8.SameLineInner();
        if (_icons.Draw("IconSelector", cache.NewGroup.Icon, 10))
            cache.NewGroup.Icon = _icons.Current;
        CkGui.AttachToolTip("Edit the icon for your group.");

        ImUtf8.SameLineInner();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        var tmpName = cache.NewGroup.Label;
        if (ImGui.InputTextWithHint("##NewGroupLabel", "Group Name..", ref tmpName, 40))
            cache.NewGroup.Label = tmpName;
        CkGui.AttachToolTip("Set the name of the Group.");

        ImGui.Separator();
        CkGui.FontText("Stylization", UiFontService.Default150Percent);
        DrawIconStyle(cache.NewGroup, width, newCol => cache.NewGroup.IconColor = newCol);

        ImUtf8.SameLineInner();
        DrawLabelStyle(cache.NewGroup, width, newCol => cache.NewGroup.LabelColor = newCol);
        
        ImUtf8.SameLineInner();
        DrawBorderStyle(cache.NewGroup, width, newCol => cache.NewGroup.BorderColor = newCol);
        
        ImUtf8.SameLineInner();
        DrawGradientStyle(cache.NewGroup, width, newCol => cache.NewGroup.GradientColor = newCol);

        ImGui.Separator();
        CkGui.FontText("Preferences", UiFontService.Default150Percent);
        DrawOfflineFlag(cache.NewGroup, width, newState => cache.NewGroup.ShowOffline = newState);
        DrawLocationFlag(cache.NewGroup, width);

        ImGui.Separator();
        DrawFolderPreview(cache.NewGroup);
    }

    public void DrawGroupEditor(GroupEditorCache cache, float width)
    {
        cache.DrawTabBar(width);

        // Children to encapsulate the rest of the region, if scrollable.
        using var _ = CkRaii.Child("GroupEditor", new(width, ImGui.GetContentRegionAvail().Y), wFlags: WFlags.NoScrollbar);

        if (cache.CurrentTab is GroupEditorTabs.SelectedTab.Attributes)
            GroupEditorMain(cache, width);
        else
            GroupEditorUsers(cache, width);
    }

    private void GroupEditorMain(GroupEditorCache cache, float width)
    {
        CkGui.FramedIconText(FAI.FolderTree);
        ImUtf8.SameLineInner();
        var cellWidth = ImGui.GetContentRegionAvail().X;
        if (_folderGroupCombo.Draw(cache.ParentNode, cellWidth, 1f))
        {
            _logger.LogInformation($"Setting new Parent node to: {_folderGroupCombo.Current?.Name ?? "UNK"}");
            cache.ChangeParentNode(_folderGroupCombo.Current);
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            _folderGroupCombo.ClearSelected();
            cache.ChangeParentNode(null);
        }

        // The Icon
        CkGui.FramedIconText(FAI.Tag);
        ImUtf8.SameLineInner();
        if (_icons.Draw("IconSelector", cache.GroupInEditor.Icon, 10))
        {
            cache.GroupInEditor.Icon = _icons.Current;
            _groups.Save();
            cache.UpdateStyle();
        }
        CkGui.AttachToolTip("Edit the icon for your group.");

        ImUtf8.SameLineInner();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        var tmpName = cache.GroupInEditor.Label;
        ImGui.InputTextWithHint("##NewGroupLabel", "Group Name..", ref tmpName, 30);
        if (ImGui.IsItemDeactivatedAfterEdit())
            cache.TryRenameNode(_groups, tmpName);
        CkGui.AttachToolTip("Set the name of the Group.");

        ImGui.Separator();
        CkGui.FontText("Stylization", UiFontService.Default150Percent);
        DrawIconStyle(cache.GroupInEditor, width, newCol =>
        {
            cache.GroupInEditor.IconColor = newCol;
            _groups.Save();
            cache.UpdateStyle();
        });

        ImUtf8.SameLineInner();
        DrawLabelStyle(cache.GroupInEditor, width, newCol =>
        {
            cache.GroupInEditor.LabelColor = newCol;
            _groups.Save();
            cache.UpdateStyle();
        });

        ImUtf8.SameLineInner();
        DrawBorderStyle(cache.GroupInEditor, width, newCol =>
        {
            cache.GroupInEditor.BorderColor = newCol;
            _groups.Save();
            cache.UpdateStyle();
        });

        ImUtf8.SameLineInner();
        DrawGradientStyle(cache.GroupInEditor, width, newCol =>
        {
            cache.GroupInEditor.GradientColor = newCol;
            _groups.Save();
            cache.UpdateStyle();
        });

        ImGui.Separator();
        CkGui.FontText("Preferences", UiFontService.Default150Percent);
        DrawOfflineFlag(cache.GroupInEditor, width, newState =>
        {
            cache.GroupInEditor.ShowOffline = newState;
            _groups.Save();
            cache.UpdateState();
        });
        DrawLocationFlag(cache.GroupInEditor, width, () =>
        {
            _groups.Save();
            _groups.LinkByMatchingLocation(); // Refresh folders.
        });

        ImGui.Separator();
        DrawFolderPreview(cache.GroupInEditor);
    }

    private void GroupEditorUsers(GroupEditorCache cache, float width)
    {
        if (CkGui.IconTextButton(FAI.Plus, "Users In Area"))
        {
            _groups.LinkToGroup(_sundesmos.GetVisible().Select(s => s.UID), cache.GroupInEditor);
            _mediator.Publish(new FolderUpdateGroup(cache.GroupInEditor.Label));
        }
        CkGui.AttachToolTip("Add all visible sundesmos nearby.");

        ImUtf8.SameLineInner();
        if (_usersCombo.Draw(cache.GroupInEditor, ImGui.GetContentRegionAvail().X))
        {
            if (_usersCombo.Current is not { } selected)
                return;
            
            if (cache.GroupInEditor.LinkedUids.Contains(selected.UserData.UID))
                return;

            _logger.LogInformation($"Adding {selected.GetDisplayName()} to Group {cache.GroupInEditor.Label}");
            _groups.LinkToGroup(selected.UserData.UID, cache.GroupInEditor);
            _mediator.Publish(new FolderUpdateGroup(cache.GroupInEditor.Label));
        }

        using var t = ImRaii.Table("##UsersTable", 1, ImGuiTableFlags.BordersOuter | ImGuiTableFlags.RowBg, ImGui.GetContentRegionAvail());
        if (!t) return;

        ImGui.TableSetupColumn("UserListing");
        ImGui.TableNextColumn();
        var widthInner = ImGui.GetContentRegionAvail().X;

        foreach (var leaf in cache.GroupChildren.ToList())
        {
            ImGui.TableNextColumn();
            var icon = leaf.Data.IsRendered ? FAI.Eye : FAI.User;
            var color = leaf.Data.IsOnline ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
            CkGui.IconTextAligned(icon, color);
            ImGui.SameLine();
            DrawSundesmoName(leaf);
            CkGui.AttachToolTip($"UID: {leaf.Data.UserData.UID}");

            ImGui.SameLine(widthInner - CkGui.IconButtonSize(FAI.Minus).X);
            if (CkGui.IconButton(FAI.Minus, id: leaf.Data.UserData.UID, inPopup: true))
            {
                _groups.UnlinkFromGroup(leaf.Data.UserData.UID, cache.GroupInEditor);
                _mediator.Publish(new FolderUpdateGroup(cache.GroupInEditor.Label));
            }
            CkGui.AttachToolTip($"Remove from Selection");
            ImGui.TableNextRow();
        }
        
        void DrawSundesmoName(IDynamicLeaf<Sundesmo> s)
        {
            // Assume we use mono font initially.
            var useMono = true;
            // obtain the DisplayName (Player || Nick > Alias/UID).
            var dispName = string.Empty;
            // If we should be showing the uid, then set the display name to it.
            if (cache.ShownUIDs.Contains(s))
                dispName = s.Data.UserData.AliasOrUID;
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
    }

    private void DrawIconStyle(SundesmoGroup group, float width, Action<uint> onChange)
    {
        var iconCol = ImGui.ColorConvertU32ToFloat4(group.IconColor);
        if (ImGui.ColorEdit4("Icon", ref iconCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
            onChange?.Invoke(ImGui.ColorConvertFloat4ToU32(iconCol));
        CkGui.AttachToolTip("Change the color of the folder icon.");
    }

    private void DrawLabelStyle(SundesmoGroup group, float width, Action<uint> onChange)
    {
        var labelCol = ImGui.ColorConvertU32ToFloat4(group.LabelColor);
        if (ImGui.ColorEdit4("Label", ref labelCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
            onChange?.Invoke(ImGui.ColorConvertFloat4ToU32(labelCol));
        CkGui.AttachToolTip("Change the color of the folder label.");
    }

    private void DrawBorderStyle(SundesmoGroup group, float width, Action<uint> onChange)
    {
        var borderCol = ImGui.ColorConvertU32ToFloat4(group.BorderColor);
        if (ImGui.ColorEdit4("Border", ref borderCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
            onChange?.Invoke(ImGui.ColorConvertFloat4ToU32(borderCol));
        CkGui.AttachToolTip("Change the color of the folder border.");
    }

    private void DrawGradientStyle(SundesmoGroup group, float width, Action<uint> onChange)
    {
        var gradCol = ImGui.ColorConvertU32ToFloat4(group.GradientColor);
        if (ImGui.ColorEdit4("Gradient", ref gradCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
            onChange?.Invoke(ImGui.ColorConvertFloat4ToU32(gradCol));
        CkGui.AttachToolTip("Change the color of the folder gradient when expanded.");
    }

    private void DrawOfflineFlag(SundesmoGroup group, float width, Action<bool> onChange)
    {
        using (ImRaii.Group())
        {
            CkGui.FramedIconText(FAI.Unlink);
            CkGui.TextFrameAlignedInline("Include Offline");
            ImUtf8.SameLineInner();
            var showOffline = group.ShowOffline;
            if (ImGui.Checkbox("##showOffline", ref showOffline))
                onChange?.Invoke(showOffline);
        }
        CkGui.AttachToolTip("Show offline pairs in this folder.");
    }

    private void DrawLocationFlag(SundesmoGroup group, float width, Action? onChange = null)
    {
        using (ImRaii.Group())
        {
            CkGui.FramedIconText(FAI.Bullseye);
            CkGui.TextFrameAlignedInline("Linked Location");
        }
        CkGui.AttachToolTip("Visible pairs you see in this scope are added to the group automatically.");

        ImUtf8.SameLineInner();
        var comboW = ImGui.GetContentRegionAvail().X - ImUtf8.ItemInnerSpacing.X - CkGui.IconButtonSize(FAI.MapPin).X;
        if (CkGuiUtils.EnumCombo("##scope", comboW, group.Scope, out var newScope))
        {
            if (newScope is not LocationScope.None)
                group.AreaBound = true;

            group.Scope = newScope;
            onChange?.Invoke();
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            group.Scope = LocationScope.None;
            group.AreaBound = false;
            onChange?.Invoke();
        }
        CkGui.AttachToolTip("The scope required for a valid match.");

        ImUtf8.SameLineInner();
        if (CkGui.IconButton(FAI.MapPin))
        {
            group.AreaBound = true;
            group.Location = LocationSvc.Current.Clone();
            group.Scope = group.Location.IsInHousing ? LocationScope.HousingPlot : LocationScope.Territory;
            onChange?.Invoke();
        }
        CkGui.AttachToolTip("Set to current location.");
        
        // Attempt to draw the location area.
        DrawGroupLocation(group, width, onChange);
    }

    #region Location
    // All Rows are drawn, but only the ones for our scope are viewed.
    private void DrawGroupLocation(SundesmoGroup group, float width, Action? onChange = null)
    {
        if (!group.AreaBound || group.Scope is LocationScope.None)
            return;

        var height = CkStyle.GetFrameRowsHeight(GetScopeRows(group.Scope));

        using var _ = CkRaii.FramedChildPaddedW("AreaChild", width, height, 0, uint.MaxValue, 5f, 1f);

        var rowWidth = _.InnerRegion.X - ImUtf8.FrameHeight - ImUtf8.ItemInnerSpacing.X;
        switch (group.Scope)
        {
            case LocationScope.DataCenter:
                DCOnlyRow(group, rowWidth, onChange);
                return;
            case LocationScope.World:
                DCWorldRow(group, rowWidth, onChange);
                return;
            case LocationScope.IntendedUse:
                DCWorldRow(group, rowWidth, onChange);
                IntendedUseRow(group, rowWidth, onChange);
                return;
            case LocationScope.Territory:
                DCWorldRow(group, rowWidth, onChange);
                IntendedUseRow(group, rowWidth, onChange);
                TerritoryRow(group, rowWidth, onChange);
                return;
            case LocationScope.HousingDistrict:
                DCWorldRow(group, rowWidth, onChange);
                DistrictRow(group, rowWidth, onChange);
                return;
            case LocationScope.HousingWard:
                DCWorldRow(group, rowWidth, onChange);
                DistrictWardRow(group, rowWidth, onChange);
                return;
            case LocationScope.HousingPlot:
            case LocationScope.Indoor:
                DCWorldRow(group, rowWidth, onChange);
                DistrictWardRow(group, rowWidth, onChange);
                PlotRow(group, rowWidth, onChange);
                return;
        }
    }

    private void DCOnlyRow(SundesmoGroup group, float rowWidth, Action? onChange = null)
    {
        CkGui.FramedIconText(FAI.Database);
        ImUtf8.SameLineInner();
        if (_dataCenters.Draw(group.Location.DataCenterId, rowWidth))
        {
            group.Location.DataCenterId = _dataCenters.Current.Key;
            onChange?.Invoke();
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            group.Location.DataCenterId = byte.MaxValue;
            onChange?.Invoke();
        }
        CkGui.AttachToolTip("The DataCenter required for a match.");
    }

    private void DCWorldRow(SundesmoGroup group, float rowWidth, Action? onChange = null)
    {
        CkGui.FramedIconText(FAI.Globe);
        ImUtf8.SameLineInner();
        var halfW = (rowWidth - ImUtf8.ItemInnerSpacing.X) / 2;
        if (_dataCenters.Draw(group.Location.DataCenterId, halfW))
        {
            group.Location.DataCenterId = _dataCenters.Current.Key;
            onChange?.Invoke();
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            group.Location.DataCenterId = byte.MaxValue;
            onChange?.Invoke();
        }
        CkGui.AttachToolTip("The DataCenter required for a match.");

        ImUtf8.SameLineInner();
        if (_worlds.Draw(group.Location.WorldId, halfW))
        {
            group.Location.WorldId = _worlds.Current.Key;
            onChange?.Invoke();
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            group.Location.WorldId = ushort.MaxValue;
            onChange?.Invoke();
        }
        CkGui.AttachToolTip("The World required for a match.");
    }

    private void IntendedUseRow(SundesmoGroup group, float rowWidth, Action? onChange = null)
    {
        CkGui.FramedIconText(FAI.MapSigns);
        CkGui.TextFrameAlignedInline("Related Content:");
        ImUtf8.SameLineInner();
        if (CkGuiUtils.EnumCombo("##usage", rowWidth, group.Location.IntendedUse, out var newUse, defaultText: "Related Content.."))
        {
            group.Location.IntendedUse = newUse;
            onChange?.Invoke();
        }
        CkGui.AttachToolTip("The required Related Content Area to match.");
    }

    private void TerritoryRow(SundesmoGroup group, float rowWidth, Action? onChange = null)
    {
        CkGui.FramedIconText(FAI.MapMarkedAlt);
        ImUtf8.SameLineInner();
        if (_territories.Draw(group.Location.TerritoryId, 200f))
        {
            group.Location.TerritoryId = _territories.Current.Key;
            onChange?.Invoke();
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            group.Location.TerritoryId = ushort.MaxValue;
            onChange?.Invoke();
        }
        CkGui.AttachToolTip("The required zone to match.");
    }

    private void DistrictRow(SundesmoGroup group, float rowWidth, Action? onChange = null)
    {
        CkGui.FramedIconText(FAI.MapSigns);
        ImUtf8.SameLineInner();
        if (CkGuiUtils.EnumCombo("##area", rowWidth, group.Location.HousingArea, out var newVal,
            ToName, "Choose Area...", 0, CFlags.NoArrowButton))
        {
            group.Location.HousingArea = newVal;
            group.Location.Ward = 0;
            group.Location.Plot = 0;
            onChange?.Invoke();
        }
        CkGui.AttachToolTip("The Housing District to match.");
    }

    private void DistrictWardRow(SundesmoGroup group, float rowWidth, Action? onChange = null)
    {
        CkGui.FramedIconText(FAI.MapSigns);
        ImUtf8.SameLineInner();
        var halfW = (rowWidth - ImUtf8.ItemInnerSpacing.X) / 2;

        if (CkGuiUtils.EnumCombo("##area", halfW, group.Location.HousingArea, out var newVal,
            ToName, "Choose Area...", 0, CFlags.NoArrowButton))
        {
            group.Location.HousingArea = newVal;
            group.Location.Ward = 0;
            group.Location.Plot = 0;
            onChange?.Invoke();
        }
        CkGui.AttachToolTip("The Housing District to match.");

        ImUtf8.SameLineInner();
        var ward = (sbyte)(group.Location.Ward + 1);
        ImGui.SetNextItemWidth(halfW);
        if (ImGui.DragSByte($"##ward", ref ward, .5f, 1, 30, "Ward %d"))
            group.Location.Ward = (sbyte)(ward - 1);
        if (ImGui.IsItemDeactivatedAfterEdit())
            onChange?.Invoke();
        CkGui.AttachToolTip("The Ward to match.");
    }

    private void PlotRow(SundesmoGroup group, float rowWidth, Action? onChange = null)
    {
        CkGui.FramedIconText(FAI.Home);
        ImUtf8.SameLineInner();
        ImGui.SetNextItemWidth(rowWidth);
        var plot = (sbyte)(group.Location.Plot + 1);
        if (ImGui.SliderSByte("##plot", ref plot, 1, 60, "Plot %d"))
            group.Location.Plot = (sbyte)(plot - 1);
        if (ImGui.IsItemDeactivatedAfterEdit())
            onChange?.Invoke();
        CkGui.AttachToolTip($"The plot.");
    }
    #endregion Location

    private int GetScopeRows(LocationScope scope) => scope switch
    {
        LocationScope.DataCenter or LocationScope.World => 1,
        LocationScope.IntendedUse or LocationScope.HousingDistrict => 2,
        LocationScope.Territory or LocationScope.HousingWard or
        LocationScope.HousingPlot or LocationScope.Indoor => 3,
        _ => 0
    };

    private string ToName(ResidentialArea area)
        => LocationSvc.ResidentialNames.GetValueOrDefault(area) ?? area.ToString();


}
