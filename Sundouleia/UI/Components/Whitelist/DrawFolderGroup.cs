using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using System.Collections.Immutable;
using TerraFX.Interop.Windows;
using static FFXIVClientStructs.FFXIV.Client.UI.Misc.GroupPoseModule;

namespace Sundouleia.Gui.Components;

/// <summary>
///     The Draw Folders used by Sundouleia by default. (Visible, Online, Offline)
/// </summary>
public class DrawFolderGroup : DrawFolderBase
{
    // Use this to hide the entities that are offline.
    private bool _showOffline { get; init; }
    public DrawFolderGroup(SundesmoGroup group, IImmutableList<DrawEntitySundesmo> drawEntities,
        IImmutableList<Sundesmo> allSundesmos, MainConfig config, GroupsManager manager)
        : base(group.Label, drawEntities, allSundesmos, config, manager)
    {
        // Globals.
        _labelColor = group.LabelColor;
        _icon = group.Icon;
        _iconColor = group.IconColor;
        _colorBG = uint.MinValue;
        _colorBorder = uint.MaxValue;

        _showOffline = group.ShowOffline;
    }

    public override void Draw()
    {
        // If we have opted to not render the folder if empty, and we have nothing to draw, return.
        if (!RenderIfEmpty && !DrawEntities.Any())
            return;

        // Give the folder a id for the contents.
        using var id = ImRaii.PushId($"sundouleia_folder_group_{_label}");
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One);
        // pre-determine the size of the folder.
        var folderWidth = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        var bgCol = _hovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : _colorBG;
        var rightWidth = CkGui.IconButtonSize(FAI.Cog).X + CkGui.IconButtonSize(FAI.Filter).X + ImUtf8.ItemInnerSpacing.X * 2;

        // Draw framed child via CkRaii with background based on hover state 
        using (var _ = CkRaii.FramedChildPaddedW($"sundouleia_folder_group__{_label}", folderWidth, ImUtf8.FrameHeight, bgCol, _colorBorder, 5f, 2f))
        {
            using (ImRaii.Group())
            {
                CkGui.FramedIconText(_manager.IsOpen(_label) ? FAI.CaretDown : FAI.CaretRight);

                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                CkGui.IconText(_icon, _iconColor);

                CkGui.ColorTextFrameAlignedInline(_label, _labelColor);
                CkGui.ColorTextFrameAlignedInline($"[{Online}]", ImGuiColors.DalamudGrey2);
                CkGui.AttachToolTip($"{Online} online\n{Total} total");

                ImGui.SameLine(0, 0);
                ImGui.Dummy(ImGui.GetContentRegionAvail() - new Vector2(rightWidth, _.InnerRegion.Y));
            }
            if (ImGui.IsItemClicked())
                _manager.ToggleState(_label);

            ImUtf8.SameLineInner();
            DrawFolderOptions();
        }
        _hovered = ImGui.IsItemHovered();

        if (!_manager.IsOpen(_label))
            return;

        // if opened draw content
        using var indent = ImRaii.PushIndent(ImUtf8.FrameHeight + ImUtf8.ItemInnerSpacing.X + ImGuiHelpers.GlobalScale, false);
        // process a clipped draw for the drawn items.
        ImGuiClip.ClippedDraw(DrawEntities, (i) => i.DrawListItem(), ImUtf8.FrameHeight);
        ImGui.Separator();
    }

    private void DrawFolderOptions()
    {
        var config = CkGui.IconButtonSize(FAI.Cog);
        var filter = CkGui.IconButtonSize(FAI.Filter);
        var spacingX = ImUtf8.ItemInnerSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        var currentRightSide = windowEndX - config.X;

        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        if (CkGui.IconButton(FAI.Cog, inPopup: true))
            ImGui.OpenPopup("Folder Config Menu");
        CkGui.AttachToolTip("Open Folder Configuration");

        currentRightSide -= filter.X + spacingX;
        ImGui.SameLine(currentRightSide);
        if (CkGui.IconButton(FAI.Filter, inPopup: true))
            ImGui.OpenPopup("Folder Filter Menu");
        CkGui.AttachToolTip("Set Folder Filters");

        if (ImGui.BeginPopup("Folder Config Menu"))
        {
            DrawFolderConfigMenu(200f);
            ImGui.EndPopup();
        }
        if (ImGui.BeginPopup("Folder Filter Menu"))
        {
            DrawFolderFilterMenu(200f);
            ImGui.EndPopup();
        }
    }

    private void DrawFolderConfigMenu(float width)
    {
        CkGui.ColorText("I'm a working FolderConfigMenu Popup!", ImGuiColors.DalamudRed);

    }

    private void DrawFolderFilterMenu(float width)
    {
        CkGui.ColorText("I'm a working FolderFilter Popup!", ImGuiColors.DalamudRed);
    }
}
