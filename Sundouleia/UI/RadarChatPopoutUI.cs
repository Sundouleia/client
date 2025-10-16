using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Radar.Chat;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.Utils;
using Sundouleia.WebAPI;

namespace Sundouleia.Gui;

public class RadarChatPopoutUI : WindowMediatorSubscriberBase
{
    private readonly PopoutRadarChatlog _chat;
    private bool _themePushed = false;

    public RadarChatPopoutUI(ILogger<RadarChatPopoutUI> logger, SundouleiaMediator mediator, PopoutRadarChatlog chat) 
        : base(logger, mediator, "Radar Chat Popout UI")
    {
        _chat = chat;

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
        using var _ = ImRaii.PushColor(ImGuiCol.ScrollbarBg, CkColor.LushPinkButton.Uint())
            .Push(ImGuiCol.ScrollbarGrab, CkColor.VibrantPink.Uint())
            .Push(ImGuiCol.ScrollbarGrabHovered, CkColor.VibrantPinkHovered.Uint());
        
        var min = ImGui.GetCursorScreenPos();
        var max = min + ImGui.GetContentRegionAvail();
        var col = RadarChatLog.AccessBlocked ? ImGuiColors.DalamudGrey : ImGuiColors.DalamudWhite;
        // Add some CkRichText variant here later.
        CkGui.FontTextCentered($"Radar Chat - {RadarService.CurrZoneName}", UiFontService.Default150Percent, col);
        ImGui.Separator();

        // Restrict drawing the chat if their not verified or blocked from using it.
        var chatTL = ImGui.GetCursorScreenPos();

        // if not verified, show the chat, but disable it.
        _chat.SetDisabledStates(RadarChatLog.AccessBlocked, RadarChatLog.AccessBlocked);
        
        using (ImRaii.Group())
            _chat.DrawChat(ImGui.GetContentRegionAvail());
        if (RadarChatLog.NotVerified)
            CkGui.AttachToolTip("Cannot use chat, your account is not verified!");

        // If blocked, draw the warning.
        if (RadarChatLog.ChatBlocked)
        {
            ImGui.GetWindowDrawList().AddRectFilledMultiColor(min, max, 0x11000000, 0x11000000, 0x77000000, 0x77000000);
            ImGui.SetCursorScreenPos(chatTL);
            DrawChatUseBlockedWarning();
        }
        else if (RadarChatLog.NotVerified)
        {
            ImGui.GetWindowDrawList().AddRectFilledMultiColor(min, max, 0x11000000, 0x11000000, 0x77000000, 0x77000000);
            ImGui.SetCursorScreenPos(chatTL);
            DrawNotVerifiedHelp();
        }
    }

    private void DrawChatUseBlockedWarning()
    {
        var errorHeight = CkGui.CalcFontTextSize("A", UiFontService.UidFont).Y * 2 + CkGui.CalcFontTextSize("A", UiFontService.Default150Percent).Y + ImUtf8.ItemSpacing.Y * 2;
        var centerDrawHeight = (ImGui.GetContentRegionAvail().Y - ImUtf8.FrameHeightSpacing - errorHeight) / 2;

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + centerDrawHeight);
        CkGui.FontTextCentered("Blocked Via Bad Reputation!", UiFontService.UidFont, ImGuiColors.DalamudRed);
        CkGui.FontTextCentered("Unable to view chat anymore.", UiFontService.UidFont, ImGuiColors.DalamudRed);
        CkGui.FontTextCentered($"You have [{MainHub.Reputation.ChatStrikes}] radar chat strikes.", UiFontService.Default150Percent, ImGuiColors.DalamudRed);
    }

    private void DrawNotVerifiedHelp()
    {
        var errorHeight = CkGui.CalcFontTextSize("A", UiFontService.UidFont).Y * 2 + CkGui.CalcFontTextSize("A", UiFontService.Default150Percent).Y * 2 + ImUtf8.TextHeight * 3 + ImUtf8.ItemSpacing.Y * 6;
        var centerDrawHeight = (ImGui.GetContentRegionAvail().Y - errorHeight) / 2;

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + centerDrawHeight);
        CkGui.FontTextCentered("Must Claim Account To Chat!", UiFontService.UidFont, ImGuiColors.DalamudRed);
        CkGui.FontTextCentered("For Moderation & Safety Reasons", UiFontService.Default150Percent, ImGuiColors.DalamudGrey);
        CkGui.FontTextCentered("Only Verified Users Get Social Features.", UiFontService.Default150Percent, ImGuiColors.DalamudGrey);
        ImGui.Spacing();
        CkGui.CenterText("You can verify via Sundouleia's Discord Bot.");
        CkGui.CenterText("Verification is easy & doesn't interact with lodestone");
        CkGui.CenterText("or any other SE properties.");
        CkGui.FontTextCentered("Now In View-Only Mode", UiFontService.UidFont);
    }
}
