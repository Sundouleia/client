using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using System.Collections.Immutable;

namespace Sundouleia.Gui.Components;

/// <summary>
///     Attempting to test a new iteration of draw folders with a LazyGeneration for recalculation on radar users.
/// </summary>
public class DrawFolderRadar : IRadarFolder
{
    protected readonly MainConfig _config;
    protected readonly GroupsManager _manager;

    protected bool _hovered;

    // Required Stylization for all folders.
    protected uint _colorBG = uint.MinValue;
    protected uint _colorBorder = uint.MaxValue;

    protected FAI _icon = FAI.Folder;
    protected uint _iconColor = uint.MaxValue;

    protected readonly string _label;
    protected uint _labelColor = uint.MaxValue;

    // Tracks all Sundesmos involved with this folder.
    private readonly List<RadarUser> _allUsers;
    private readonly Func<List<RadarUser>, IImmutableList<DrawEntityRadarUser>> _lazyGen;

    public DrawFolderRadar(string label,
        List<RadarUser> allUsers, // Could add a second generator for this to make it truly dynamic but eh.
        Func<List<RadarUser>, IImmutableList<DrawEntityRadarUser>> lazyGen,
        MainConfig config,
        GroupsManager manager)
    {
        _label = label;
        _allUsers = allUsers;
        _lazyGen = lazyGen;
        _config = config;
        _manager = manager;

        DrawEntities = _lazyGen(_allUsers);

        // Globals.
        _icon = label == Constants.FolderTagRadarPaired ? FAI.Link : FAI.SatelliteDish;
        _iconColor = uint.MaxValue;
        _labelColor = uint.MaxValue;
        _colorBG = uint.MinValue;
        _colorBorder = ImGui.GetColorU32(ImGuiCol.TextDisabled);
    }

    // Interface satisfaction.
    public int Total => _allUsers.Count;
    public int Rendered => _allUsers.Count(s => s.IsValid);
    public int Lurkers => _allUsers.Count(s => !s.IsValid);
    private bool RenderIfEmpty => _label == Constants.FolderTagRadarUnpaired;
    public IImmutableList<DrawEntityRadarUser> DrawEntities { get; private set; }

    public void RefreshEntityOrder() => DrawEntities = _lazyGen(_allUsers);

    public void Draw()
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

        // Draw framed child via CkRaii with background based on hover state 
        using (var _ = CkRaii.FramedChildPaddedW($"sundouleia_folder__{_label}", folderWidth, ImUtf8.FrameHeight, bgCol, _colorBorder, 5f, 1f))
        {
            CkGui.FramedIconText(_manager.IsOpen(_label) ? FAI.CaretDown : FAI.CaretRight);

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            CkGui.IconText(_icon, _iconColor);

            CkGui.ColorTextFrameAlignedInline(GetLabelName(), _labelColor);
            CkGui.ColorTextFrameAlignedInline($"[{Total}]", ImGuiColors.DalamudGrey2);
            CkGui.AttachToolTip($"{Total} total. --COL--({Lurkers} lurkers)--COL--", ImGuiColors.DalamudGrey2);
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
            Constants.FolderTagRadarUnpaired => "Unpaired",
            Constants.FolderTagRadarPaired => "Paired",
            _ => _label,
        };
}
