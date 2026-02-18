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

namespace Sundouleia.Gui.MainWindow;

public class RadarChatTab
{
    private readonly MainMenuTabs _tabMenu;
    private readonly RadarChatLog _chat;
    private readonly LocationSvc _service;
    private readonly TutorialService _guides;

    public RadarChatTab(MainMenuTabs tabs, RadarChatLog chat, LocationSvc service, TutorialService guides)
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
        var text = isInside ? "Chat Disabled Indoors" : $"Radar Chat - {LocationSvc.Current.TerritoryName}";

        // Add some CkRichText variant here later.
        CkGui.FontTextCentered(text, Fonts.Default150Percent, col);
        CkGui.ColorTextCentered(PlayerContent.TerritoryIntendedUse.ToString(), ImGuiColors.DalamudOrange);
        ImGui.Separator();

        // Restrict drawing the chat if their not verified or blocked from using it.
        var chatTL = ImGui.GetCursorScreenPos();
        var disable = RadarChatLog.AccessBlocked || isInside;
        // if not verified, show the chat, but disable it.
        _chat.SetDisabledStates(disable, disable);
        DrawChatContents();
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.RadarChatRules, MainUI.LastPos, MainUI.LastSize);
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.RadarChatPrivacy, MainUI.LastPos, MainUI.LastSize);
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.ChatUserExamine, MainUI.LastPos, MainUI.LastSize, () => _tabMenu.TabSelection = MainMenuTabs.SelectedTab.Homepage);

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
            using var style = ImRaii.PushStyle(ImGuiStyleVar.ScrollbarSize, 8f);
            _chat.DrawChat(ImGui.GetContentRegionAvail());
        }
        if (RadarChatLog.NotVerified)
            CkGui.AttachToolTip("Cannot use chat, your account is not verified!");
        // Attach tutorials.
    }

    private void DrawChatUseBlockedWarning()
    {
        var errorHeight = CkGui.CalcFontTextSize("A", Fonts.UidFont).Y * 2 + CkGui.CalcFontTextSize("A", Fonts.Default150Percent).Y + ImUtf8.ItemSpacing.Y * 2;
        var centerDrawHeight = (ImGui.GetContentRegionAvail().Y - ImUtf8.FrameHeightSpacing - errorHeight) / 2;

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + centerDrawHeight);
        CkGui.FontTextCentered("Blocked Via Bad Reputation!", Fonts.UidFont, ImGuiColors.DalamudRed);
        CkGui.FontTextCentered("Unable to view chat anymore.", Fonts.UidFont, ImGuiColors.DalamudRed);
        CkGui.FontTextCentered($"You have [{MainHub.Reputation.ChatStrikes}] radar chat strikes.", Fonts.Default150Percent, ImGuiColors.DalamudRed);
    }

    private void DrawNotVerifiedHelp()
    {
        var errorHeight = CkGui.CalcFontTextSize("A", Fonts.UidFont).Y * 2 + CkGui.CalcFontTextSize("A", Fonts.Default150Percent).Y * 2 + ImUtf8.TextHeight * 3 + ImUtf8.ItemSpacing.Y * 6;
        var centerDrawHeight = (ImGui.GetContentRegionAvail().Y - errorHeight) / 2;

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + centerDrawHeight);
        CkGui.FontTextCentered("Must Claim Account To Chat!", Fonts.UidFont, ImGuiColors.DalamudRed);
        CkGui.FontTextCentered("For Moderation & Safety Reasons", Fonts.Default150Percent, ImGuiColors.DalamudGrey);
        CkGui.FontTextCentered("Only Verified Users Get Social Features.", Fonts.Default150Percent, ImGuiColors.DalamudGrey);
        ImGui.Spacing();
        CkGui.CenterText("You can verify via Sundouleia's Discord Bot.");
        CkGui.CenterText("Verification is easy & doesn't interact with lodestone");
        CkGui.CenterText("or any other SE properties.");
    }
}

