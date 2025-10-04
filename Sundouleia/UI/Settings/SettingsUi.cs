using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui;
using OtterGui.Text;
using Sundouleia.Interop;
using Sundouleia.Localization;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.WebAPI;

namespace Sundouleia.Gui;

public class SettingsUi : WindowMediatorSubscriberBase
{
    private bool THEME_PUSHED = false;

    private readonly MainHub _hub;
    private readonly MainConfig _mainConfig;
    private readonly AccountManagerTab _accountsTab;
    private readonly DebugTab _debugTab;

    public SettingsUi(ILogger<SettingsUi> logger, SundouleiaMediator mediator, MainHub hub,
        MainConfig config, AccountManagerTab accounts, DebugTab debug) 
        : base(logger, mediator, "Sundouleia Settings")
    {
        _hub = hub;
        _mainConfig = config;
        _accountsTab = accounts;
        _debugTab = debug;

        Flags = WFlags.NoScrollbar;
        this.PinningClickthroughFalse();
        this.SetBoundaries(new Vector2(625, 400), ImGui.GetIO().DisplaySize);

#if DEBUG
        TitleBarButtons = new TitleBarButtonBuilder()
            .Add(FAI.Tshirt, "Open Active State Debugger", () => Mediator.Publish(new UiToggleMessage(typeof(DebugActiveStateUI))))
            .Add(FAI.PersonRays, "Open Personal Data Debugger", () => Mediator.Publish(new UiToggleMessage(typeof(DebugPersonalDataUI))))
            .Add(FAI.Database, "Open Storages Debugger", () => Mediator.Publish(new UiToggleMessage(typeof(DebugStorageUI))))
            .Build();
#endif
    }

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
        ImGui.Text(CkLoc.Settings.OptionalPlugins);

        ImGui.SameLine();
        CkGui.ColorTextBool("Penumbra", IpcCallerPenumbra.APIAvailable);
        CkGui.AttachToolTip(IpcCallerPenumbra.APIAvailable ? CkLoc.Settings.PluginValid : CkLoc.Settings.PluginInvalid);

        ImGui.SameLine();
        CkGui.ColorTextBool("Glamourer", IpcCallerGlamourer.APIAvailable);
        CkGui.AttachToolTip(IpcCallerGlamourer.APIAvailable ? CkLoc.Settings.PluginValid : CkLoc.Settings.PluginInvalid);

        ImGui.SameLine();
        CkGui.ColorTextBool("Customize+", IpcCallerCustomize.APIAvailable);
        CkGui.AttachToolTip(IpcCallerCustomize.APIAvailable ? CkLoc.Settings.PluginValid : CkLoc.Settings.PluginInvalid);

        ImGui.SameLine();
        CkGui.ColorTextBool("Moodles", IpcCallerMoodles.APIAvailable);
        CkGui.AttachToolTip(IpcCallerMoodles.APIAvailable ? CkLoc.Settings.PluginValid : CkLoc.Settings.PluginInvalid);

        ImGui.SameLine();
        CkGui.ColorTextBool("Heels", IpcCallerHeels.APIAvailable);
        CkGui.AttachToolTip(IpcCallerHeels.APIAvailable ? CkLoc.Settings.PluginValid : CkLoc.Settings.PluginInvalid);

        ImGui.SameLine();
        CkGui.ColorTextBool("Honorific", IpcCallerHonorific.APIAvailable);
        CkGui.AttachToolTip(IpcCallerHonorific.APIAvailable ? CkLoc.Settings.PluginValid : CkLoc.Settings.PluginInvalid);

        ImGui.SameLine();
        CkGui.ColorTextBool("PetNames", IpcCallerPetNames.APIAvailable);
        CkGui.AttachToolTip(IpcCallerPetNames.APIAvailable ? CkLoc.Settings.PluginValid : CkLoc.Settings.PluginInvalid);

        ImGui.Text("Register account:");

        ImGui.SameLine();
        if (ImUtf8.SmallButton("Sundouleia Discord"))
        {
            // Void for now.
        }

        // draw out the tab bar for us.
        if (ImGui.BeginTabBar("mainTabBar"))
        {
            if (MainHub.IsConnected)
            {
                if (ImGui.BeginTabItem(CkLoc.Settings.TabGeneral))
                {
                    DrawGlobalSettings();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(CkLoc.Settings.TabPreferences))
                {
                    DrawBasicPreferences();
                    DrawNotificationPreferences();
                    ImGui.EndTabItem();
                }
            }

            if (ImGui.BeginTabItem(CkLoc.Settings.TabAccounts))
            {
                _accountsTab.DrawManager();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(CkLoc.Settings.TabStorage))
            {
                CkGui.CenterColorTextAligned("Storage Manager WIP", ImGuiColors.ParsedGold);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Debug"))
            {
                _debugTab.DrawDebugMain();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawGlobalSettings()
    {
        CkGui.FontText(CkLoc.Settings.MainOptions.HeaderRadar, UiFontService.UidFont);
        var sendPings = _mainConfig.Current.RadarSendPings;
        var showDtr = _mainConfig.Current.RadarNearbyDtr;
        var showDtrUnread = _mainConfig.Current.RadarChatUnreadDtr;
        var joinChats = _mainConfig.Current.RadarJoinChats;
        var chatUnreadBubble = _mainConfig.Current.RadarShowUnreadBubble;

        if (ImGui.Checkbox("Send Radar Pings", ref sendPings))
        {
            _mainConfig.Current.RadarSendPings = sendPings;
            _mainConfig.Save();
        }
        CkGui.HelpText("Show up on other user's radar that are in your area!" +
            "--SEP--Users will not know your location or anything, just that you are present.");

        if (ImGui.Checkbox("Show Radar DTR", ref showDtr))
        {
            _mainConfig.Current.RadarNearbyDtr = showDtr;
            _mainConfig.Save();
        }
        CkGui.HelpText("Add the DTR entry for the radar, showing you how many other users are in your local territory.");


        if (ImGui.Checkbox("Join Radar Chats", ref joinChats))
        {
            _mainConfig.Current.RadarJoinChats = joinChats;
            _mainConfig.Save();
        }
        CkGui.HelpText("Automatically join the chat channels created by the radar when you enter a new area.");

        if (ImGui.Checkbox("Show Chat Unread DTR", ref showDtrUnread))
        {
            _mainConfig.Current.RadarChatUnreadDtr = showDtrUnread;
            _mainConfig.Save();
        }
        CkGui.HelpText("If you have unread chat messages from your local radar's chat, change the DTR entries color!");

        if (ImGui.Checkbox("Show Unread Chat Bubble", ref chatUnreadBubble))
        {
            _mainConfig.Current.RadarShowUnreadBubble = chatUnreadBubble;
            _mainConfig.Save();
        }
        CkGui.HelpText("Toggles the visibility of the unread message bubble in the main UI.");


        ImGui.Separator();
        CkGui.FontText(CkLoc.Settings.MainOptions.HeaderUi, UiFontService.UidFont);
        var openOnLaunch = _mainConfig.Current.OpenUiOnStartup;
        var showProfiles = _mainConfig.Current.ShowProfiles;
        var popOutDelay = _mainConfig.Current.ProfileDelay;

        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.ShowMainUiOnStartLabel, ref openOnLaunch))
        {
            _mainConfig.Current.OpenUiOnStartup = openOnLaunch;
            _mainConfig.Save();
        }
        CkGui.HelpText(CkLoc.Settings.MainOptions.ShowMainUiOnStartTT);

        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.ShowProfilesLabel, ref showProfiles))
        {
            Mediator.Publish(new ClearProfileCache());
            _mainConfig.Current.ShowProfiles = showProfiles;
            _mainConfig.Save();
        }
        CkGui.HelpText(CkLoc.Settings.MainOptions.ShowProfilesTT);

        using (ImRaii.Disabled(!showProfiles))
        {
            ImGui.Indent();
            ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderFloat(CkLoc.Settings.MainOptions.ProfileDelayLabel, ref popOutDelay, 0.3f, 5))
            {
                _mainConfig.Current.ProfileDelay = popOutDelay;
                _mainConfig.Save();
            }
            CkGui.HelpText(CkLoc.Settings.MainOptions.ProfileDelayTT);
            ImGui.Unindent();
        }
    }

    private void DrawBasicPreferences()
    {
        CkGui.FontText(CkLoc.Settings.Preferences.HeaderPairPref, UiFontService.UidFont);
        var nickOverName = _mainConfig.Current.PreferNicknamesOverNames;
        var sepVisibleUsers = _mainConfig.Current.ShowVisibleUsersSeparately;
        var sepOfflineUsers = _mainConfig.Current.ShowOfflineUsersSeparately;
        var contextMenus = _mainConfig.Current.ShowContextMenus;
        var useFocusTarget = _mainConfig.Current.FocusTargetOverTarget;

        if (ImGui.Checkbox(CkLoc.Settings.Preferences.PreferNicknamesLabel, ref nickOverName))
        {
            _mainConfig.Current.PreferNicknamesOverNames = nickOverName;
            _mainConfig.Save();
            Mediator.Publish(new RefreshWhitelistMessage());
        }
        CkGui.HelpText(CkLoc.Settings.Preferences.PreferNicknamesTT);

        if (ImGui.Checkbox(CkLoc.Settings.Preferences.ShowVisibleSeparateLabel, ref sepVisibleUsers))
        {
            _mainConfig.Current.ShowVisibleUsersSeparately = sepVisibleUsers;
            _mainConfig.Save();
            Mediator.Publish(new RefreshWhitelistMessage());
        }
        CkGui.HelpText(CkLoc.Settings.Preferences.ShowVisibleSeparateTT);

        if (ImGui.Checkbox(CkLoc.Settings.Preferences.ShowOfflineSeparateLabel, ref sepOfflineUsers))
        {
            _mainConfig.Current.ShowOfflineUsersSeparately = sepOfflineUsers;
            _mainConfig.Save();
            Mediator.Publish(new RefreshWhitelistMessage());
        }
        CkGui.HelpText(CkLoc.Settings.Preferences.ShowOfflineSeparateTT);

        if (ImGui.Checkbox(CkLoc.Settings.Preferences.ContextMenusLabel, ref contextMenus))
        {
            _mainConfig.Current.ShowContextMenus = contextMenus;
            _mainConfig.Save();
        }
        CkGui.HelpText(CkLoc.Settings.Preferences.ContextMenusTT);

        if (ImGui.Checkbox(CkLoc.Settings.Preferences.FocusTargetLabel, ref useFocusTarget))
        {
            _mainConfig.Current.FocusTargetOverTarget = useFocusTarget;
            _mainConfig.Save();
            Mediator.Publish(new RefreshWhitelistMessage());
        }
        CkGui.HelpText(CkLoc.Settings.Preferences.FocusTargetTT);
    }

    private void DrawNotificationPreferences()
    {
        /* --------------- Separator for moving onto the Notifications Section ----------- */
        ImGui.Separator();
        CkGui.FontText(CkLoc.Settings.Preferences.HeaderNotifications, UiFontService.UidFont);
        var connectionNotifs = _mainConfig.Current.NotifyForConnections;
        var onlineNotifs = _mainConfig.Current.NotifyForOnlinePairs;
        var onlineNotifsNickLimited = _mainConfig.Current.NotifyLimitToNickedPairs;

        if (ImGui.Checkbox(CkLoc.Settings.Preferences.ConnectedNotifLabel, ref connectionNotifs))
        {
            _mainConfig.Current.NotifyForConnections = connectionNotifs;
            _mainConfig.Save();
        }
        CkGui.HelpText(CkLoc.Settings.Preferences.ConnectedNotifTT);

        if (ImGui.Checkbox(CkLoc.Settings.Preferences.OnlineNotifLabel, ref onlineNotifs))
        {
            _mainConfig.Current.NotifyForOnlinePairs = onlineNotifs;
            if (!onlineNotifs) _mainConfig.Current.NotifyLimitToNickedPairs = false;
            _mainConfig.Save();
        }
        CkGui.HelpText(CkLoc.Settings.Preferences.OnlineNotifTT);

        using (ImRaii.Disabled(!onlineNotifs))
        {
            if (ImGui.Checkbox(CkLoc.Settings.Preferences.LimitForNicksLabel, ref onlineNotifsNickLimited))
            {
                _mainConfig.Current.NotifyLimitToNickedPairs = onlineNotifsNickLimited;
                _mainConfig.Save();
            }
            CkGui.HelpText(CkLoc.Settings.Preferences.LimitForNicksTT);
        }

        if(ImGuiUtil.GenericEnumCombo("Info Location##notifInfo", 125f, _mainConfig.Current.InfoNotification, out var newInfo, i => i.ToString()))
        {
            _mainConfig.Current.InfoNotification = newInfo;
            _mainConfig.Save();
        }
        CkGui.HelpText("The location where \"Info\" notifications will display." +
            "--NL----COL--Nowhere--COL-- will not show any Info notifications." +
            "--NL----COL--Chat--COL-- prints Info notifications in chat" +
            "--NL----COL--Toast--COL-- shows Info toast notifications in the bottom right corner" +
            "--NL----COL--Both--COL-- shows chat as well as the toast notification", ImGuiColors.ParsedGold);

        if (ImGuiUtil.GenericEnumCombo("Warning Location##notifWarn", 125f, _mainConfig.Current.WarningNotification, out var newWarn, i => i.ToString()))
        {
            _mainConfig.Current.WarningNotification = newWarn;
            _mainConfig.Save();
        }
        CkGui.HelpText("The location where \"Warning\" notifications will display." +
            "--NL----COL--Nowhere--COL-- will not show any Warning notifications." +
            "--NL----COL--Chat--COL-- prints Warning notifications in chat" +
            "--NL----COL--Toast--COL-- shows Warning toast notifications in the bottom right corner" +
            "--NL----COL--Both--COL-- shows chat as well as the toast notification", ImGuiColors.ParsedGold);

        if (ImGuiUtil.GenericEnumCombo("Error Location##notifError", 125f, _mainConfig.Current.ErrorNotification, out var newError, i => i.ToString()))
        {
            _mainConfig.Current.ErrorNotification = newError;
            _mainConfig.Save();
        }
        CkGui.HelpText("The location where \"Error\" notifications will display." +
            "--NL----COL--Nowhere--COL-- will not show any Error notifications." +
            "--NL----COL--Chat--COL-- prints Error notifications in chat" +
            "--NL----COL--Toast--COL-- shows Error toast notifications in the bottom right corner" +
            "--NL----COL--Both--COL-- shows chat as well as the toast notification", ImGuiColors.ParsedGold);
    }
}
