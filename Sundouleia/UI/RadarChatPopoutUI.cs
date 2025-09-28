using CkCommons;
using CkCommons.Gui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.WebAPI;
using Dalamud.Bindings.ImGui;

namespace Sundouleia.Gui;

public class RadarChatPopoutUI : WindowMediatorSubscriberBase
{
    private readonly ProfileService _plateService;
    private readonly PopoutRadarChatlog _popoutRadarChat;
    private bool _themePushed = false;

    public RadarChatPopoutUI(ILogger<RadarChatPopoutUI> logger, SundouleiaMediator mediator,
        ProfileService plateService, PopoutRadarChatlog popoutRadarChat) 
        : base(logger, mediator, "Global Chat Popout UI")
    {
        _plateService = plateService;
        _popoutRadarChat = popoutRadarChat;

        IsOpen = false;
        this.PinningClickthroughFalse();
        this.SetBoundaries(new Vector2(380, 500), new Vector2(700, 2000));
    }
    protected override void PreDrawInternal()
    {
        if (!_themePushed)
        {
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.331f, 0.081f, 0.169f, .803f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.579f, 0.170f, 0.359f, 0.828f));

            _themePushed = true;
        }
    }

    protected override void PostDrawInternal()
    {
        if (_themePushed)
        {
            ImGui.PopStyleColor(2);
            _themePushed = false;
        }
    }

    protected override void DrawInternal()
    {
        using var font = UiFontService.Default150Percent.Push();
        using var col = ImRaii.PushColor(ImGuiCol.ScrollbarBg, CkColor.LushPinkButton.Uint())
            .Push(ImGuiCol.ScrollbarGrab, CkColor.VibrantPink.Uint())
            .Push(ImGuiCol.ScrollbarGrabHovered, CkColor.VibrantPinkHovered.Uint());
        // grab the profile object from the profile service.
        var profile = _plateService.GetProfile(MainHub.OwnUserData);
        if (profile.Info.Disabled || !MainHub.Reputation.IsVerified)
        {
            ImGui.Spacing();
            CkGui.ColorTextCentered("Social Features have been Restricted", ImGuiColors.DalamudRed);
            ImGui.Spacing();
            CkGui.ColorTextCentered("Cannot View Global Chat because of this.", ImGuiColors.DalamudRed);
            return;
        }
        
        _popoutRadarChat.DrawChat(ImGui.GetContentRegionAvail());
    }
}
