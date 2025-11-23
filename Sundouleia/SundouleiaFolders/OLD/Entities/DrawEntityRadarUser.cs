using CkCommons;
using CkCommons.Gui;
using CkCommons.Helpers;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;

namespace Sundouleia.Gui.Components;

// Drawn Radar User Entity. Managed via DrawEntityFactory in Radar Tab.
// Handles the display of a drawn radar user.
public class DrawEntityRadarUser : IDrawEntity<RadarUser>
{
    private readonly MainHub _hub;
    private readonly SundesmoManager _sundesmos; // Know if they are a pair or not.
    private readonly RequestsManager _requests; // Know if they are pending request or not.

    private bool _hovered = false;
    private bool _draftingRequest = false;
    private string _requestDesc = string.Empty;

    // parent folder could go here if we ever wanted to support drag drop stuff.
    public RadarUser Item { get; init; }

    public DrawEntityRadarUser(RadarUser user, MainHub hub, SundesmoManager sundesmos, RequestsManager requests)
    {
        DistinctId = $"radarItem_{user.UID}";
        Item = user;

        _hub = hub;
        _sundesmos = sundesmos;
        _requests = requests;
    }

    public string DistinctId { get; init; }
    public string DisplayName => _sundesmos.TryGetNickAliasOrUid(Item.UID, out var res) ? res : Item.AnonymousName;
    public string EntityId => Item.UID;
    public bool IsLinked => _sundesmos.ContainsSundesmo(Item.UID);
    public bool Draw(bool _)
    {
        // get the current cursor pos
        using var id = ImRaii.PushId(DistinctId);
        var childSize = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight());
        if (_draftingRequest)
            DrawOpenRequestEntry(childSize);
        else
            DrawNormalEntry(childSize);
        // we dont care about selections for radar users.
        return false;
    }

    private void DrawNormalEntry(Vector2 childSize)
    {
        using (CkRaii.Child(DistinctId, childSize, _hovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0, 5f))
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
        if (Item.IsValid)
        {
            CkGui.IconText(FAI.Eye, ImGuiColors.ParsedGreen);
            CkGui.AttachToolTip($"Nearby and Rendered / Visible!");
#if DEBUG
            if (ImGui.IsItemHovered())
            {
                TargetSystem.Instance()->FocusTarget = (GameObject*)Item.Address;
            }
            else
            {
                if (TargetSystem.Instance()->FocusTarget == (GameObject*)Item.Address)
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
        if (_sundesmos.GetUserOrDefault(new(Item.UID)) is { } match)
        {
            if (match.GetNickname() is { } nick)
                CkGui.TextFrameAligned(nick);
            else
                CkGui.TextFrameAligned(match.UserData.AliasOrUID);
            CkGui.ColorTextFrameAlignedInline($"({match.UserData.AnonTag})", ImGuiColors.DalamudGrey2);
        }
        else
        {
            ImGui.Text(Item.AnonymousName);
        }
    }

    // True if we wanted to expand the area.
    private bool DrawInteractions()
    {
        var sendRequestSize = CkGui.IconTextButtonSize(FAI.CloudUploadAlt, "Send Request");
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        // If we are already paired, no interactions. (Add condition here for if we have a request pending already.)
        if (!Item.CanSendRequest || _requests.Outgoing.Any(r => r.RecipientUID == EntityId) || _sundesmos.ContainsSundesmo(Item.UID))
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

    private void SendRequest()
    {
        UiService.SetUITask(async () =>
        {
            var res = await _hub.UserSendRequest(new(new(Item.UID), true, _requestDesc));
            if (res.ErrorCode is SundouleiaApiEc.Success && res.Value is { } sentRequest)
            {
                Svc.Logger.Information($"Successfully sent sundesmo request to {Item.AnonymousName}");
                _requests.AddNewRequest(sentRequest);
                _requestDesc = string.Empty;
                return;
            }
            // Notify failure.
            Svc.Logger.Warning($"Request to {Item.AnonymousName} failed with error code {res.ErrorCode}");
        });
    }
}
