using CkCommons;
using CkCommons.Gui;
using CkCommons.Gui.Utility;
using CkCommons.Helpers;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui;
using OtterGui.Text;
using OtterGuiInternal.Enums;
using Sundouleia.Interop;
using Sundouleia.Localization;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Util;
using TerraFX.Interop.Windows;

namespace Sundouleia.Gui;

public class SettingsUi : WindowMediatorSubscriberBase
{
    private bool THEME_PUSHED = false;

    private readonly MainHub _hub;
    private readonly MainConfig _mainConfig;
    private readonly ProfilesTab _accountsTab;
    private readonly DebugTab _debugTab;
    private readonly UiDataStorageShared _storageShared;

    public SettingsUi(ILogger<SettingsUi> logger, SundouleiaMediator mediator, MainHub hub,
        MainConfig config, ProfilesTab accounts, DebugTab debug, UiDataStorageShared dataStorage)
        : base(logger, mediator, "Sundouleia Settings")
    {
        _hub = hub;
        _mainConfig = config;
        _accountsTab = accounts;
        _debugTab = debug;
        _storageShared = dataStorage;

        Flags = WFlags.NoScrollbar;
        this.PinningClickthroughFalse();
        this.SetBoundaries(new(625, 420), ImGui.GetIO().DisplaySize);

        TitleBarButtons = new TitleBarButtonBuilder()
            .Add(FAI.Tshirt, "Open Active State Debugger", () => Mediator.Publish(new UiToggleMessage(typeof(DebugActiveStateUI))))
            .Add(FAI.PersonRays, "Open Personal Data Debugger", () => Mediator.Publish(new UiToggleMessage(typeof(DebugPersonalDataUI))))
            .Add(FAI.Database, "Open Storages Debugger", () => Mediator.Publish(new UiToggleMessage(typeof(DebugStorageUI))))
            .Build();
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

        ImGui.SameLine();
        CkGui.ColorTextBool("Brio", IpcCallerBrio.APIAvailable);
        CkGui.AttachToolTip(IpcCallerBrio.APIAvailable ? CkLoc.Settings.PluginValid : CkLoc.Settings.PluginInvalid);

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
                    DrawMainGeneric();
                    ImGui.Separator();
                    DrawMainGlobals();
                    ImGui.Separator();
                    DrawMainRadar();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(CkLoc.Settings.TabPreferences))
                {
                    DrawPrefsDownloads();
                    ImGui.Separator();
                    DrawPrefsNotify();
                    ImGui.EndTabItem();
                }
            }

            if (ImGui.BeginTabItem(CkLoc.Settings.TabAccounts))
            {
                _accountsTab.DrawContent();
                ImGui.EndTabItem();
            }

#if DEBUG
            if (ImGui.BeginTabItem(CkLoc.Settings.TabSmaStorage))
            {
                // Temporary placeholder
                _storageShared.DrawSmaStorage();
                ImGui.EndTabItem();
            }
#endif

            if (ImGui.BeginTabItem(CkLoc.Settings.TabStorage))
            {
                _storageShared.DrawFileCacheStorageBox();
                ImGui.Separator();
                _storageShared.DrawCacheMonitoring(true, true, true);
                ImGui.Separator();
                _storageShared.DrawFileCompactor(true);
                ImGui.Separator();
                _storageShared.DrawTransfers();
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

    private void DrawMainGeneric()
    {
        CkGui.FontText(CkLoc.Settings.MainOptions.HeaderGeneric, UiFontService.UidFont);
        var autoOpen = _mainConfig.Current.OpenUiOnStartup;
        var contextMenus = _mainConfig.Current.ShowContextMenus;
        var showProfiles = _mainConfig.Current.ShowProfiles;
        var profileDelay = _mainConfig.Current.ProfileDelay;
        var allowNsfw = _mainConfig.Current.AllowNSFW;

        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.ShowMainUiOnStartLabel, ref autoOpen))
        {
            _mainConfig.Current.OpenUiOnStartup = autoOpen;
            _mainConfig.Save();
        }
        CkGui.HelpText(CkLoc.Settings.MainOptions.ShowMainUiOnStartTT);

        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.ContextMenusLabel, ref contextMenus))
        {
            _mainConfig.Current.ShowContextMenus = contextMenus;
            _mainConfig.Save();
        }
        CkGui.HelpText(CkLoc.Settings.MainOptions.ContextMenusTT);

        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.ShowProfilesLabel, ref showProfiles))
        {
            Mediator.Publish(new ClearProfileCache());
            _mainConfig.Current.ShowProfiles = showProfiles;
            _mainConfig.Save();
        }
        CkGui.HelpText(CkLoc.Settings.MainOptions.ShowProfilesTT);

        using var dis = ImRaii.Disabled(!showProfiles);
        using (ImRaii.PushIndent())
        {
            ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderFloat("##Profile-Delay", ref profileDelay, 0.3f, 5, $"%.1f {CkLoc.Settings.MainOptions.ProfileDelayLabel}"))
            {
                _mainConfig.Current.ProfileDelay = profileDelay;
                _mainConfig.Save();
            }
            CkGui.HelpText(CkLoc.Settings.MainOptions.ProfileDelayTT);
        }

        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.AllowNSFWLabel, ref allowNsfw))
        {
            _mainConfig.Current.AllowNSFW = allowNsfw;
            _mainConfig.Save();
        }
        CkGui.HelpText(CkLoc.Settings.MainOptions.AllowNSFWTT);
    }

    private string? _timespanStrCache = null;
    private void DrawMainGlobals()
    {
        if (!MainHub.IsConnected)
            return;

        CkGui.FontText(CkLoc.Settings.MainOptions.HeaderGlobalPerms, UiFontService.UidFont);

        using (ImRaii.Group())
        {
            CkGui.FramedIconText(FAI.ArrowsDownToPeople);
            CkGui.TextFrameAlignedInline("Global DataSync Filters:");
        }
        CkGui.HelpText("What DataSync related filters to set on others when first paired.", true);

        ImGui.SameLine();
        var animationsGlobal = MainHub.GlobalPerms.DefaultAllowAnimations;
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.AllowAnimationsLabel, ref animationsGlobal, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultAllowAnimations), animationsGlobal));
        CkGui.AttachToolTip(CkLoc.Settings.MainOptions.AllowAnimationsTT);

        ImGui.SameLine();
        var soundsGlobal = MainHub.GlobalPerms.DefaultAllowSounds;
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.AllowSoundsLabel, ref soundsGlobal, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultAllowSounds), soundsGlobal));
        CkGui.AttachToolTip(CkLoc.Settings.MainOptions.AllowSoundsTT);

        ImGui.SameLine();
        var vfxGlobal = MainHub.GlobalPerms.DefaultAllowVfx;
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.AllowVfxLabel, ref vfxGlobal, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultAllowVfx), vfxGlobal));
        CkGui.AttachToolTip(CkLoc.Settings.MainOptions.AllowVfxTT);

        var curShare = MainHub.GlobalPerms.DefaultShareOwnMoodles;
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.ShareMoodles, ref curShare, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultShareOwnMoodles), curShare));
        CkGui.HelpText(CkLoc.Settings.MainOptions.ShareMoodlesTT);

        var height = ImUtf8.FrameHeight * 4 + ImGui.GetStyle().CellPadding.Y * 8;
        using var _ = CkRaii.FramedChildPaddedW("access", ImGui.GetContentRegionAvail().X, height, 0, ImGui.GetColorU32(ImGuiCol.Separator), CkStyle.ChildRounding(), 2f);
        using var t = ImRaii.Table("##globalpermTable", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV);
        if (!t) return;
        
        ImGui.TableSetupColumn("##section");
        ImGui.TableSetupColumn("##values", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextRow();

        var curAccess = MainHub.GlobalPerms.DefaultMoodleAccess;

        // Moodle Status Types.
        ImGui.TableNextColumn();
        CkGui.TextFrameAligned($"Allowed Status Types:    ");
        ImGui.TableNextColumn();
        var curPos = curAccess.HasAny(MoodleAccess.Positive);
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.AllowPosMoodles, ref curPos, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultMoodleAccess), curAccess ^ MoodleAccess.Positive));
        CkGui.AttachToolTip(CkLoc.Settings.MainOptions.AllowPosMoodlesTT);

        ImGui.SameLine();
        var curNeg = curAccess.HasAny(MoodleAccess.Negative);
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.AllowNegMoodles, ref curNeg, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultMoodleAccess), curAccess ^ MoodleAccess.Negative));
        CkGui.AttachToolTip(CkLoc.Settings.MainOptions.AllowNegMoodlesTT);

        ImGui.SameLine();
        var curSpecial = curAccess.HasAny(MoodleAccess.Special);
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.AllowSpecialMoodles, ref curSpecial, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultMoodleAccess), curAccess ^ MoodleAccess.Special));
        CkGui.AttachToolTip(CkLoc.Settings.MainOptions.AllowSpecialMoodlesTT);
        ImGui.TableNextRow();

        // Moodle Time
        ImGui.TableNextColumn();
        CkGui.TextFrameAligned("Maximum Duration:");
        ImGui.TableNextColumn();
        var refPermAccess = curAccess.HasAny(MoodleAccess.Permanent);
        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.AllowPermanentMoodles, ref refPermAccess))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultMoodleAccess), curAccess ^ MoodleAccess.Permanent));
        CkGui.AttachToolTip(CkLoc.Settings.MainOptions.AllowPermanentMoodlesTT);

        // Display inline, the icon input text field for setting a maximum duration, if we are not permanent.
        if (!refPermAccess)
        {
            var str = _timespanStrCache ?? MainHub.GlobalPerms.DefaultMaxMoodleTime.ToTimeSpanStr();
            ImGui.SameLine();
            if (CkGui.IconInputText(FAI.HourglassHalf, "Maximum Time", "0d0h0m0s", ref str, 32, 100f, true, !IpcCallerMoodles.APIAvailable || UiService.DisableUI))
            {
                if (str != MainHub.GlobalPerms.DefaultMaxMoodleTime.ToTimeSpanStr() && CkTimers.TryParseTimeSpan(str, out var newTime))
                {
                    var ticks = (ulong)newTime.Ticks;
                    _logger.LogInformation($"Changing OwnGlobals MaxMoodleTime to {ticks} ticks.", LoggerType.PairDataTransfer);
                    UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultMaxMoodleTime), ticks));
                }
                _timespanStrCache = null;
            }
            CkGui.AttachToolTip($"The longest duration a timed moodle can be applied to you.");
        }
        ImGui.TableNextRow();

        // Moodle Application Types
        ImGui.TableNextColumn();
        CkGui.TextFrameAligned("Let Others Apply:");
        
        ImGui.TableNextColumn();
        var canApplyOwn = curAccess.HasAny(MoodleAccess.AllowOwn);
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.AllowOwnMoodles, ref canApplyOwn, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultMoodleAccess), curAccess ^ MoodleAccess.AllowOwn));
        CkGui.AttachToolTip(CkLoc.Settings.MainOptions.AllowOwnMoodlesTT);

        ImGui.SameLine();
        var canApplyOthers = curAccess.HasAny(MoodleAccess.AllowOther);
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.AllowOtherMoodles, ref canApplyOthers, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultMoodleAccess), curAccess ^ MoodleAccess.AllowOther));
        CkGui.AttachToolTip(CkLoc.Settings.MainOptions.AllowOtherMoodlesTT);
        ImGui.TableNextRow();

        // Removal
        ImGui.TableNextColumn();
        CkGui.TextFrameAligned("Let Others Remove:");
        ImGui.TableNextColumn();
        var canRemoveApplied = curAccess.HasAny(MoodleAccess.RemoveApplied);
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.RemoveAppliedMoodles, ref canRemoveApplied, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultMoodleAccess), curAccess ^ MoodleAccess.RemoveApplied));
        CkGui.AttachToolTip(CkLoc.Settings.MainOptions.RemoveAppliedMoodlesTT);

        ImGui.SameLine();
        var canRemoveAny = curAccess.HasAny(MoodleAccess.RemoveAny);
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.RemoveAnyMoodles, ref canRemoveAny, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultMoodleAccess), curAccess ^ MoodleAccess.RemoveAny));
        CkGui.AttachToolTip(CkLoc.Settings.MainOptions.RemoveAnyMoodlesTT);
    }


    private void DrawMainRadar()
    {
        CkGui.FontText(CkLoc.Settings.MainOptions.HeaderRadar, UiFontService.UidFont);
        var enabled = _mainConfig.Current.RadarEnabled;
        var sendPings = _mainConfig.Current.RadarSendPings;
        var nearbyDtr = _mainConfig.Current.RadarNearbyDtr;
        var joinChats = _mainConfig.Current.RadarJoinChats;
        var chatUnreadDtr = _mainConfig.Current.RadarChatUnreadDtr;
        var showUnreadBubble = _mainConfig.Current.RadarShowUnreadBubble;

        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.RadarEnabledLabel, ref enabled))
        {
            _mainConfig.Current.RadarEnabled = enabled;
            _mainConfig.Save();
            Mediator.Publish(new RadarConfigChanged(nameof(ConfigStorage.RadarEnabled)));
        }

        using var _ = ImRaii.Disabled(!enabled);

        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.RadarSendPingsLabel, ref sendPings))
        {
            _mainConfig.Current.RadarSendPings = sendPings;
            _mainConfig.Save();
            Mediator.Publish(new RadarConfigChanged(nameof(ConfigStorage.RadarSendPings)));
        }
        ImUtf8.SameLineInner();
        CkGui.FramedIconText(FAI.SatelliteDish);
        CkGui.HelpText(CkLoc.Settings.MainOptions.RadarSendPingsTT, true);

        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.RadarNearbyDtrLabel, ref nearbyDtr))
        {
            _mainConfig.Current.RadarNearbyDtr = nearbyDtr;
            _mainConfig.Save();
            Mediator.Publish(new RadarConfigChanged(nameof(ConfigStorage.RadarNearbyDtr)));
        }
        ImUtf8.SameLineInner();
        CkGui.FramedIconText(FAI.PersonDressBurst);
        CkGui.HelpText(CkLoc.Settings.MainOptions.RadarNearbyDtrTT, true);

        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.RadarJoinChatsLabel, ref joinChats))
        {
            _mainConfig.Current.RadarJoinChats = joinChats;
            _mainConfig.Save();
            Mediator.Publish(new RadarConfigChanged(nameof(ConfigStorage.RadarJoinChats)));
        }
        ImUtf8.SameLineInner();
        CkGui.FramedIconText(FAI.CommentDots);
        CkGui.HelpText(CkLoc.Settings.MainOptions.RadarJoinChatsTT, true);

        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.RadarChatUnreadDtrLabel, ref chatUnreadDtr))
        {
            _mainConfig.Current.RadarChatUnreadDtr = chatUnreadDtr;
            _mainConfig.Save();
            Mediator.Publish(new RadarConfigChanged(nameof(ConfigStorage.RadarChatUnreadDtr)));
        }
        ImUtf8.SameLineInner();
        CkGui.FramedIconText(FAI.PersonCircleExclamation);
        CkGui.HelpText(CkLoc.Settings.MainOptions.RadarChatUnreadDtrTT, true);

        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.RadarShowUnreadBubbleLabel, ref showUnreadBubble))
        {
            _mainConfig.Current.RadarShowUnreadBubble = showUnreadBubble;
            _mainConfig.Save();
            Mediator.Publish(new RadarConfigChanged(nameof(ConfigStorage.RadarShowUnreadBubble)));
        }
        ImUtf8.SameLineInner();
        CkGui.FramedIconText(FAI.Bell);
        CkGui.HelpText(CkLoc.Settings.MainOptions.RadarShowUnreadBubbleTT, true);
    }

    private void DrawPrefsDownloads()
    {
        CkGui.FontText(CkLoc.Settings.Preferences.HeaderDownloads, UiFontService.UidFont);
        var maxParallelDLs = _mainConfig.Current.MaxParallelDownloads;
        var dlLimit = _mainConfig.Current.DownloadLimitBytes;
        var dlSpeedType = _mainConfig.Current.DownloadSpeedType;

        CkGui.FramedIconText(FAI.Gauge);
        CkGui.TextFrameAlignedInline(CkLoc.Settings.Preferences.DownloadLimitLabel);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("###DownloadSpeedLimit", ref dlLimit))
        {
            _mainConfig.Current.DownloadLimitBytes = dlLimit;
            _mainConfig.Save();
            Mediator.Publish(new DownloadLimitChangedMessage());
        }
        CkGui.AttachToolTip(CkLoc.Settings.Preferences.DownloadLimitTT);

        ImUtf8.SameLineInner();
        if (CkGuiUtils.EnumCombo("###SpeedType", 50 * ImGuiHelpers.GlobalScale, _mainConfig.Current.DownloadSpeedType, out var newType, s => s.ToName()))
        {
            _mainConfig.Current.DownloadSpeedType = newType;
            _mainConfig.Save();
            Mediator.Publish(new DownloadLimitChangedMessage());
        }
        CkGui.AttachToolTip(CkLoc.Settings.Preferences.DownloadSpeedTypeTT);

        CkGui.FramedIconText(FAI.Download);
        CkGui.TextFrameAlignedInline(CkLoc.Settings.Preferences.MaxParallelDLsLabel);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("###Maximum Parallel Downloads", ref maxParallelDLs, 1, 10))
        {
            _mainConfig.Current.MaxParallelDownloads = maxParallelDLs;
            _mainConfig.Save();
        }
        CkGui.HelpText(CkLoc.Settings.Preferences.MaxParallelDLsTT);

        var showUploadText = _mainConfig.Current.ShowUploadingText;
        var transferDebug = _mainConfig.Current.TransferWindow;
        var progressBars = _mainConfig.Current.TransferBars;
        var showBarText = _mainConfig.Current.TransferBarText;
        var barHeight = _mainConfig.Current.TransferBarHeight;
        var barWidth = _mainConfig.Current.TransferBarWidth;

        if (ImGui.Checkbox(CkLoc.Settings.Preferences.ShowUploadingTextLabel, ref showUploadText))
        {
            _mainConfig.Current.ShowUploadingText = showUploadText;
            _mainConfig.Save();
        }
        CkGui.HelpText(CkLoc.Settings.Preferences.ShowUploadingTextTT);

        if (ImGui.Checkbox(CkLoc.Settings.Preferences.TransferWindowLabel, ref transferDebug))
        {
            _mainConfig.Current.TransferWindow = transferDebug;
            _mainConfig.Save();
            Mediator.Publish(new UiToggleMessage(typeof(TransferBarUI), transferDebug ? ToggleType.Show : ToggleType.Hide));
        }
        CkGui.HelpText(CkLoc.Settings.Preferences.TransferWindowTT);

        if (ImGui.Checkbox(CkLoc.Settings.Preferences.TransferBarsLabel, ref progressBars))
        {
            _mainConfig.Current.TransferBars = progressBars;
            _mainConfig.Save();
        }
        CkGui.HelpText(CkLoc.Settings.Preferences.TransferBarsTT);

        using var dis = ImRaii.Disabled(!progressBars);
        using var ident = ImRaii.PushIndent();

        using (ImRaii.Group())
        {
            using (ImRaii.Group())
            {
                CkGui.FramedIconText(FAI.TextWidth);
                ImUtf8.SameLineInner();
                ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
                if (ImGui.SliderInt("##transfer-width", ref barWidth, 50, 1000))
                {
                    _mainConfig.Current.TransferBarWidth = barWidth;
                    _mainConfig.Save();
                }
            }
            CkGui.AttachToolTip(CkLoc.Settings.Preferences.TransferBarWidthTT);

            CkGui.FrameSeparatorV();

            using (ImRaii.Group())
            {
                CkGui.FramedIconText(FAI.TextHeight);
                ImUtf8.SameLineInner();
                ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
                if (ImGui.SliderInt("##transfer-height", ref barHeight, 10, 500))
                {
                    _mainConfig.Current.TransferBarHeight = barHeight;
                    _mainConfig.Save();
                }
            }
            CkGui.AttachToolTip(CkLoc.Settings.Preferences.TransferBarHeightTT);

            CkGui.FrameSeparatorV();

            if (ImGui.Checkbox(CkLoc.Settings.Preferences.TransferBarTextLabel, ref showBarText))
            {
                _mainConfig.Current.TransferBarText = showBarText;
                _mainConfig.Save();
            }
            CkGui.HelpText(CkLoc.Settings.Preferences.TransferBarTextTT);
        }
    }

    private void DrawPrefsNotify()
    {
        /* --------------- Separator for moving onto the Notifications Section ----------- */
        CkGui.FontText(CkLoc.Settings.Preferences.HeaderNotifications, UiFontService.UidFont);
        var onlineNotifs = _mainConfig.Current.OnlineNotifications;
        var onlineNotifsNickLimited = _mainConfig.Current.NotifyLimitToNickedPairs;

        if (ImGui.Checkbox(CkLoc.Settings.Preferences.OnlineNotifLabel, ref onlineNotifs))
        {
            _mainConfig.Current.OnlineNotifications = onlineNotifs;
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

    /// <summary>
    ///     Updates the global permissions on the server. Should be executed as a UIService task to prevent edits while processing.
    /// </summary>
    public async Task<bool> ChangeGlobalPerm(string propertyName, object newValue)
    {
        if (MainHub.ConnectionResponse?.GlobalPerms is not { } globals)
            return false;

        var type = globals.GetType();
        var property = type.GetProperty(propertyName);
        if (property is null || !property.CanRead || !property.CanWrite)
            return false;

        // Initially, Before sending it off, store the current value.
        var currentValue = property.GetValue(globals);

        try
        {
            // Update it before we send off for validation.
            if (!PropertyChanger.TrySetProperty(globals, propertyName, newValue, out object? finalVal))
                throw new InvalidOperationException($"Failed to set property {propertyName} in GlobalPerms with value {newValue}.");

            if (finalVal is null)
                throw new InvalidOperationException($"Property {propertyName} in GlobalPerms, has a finalValue of null.");

            // Now that it is updated client-side, attempt to make the change on the server, and get the hub response.
            var response = await _hub.ChangeGlobalPerm(propertyName, newValue);

            if (response.ErrorCode is not SundouleiaApiEc.Success)
                throw new InvalidOperationException($"Failed to change {propertyName} to {finalVal}. Reason: {response.ErrorCode}");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"(Resetting to Previous Value): {ex.Message}");
            property.SetValue(globals, currentValue);
            return false;
        }

        return true;
    }
}
