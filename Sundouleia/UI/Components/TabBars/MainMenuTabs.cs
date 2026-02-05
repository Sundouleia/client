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
        BasicWhitelist,
        GroupWhitelist,
        Radar,
        RadarChat,
    }

    public override SelectedTab TabSelection
    {
        get => base.TabSelection;
        set
        {
            _config.Current.CurMainUiTab = value;
            _config.Save();
            base.TabSelection = value;
        }
    }

    private readonly SundouleiaMediator _mediator;
    private readonly MainConfig _config;
    private readonly RequestsManager _requests;
    public MainMenuTabs(SundouleiaMediator mediator, MainConfig config, RequestsManager requests, TutorialService guides)
    {
        _mediator = mediator;
        _config = config;
        _requests = requests;
        TabSelection = _config.Current.CurMainUiTab;

        AddDrawButton(FontAwesomeIcon.Home, SelectedTab.Homepage, "Homepage",
            () => guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.Homepage, ImGui.GetWindowPos(), ImGui.GetWindowSize(), () => TabSelection = SelectedTab.BasicWhitelist));

        AddDrawButton(FontAwesomeIcon.Inbox, SelectedTab.Requests, "Incoming / Outgoing Requests",
            () => guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.Requests, ImGui.GetWindowPos(), ImGui.GetWindowSize(), () => TabSelection = SelectedTab.Radar));
        
        AddDrawButton(FontAwesomeIcon.PeopleArrows, SelectedTab.BasicWhitelist, "Whitelist", 
            () => guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.Whitelist, ImGui.GetWindowPos(), ImGui.GetWindowSize()));

        AddDrawButton(FAI.PeopleGroup, SelectedTab.GroupWhitelist, "Sundesmo Groups");

        AddDrawButton(FontAwesomeIcon.BroadcastTower, SelectedTab.Radar, "Connect easily with others!",
            () => guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.Radar, ImGui.GetWindowPos(), ImGui.GetWindowSize()));

        AddDrawButton(FontAwesomeIcon.Comments, SelectedTab.RadarChat, "Chat with others in similar areas!",
            () => guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.RadarChat, ImGui.GetWindowPos(), ImGui.GetWindowSize()));
         
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

        using var id = ImRaii.PushId(tab.TargetTab.ToString());
        using (ImRaii.Disabled(isDisabled))
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
                if (ImGui.Button(tab.Icon.ToIconString(), buttonSize))
                    TabSelection = tab.TargetTab;

            ImGui.SameLine();
            var xPost = ImGui.GetCursorScreenPos();

            if (EqualityComparer<SelectedTab>.Default.Equals(TabSelection, tab.TargetTab))
            {
                drawList.AddLine(
                    x with { Y = x.Y + buttonSize.Y + spacing.Y },
                    xPost with { Y = xPost.Y + buttonSize.Y + spacing.Y, X = xPost.X - spacing.X },
                    ImGui.GetColorU32(ImGuiCol.Separator), 2f);
            }

            if (tab.TargetTab is SelectedTab.Requests && _config.Current.RequestNotifiers.HasAny(RequestAlertKind.Bubble))
            {
                if (_requests.Incoming.Count > 0)
                {
                    var newMsgTxtPos = new Vector2(x.X + buttonSize.X * .65f, x.Y - spacing.Y);
                    var newMsgTxt = _requests.Incoming.Count > 99 ? "99+" : _requests.Incoming.Count.ToString();
                    drawList.OutlinedFont(newMsgTxt, newMsgTxtPos, ImGuiColors.TankBlue.ToUint(), 0xFF000000, 1);
                }
            }
            // For Radar Chat.
            else if (tab.TargetTab is SelectedTab.RadarChat && _config.Current.RadarShowUnreadBubble)
            {
                if (RadarChatLog.NewMsgCount > 0)
                {
                    var newMsgTxtPos = new Vector2(x.X + buttonSize.X / 2, x.Y - spacing.Y);
                    var newMsgTxt = RadarChatLog.NewMsgCount > 99 ? "99+" : RadarChatLog.NewMsgCount.ToString();
                    var newMsgCol = RadarChatLog.NewCorbyMsg ? ImGuiColors.ParsedPink : SundColor.Gold.Vec4();
                    drawList.OutlinedFont(newMsgTxt, newMsgTxtPos, newMsgCol.ToUint(), 0xFF000000, 1);
                }
            }
        }
        CkGui.AttachToolTip(tab.Tooltip);

        // invoke action if we should.
        tab.CustomAction?.Invoke();
    }

}
