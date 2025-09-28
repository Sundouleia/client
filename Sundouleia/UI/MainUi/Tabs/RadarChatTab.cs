using CkCommons.Gui;
using Dalamud.Interface.Colors;
using Sundouleia.Gui.Components;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.Utils;
using Sundouleia.WebAPI;
using Dalamud.Bindings.ImGui;
using OtterGui;
using Dalamud.Interface.Utility.Raii;

namespace Sundouleia.Gui.MainWindow;

public class RadarChatTab
{
    private readonly MainMenuTabs _tabMenu;
    private readonly RadarChatLog _chat;
    private readonly TutorialService _guides;

    public RadarChatTab(RadarChatLog chat, MainMenuTabs tabMenu, TutorialService guides)
    {
        _tabMenu = tabMenu;
        _chat = chat;
        _guides = guides;
    }

    public void DrawChatSection()
    {
        // Add some CkRichText variant here later.
        ImGuiUtil.Center("Radar Chat ( World X, Territory XX)");
        ImGui.Separator();

        // Restrict drawing the chat if their not verified or blocked from using it.
        if (!MainHub.Reputation.ChatUsage)
        {
            CkGui.CenterColorTextAligned("Blocked by Account Reputation!", ImGuiColors.DalamudRed);
            CkGui.CenterColorTextAligned("Unable to view chat anymore.", ImGuiColors.DalamudRed);
            CkGui.CenterTextAligned($"You have [{MainHub.Reputation.ChatStrikes}] radar chat strikes.");
            return;
        }

        // if not verified, show the chat, but disable it.
        using var dis = ImRaii.Disabled(!MainHub.Reputation.IsVerified);
        using (ImRaii.Group())
        {
            _chat.DrawChat(ImGui.GetContentRegionAvail());
        }
        CkGui.AttachToolTip("Only verified accounts are able to use social features!" +
            "--SEP--You can verify your account via the Sundouleia Discord Bot." +
            "--NL--Verification is between discord and your game, no lodestone!", ImGuiColors.DalamudYellow);
        
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.RadarChat, ImGui.GetWindowPos(), ImGui.GetWindowSize());
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.ChatUserExamine, ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            () => _tabMenu.TabSelection = MainMenuTabs.SelectedTab.Account);
    }
}

