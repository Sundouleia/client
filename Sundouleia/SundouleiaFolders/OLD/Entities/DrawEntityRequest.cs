using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;

namespace Sundouleia.Gui.Components;

// Will likely remove this soon but is just a temporary migration from GSpeak
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
    public string DisplayName => _sundesmos.TryGetNickAliasOrUid(Item.SenderUID, out var res) ? res : Item.SenderAnonName;
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
            // draw here the left side icon and the name that follows it.
            ImUtf8.SameLineInner();
            CkGui.FramedIconText(FAI.QuestionCircle, ImGuiColors.DalamudYellow);
            var timeLeft = Item.TimeToRespond;
            var displayText = $"Expires in {timeLeft.Days}d {timeLeft.Hours}h {timeLeft.Minutes}m.";
            if (Item.AttachedMessage.Length > 0) displayText += $" --SEP----COL--Message: --COL--{Item.AttachedMessage}";
            CkGui.AttachToolTip(displayText, color: ImGuiColors.TankBlue);
            ImGui.SameLine();

            using (ImRaii.PushFont(UiBuilder.MonoFont))
                CkGui.TextFrameAlignedInline(Item.RecipientAnonName);

            DrawPendingCancel();
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
            ImUtf8.SameLineInner();
            CkGui.FramedIconText(FAI.QuestionCircle, ImGuiColors.DalamudYellow);
            var timeLeft = Item.TimeToRespond;
            var displayText = $"Expires in {timeLeft.Days}d {timeLeft.Hours}h {timeLeft.Minutes}m.";
            if (Item.AttachedMessage.Length > 0) displayText += $" --SEP----COL--Message: --COL--{Item.AttachedMessage}";
            CkGui.AttachToolTip(displayText, color: ImGuiColors.TankBlue);
            ImGui.SameLine();

            using (ImRaii.PushFont(UiBuilder.MonoFont))
                CkGui.TextFrameAlignedInline(Item.SenderAnonName);

            DrawAcceptReject();
        }

        return false;
    }

    private void DrawAcceptReject()
    {
        var acceptButtonSize = CkGui.IconTextButtonSize(FAI.PersonCircleCheck, "Accept");
        var rejectButtonSize = CkGui.IconTextButtonSize(FAI.PersonCircleXmark, "Reject");
        var spacingX = ImGui.GetStyle().ItemInnerSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        var currentRightSide = windowEndX - acceptButtonSize;

        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed))
        {
            if (CkGui.IconTextButton(FAI.PersonCircleXmark, "Reject", null, true, UiService.DisableUI))
            {
                UiService.SetUITask(async () =>
                {
                    if (await _hub.UserRejectRequest(new(new(Item.RecipientUID))) is { } res && res.ErrorCode is SundouleiaApiEc.Success)
                        _manager.RemoveRequest(Item);
                });
            }
        }
        CkGui.AttachToolTip("Reject the Request");

        currentRightSide -= acceptButtonSize + spacingX;
        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen))
        {
            if (CkGui.IconTextButton(FAI.PersonCircleCheck, "Accept", null, true, UiService.DisableUI))
            {
                UiService.SetUITask(async () =>
                {
                    var res = await _hub.UserAcceptRequest(new(new(Item.SenderUID))).ConfigureAwait(false);
                    if (res.ErrorCode is SundouleiaApiEc.AlreadyPaired)
                        _manager.RemoveRequest(Item);
                    else if (res.ErrorCode is SundouleiaApiEc.Success)
                    {
                        _manager.RemoveRequest(Item);
                        _sundesmos.AddSundesmo(res.Value!.Pair);
                        if (res.Value!.OnlineInfo is { } onlineSundesmo)
                            _sundesmos.MarkSundesmoOnline(onlineSundesmo);
                    }
                });
            }
        }
        CkGui.AttachToolTip("Accept the Request");
    }

    private void DrawPendingCancel()
    {
        var cancelButtonSize = CkGui.IconTextButtonSize(FAI.PersonCircleXmark, "Cancel Request");
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        var currentRightSide = windowEndX - cancelButtonSize;

        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed))
        {
            if (CkGui.IconTextButton(FAI.PersonCircleXmark, "Cancel Request", null, true, UiService.DisableUI))
            {
                UiService.SetUITask(async () =>
                {
                    var res = await _hub.UserCancelRequest(new(new(Item.RecipientUID)));
                    if (res.ErrorCode is SundouleiaApiEc.Success)
                        _manager.RemoveRequest(Item);
                });
            }
        }
        CkGui.AttachToolTip("Remove the pending request from both yourself and the pending Users list.");
    }
}
