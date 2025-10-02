using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.Utils;
using Sundouleia.WebAPI;
using System.Globalization;
using System.Reflection;

namespace Sundouleia.Gui.MainWindow;

// Primary UI that hosts the bulk of Sundouleias Interface.
public class MainUI : WindowMediatorSubscriberBase
{
    private bool THEME_PUSHED = false;

    private readonly MainConfig _config;
    private readonly ServerConfigManager _serverConfigs;
    private readonly MainHub _hub;
    private readonly MainMenuTabs _tabMenu;
    private readonly SundesmoManager _sundesmos;
    private readonly TutorialService _guides;
    private readonly HomepageTab _homepage;
    private readonly WhitelistTab _whitelist;
    private readonly RequestsTab _requests;
    private readonly RadarTab _radar;
    private readonly RadarChatTab _radarChat;
    private readonly AccountTab _account;

    // Some temp values used for sending requests.
    private bool  _creatingRequest  = false;
    public string _uidToSentTo      = string.Empty;
    public string _requestMessage   = string.Empty;

    public MainUI(ILogger<MainUI> logger, SundouleiaMediator mediator, MainConfig config,
        ServerConfigManager serverConfigs, MainHub hub, MainMenuTabs tabMenu, SundesmoManager pairs,
        TutorialService guides, HomepageTab home, WhitelistTab whitelist, RequestsTab requests,
        RadarTab radar, RadarChatTab chat, AccountTab account) : base(logger, mediator, "###MainUI")
    {
        _config = config;
        _serverConfigs = serverConfigs;
        _hub = hub;
        _tabMenu = tabMenu;
        _sundesmos = pairs;
        _guides = guides;

        _homepage = home;
        _whitelist = whitelist;
        _requests = requests;
        _radar = radar;
        _radarChat = chat;
        _account = account;

        // display info about the folders
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        WindowName = $"Sundouleia Hush-Hush Testing ({ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision})###SundouleiaMainUI";
        Flags |= WFlags.NoDocking;

        this.PinningClickthroughFalse();
        this.SetBoundaries(new Vector2(380, 500), new Vector2(380, 2000));
        TitleBarButtons = new TitleBarButtonBuilder()
            .Add(FAI.Book, "Changelog", () => Mediator.Publish(new UiToggleMessage(typeof(ChangelogUI))))
            .Add(FAI.Cog, "Settings", () => Mediator.Publish(new UiToggleMessage(typeof(SettingsUi))))
            .AddTutorial(_guides, TutorialType.MainUi)
            .Build();
        
        // Default to open if the user desires for it to be open.
        if(_config.Current.OpenUiOnStartup)
            Toggle();

        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => IsOpen = true);
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = false);
    }

    public static Vector2 LastPos { get; private set; } = Vector2.Zero;
    public static Vector2 LastSize { get; private set; } = Vector2.Zero;

    // for tutorial, and for profile popouts.
    private Vector2 WindowPos => ImGui.GetWindowPos();
    private Vector2 WindowSize => ImGui.GetWindowSize();

    protected override void PreDrawInternal()
    {
        if (!THEME_PUSHED)
        {
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.331f, 0.081f, 0.169f, .803f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.579f, 0.170f, 0.359f, 0.828f));
            THEME_PUSHED = true;
        }
    }

    protected override void PostDrawInternal()
    {
        if (THEME_PUSHED)
        {
            ImGui.PopStyleColor(2);
            THEME_PUSHED = false;
        }
    }

    protected override void DrawInternal()
    {
        // get the width of the window content region we set earlier
        var winContentWidth = CkGui.GetWindowContentRegionWidth();

        // If unauthorized draw the unauthorized display, otherwise draw the server status.
        if (MainHub.ServerStatus is (ServerState.NoSecretKey or ServerState.VersionMisMatch or ServerState.Unauthorized))
        {
            DrawUIDHeader();
            var errorTitle = MainHub.ServerStatus switch
            {
                ServerState.NoSecretKey => "INVALID/NO KEY",
                ServerState.VersionMisMatch => "UNSUPPORTED VERSION",
                ServerState.Unauthorized => "UNAUTHORIZED",
                _ => "UNK ERROR"
            };
            var errorText = GetServerError();

            // push the notice that we are unsupported
            using (UiFontService.UidFont.Push())
            {
                var uidTextSize = ImGui.CalcTextSize(errorTitle);
                ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2 - uidTextSize.X / 2);
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(ImGuiColors.ParsedPink, errorTitle);
            }
            // the wrapped text explanation based on the error.
            CkGui.ColorTextWrapped(errorText, ImGuiColors.DalamudWhite);
        }
        else
        {
            DrawServerStatus();
        }

        // separate our UI once more.
        ImGui.Separator();

        // grab the current YPos, and update LastPos and LastSize with the current pos & size.
        // store a ref to the end of the content drawn.
        var menuComponentEnd = ImGui.GetCursorPosY();
        LastPos = ImGui.GetWindowPos();
        LastSize = ImGui.GetWindowSize();
        
        // If we are not connected, then do not draw any further.
        if (!MainHub.IsConnected)
            return;

        // If we are creating a request to send to another user, draw this first.
        if (_creatingRequest)
            DrawRequestCreator(winContentWidth, ImGui.GetStyle().ItemInnerSpacing.X);
        
        // draw the bottom tab bar
        _tabMenu.Draw(winContentWidth);

        // Show content based on the selected tab.
        // (Note: This used to have Using ImRaii.PushId here but never really saw a reason to need it,
        // so removed it for now. if we need it later just reference GSpeak.)
        switch (_tabMenu.TabSelection)
        {
            case MainMenuTabs.SelectedTab.Homepage:
                _homepage.DrawHomepageSection();
                break;
            case MainMenuTabs.SelectedTab.Whitelist:
                _whitelist.DrawWhitelistSection();
                break;
            case MainMenuTabs.SelectedTab.Requests:
                _requests.DrawRequestsSection();
                break;
            case MainMenuTabs.SelectedTab.Radar:
                _radar.DrawRadarSection();
                break;
            case MainMenuTabs.SelectedTab.RadarChat:
                _radarChat.DrawChatSection();
                break;
            case MainMenuTabs.SelectedTab.Account:
                _account.DrawAccountSection();
                break;
        }
    }

    public void DrawRequestCreator(float availableXWidth, float spacingX)
    {
        var buttonSize = CkGui.IconTextButtonSize(FAI.Upload, "Send Pair Request");
        ImGui.SetNextItemWidth(availableXWidth - buttonSize - spacingX);
        // Let the client say who they want the request to go to.
        ImGui.InputTextWithHint("##otherUid", "Other players UID/Alias", ref _uidToSentTo, 20);
        ImUtf8.SameLineInner();

        // Disable the add button if they are already added or nothing is in the field. (might need to also account for alias here)
        var allowSend = !string.IsNullOrEmpty(_uidToSentTo) && !_sundesmos.ContainsSundesmo(_uidToSentTo);
        if (CkGui.IconTextButton(FAI.Upload, "Send", buttonSize, false, !allowSend))
        {
            UiService.SetUITask(async () =>
            {
                await _hub.UserSendRequest(new(new(_uidToSentTo), false, _requestMessage));
                _uidToSentTo = string.Empty;
                _requestMessage = string.Empty;
                _creatingRequest = false;
            });
        }
        if (!string.IsNullOrEmpty(_uidToSentTo))
            CkGui.AttachToolTip($"Send Pair Request to {_uidToSentTo}");

        // draw a attached message field as well if they want.
        ImGui.SetNextItemWidth(availableXWidth);
        ImGui.InputTextWithHint("##pairAddOptionalMessage", "Attach Msg to Request (Optional)", ref _requestMessage, 100);
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.AttachingMessages, ImGui.GetWindowPos(), ImGui.GetWindowSize(), () =>
        {
            _creatingRequest = !_creatingRequest;
            _tabMenu.TabSelection = MainMenuTabs.SelectedTab.Account;
        });
        ImGui.Separator();
    }

    private void DrawUIDHeader()
    {
        var uidText = SundouleiaEx.GetUidText();
        using (UiFontService.UidFont.Push())
        {
            var uidTextSize = ImGui.CalcTextSize(uidText);
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X) / 2 - uidTextSize.X / 2);
            ImGui.TextColored(SundouleiaEx.UidColor(), uidText);
        }

        // if we are connected
        if (MainHub.IsConnected)
        {
            CkGui.CopyableDisplayText(MainHub.DisplayName);
            if (!string.Equals(MainHub.DisplayName, MainHub.UID, StringComparison.Ordinal))
            {
                var originalTextSize = ImGui.CalcTextSize(MainHub.UID);
                ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X) / 2 - originalTextSize.X / 2);
                ImGui.TextColored(SundouleiaEx.UidColor(), MainHub.UID);
                CkGui.CopyableDisplayText(MainHub.UID);
            }
        }
    }


    /// <summary> Draws the current status of the server, including the number of people online. </summary>
    private void DrawServerStatus()
    {
        var windowPadding = ImGui.GetStyle().WindowPadding;
        var addUserIcon = FAI.UserPlus;
        var connectionButtonSize = CkGui.IconButtonSize(FAI.Link);
        var addUserButtonSize = CkGui.IconButtonSize(addUserIcon);

        var userCount = MainHub.OnlineUsers.ToString(CultureInfo.InvariantCulture);
        var userSize = ImGui.CalcTextSize(userCount);
        var textSize = ImGui.CalcTextSize("Users Online");
        var serverText = "Main Sundouleia Server";
        var shardTextSize = ImGui.CalcTextSize(serverText);
        var totalHeight = ImGui.GetTextLineHeight()*2 + ImGui.GetStyle().ItemSpacing.Y;

        // create a table
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y);
        using (ImRaii.Table("ServerStatusMainUI", 3))
        {
            // define the column lengths.
            ImGui.TableSetupColumn("##addUser", ImGuiTableColumnFlags.WidthFixed, addUserButtonSize.X);
            ImGui.TableSetupColumn("##serverState", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##connectionButton", ImGuiTableColumnFlags.WidthFixed, connectionButtonSize.X);

            // draw the add user button
            ImGui.TableNextColumn();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (totalHeight - addUserButtonSize.Y) / 2);
            if (CkGui.IconButton(addUserIcon, disabled: !MainHub.IsConnected))
                _creatingRequest = !_creatingRequest;
            CkGui.AttachToolTip("Add New User to Whitelist");
            _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.AddingUsers, ImGui.GetWindowPos(), ImGui.GetWindowSize(), () => _creatingRequest = !_creatingRequest);

            // in the next column, draw the centered status.
            ImGui.TableNextColumn();

            if (MainHub.IsConnected)
            {
                // fancy math shit for clean display, adjust when moving things around
                ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth())
                    / 2 - (userSize.X + textSize.X) / 2 - ImGui.GetStyle().ItemSpacing.X / 2);
                using (ImRaii.Group())
                {
                    ImGui.TextColored(ImGuiColors.ParsedPink, userCount);
                    ImGui.SameLine();
                    ImGui.TextUnformatted("Users Online");
                }
                _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.InitialWelcome, WindowPos, WindowSize);

            }
            // otherwise, if we are not connected, display that we aren't connected.
            else
            {
                ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth())
                    / 2 - ImGui.CalcTextSize("Not connected to any server").X / 2 - ImGui.GetStyle().ItemSpacing.X / 2);
                ImGui.TextColored(ImGuiColors.DalamudRed, "Not connected to any server");
            }

            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y);
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth()) / 2 - shardTextSize.X / 2);
            ImGui.TextUnformatted(serverText);

            // draw the connection link button
            ImGui.TableNextColumn();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (totalHeight - addUserButtonSize.Y) / 2);
            // now we need to display the connection link button beside it.
            var color = SundouleiaEx.ServerStateColor();
            var connectedIcon = SundouleiaEx.ServerStateIcon(MainHub.ServerStatus);

            // we need to turn the button from the connected link to the disconnected link.
            using (ImRaii.PushColor(ImGuiCol.Text, color))
            {
                if (CkGui.IconButton(connectedIcon, disabled: MainHub.ServerStatus is ServerState.Reconnecting or ServerState.Disconnecting))
                {
                    if (MainHub.IsConnected)
                    {
                        // If we are connected, we want to disconnect.
                        _serverConfigs.AccountStorage.FullPause = true;
                        _serverConfigs.Save();
                        _ = _hub.Disconnect(ServerState.Disconnected);
                    }
                    else if (MainHub.ServerStatus is (ServerState.Disconnected or ServerState.Offline))
                    {
                        _serverConfigs.AccountStorage.FullPause = false;
                        _serverConfigs.Save();
                        _ = _hub.Connect();
                    }
                }
            }
            CkGui.AttachToolTip($"{(MainHub.IsConnected ? "Disconnect from" : "Connect to")} {MainHub.MAIN_SERVER_NAME}--SEP--Current Status: {MainHub.ServerStatus}");
            _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.ConnectionState, WindowPos, WindowSize, () => _tabMenu.TabSelection = MainMenuTabs.SelectedTab.Homepage);
        }
    }


    /// <summary> Retrieves the various server error messages based on the current server state. </summary>
    /// <returns> The error message of the server.</returns>
    private string GetServerError()
    {
        return MainHub.ServerStatus switch
        {
            ServerState.Connecting => "Attempting to connect to the server.",
            ServerState.Reconnecting => "Connection to server interrupted, attempting to reconnect to the server.",
            ServerState.Disconnected => "Currently disconnected from the Sundouleia server.",
            ServerState.Disconnecting => "Disconnecting from server",
            ServerState.Offline => "The Sundouleia server is currently offline.",
            ServerState.Connected => string.Empty,
            ServerState.ConnectedDataSynced => string.Empty,
            ServerState.NoSecretKey => "No secret key is set for this current character. " +
            "\nTo create UID's for your alt characters, be sure to claim your account in the CK discord." +
            "\n\nOnce you have inserted a secret key, reload the plugin to be registered with the servers.",
            ServerState.VersionMisMatch => "Current Ver: " + MainHub.ClientVerString + Environment.NewLine
            + "Expected Ver: " + MainHub.ExpectedVerString +
            "\n\nThis Means that your client is outdated, and you need to update it." +
            "\n\nIf there is no update Available, then this message Likely Means Cordy is running some last minute tests " +
            "to ensure everyone doesn't crash with the latest update. Hang in there!",
            ServerState.Unauthorized => "You are Unauthorized to access Sundouleia Servers with this account due to an " +
            "Unauthorized Access. \n\nDetails:\n" + MainHub.AuthFailureMessage,
            _ => string.Empty
        };
    }

    public override void OnClose()
    {
        Mediator.Publish(new ClosedMainUiMessage());
        base.OnClose();
    }
}
