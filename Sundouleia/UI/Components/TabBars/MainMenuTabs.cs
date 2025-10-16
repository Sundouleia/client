using CkCommons;
using CkCommons.Gui;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.PlayerClient;
using Sundouleia.Radar.Chat;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;

namespace Sundouleia.Gui.Components;

public class MainMenuTabs : IconTabBar<MainMenuTabs.SelectedTab>
{
    public enum SelectedTab
    {
        Homepage,
        Requests,
        Whitelist,
        Radar,
        RadarChat,
        Account,
    }

    public override SelectedTab TabSelection
    {
        get => base.TabSelection;
        set
        {
            _config.Current.MainUiTab = value;
            _config.Save();
            base.TabSelection = value;
        }
    }

    private readonly MainConfig _config;
    private readonly SundouleiaMediator _mediator;
    public MainMenuTabs(SundouleiaMediator mediator, MainConfig config, TutorialService guides)
    {
        _mediator = mediator;
        _config = config;
        TabSelection = _config.Current.MainUiTab;

        AddDrawButton(FontAwesomeIcon.Home, SelectedTab.Homepage, "Homepage",
            () => guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.Homepage, ImGui.GetWindowPos(), ImGui.GetWindowSize(), () => TabSelection = SelectedTab.Whitelist));

        AddDrawButton(FontAwesomeIcon.Inbox, SelectedTab.Requests, "Incoming / Outgoing Requests",
            () => guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.Requests, ImGui.GetWindowPos(), ImGui.GetWindowSize()));

        AddDrawButton(FontAwesomeIcon.PeopleArrows, SelectedTab.Whitelist, "User Whitelist", 
            () => guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.Whitelist, ImGui.GetWindowPos(), ImGui.GetWindowSize()));

        AddDrawButton(FontAwesomeIcon.BroadcastTower, SelectedTab.Radar, "Connect easily with others!",
            () => guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.Radar, ImGui.GetWindowPos(), ImGui.GetWindowSize(), () => TabSelection = SelectedTab.RadarChat));

        AddDrawButton(FontAwesomeIcon.Comments, SelectedTab.RadarChat, "Chat with other sundouleia users nearby!",
            () => guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.RadarChat, ImGui.GetWindowPos(), ImGui.GetWindowSize()));

        AddDrawButton(FontAwesomeIcon.UserCircle, SelectedTab.Account, "Account Settings",
            () => guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.AccountPage, ImGui.GetWindowPos(), ImGui.GetWindowSize(), () => TabSelection = SelectedTab.Account));

        TabSelectionChanged += (oldTab, newTab) => _mediator.Publish(new MainWindowTabChangeMessage(newTab));
    }

    public override void Draw(float availableWidth)
    {
        if (_tabButtons.Count == 0)
            return;

        using var color = ImRaii.PushColor(ImGuiCol.Button, ImGui.ColorConvertFloat4ToU32(new(0, 0, 0, 0)));
        var spacing = ImGui.GetStyle().ItemSpacing;
        var buttonX = (availableWidth - (spacing.X * (_tabButtons.Count - 1))) / _tabButtons.Count;
        var buttonY = CkGui.IconButtonSize(FontAwesomeIcon.Pause).Y;
        var buttonSize = new Vector2(buttonX, buttonY);
        var drawList = ImGui.GetWindowDrawList();
        var underlineColor = ImGui.GetColorU32(ImGuiCol.Separator);

        ImGuiHelpers.ScaledDummy(spacing.Y / 2f);

        foreach (var tab in _tabButtons)
            DrawTabButton(tab, buttonSize, spacing, drawList);

        // advance to the new line and dispose of the button color.
        ImGui.NewLine();
        color.Dispose();

        ImGuiHelpers.ScaledDummy(3f);
        ImGui.Separator();
    }

    protected override void DrawTabButton(TabButtonDefinition tab, Vector2 buttonSize, Vector2 spacing, ImDrawListPtr drawList)
    {
        var x = ImGui.GetCursorScreenPos();

        var isDisabled = IsTabDisabled(tab.TargetTab);
        using (ImRaii.Disabled(isDisabled))
        {

            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button(tab.Icon.ToIconString(), buttonSize))
                    TabSelection = tab.TargetTab;
            }

            ImGui.SameLine();
            var xPost = ImGui.GetCursorScreenPos();

            if (EqualityComparer<SelectedTab>.Default.Equals(TabSelection, tab.TargetTab))
            {
                drawList.AddLine(
                    x with { Y = x.Y + buttonSize.Y + spacing.Y },
                    xPost with { Y = xPost.Y + buttonSize.Y + spacing.Y, X = xPost.X - spacing.X },
                    ImGui.GetColorU32(ImGuiCol.Separator), 2f);
            }

            if (tab.TargetTab is SelectedTab.RadarChat && _config.Current.RadarShowUnreadBubble)
            {
                if (RadarChatLog.NewMsgCount > 0)
                {
                    var newMsgTxtPos = new Vector2(x.X + buttonSize.X / 2, x.Y - spacing.Y);
                    var newMsgTxt = RadarChatLog.NewMsgCount > 99 ? "99+" : RadarChatLog.NewMsgCount.ToString();
                    var newMsgCol = RadarChatLog.NewCorbyMsg ? ImGuiColors.ParsedPink : ImGuiColors.ParsedGold;
                    drawList.OutlinedFont(newMsgTxt, newMsgTxtPos, newMsgCol.ToUint(), 0xFF000000, 1);
                }
            }
        }
        CkGui.AttachToolTip(tab.Tooltip);

        // invoke action if we should.
        tab.CustomAction?.Invoke();
    }

}
