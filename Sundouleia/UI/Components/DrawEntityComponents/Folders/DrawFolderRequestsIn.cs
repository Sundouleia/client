using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using OtterGui.Text;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Gui.Components;

/// <summary>
///     An implementation of <see cref="DynamicFolder{TModel, TDrawEntity}"/> specifically for Requests."/>
///     
///     Might want to make a separate one for incoming and outgoing as they are fairly different.
///     But they do use the same draw method so idk.
/// </summary>
public class DrawFolderRequestsIn : DynamicRequestFolder
{
    public DrawFolderRequestsIn(ILogger log, SundouleiaMediator mediator, MainConfig config,
        DrawEntityFactory factory, GroupsManager groups, SharedFolderMemory memory, RequestsManager requests)
        : base(Constants.FolderTagRequestIncoming, log, mediator, config, factory, groups, memory, requests)
    {
        Icon = FAI.Inbox;
        IconColor = uint.MaxValue;
        LabelColor = uint.MaxValue;
        ColorBG = uint.MinValue;
        ColorBorder = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        ShowIfEmpty = true;
        // RegenerateItems here.
        RegenerateItems(string.Empty);

        // We should subscribe to radar-user-related changes here via the mediator calls.
        Mediator.Subscribe<RegenerateEntries>(this, _ =>
        {
            if (_.TargetFolders is RefreshTarget.Requests)
                RegenerateItems(string.Empty);
        });
    }

    protected override void DrawFolderInternal(bool toggles)
    {
        // pre-determine the size of the folder.
        var folderWidth = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        var bgCol = _hovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : ColorBG;
        // Draw framed child via CkRaii with background based on hover state 
        using (var _ = CkRaii.FramedChildPaddedW($"sundouleia_folder_ {Label}", folderWidth, ImUtf8.FrameHeight, bgCol, ColorBorder, 5f, 1f))
        {
            var pos = ImGui.GetCursorPos();
            ImGui.InvisibleButton($"folder_click_area_{Label}", new Vector2(folderWidth, _.InnerRegion.Y));
            if (ImGui.IsItemClicked() && toggles)
                _groups.ToggleState(Label);

            // Back to start and then draw.
            ImGui.SameLine(pos.X);
            CkGui.FramedIconText(_groups.IsOpen(Label) ? FAI.CaretDown : FAI.CaretRight);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            CkGui.IconText(Icon, IconColor);
            CkGui.ColorTextFrameAlignedInline(Label, LabelColor);
            CkGui.ColorTextFrameAlignedInline($"[{Total}]", ImGuiColors.DalamudGrey2);
        }
        _hovered = ImGui.IsItemHovered();
    }

    protected override List<RequestEntry> GetAllItems() => _requests.Incoming;
    protected override DrawEntityRequest ToDrawEntity(RequestEntry entry) => _factory.CreateRequestEntity(this, entry);

    protected override bool CheckFilter(RequestEntry u, string filter)
    {
        if (filter.IsNullOrEmpty()) return true;
        return u.RecipientAnonName.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
