using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using OtterGui.Text;
using Sundouleia.Gui.Components;
using Sundouleia.Radar.Chat;
using Sundouleia.Services;
using Sundouleia.Services.Tutorial;
using Sundouleia.WebAPI;
using System.Linq.Expressions;

namespace Sundouleia.Gui.MainWindow;

public class RadarChatTab
{
    private readonly MainMenuTabs _tabMenu;
    private readonly RadarChatLog _chat;
    private readonly RadarService _service;
    private readonly TutorialService _guides;

    public RadarChatTab(MainMenuTabs tabs, RadarChatLog chat, RadarService service, TutorialService guides)
    {
        _tabMenu = tabs;
        _chat = chat;
        _service = service;
        _guides = guides;
    }

    public unsafe void DrawSection()
    {
        var min = ImGui.GetCursorScreenPos();
        var max = min + ImGui.GetContentRegionAvail();
        var col = RadarChatLog.AccessBlocked ? ImGuiColors.DalamudGrey : ImGuiColors.DalamudWhite;
        var isInside = HousingManager.Instance()->IsInside();
        var text = isInside ? "Chat Disabled Indoors" : $"Radar Chat - {RadarService.CurrZoneName}";

        // Add some CkRichText variant here later.
        CkGui.FontTextCentered(text, UiFontService.Default150Percent, col);
        ImGui.Separator();

        // Restrict drawing the chat if their not verified or blocked from using it.
        var chatTL = ImGui.GetCursorScreenPos();
        var disable = RadarChatLog.AccessBlocked || isInside;
        // if not verified, show the chat, but disable it.
        _chat.SetDisabledStates(disable, disable);
        DrawChatContents();

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

    private void DrawChatContents()
    {
        using (ImRaii.Group())
        {
            _chat.DrawChat(ImGui.GetContentRegionAvail());
        }
        if (RadarChatLog.NotVerified)
            CkGui.AttachToolTip("Cannot use chat, your account is not verified!");
        // Attach tutorials.
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.RadarChat, ImGui.GetWindowPos(), ImGui.GetWindowSize());
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.ChatUserExamine, ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            () => _tabMenu.TabSelection = MainMenuTabs.SelectedTab.Account);
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

