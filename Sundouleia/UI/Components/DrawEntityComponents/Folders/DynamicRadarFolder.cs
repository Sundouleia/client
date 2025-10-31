using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Gui.Components;

/// <summary>
///     An implementation of <see cref="DynamicFolder{TModel, TDrawEntity}"/> specifically for Radar Users."/>
/// </summary>
public class DynamicRadarFolder : DynamicFolder<RadarUser, DrawEntityRadarUser>
{
    private readonly RadarManager _radar;
    private readonly SundesmoManager _sundesmos;

    /// <summary>
    ///     You are expected to call RegenerateItems in any derived constructor to populate the folder contents.
    /// </summary>
    public DynamicRadarFolder(string label, FolderOptions options, ILogger<DynamicRadarFolder> log, 
        SundouleiaMediator mediator, MainConfig config, DrawEntityFactory factory, 
        GroupsManager groups, SharedFolderMemory memory, RadarManager radar, SundesmoManager sundesmos)
        : base(label, options, log, mediator, config, factory, groups, memory)
    {
        _radar = radar;
        _sundesmos = sundesmos;

        Icon = label == Constants.FolderTagRadarPaired ? FAI.Link : FAI.SatelliteDish;
        IconColor = uint.MaxValue;
        LabelColor = uint.MaxValue;
        ColorBG = uint.MinValue;
        ColorBorder = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        ShowIfEmpty = label == Constants.FolderTagRadarUnpaired;
        // RegenerateItems here.
        RegenerateItems(string.Empty);

        // We should subscribe to radar-user-related changes here via the mediator calls.
        Mediator.Subscribe<RegenerateEntries>(this, _ =>
        {
            if (_.TargetFolders is RefreshTarget.Radar)
                RegenerateItems(string.Empty);
        });

        // Subscribe to pair-related changes here via the mediator calls.
    }

    public int Rendered => _allItems.Count(s => s.IsValid);
    public int Lurkers => _allItems.Count(s => !s.IsValid);

    protected override void DrawFolderInternal()
    {
        // pre-determine the size of the folder.
        var folderWidth = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        var bgCol = _hovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : ColorBG;
        // Draw framed child via CkRaii with background based on hover state 
        using (var _ = CkRaii.FramedChildPaddedW($"sundouleia_folder_ {Label}", folderWidth, ImUtf8.FrameHeight, bgCol, ColorBorder, 5f, 1f))
        {
            var pos = ImGui.GetCursorPos();
            ImGui.InvisibleButton($"folder_click_area_{Label}", new Vector2(folderWidth, _.InnerRegion.Y));
            if (ImGui.IsItemClicked())
                _groups.ToggleState(Label);

            // Back to start and then draw.
            ImGui.SameLine(pos.X);
            CkGui.FramedIconText(_groups.IsOpen(Label) ? FAI.CaretDown : FAI.CaretRight);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            CkGui.IconText(Icon, IconColor);
            CkGui.ColorTextFrameAlignedInline(Label, LabelColor);
            CkGui.ColorTextFrameAlignedInline($"[{Total}]", ImGuiColors.DalamudGrey2);
            CkGui.AttachToolTip($"{Total} total. --COL--({Lurkers} lurkers)--COL--", ImGuiColors.DalamudGrey2);
        }
        _hovered = ImGui.IsItemHovered();
    }


    protected override List<RadarUser> GetAllItems() => Label == Constants.FolderTagRadarPaired
        ? _radar.RadarUsers.Where(u => _sundesmos.ContainsSundesmo(u.UID)).ToList()
        : _radar.RadarUsers.Where(u => !_sundesmos.ContainsSundesmo(u.UID)).ToList();
    protected override DrawEntityRadarUser ToDrawEntity(RadarUser user) => _factory.CreateRadarEntity(user);

    protected override bool CheckFilter(RadarUser u, string filter)
    {
        if (filter.IsNullOrEmpty()) return true;
        return u.UID.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private string ToRadarName(RadarUser user)
        => _sundesmos.TryGetNickAliasOrUid(user.UID, out var dispName) ? dispName : user.AnonymousName;
}
