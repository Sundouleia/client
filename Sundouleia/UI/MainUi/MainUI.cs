using CkCommons;
using CkCommons.Classes;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using OtterGuiInternal;
using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.Utils;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;
using System.Globalization;
using System.Reflection;
using TerraFX.Interop.Windows;
using static Lumina.Data.Parsing.Uld.NodeData;

namespace Sundouleia.Gui.MainWindow;

// Primary UI that hosts the bulk of Sundouleias Interface.
public class MainUI : WindowMediatorSubscriberBase
{
    // Note that if you ever change this width you will need to also adjust the display width for the account page display.
    public const float MAIN_UI_WIDTH = 380f;

    private bool THEME_PUSHED = false;

    private readonly MainConfig _config;
    private readonly AccountManager _account;
    private readonly MainHub _hub;
    private readonly MainMenuTabs _tabMenu;
    private readonly HomeTab _homeTab;
    private readonly WhitelistTabs _whitelist;
    private readonly RadarTab _radar;
    private readonly RadarChatTab _radarChat;
    private readonly RequestsTab _requestsTab;
    private readonly RequestsManager _requests;
    private readonly SundesmoManager _sundesmos;
    private readonly TutorialService _guides;
    private readonly SidePanelService _stickyService;

    // Some temp values used for sending requests.
    private bool  _creatingRequest  = false;
    public string _uidToSentTo      = string.Empty;
    public string _requestMessage   = string.Empty;

    public MainUI(ILogger<MainUI> logger, SundouleiaMediator mediator, MainConfig config,
        AccountManager account, MainHub hub, MainMenuTabs tabMenu, HomeTab homeTab,
        RequestsTab requestsTab, WhitelistTabs whitelist, RadarTab radar, RadarChatTab chat,
        RequestsManager requests, SundesmoManager sundesmos, TutorialService guides,
        SidePanelService stickyService)
        : base(logger, mediator, "###Sundouleia_MainUI")
    {
        _config = config;
        _account = account;
        _hub = hub;
        _tabMenu = tabMenu;
        _homeTab = homeTab;
        _whitelist = whitelist;
        _radar = radar;
        _radarChat = chat;
        _requestsTab = requestsTab;
        _requests = requests;
        _sundesmos = sundesmos;
        _guides = guides;
        _stickyService = stickyService;

        // display info about the folders
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        WindowName = $"Sundouleia Hush-Hush Testing ({ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision})###SundouleiaMainUI";
        Flags |= WFlags.NoDocking;

        this.PinningClickthroughFalse();
        this.SetBoundaries(new(380, 500), new(380, 2000));
        TitleBarButtons = new TitleBarButtonBuilder()
            .Add(FAI.Cog, "Settings", () => Mediator.Publish(new UiToggleMessage(typeof(SettingsUi))))
            .AddTutorial(_guides, TutorialType.MainUi)
            .Build();

        // Default to open if the user desires for it to be open.
        if (_config.Current.OpenUiOnStartup)
            Toggle();
        // Update the tab menu selection.
        _tabMenu.TabSelection = _config.Current.CurMainUiTab;

        Mediator.Subscribe<OpenSundesmoSidePanel>(this, _ =>
        {
            IsOpen = true;
            _tabMenu.TabSelection = MainMenuTabs.SelectedTab.BasicWhitelist;
        });
        Mediator.Subscribe<OpenMainUiTab>(this, _ =>
        {
            IsOpen = true;
            _tabMenu.TabSelection = _.ToOpen;
        });

        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => IsOpen = true);
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = false);
    }

    public static Vector2 LastPos { get; private set; } = Vector2.Zero;
    public static Vector2 LastSize { get; private set; } = Vector2.Zero;
    public static Vector2 LastBottomTabMenuPos { get; private set; } = Vector2.Zero;

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

    public override void OnClose()
    {
        base.OnClose();
        _stickyService.ClearDisplay();
    }

    protected override void DrawInternal()
    {
        // get the width of the window content region we set earlier
        var winContentWidth = CkGui.GetWindowContentRegionWidth();

        // If unauthorized draw the unauthorized display, otherwise draw the server status.
        if (MainHub.ServerStatus is (ServerState.NoSecretKey or ServerState.VersionMisMatch or ServerState.Unauthorized))
        {
            CkGui.FontTextCentered(SundouleiaEx.GetUidText(), UiFontService.UidFont, SundouleiaEx.UidColor());
            // the wrapped text explanation based on the error.
            CkGui.ColorTextWrapped(GetServerError(), ImGuiColors.DalamudWhite);
        }
        else
        {
            DrawTopBar();
        }

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

        // store the bottom of the tab menu for tutorial purposes.
        LastBottomTabMenuPos = ImGui.GetCursorScreenPos();

        // Show content based on the selected tab.
        // (Note: This used to have Using ImRaii.PushId here but never really saw a reason to need it,
        // so removed it for now. if we need it later just reference GSpeak.)
        switch (_tabMenu.TabSelection)
        {
            case MainMenuTabs.SelectedTab.Homepage:
                _homeTab.DrawSection();
                break;
            case MainMenuTabs.SelectedTab.Requests:
                _requestsTab.DrawSection();
                break;
            case MainMenuTabs.SelectedTab.BasicWhitelist:
                _whitelist.DrawBasicView();
                break;
            case MainMenuTabs.SelectedTab.GroupWhitelist:
                _whitelist.DrawGroupsView();
                break;
            case MainMenuTabs.SelectedTab.Radar:
                _radar.DrawSection();
                break;
            case MainMenuTabs.SelectedTab.RadarChat:
                _radarChat.DrawSection();
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
                var res = await _hub.UserSendRequest(new(new(_uidToSentTo), false, _requestMessage));
                _uidToSentTo = string.Empty;
                _requestMessage = string.Empty;
                _creatingRequest = false;
                // Add the request if it was successful!
                if (res.ErrorCode is SundouleiaApiEc.Success)
                    _requests.AddNewRequest(res.Value!);
            });
        }
        if (!string.IsNullOrEmpty(_uidToSentTo))
            CkGui.AttachToolTip($"Send Pair Request to {_uidToSentTo}");

        // draw a attached message field as well if they want.
        ImGui.SetNextItemWidth(availableXWidth);
        ImGui.InputTextWithHint("##pairAddOptionalMessage", "Attach Msg to Request (Optional)", ref _requestMessage, 100);
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.AttachingMessages, LastPos, LastSize, () =>
        {
            _creatingRequest = !_creatingRequest;
            _tabMenu.TabSelection = MainMenuTabs.SelectedTab.Requests;
        });
        ImGui.Separator();
    }

    private void DrawTopBar()
    {
        // Get the window pointer before we draw.
        var winPtr = ImGuiInternal.GetCurrentWindow();
        // Expand the region of the topbar to cross the full width.
        var winPadding = ImGui.GetStyle().WindowPadding;
        // ImGui hides the actual possible clip-rect-min from going to 0,0.
        // This is because the ClipRect skips over the titlebar, so if WinPadding is 8,8
        // then the content region min returns 8,40
        // Note to only subtract the X padding. ClipRectMin gets Y correctly.
        var winClipX = winPadding.X / 2;
        var minPos = winPtr.DrawList.GetClipRectMin() + new Vector2(-winClipX, winPadding.Y);
        var maxPos = winPtr.DrawList.GetClipRectMax() + new Vector2(winClipX, 0);
        // Expand the area for our custom header.
        winPtr.DrawList.PushClipRect(minPos, maxPos, false);

        // Get the expanded width
        var topBarWidth = maxPos.X - minPos.X;
        var offlineSize = CkGui.IconSize(FAI.Unlink);
        var tryonSize = CkGui.IconSize(FAI.Toilet);
        var streamerSize = CkGui.IconSize(FAI.BroadcastTower);
        var connectedSize = CkGui.IconSize(FAI.Link);
        var sideWidth = offlineSize.X + tryonSize.X + streamerSize.X + connectedSize.X + ImUtf8.ItemSpacing.X * 5;
        var height = CkGui.CalcFontTextSize("A", UiFontService.Default150Percent).Y;

        if (DrawAddUser(winPtr, new Vector2(sideWidth, height), minPos))
            _creatingRequest = !_creatingRequest;
        CkGui.AttachToolTip("Add New User to Whitelist");
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.AddingUsers, ImGui.GetWindowPos(), ImGui.GetWindowSize(), () => _creatingRequest = !_creatingRequest);

        ImGui.SetCursorScreenPos(minPos + new Vector2(sideWidth, 0));
        DrawConnectedUsers(winPtr, new Vector2(topBarWidth - sideWidth * 2, height), topBarWidth);
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.InitialWelcome, WindowPos, WindowSize);

        ImGui.SameLine(topBarWidth - sideWidth);
        using (ImRaii.Group()) // Grouping for tutorial formatting.
            DrawConnectionState(winPtr, new Vector2(sideWidth, height));
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.ConnectionState, WindowPos, WindowSize, () => _tabMenu.TabSelection = MainMenuTabs.SelectedTab.Homepage);

        winPtr.DrawList.PopClipRect();
    }

    private bool DrawAddUser(ImGuiWindowPtr winPtr, Vector2 region, Vector2 minPos)
    {
        if (winPtr.SkipItems)
            return false;

        var id = ImGui.GetID("add-user");
        var style = ImGui.GetStyle();
        var shadowSize = ImGuiHelpers.ScaledVector2(1);
        var styleOffset = ImGuiHelpers.ScaledVector2(2f);
        var buttonPadding = styleOffset + ImUtf8.FramePadding;
        var bend = region.Y * .5f;
        var min = minPos;
        var hitbox = new ImRect(min, min + region);

        ImGuiInternal.ItemSize(region);
        if (!ImGuiP.ItemAdd(hitbox, id, null))
            return false;

        // Process interaction with this 'button'
        var hovered = false;
        var active = false;
        var clicked = ImGuiP.ButtonBehavior(hitbox, id, ref hovered, ref active);

        // Render possible nav highlight space over the bounding box region.
        ImGuiP.RenderNavHighlight(hitbox, id);

        // Define our colors based on states.
        uint shadowCol = 0x64000000;
        uint borderCol = CkGui.ApplyAlpha(0xDCDCDCDC, active ? 0.7f : hovered ? 0.63f : 0.39f);
        uint bgCol = CkGui.ApplyAlpha(0x64000000, active ? 0.19f : hovered ? 0.26f : 0.39f);

        winPtr.DrawList.AddRectFilled(min, hitbox.Max, shadowCol, bend, ImDrawFlags.RoundCornersRight);
        winPtr.DrawList.AddRectFilled(min + new Vector2(0, shadowSize.Y), hitbox.Max - shadowSize, borderCol, bend, ImDrawFlags.RoundCornersRight);
        winPtr.DrawList.AddRectFilled(min + new Vector2(0, styleOffset.Y), hitbox.Max - styleOffset, bgCol, bend, ImDrawFlags.RoundCornersRight);

        // Text computation.
        var iconSize = CkGui.IconSize(FAI.Plus);
        var textSize = ImGui.CalcTextSize("Add User");
        var iconTextWidth = iconSize.X + style.ItemInnerSpacing.X + textSize.X;
        var iconPos = min + new Vector2((region.X - iconTextWidth) / 2f, (region.Y - textSize.Y) / 2f);
        var textPos = iconPos + new Vector2(iconSize.X + style.ItemInnerSpacing.X, 0);
        // Then draw out the icon and text.
        using (Svc.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            winPtr.DrawList.AddText(FAI.Plus.ToIconString(), iconPos, ImGui.GetColorU32(ImGuiCol.Text));
        winPtr.DrawList.AddText("Add User", textPos, ImGui.GetColorU32(ImGuiCol.Text));

        return clicked;
    }

    private void DrawConnectedUsers(ImGuiWindowPtr winPtr, Vector2 region, float topBarWidth)
    {
        using var font = UiFontService.Default150Percent.Push();

        var userCount = MainHub.OnlineUsers.ToString(CultureInfo.InvariantCulture);
        var textSize = ImGui.CalcTextSize(MainHub.IsConnected ? $"{userCount}Online" : "Disconnected");
        var offsetX = (topBarWidth - textSize.X - ImUtf8.ItemInnerSpacing.X) / 2;

        // Make two gradients from the left and right, based on region.
        var posMin = winPtr.DC.CursorPos;
        var posMax = posMin + region;
        var halfRegion = region with { X = region.X * .5f };
        var innerCol = ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.TextDisabled), .75f);
        var outerCol = ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.TextDisabled), .99f);

        winPtr.DrawList.AddRectFilledMultiColor(posMin, posMin + halfRegion, outerCol, innerCol, innerCol, outerCol);
        winPtr.DrawList.AddRectFilledMultiColor(posMin with { X = posMin.X + halfRegion.X }, posMax, innerCol, outerCol, outerCol, innerCol);

        ImGui.SetCursorPosX(offsetX);
        using (ImRaii.Group())
        {
            if (MainHub.IsConnected)
            {
                CkGui.ColorText(userCount, ImGuiColors.ParsedGold);
                CkGui.TextInline("Online");
            }
            else
            {
                CkGui.ColorText("Disconnected", ImGuiColors.DalamudRed);
            }
        }
    }

    private void DrawConnectionState(ImGuiWindowPtr winPtr, Vector2 region)
    {
        if (winPtr.SkipItems)
            return;

        var style = ImGui.GetStyle();
        var shadowSize = ImGuiHelpers.ScaledVector2(1);
        var styleOffset = ImGuiHelpers.ScaledVector2(2);
        var buttonPadding = styleOffset + ImUtf8.FramePadding;
        var bend = region.Y * .5f;
        var min = winPtr.DC.CursorPos;
        var max = min + region;

        // Define our colors based on states.
        uint shadowCol = 0x64000000;
        uint borderCol = CkGui.ApplyAlpha(0xDCDCDCDC, 0.39f);
        uint bgCol = CkGui.ApplyAlpha(0x64000000, 0.39f);

        winPtr.DrawList.AddRectFilled(min, max, shadowCol, bend, ImDrawFlags.RoundCornersLeft);
        winPtr.DrawList.AddRectFilled(min + shadowSize, max - new Vector2(0, shadowSize.Y), borderCol, bend, ImDrawFlags.RoundCornersLeft);
        winPtr.DrawList.AddRectFilled(min + styleOffset, max - new Vector2(0, styleOffset.Y), bgCol, bend, ImDrawFlags.RoundCornersLeft);

        var offlineSize = CkGui.IconSize(FAI.Unlink);
        var tryonSize = CkGui.IconSize(FAI.Toilet);
        var streamerSize = CkGui.IconSize(FAI.BroadcastTower);
        var connectedSize = CkGui.IconSize(FAI.Link);
        var iconsWidth = offlineSize.X + tryonSize.X + streamerSize.X + connectedSize.X;

        CkGui.InlineSpacingInner();
        if (DrawConnectionButton(ConnectionKind.FullPause, FAI.Unlink, CkColor.TriStateCross.Uint(), offlineSize, !MainHub.IsConnected))
        {
            _account.ConnectionKind = ConnectionKind.FullPause;
            _ = _hub.Disconnect(ServerState.Disconnected, DisconnectIntent.Normal);
        }
        CkGui.AttachToolTip($"--COL--[Disconnected]--COL--" +
            $"--NL--Disconnected from Servers.", ImGuiColors.DalamudOrange);

        ImGui.SameLine();
        if (DrawConnectionButton(ConnectionKind.WardrobeMode, FAI.ToiletPortable, CkColor.TriStateCheck.Uint(), offlineSize, false))
        {
            _account.ConnectionKind = ConnectionKind.WardrobeMode;

            if (MainHub.ServerStatus is (ServerState.Disconnected or ServerState.Offline))
                _ = _hub.Connect();
        }
        CkGui.AttachToolTip($"--COL--[Try Out Mode]--COL--" +
            $"--NL--Changes you make are not pushed to others, but you still see others normally.", ImGuiColors.DalamudOrange);

        ImGui.SameLine();
        if (DrawConnectionButton(ConnectionKind.StreamerMode, FAI.BroadcastTower, CkColor.TriStateCheck.Uint(), offlineSize, false))
        {
            _account.ConnectionKind = ConnectionKind.StreamerMode;

            if (MainHub.ServerStatus is (ServerState.Disconnected or ServerState.Offline))
                _ = _hub.Connect();
        }
        CkGui.AttachToolTip($"--COL--[Streamer Mode]--COL--" +
            $"--NL--Appearance is sent to others, but others remain vanilla.", ImGuiColors.DalamudOrange);

        ImGui.SameLine();
        if (DrawConnectionButton(ConnectionKind.Normal, FAI.Link, CkColor.TriStateCheck.Uint(), offlineSize, false))
        {
            _account.ConnectionKind = ConnectionKind.Normal;

            if (MainHub.ServerStatus is (ServerState.Disconnected or ServerState.Offline))
                _ = _hub.Connect();
        }
        CkGui.AttachToolTip($"--COL--[Connected]--COL--" +
            $"--NL--Normal/Default connection with the server.", ImGuiColors.DalamudOrange);

        bool DrawConnectionButton(ConnectionKind kind, FAI icon, uint color, Vector2 size, bool disabled)
        {
            if (winPtr.SkipItems)
                return false;

            var id = ImGui.GetID($"connection-{kind}");
            var min = winPtr.DC.CursorPos + buttonPadding;
            var hitbox = new ImRect(min, min + size);
            // Add the item into the ImGuiInternals
            ImGuiInternal.ItemSize(size);
            if (!ImGuiP.ItemAdd(hitbox, id, null))
                return false;

            // Process interaction with this 'button'
            var hovered = false;
            var active = false;
            var clicked = ImGuiP.ButtonBehavior(hitbox, id, ref hovered, ref active);

            // Render possible nav highlight space over the bounding box region.
            ImGuiP.RenderNavHighlight(hitbox, id);

            // Draw based on current state.
            if (_account.ConnectionKind == kind)
            {
                using (Svc.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                    winPtr.DrawList.AddText(icon.ToIconString(), min, color);
            }
            else
            {
                // Define our colors based on states.
                var iconCol = CkGui.ApplyAlpha(color, disabled ? .27f : active ? .9f : hovered ? .73f : .39f);
                using (Svc.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                    winPtr.DrawList.AddText(icon.ToIconString(), min, iconCol);
            }
            
            return clicked && !disabled;
        }
    }

    /// <summary> 
    ///     Retrieves the various server error messages based on the current server state.
    /// </summary>
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
            "\nTo create UID's for your alt characters, be sure to claim your account in the Sundouleia discord." +
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
}
