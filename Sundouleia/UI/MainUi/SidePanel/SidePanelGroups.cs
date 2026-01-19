using CkCommons;
using CkCommons.Gui;
using CkCommons.Gui.Utility;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.CustomCombos;
using Sundouleia.CustomCombos.Editor;
using Sundouleia.DrawSystem;
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
    private readonly SidePanelService _service;

    // Maybe a pair combo here or something, idk.
    private bool _previewOpen = false;
    private DDSFolderGroupCombo _folderGroupCombo;
    private FAIconCombo _icons;
    private DataCenterCombo _dataCenters;
    private WorldCombo _worlds;
    private TerritoryCombo _territories;

    public SidePanelGroups(ILogger<SidePanelInteractions> logger, SundouleiaMediator mediator,
        GroupsManager groups, SidePanelService service, GroupsDrawSystem groupsDDS)
    {
        _logger = logger;
        _mediator = mediator;
        _groups = groups;
        _service = service;

        _folderGroupCombo = new DDSFolderGroupCombo(logger, groupsDDS);
        _icons = new FAIconCombo(logger);
        _dataCenters = new DataCenterCombo(logger);
        _worlds = new WorldCombo(logger);
        _territories = new TerritoryCombo(logger);
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

        DrawStylizations(cache.NewGroup, width);

        ImGui.Separator();
        DrawPreferences(cache.NewGroup, width);

        ImGui.Separator();
        DrawFolderPreview(cache.NewGroup);
    }

    public void DrawGroupEditor(GroupEditorCache cache, float width)
    {
        using var _ = CkRaii.Child("GroupEditor", new(width, ImGui.GetContentRegionAvail().Y), wFlags: WFlags.NoScrollbar);

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
            // Update the icon within the group manager.
            _groups.SetIcon(cache.GroupInEditor, _icons.Current, cache.GroupInEditor.IconColor);
            cache.UpdateStyle();
        }
        CkGui.AttachToolTip("Edit the icon for your group.");

        ImUtf8.SameLineInner();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        var tmpName = cache.GroupInEditor.Label;
        if (ImGui.InputTextWithHint("##NewGroupLabel", "Group Name..", ref tmpName, 40))
            cache.TryRenameNode(_groups, tmpName);
        CkGui.AttachToolTip("Set the name of the Group.");

        ImGui.Separator();
        if (DrawStylizations(cache.GroupInEditor, width))
            cache.UpdateStyle();

        ImGui.Separator();
        DrawPreferences(cache.GroupInEditor, width);

        ImGui.Separator();
        DrawFolderPreview(cache.GroupInEditor);
    }


    private bool DrawStylizations(SundesmoGroup group, float width)
    {
        bool changed = false;
        ImGui.Separator();
        CkGui.FontText("Stylization", UiFontService.Default150Percent);

        var iconCol = ImGui.ColorConvertU32ToFloat4(group.IconColor);
        if (ImGui.ColorEdit4("Icon", ref iconCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
        {
            group.IconColor = ImGui.ColorConvertFloat4ToU32(iconCol);
            _groups.Save();
            changed |= true;
        }
        CkGui.AttachToolTip("Change the color of the folder icon.");

        ImGui.SameLine();
        var labelCol = ImGui.ColorConvertU32ToFloat4(group.LabelColor);
        if (ImGui.ColorEdit4("Label", ref labelCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
        {
            group.LabelColor = ImGui.ColorConvertFloat4ToU32(labelCol);
            _groups.Save();
            changed |= true;
        }
        CkGui.AttachToolTip("Change the color of the folder label.");

        ImGui.SameLine();
        var borderCol = ImGui.ColorConvertU32ToFloat4(group.BorderColor);
        if (ImGui.ColorEdit4("Border", ref borderCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
        {
            group.BorderColor = ImGui.ColorConvertFloat4ToU32(borderCol);
            _groups.Save();
            changed |= true;
        }
        CkGui.AttachToolTip("Change the color of the folder border.");

        ImGui.SameLine();
        var gradCol = ImGui.ColorConvertU32ToFloat4(group.GradientColor);
        if (ImGui.ColorEdit4("Gradient", ref gradCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
        {
            group.GradientColor = ImGui.ColorConvertFloat4ToU32(gradCol);
            _groups.Save();
            changed |= true;
        }
        CkGui.AttachToolTip("Change the color of the folder gradient when expanded.");

        return changed;
    }

    private void DrawPreferences(SundesmoGroup group, float width)
    {
        CkGui.FontText("Preferences", UiFontService.Default150Percent);

        using (ImRaii.Group())
        {
            CkGui.FramedIconText(FAI.Unlink);
            CkGui.TextFrameAlignedInline("Include Offline");
            ImUtf8.SameLineInner();
            var showOffline = group.ShowOffline;
            if (ImGui.Checkbox("##showOffline", ref showOffline))
                group.ShowOffline = showOffline;
        }
        CkGui.AttachToolTip("Show offline pairs in this folder.");

        using (ImRaii.Group())
        {
            CkGui.FramedIconText(FAI.Bullseye);
            CkGui.TextFrameAlignedInline("Linked Location Scope");
            ImUtf8.SameLineInner();
            if (CkGuiUtils.EnumCombo("##scope", ImGui.GetContentRegionAvail().X, group.Scope, out var newScope))
            {
                if (newScope is not LocationScope.None)
                    group.AreaBound = true;

                group.Scope = newScope;
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                group.Scope = LocationScope.None;
                group.AreaBound = false;
            }
        }
        CkGui.AttachToolTip("If others rendered in this scope are added.");
        // Attempt to draw the location area.
        DrawGroupLocation(group, width);
    }

    #region Location
    // All Rows are drawn, but only the ones for our scope are viewed.
    private void DrawGroupLocation(SundesmoGroup group, float width)
    {
        if (!group.AreaBound || group.Scope is LocationScope.None)
            return;

        var height = CkStyle.GetFrameRowsHeight(GetScopeRows(group.Scope));

        using var _ = CkRaii.FramedChildPaddedW("AreaChild", width, height, 0, uint.MaxValue, 5f, 1f);

        var rowWidth = _.InnerRegion.X - ImUtf8.FrameHeight - ImUtf8.ItemInnerSpacing.X;
        switch (group.Scope)
        {
            case LocationScope.DataCenter:
                DCOnlyRow(group, rowWidth);
                return;
            case LocationScope.World:
                DCWorldRow(group, rowWidth);
                return;
            case LocationScope.IntendedUse:
                DCWorldRow(group, rowWidth);
                IntendedUseRow(group, rowWidth);
                return;
            case LocationScope.Territory:
                DCWorldRow(group, rowWidth);
                IntendedUseRow(group, rowWidth);
                TerritoryRow(group, rowWidth);
                return;
            case LocationScope.HousingDistrict:
                DCWorldRow(group, rowWidth);
                DistrictRow(group, rowWidth);
                return;
            case LocationScope.HousingWard:
                DCWorldRow(group, rowWidth);
                DistrictWardRow(group, rowWidth);
                return;
            case LocationScope.HousingPlot:
            case LocationScope.Indoor:
                DCWorldRow(group, rowWidth);
                DistrictWardRow(group, rowWidth);
                PlotRow(group, rowWidth);
                return;
        }
    }

    private void DCOnlyRow(SundesmoGroup group, float rowWidth)
    {
        CkGui.FramedIconText(FAI.Database);
        ImUtf8.SameLineInner();
        if (_dataCenters.Draw(group.Location.DataCenterId, rowWidth))
            group.Location.DataCenterId = _dataCenters.Current.Key;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            group.Location.DataCenterId = byte.MaxValue;
        CkGui.AttachToolTip("The DataCenter required for a match.");
    }

    private void DCWorldRow(SundesmoGroup group, float rowWidth)
    {
        CkGui.FramedIconText(FAI.Globe);
        ImUtf8.SameLineInner();
        var halfW = (rowWidth - ImUtf8.ItemInnerSpacing.X) / 2;
        if (_dataCenters.Draw(group.Location.DataCenterId, halfW))
            group.Location.DataCenterId = _dataCenters.Current.Key;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            group.Location.DataCenterId = byte.MaxValue;
        CkGui.AttachToolTip("The DataCenter required for a match.");

        ImUtf8.SameLineInner();
        if (_worlds.Draw(group.Location.WorldId, halfW))
            group.Location.WorldId = _worlds.Current.Key;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            group.Location.WorldId = ushort.MaxValue;
        CkGui.AttachToolTip("The World required for a match.");
    }

    private void IntendedUseRow(SundesmoGroup group, float rowWidth)
    {
        CkGui.FramedIconText(FAI.MapSigns);
        CkGui.TextFrameAlignedInline("Related Content:");
        ImUtf8.SameLineInner();
        if (CkGuiUtils.EnumCombo("##usage", rowWidth, group.Location.IntendedUse, out var newUse, defaultText: "Related Content.."))
            group.Location.IntendedUse = newUse;
        CkGui.AttachToolTip("The required Related Content Area to match.");
    }

    private void TerritoryRow(SundesmoGroup group, float rowWidth)
    {
        CkGui.FramedIconText(FAI.MapMarkedAlt);
        ImUtf8.SameLineInner();
        if (_territories.Draw(group.Location.TerritoryId, 200f))
            group.Location.TerritoryId = _territories.Current.Key;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            group.Location.TerritoryId = ushort.MaxValue;
        CkGui.AttachToolTip("The required zone to match.");
    }

    private void DistrictRow(SundesmoGroup group, float rowWidth)
    {
        CkGui.FramedIconText(FAI.MapSigns);
        ImUtf8.SameLineInner();
        if (CkGuiUtils.EnumCombo("##area", rowWidth, group.Location.HousingArea, out var newVal,
            ToName, "Choose Area...", 0, CFlags.NoArrowButton))
        {
            group.Location.HousingArea = newVal;
            group.Location.Ward = 0;
            group.Location.Plot = 0;
        }
        CkGui.AttachToolTip("The Housing District to match.");
    }

    private void DistrictWardRow(SundesmoGroup group, float rowWidth)
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
        }
        CkGui.AttachToolTip("The Housing District to match.");

        ImUtf8.SameLineInner();
        var ward = (sbyte)(group.Location.Ward + 1);
        ImGui.SetNextItemWidth(halfW);
        if (ImGui.DragSByte($"##ward", ref ward, .5f, 1, 30, "Ward %d"))
            group.Location.Ward = (sbyte)(ward - 1);
        CkGui.AttachToolTip("The Ward to match.");
    }

    private void PlotRow(SundesmoGroup group, float rowWidth)
    {
        CkGui.FramedIconText(FAI.Home);
        ImUtf8.SameLineInner();
        ImGui.SetNextItemWidth(rowWidth);
        var plot = (sbyte)(group.Location.Plot + 1);
        if (ImGui.SliderSByte("##plot", ref plot, 1, 60, "Plot %d"))
            group.Location.Plot = (sbyte)(plot - 1);
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
