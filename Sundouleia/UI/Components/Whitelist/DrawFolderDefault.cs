using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using System.Collections.Immutable;

namespace Sundouleia.Gui.Components;

/// <summary>
///     The Draw Folders used by Sundouleia by default. (Visible, Online, Offline)
/// </summary>
public class DrawFolderDefault : DrawFolderBase
{
    public DrawFolderDefault(string label, IImmutableList<DrawEntitySundesmo> drawEntities,
        IImmutableList<Sundesmo> allSundesmos, MainConfig config, GroupsManager manager)
        : base(label, drawEntities, allSundesmos, config, manager)
    {
        // Globals.
        _labelColor = uint.MaxValue;
        _colorBG = uint.MinValue;
        _colorBorder = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        // Label specific.
        switch (label)
        {
            case Constants.CustomAllTag:
                _icon = FAI.Globe;
                _iconColor = uint.MaxValue;
                break;
            case Constants.CustomVisibleTag:
                _icon = FAI.Eye;
                _iconColor = CkColor.TriStateCheck.Uint();
                break;
            case Constants.CustomOnlineTag:
                _icon = FAI.Link;
                _iconColor = CkColor.TriStateCheck.Uint();
                break;
            case Constants.CustomOfflineTag:
                _icon = FAI.Link;
                _iconColor = CkColor.TriStateCross.Uint();
                break;
            default:
                _icon = FAI.Folder;
                _iconColor = uint.MaxValue;
                break;
        }
    }

    protected override bool RenderIfEmpty => _label switch
    {
        Constants.CustomAllTag => true, 
        Constants.CustomVisibleTag => false,
        Constants.CustomOnlineTag => false,
        Constants.CustomOfflineTag => false,
        _ => false,
    };

    private string GetBracketText() => _label switch
    {
        Constants.CustomAllTag => $"[{Online}]",
        Constants.CustomVisibleTag => $"[{Online}]",
        Constants.CustomOnlineTag => $"[{Online}]",
        Constants.CustomOfflineTag => $"[{Total}]",
        _ => _label,
    };

    private string GetBracketTooltip() => _label switch
    {
        Constants.CustomAllTag => $"{Online} online\n{Total} total",
        Constants.CustomVisibleTag => $"{Online} online\n{Total} total",
        Constants.CustomOnlineTag => $"{Online} online\n{Total} total",
        Constants.CustomOfflineTag => $"{Total} offline",
        _ => string.Empty,
    };

    public override void Draw()
    {
        // If we have opted to not render the folder if empty, and we have nothing to draw, return.
        if (!RenderIfEmpty && !DrawEntities.Any())
            return;

        // Give the folder a id for the contents.
        using var id = ImRaii.PushId($"sundouleia_folder_{_label}");
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One);
        // pre-determine the size of the folder.
        var folderWidth = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        var bgCol = _hovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : _colorBG;
        var rightWidth = CkGui.IconButtonSize(FAI.Cog).X + CkGui.IconButtonSize(FAI.Filter).X + ImUtf8.ItemInnerSpacing.X * 2;

        // Draw framed child via CkRaii with background based on hover state 
        using (var _ = CkRaii.FramedChildPaddedW($"sundouleia_folder__{_label}", folderWidth, ImUtf8.FrameHeight, bgCol, _colorBorder, 5f, 1f))
        {
            CkGui.FramedIconText(_manager.IsOpen(_label) ? FAI.CaretDown : FAI.CaretRight);

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            CkGui.IconText(_icon, _iconColor);

            CkGui.ColorTextFrameAlignedInline(GetLabelName(), _labelColor);

            CkGui.ColorTextFrameAlignedInline(GetBracketText(), ImGuiColors.DalamudGrey2);
            CkGui.AttachToolTip(GetBracketTooltip());
        }
        var folderMin = ImGui.GetItemRectMin();
        var folderMax = ImGui.GetItemRectMax();

        _hovered = ImGui.IsItemHovered();

        if (ImGui.IsItemClicked())
            _manager.ToggleState(_label);


        if (!_manager.IsOpen(_label))
            return;

        var wdl = ImGui.GetWindowDrawList();
        wdl.ChannelsSplit(2);
        wdl.ChannelsSetCurrent(1); // Foreground.
        // if opened draw content
        using var indent = ImRaii.PushIndent(ImUtf8.FrameHeight + ImUtf8.ItemInnerSpacing.X + ImGuiHelpers.GlobalScale, false);
        // process a clipped draw for the drawn items.
        ImGuiClip.ClippedDraw(DrawEntities, (i) => i.DrawListItem(), ImUtf8.FrameHeightSpacing);

        wdl.ChannelsSetCurrent(0); // Background.
        // folder start position
        var gradientTL = new Vector2(folderMin.X, folderMax.Y);
        var gradientTR = new Vector2(folderMax.X, ImGui.GetItemRectMax().Y);

        wdl.AddRectFilledMultiColor(gradientTL, gradientTR, ColorHelpers.Fade(_colorBorder, .9f), ColorHelpers.Fade(_colorBorder, .9f), 0, 0);
        wdl.ChannelsMerge();
    }

    private string GetLabelName()
        => _label switch
        {
            Constants.CustomAllTag => "All Sundesmos",
            Constants.CustomVisibleTag => "Visible",
            Constants.CustomOnlineTag => "Online",
            Constants.CustomOfflineTag => "Offline",
            _ => _label,
        };
}
