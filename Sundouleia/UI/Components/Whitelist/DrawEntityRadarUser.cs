using CkCommons;
using CkCommons.Gui;
using CkCommons.Helpers;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using OtterGui.Text;
using Sundouleia.Gui.MainWindow;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;
using TerraFX.Interop.Windows;
using static FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxy24.Delegates;

namespace Sundouleia.Gui.Components;

// Drawn Radar User Entity. Managed via DrawEntityFactory in Radar Tab.
// Handles the display of a drawn radar user.
public class DrawEntityRadarUser
{
    private readonly MainHub _hub;
    private readonly SundesmoManager _sundesmos; // Know if they are a pair or not.
    private readonly RequestsManager _requests; // Know if they are pending request or not.

    private readonly string _id;
    private bool _hovered = false;
    private bool _draftingRequest = false;
    private string _requestDesc = string.Empty;
    private RadarUser _user;
    public DrawEntityRadarUser(RadarUser user, SundouleiaMediator mediator, 
        MainHub hub, SundesmoManager sundesmos, RequestsManager requests)
    {
        _id = $"radar_user_{user.UID}";
        _user = user;

        _hub = hub;
        _sundesmos = sundesmos;
        _requests = requests;
    }

    public RadarUser User => _user;
    public bool IsLinked => _sundesmos.ContainsSundesmo(_user.UID);
    public string QuickDispName => _sundesmos.TryGetNickAliasOrUid(new(_user.UID), out var res) ? res : _user.AnonymousName;
    public void DrawListItem()
    {
        // get the current cursor pos
        var cursorPos = ImGui.GetCursorPosX();
        using var id = ImRaii.PushId(GetType() + _id);
        var childSize = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight());
        if (_draftingRequest)
            DrawOpenRequestEntry(childSize);
        else
            DrawNormalEntry(childSize);
    }

    private void DrawNormalEntry(Vector2 childSize)
    {
        using (CkRaii.Child(GetType() + _id, childSize, _hovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0, 5f))
        {
            ImUtf8.SameLineInner();
            DrawLeftSide();
            ImGui.SameLine();
            var posX = ImGui.GetCursorPosX();
            if (DrawInteractions())
                _draftingRequest = !_draftingRequest;

            ImGui.SameLine(posX);
            DrawNameText();
        }
        _hovered = ImGui.IsItemHovered();
    }

    private void DrawOpenRequestEntry(Vector2 childSize)
    {
        using (ImRaii.Group())
        {
            DrawNormalEntry(childSize);
            var sendRequestSize = CkGui.IconTextButtonSize(FAI.CloudUploadAlt, "Send");
            using (ImRaii.Group())
            {
                ImGui.SetNextItemWidth(childSize.X - sendRequestSize - ImUtf8.ItemInnerSpacing.X);
                ImGui.InputTextWithHint("##sendRequestMessage", "Attached Message (Optional)", ref _requestDesc, 100);
                ImUtf8.SameLineInner();
                if (CkGui.IconTextButton(FAI.CloudUploadAlt, "Send"))
                {
                    SendRequest();
                    _draftingRequest = false;
                    _requestDesc = string.Empty;
                }
            }
        }
        var min = ImGui.GetItemRectMin() - ImGuiHelpers.ScaledVector2(2);
        var max = ImGui.GetItemRectMax() + ImGuiHelpers.ScaledVector2(2);
        ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(ImGuiCol.TextDisabled), 5f);
    }

    private unsafe void DrawLeftSide()
    {
        ImGui.AlignTextToFramePadding();
        if (_user.IsValid)
        {
            CkGui.IconText(FAI.Eye, ImGuiColors.ParsedGreen);
            CkGui.AttachToolTip($"Nearby and Rendered / Visible!");
#if DEBUG
            if (ImGui.IsItemHovered())
            {
                TargetSystem.Instance()->FocusTarget = (GameObject*)_user.Address;
            }
            else
            {
                if (TargetSystem.Instance()->FocusTarget == (GameObject*)_user.Address)
                    TargetSystem.Instance()->FocusTarget = null;
            }
#endif
        }
        else
        {
            CkGui.IconText(FAI.EyeSlash, ImGuiColors.DalamudRed);
            CkGui.AttachToolTip($"Not Rendered, or Requesting is disabled. --COL--(Lurker)--COL--", ImGuiColors.DalamudGrey2);
        }
    }

    private void DrawNameText()
    {
        if (_sundesmos.GetUserOrDefault(new(_user.UID)) is { } match)
        {
            if (match.GetNickname() is { } nick)
                CkGui.TextFrameAligned(nick);
            else
                CkGui.TextFrameAligned(match.UserData.AliasOrUID);
            CkGui.ColorTextFrameAlignedInline($"({match.UserData.AnonTag})", ImGuiColors.DalamudGrey2);
        }
        else
        {
            ImGui.Text(User.AnonymousName);
        }
    }

    // True if we wanted to expand the area.
    private bool DrawInteractions()
    {
        var sendRequestSize = CkGui.IconTextButtonSize(FAI.CloudUploadAlt, "Send Request");
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        // If we are already paired, no interactions. (Add condition here for if we have a request pending already.)
        if (!_user.CanSendRequest || _sundesmos.ContainsSundesmo(_user.UID))
            return false;

        // Otherwise, draw out the send request button.
        var currentRightSide = windowEndX - sendRequestSize;
        
        ImGui.SameLine(currentRightSide);
        bool shifting = KeyMonitor.ShiftPressed();
        bool pressed = CkGui.IconTextButton(FAI.CloudUploadAlt, "Draft Request", isInPopup: true);
        CkGui.AttachToolTip("Draft a temporary request to this user." +
            "--SEP----COL--[SHIFT+L-Click] - --COL--Quick-Send request with no draft.", ImGuiColors.DalamudOrange);

        // Do quick request over draft request if desired.
        if (pressed && shifting)
            SendRequest();
        
        // Return if requesting to draft without quick-send.
        return pressed && !shifting;
    }

    private void DrawPopupIfValid()
    {
        var pos = ImGui.GetItemRectMin() + new Vector2(ImGui.GetItemRectSize().X, 0);
        ImGui.SetNextWindowPos(pos);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupRounding, 5f)
            .Push(ImGuiStyleVar.PopupBorderSize, 2f);
        using var color = ImRaii.PushColor(ImGuiCol.Border, CkColor.VibrantPink.Uint());
        using var popup = ImRaii.Popup("Send Request Popup", WFlags.AlwaysAutoResize | WFlags.NoMove);
        using var id = ImRaii.PushId($"popup_send_request_{_user.UID}");
        if (!popup) return;

        // NOTE: we could potentially add something like "groups to filter to on accept" but we can do this later.
        CkGui.ColorTextCentered($"Send Request to {User.AnonymousName}", ImGuiColors.ParsedGold);
        ImGui.SameLine(CkGui.IconTextButtonSize(FAI.CloudUploadAlt, "Send"));
        if (CkGui.IconTextButton(FAI.CloudUploadAlt, "Send"))
        {
            SendRequest();
            ImGui.CloseCurrentPopup();
        }

        CkGui.Separator(CkColor.VibrantPink.Uint());
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint("##sendRequestMessage", "Attached Message (Optional)", ref _requestDesc, 100);
    }

    private void SendRequest()
    {
        UiService.SetUITask(async () =>
        {
            var res = await _hub.UserSendRequest(new(new(_user.UID), true, _requestDesc));
            if (res.ErrorCode is SundouleiaApiEc.Success && res.Value is { } sentRequest)
            {
                Svc.Logger.Information($"Successfully sent sundesmo request to {User.AnonymousName}");
                _requests.AddRequest(sentRequest);
                _requestDesc = string.Empty;
                return;
            }
            // Notify failure.
            Svc.Logger.Warning($"Request to {User.AnonymousName} failed with error code {res.ErrorCode}");
        });
    }
}
