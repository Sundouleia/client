using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.WebAPI;

namespace Sundouleia.Gui.Components;

// Will likely remove this soon but is just a temporary migration from gspeak
public class DrawEntityRequest : IDrawEntity<RequestEntry>
{
    private readonly MainHub _hub;
    private readonly SundesmoManager _sundesmos;
    private readonly RequestsManager _manager;

    // Multi-Select support.
    private DynamicRequestFolder _parentFolder;
    public RequestEntry Item { get; init; }

    private bool _hovered = false;
    private bool _selectingGroups = false;

    public DrawEntityRequest(DynamicRequestFolder parent, RequestEntry user, MainHub hub, SundesmoManager sundesmos, RequestsManager requests)
    {
        DistinctId = $"requestItem_{user.SenderUID}_{user.RecipientUID}";
        Item = user;
        _hub = hub;
        _sundesmos = sundesmos;
        _manager = requests;
    }

    public string DistinctId { get; init; }
    // a bit botched at the moment.
    public string DisplayName => _sundesmos.TryGetNickAliasOrUid(new(Item.SenderUID), out var res) ? res : Item.SenderAnonName;
    public string EntityId => Item.SenderUID + '_' + Item.RecipientUID;

    public bool Draw(bool isSelected)
    {
        var clicked = false;

        // The button we draw for the drag-drop / multi-select support depends on what we draw.
        if (Item.FromClient)
            clicked = DrawOutgoingRequest(isSelected);
        else
            clicked = DrawIncomingRequest(isSelected);
        // can modify hover inside these, dont need to do it outside.
        _hovered = ImGui.IsItemHovered();

        // If they are a supporter, we can draw their icon.
        // TODO ?

        return clicked;
    }

    private bool DrawOutgoingRequest(bool isSelected)
    {
        var childSize = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight());
        var bgCol = (_hovered || isSelected) ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0;
        using (var _ = CkRaii.Child(DistinctId, childSize, bgCol, 5f))
        {
            CkGui.CenterText("Bagagwa");
        }

        return false;
    }

    // These windows can be expanded to select things like groups and what not upon acceptance.
    private bool DrawIncomingRequest(bool isSelected)
    {
        var childSize = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight());
        var bgCol = (_hovered || isSelected) ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0;
        using (var _ = CkRaii.Child(DistinctId, childSize, bgCol, 5f))
        {
            CkGui.CenterText("Bagagwa");
        }

        return false;
    }

    //private void DrawAcceptReject()
    //{
    //    var acceptButtonSize = CkGui.IconTextButtonSize(FAI.PersonCircleCheck, "Accept");
    //    var rejectButtonSize = CkGui.IconTextButtonSize(FAI.PersonCircleXmark, "Reject");
    //    var spacingX = ImGui.GetStyle().ItemInnerSpacing.X;
    //    var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
    //    var currentRightSide = windowEndX - acceptButtonSize;

    //    ImGui.SameLine(currentRightSide);
    //    ImGui.AlignTextToFramePadding();
    //    using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed))
    //    {
    //        if (CkGui.IconTextButton(FAI.PersonCircleXmark, "Reject", null, true, UiService.DisableUI))
    //        {
    //            UiService.SetUITask(async () =>
    //            {
    //                if (await _hub.UserRejectRequest(new(Item..User)) is { } res && res.ErrorCode is SundouleiaApiEc.Success)
    //                    _manager.RemoveRequest(_entry);
    //            });
    //        }
    //    }
    //    CkGui.AttachToolTip("Reject the Request");

    //    currentRightSide -= acceptButtonSize + spacingX;
    //    ImGui.SameLine(currentRightSide);
    //    ImGui.AlignTextToFramePadding();
    //    using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen))
    //    {
    //        if (CkGui.IconTextButton(FAI.PersonCircleCheck, "Accept", null, true, UiService.DisableUI))
    //        {
    //            UiService.SetUITask(async () =>
    //            {
    //                var res = await _hub.UserAcceptRequest(new(_entry.User)).ConfigureAwait(false);
    //                if (res.ErrorCode is SundouleiaApiEc.AlreadyPaired)
    //                    _manager.RemoveRequest(_entry);
    //                else if (res.ErrorCode is SundouleiaApiEc.Success)
    //                {
    //                    _manager.RemoveRequest(_entry);
    //                    _sundesmos.AddSundesmo(res.Value!.Pair);
    //                    if (res.Value!.OnlineInfo is { } onlineSundesmo)
    //                        _sundesmos.MarkSundesmoOnline(onlineSundesmo);
    //                }
    //            });
    //        }
    //    }
    //    CkGui.AttachToolTip("Accept the Request");
    //}

    //private void DrawPendingCancel()
    //{
    //    var cancelButtonSize = CkGui.IconTextButtonSize(FAI.PersonCircleXmark, "Cancel Request");
    //    var spacingX = ImGui.GetStyle().ItemSpacing.X;
    //    var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
    //    var currentRightSide = windowEndX - cancelButtonSize;

    //    ImGui.SameLine(currentRightSide);
    //    ImGui.AlignTextToFramePadding();
    //    using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed))
    //    {
    //        if (CkGui.IconTextButton(FAI.PersonCircleXmark, "Cancel Request", null, true, UiService.DisableUI))
    //        {
    //            UiService.SetUITask(async () =>
    //            {
    //                var res = await _hub.UserCancelRequest(new(_entry.Target));
    //                if (res.ErrorCode is SundouleiaApiEc.Success)
    //                    _manager.RemoveRequest(_entry);
    //            });
    //        }
    //    }
    //    CkGui.AttachToolTip("Remove the pending request from both yourself and the pending Users list.");
    //}
}
