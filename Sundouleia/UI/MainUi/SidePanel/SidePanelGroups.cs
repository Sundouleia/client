using CkCommons;
using CkCommons.Classes;
using CkCommons.Gui;
using CkCommons.Gui.Utility;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.CustomCombos;
using Sundouleia.CustomCombos.Editor;
using Sundouleia.DrawSystem;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using System;
using TerraFX.Interop.WinRT;

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
        // The common methods below can be migrated later.
        using var _ = CkRaii.Child("GroupCreator", new(width, drawHeight), wFlags: WFlags.NoScrollbar);

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

        var iconCol = ImGui.ColorConvertU32ToFloat4(cache.NewGroup.IconColor);
        if (ImGui.ColorEdit4("Icon", ref iconCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
            cache.NewGroup.IconColor = ImGui.ColorConvertFloat4ToU32(iconCol);
        CkGui.AttachToolTip("Change the color of the folder icon.");

        ImGui.SameLine();
        var labelCol = ImGui.ColorConvertU32ToFloat4(cache.NewGroup.LabelColor);
        if (ImGui.ColorEdit4("Label", ref labelCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
            cache.NewGroup.LabelColor = ImGui.ColorConvertFloat4ToU32(labelCol);
        CkGui.AttachToolTip("Change the color of the folder label.");

        ImGui.SameLine();
        var borderCol = ImGui.ColorConvertU32ToFloat4(cache.NewGroup.BorderColor);
        if (ImGui.ColorEdit4("Border", ref borderCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
            cache.NewGroup.BorderColor = ImGui.ColorConvertFloat4ToU32(borderCol);
        CkGui.AttachToolTip("Change the color of the folder border.");

        ImGui.SameLine();
        var gradCol = ImGui.ColorConvertU32ToFloat4(cache.NewGroup.GradientColor);
        if (ImGui.ColorEdit4("Gradient", ref gradCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
            cache.NewGroup.GradientColor = ImGui.ColorConvertFloat4ToU32(gradCol);
        CkGui.AttachToolTip("Change the color of the folder gradient when expanded.");


        ImGui.Separator();
        CkGui.FontText("Preferences", UiFontService.Default150Percent);

        using (ImRaii.Group())
        {
            CkGui.FramedIconText(FAI.Unlink);
            CkGui.TextFrameAlignedInline("Include Offline");
            ImUtf8.SameLineInner();
            var showOffline = cache.NewGroup.ShowOffline;
            if (ImGui.Checkbox("##showOffline", ref showOffline))
                cache.NewGroup.ShowOffline = showOffline;
        }
        CkGui.AttachToolTip("Show offline pairs in this folder.");

        using (ImRaii.Group())
        {
            CkGui.FramedIconText(FAI.Bullseye);
            CkGui.TextFrameAlignedInline("Linked Location Scope");
            ImUtf8.SameLineInner();
            if (CkGuiUtils.EnumCombo("##scope", ImGui.GetContentRegionAvail().X, cache.NewGroup.Scope, out var newScope))
            {
                if (newScope is not LocationScope.None)
                    cache.NewGroup.AreaBound = true;

                cache.NewGroup.Scope = newScope;
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                cache.NewGroup.Scope = LocationScope.None;
                cache.NewGroup.AreaBound = false;
            }
        }
        CkGui.AttachToolTip("If others rendered in this scope are added.");
        // Attempt to draw the location area.
        DrawGroupLocation(cache, width);

        ImGui.Separator();
        DrawFolderPreview(cache.NewGroup);
    }

    // All Rows are drawn, but only the ones for our scope are viewed.
    private void DrawGroupLocation(NewGroupCache cache, float width)
    {
        if (!cache.NewGroup.AreaBound || cache.NewGroup.Scope is LocationScope.None)
            return;

        var height = CkStyle.GetFrameRowsHeight(GetScopeRows(cache.NewGroup.Scope));

        using var _ = CkRaii.FramedChildPaddedW("AreaChild", width, height, 0, uint.MaxValue, 5f, 1f);

        var rowWidth = _.InnerRegion.X - ImUtf8.FrameHeight - ImUtf8.ItemInnerSpacing.X;
        switch (cache.NewGroup.Scope)
        {
            case LocationScope.DataCenter:
                DCOnlyRow(cache.NewGroup, rowWidth);
                return;
            case LocationScope.World:
                DCWorldRow(cache.NewGroup, rowWidth);
                return;
            case LocationScope.IntendedUse:
                DCWorldRow(cache.NewGroup, rowWidth);
                IntendedUseRow(cache.NewGroup, rowWidth);
                return;
            case LocationScope.Territory:
                DCWorldRow(cache.NewGroup, rowWidth);
                IntendedUseRow(cache.NewGroup, rowWidth);
                TerritoryRow(cache.NewGroup, rowWidth);
                return;
            case LocationScope.HousingDistrict:
                DCWorldRow(cache.NewGroup, rowWidth);
                DistrictRow(cache.NewGroup, rowWidth);
                return;
            case LocationScope.HousingWard:
                DCWorldRow(cache.NewGroup, rowWidth);
                DistrictWardRow(cache.NewGroup, rowWidth);
                return;
            case LocationScope.HousingPlot:
            case LocationScope.Indoor:
                DCWorldRow(cache.NewGroup, rowWidth);
                DistrictWardRow(cache.NewGroup, rowWidth);
                PlotRow(cache.NewGroup, rowWidth);
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

    public void DrawGroupEditor(GroupEditorCache cache, float width)
    {
        ImGui.Text("Bwaaaa");
    }

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
