using CkCommons;
using CkCommons.DrawSystem.Selector;
using CkCommons.Gui;
using CkCommons.Gui.Utility;
using CkCommons.Helpers;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.UI;
using OtterGui;
using OtterGui.Text;
using Sundouleia.Interop;
using Sundouleia.Localization;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Util;

namespace Sundouleia.Gui;

public class SettingsUi : WindowMediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly MainConfig _config;
    private readonly ProfilesTab _accountsTab;
    private readonly DebugTab _debugTab;
    private readonly ConfigDirector _configDirector;
    private readonly UiDataStorageShared _storageShared;
    private readonly DtrService _dtr;

    public SettingsUi(ILogger<SettingsUi> logger, SundouleiaMediator mediator, 
        MainHub hub, MainConfig config, ProfilesTab accounts, DebugTab debug, 
        ConfigDirector configDirector, UiDataStorageShared dataStorage, DtrService dtr)
        : base(logger, mediator, "Sundouleia Settings")
    {
        _hub = hub;
        _config = config;
        _accountsTab = accounts;
        _debugTab = debug;
        _configDirector = configDirector;
        _storageShared = dataStorage;
        _dtr = dtr;

        Flags = WFlags.NoScrollbar;
        this.PinningClickthroughFalse();
        this.SetBoundaries(new(625, 420), ImGui.GetIO().DisplaySize);

        TitleBarButtons = new TitleBarButtonBuilder()
            .Add(FAI.Tshirt, "Open Active State Debugger", () => Mediator.Publish(new UiToggleMessage(typeof(DebugActiveStateUI))))
#if DEBUG
            .Add(FAI.PersonRays, "Open Personal Data Debugger", () => Mediator.Publish(new UiToggleMessage(typeof(DebugPersonalDataUI))))
            .Add(FAI.Database, "Open Storages Debugger", () => Mediator.Publish(new UiToggleMessage(typeof(DebugStorageUI))))
#endif
            .Add(FAI.Bell, "Actions Notifier", () => Mediator.Publish(new UiToggleMessage(typeof(DataEventsUI))))
            .Build();
    }

    protected override void PreDrawInternal()
    { }

    protected override void PostDrawInternal()
    { }

    protected override void DrawInternal()
    {
        var minPos = ImGui.GetCursorPos();
        var buttonPos = minPos + new Vector2(ImGui.GetContentRegionAvail().X - 100f, 0);
        ImGui.Text(CkLoc.Settings.OptionalPlugins);

        ImGui.SameLine();
        CkGui.ColorTextBool("Penumbra", IpcCallerPenumbra.APIAvailable);
        CkGui.AttachTooltip(IpcCallerPenumbra.APIAvailable ? CkLoc.Settings.PluginValid : CkLoc.Settings.PluginInvalid);

        ImGui.SameLine();
        CkGui.ColorTextBool("Glamourer", IpcCallerGlamourer.APIAvailable);
        CkGui.AttachTooltip(IpcCallerGlamourer.APIAvailable ? CkLoc.Settings.PluginValid : CkLoc.Settings.PluginInvalid);

        ImGui.SameLine();
        CkGui.ColorTextBool("C+", IpcCallerCustomize.APIAvailable);
        CkGui.AttachTooltip(IpcCallerCustomize.APIAvailable ? CkLoc.Settings.PluginValid : CkLoc.Settings.PluginInvalid);

        ImGui.SameLine();
        CkGui.ColorTextBool("Heels", IpcCallerHeels.APIAvailable);
        CkGui.AttachTooltip(IpcCallerHeels.APIAvailable ? CkLoc.Settings.PluginValid : CkLoc.Settings.PluginInvalid);

        ImGui.SameLine();
        CkGui.ColorTextBool("Honorific", IpcCallerHonorific.APIAvailable);
        CkGui.AttachTooltip(IpcCallerHonorific.APIAvailable ? CkLoc.Settings.PluginValid : CkLoc.Settings.PluginInvalid);

        ImGui.SameLine();
        var hasMoodles = IpcCallerMoodles.APIAvailable;
        var hasLoci = IpcCallerLoci.APIAvailable;
        var hasEither = hasMoodles || hasLoci;
        CkGui.ColorTextBool("Moodles", hasMoodles, ImGuiColors.HealerGreen, hasEither ? ImGuiColors.ParsedGrey : ImGuiColors.DalamudRed);
        CkGui.AttachTooltip(IpcCallerMoodles.APIAvailable ? CkLoc.Settings.PluginValid : hasEither ? "Loci satisfies this dependancy." : CkLoc.Settings.PluginInvalid);

        ImGui.SameLine(0, 0);
        ImGui.TextUnformatted("/");
        ImGui.SameLine(0, 0);
        CkGui.ColorTextBool("Loci", hasLoci, ImGuiColors.HealerGreen, hasEither ? ImGuiColors.DalamudYellow : ImGuiColors.DalamudRed);
        var noLociTT = "--COL--Moodles--COL-- is used for your custom status icon display plugin." +
            "--SEP--This will work fine, however you will not be able to see other's Loci statuses.";
        CkGui.AttachTooltip(IpcCallerLoci.APIAvailable ? CkLoc.Settings.PluginValid : hasEither ? noLociTT : CkLoc.Settings.PluginInvalid);

        ImGui.SameLine();
        CkGui.ColorTextBool("PetNames", IpcCallerPetNames.APIAvailable);
        CkGui.AttachTooltip(IpcCallerPetNames.APIAvailable ? CkLoc.Settings.PluginValid : CkLoc.Settings.PluginInvalid);

        ImGui.SameLine();
        CkGui.ColorTextBool("Brio", IpcCallerBrio.APIAvailable);
        CkGui.AttachTooltip(IpcCallerBrio.APIAvailable ? CkLoc.Settings.PluginValid : CkLoc.Settings.PluginInvalid);

        ImGui.Text("Register account:");

        ImGui.SameLine();
        if (ImUtf8.SmallButton("Sundouleia Discord"))
            Util.OpenLink("https://discord.gg/QJy4zTqpMD");

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
                    DrawPrefsRequests();
                    ImGui.Separator();
                    DrawPrefsNotify();
                    ImGui.EndTabItem();
                }
            }

#if DEBUG
            if (ImGui.BeginTabItem("HubService Settings"))
            {
                DrawHubServiceSelector();
                ImGui.EndTabItem();
            }
#endif

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

        ImGui.SetCursorPos(buttonPos);
        using (ImRaii.Group())
        {
            if (CkGui.FancyButton(FAI.Folder, "Configs", 100f, false))
            {
                try { Process.Start(new ProcessStartInfo { FileName = ConfigFileProvider.ConfigDirectory, UseShellExecute = true }); }
                catch (Bagagwa e) { Svc.Logger.Error($"Failed to open the config directory. {e.Message}"); }
            }
            CkGui.AttachTooltip("Opens the Config Folder.--NL--(Useful for debugging)");
        }
    }

    private void DrawMainGeneric()
    {
        CkGui.FontText(CkLoc.Settings.MainOptions.HeaderGeneric, Fonts.UidFont);
        var autoOpen = _config.Current.OpenUiOnStartup;
        var contextMenus = _config.Current.ShowContextMenus;
        var showProfiles = _config.Current.ShowProfiles;
        var profileDelay = _config.Current.ProfileDelay;
        var allowNsfw = _config.Current.AllowNSFW;

        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.ShowMainUiOnStartLabel, ref autoOpen))
        {
            _config.Current.OpenUiOnStartup = autoOpen;
            _config.Save();
        }
        CkGui.HelpText(CkLoc.Settings.MainOptions.ShowMainUiOnStartTT);

        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.ContextMenusLabel, ref contextMenus))
        {
            _config.Current.ShowContextMenus = contextMenus;
            _config.Save();
        }
        CkGui.HelpText(CkLoc.Settings.MainOptions.ContextMenusTT);

        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.ShowProfilesLabel, ref showProfiles))
        {
            Mediator.Publish(new ClearProfileCache());
            _config.Current.ShowProfiles = showProfiles;
            _config.Save();
        }
        CkGui.HelpText(CkLoc.Settings.MainOptions.ShowProfilesTT);

        using var dis = ImRaii.Disabled(!showProfiles);
        using (ImRaii.PushIndent())
        {
            ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderFloat("##Profile-Delay", ref profileDelay, 0.3f, 5, $"%.1f {CkLoc.Settings.MainOptions.ProfileDelayLabel}"))
            {
                _config.Current.ProfileDelay = profileDelay;
                _config.Save();
            }
            CkGui.HelpText(CkLoc.Settings.MainOptions.ProfileDelayTT);
        }

        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.AllowNSFWLabel, ref allowNsfw))
        {
            _config.Current.AllowNSFW = allowNsfw;
            _config.Save();
        }
        CkGui.HelpText(CkLoc.Settings.MainOptions.AllowNSFWTT);
    }

    private string? _timespanStrCache = null;
    private void DrawMainGlobals()
    {
        if (!MainHub.IsConnected)
            return;

        CkGui.FontText(CkLoc.Settings.MainOptions.HeaderGlobalPerms, Fonts.UidFont);

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
        CkGui.AttachTooltip(CkLoc.Settings.MainOptions.AllowAnimationsTT);

        ImGui.SameLine();
        var soundsGlobal = MainHub.GlobalPerms.DefaultAllowSounds;
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.AllowSoundsLabel, ref soundsGlobal, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultAllowSounds), soundsGlobal));
        CkGui.AttachTooltip(CkLoc.Settings.MainOptions.AllowSoundsTT);

        ImGui.SameLine();
        var vfxGlobal = MainHub.GlobalPerms.DefaultAllowVfx;
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.AllowVfxLabel, ref vfxGlobal, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultAllowVfx), vfxGlobal));
        CkGui.AttachTooltip(CkLoc.Settings.MainOptions.AllowVfxTT);

        var curShare = MainHub.GlobalPerms.DefaultShareOwnLociData;
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.ShareStatuses, ref curShare, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultShareOwnLociData), curShare));
        CkGui.HelpText(CkLoc.Settings.MainOptions.ShareStatusesTT);

        var height = ImUtf8.FrameHeight * 4 + ImGui.GetStyle().CellPadding.Y * 8;
        using var _ = CkRaii.FramedChildPaddedW("access", ImGui.GetContentRegionAvail().X, height, 0, ImGui.GetColorU32(ImGuiCol.Separator), CkStyle.ChildRounding(), 2f);
        using var t = ImRaii.Table("##globalpermTable", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV);
        if (!t) return;
        
        ImGui.TableSetupColumn("##section");
        ImGui.TableSetupColumn("##values", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextRow();

        var curAccess = MainHub.GlobalPerms.DefaultLociAccess;

        // Loci Status Types.
        ImGui.TableNextColumn();
        CkGui.TextFrameAligned($"Allowed Status Types:");
        ImGui.TableNextColumn();
        var curPos = curAccess.HasAny(LociAccess.Positive);
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.AllowPosLociStatuses, ref curPos, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultLociAccess), curAccess ^ LociAccess.Positive));
        CkGui.AttachTooltip(CkLoc.Settings.MainOptions.AllowPosLociStatusesTT);

        ImGui.SameLine();
        var curNeg = curAccess.HasAny(LociAccess.Negative);
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.AllowNegLociStatuses, ref curNeg, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultLociAccess), curAccess ^ LociAccess.Negative));
        CkGui.AttachTooltip(CkLoc.Settings.MainOptions.AllowNegLociStatusesTT);

        ImGui.SameLine();
        var curSpecial = curAccess.HasAny(LociAccess.Special);
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.AllowSpecialLociStatuses, ref curSpecial, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultLociAccess), curAccess ^ LociAccess.Special));
        CkGui.AttachTooltip(CkLoc.Settings.MainOptions.AllowSpecialLociStatusesTT);
        ImGui.TableNextRow();

        // Loci Time
        ImGui.TableNextColumn();
        CkGui.TextFrameAligned("Maximum Duration:");
        ImGui.TableNextColumn();
        var refPermAccess = curAccess.HasAny(LociAccess.Permanent);
        if (ImGui.Checkbox(CkLoc.Settings.MainOptions.AllowPermanentLociStatuses, ref refPermAccess))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultLociAccess), curAccess ^ LociAccess.Permanent));
        CkGui.AttachTooltip(CkLoc.Settings.MainOptions.AllowPermanentLociStatusesTT);

        // Display inline, the icon input text field for setting a maximum duration, if we are not permanent.
        if (!refPermAccess)
        {
            var str = _timespanStrCache ?? MainHub.GlobalPerms.DefaultMaxLociTime.ToTimeSpanStr();
            ImGui.SameLine();
            if (CkGui.IconInputText(FAI.HourglassHalf, "Maximum Time", "0d0h0m0s", ref str, 32, 100f, true, UiService.DisableUI))
            {
                if (str != MainHub.GlobalPerms.DefaultMaxLociTime.ToTimeSpanStr() && CkTimers.TryParseTimeSpan(str, out var newTime))
                {
                    var ticks = (ulong)newTime.Ticks;
                    _logger.LogInformation($"Changing OwnGlobals MaxLociTime to {ticks} ticks.", LoggerType.PairDataTransfer);
                    UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultMaxLociTime), ticks));
                }
                _timespanStrCache = null;
            }
            CkGui.AttachTooltip($"The longest duration a timed loci can be applied to you.");
        }
        ImGui.TableNextRow();

        // Loci Application Types
        ImGui.TableNextColumn();
        CkGui.TextFrameAligned("Let Others Apply:");
        
        ImGui.TableNextColumn();
        var canApplyOwn = curAccess.HasAny(LociAccess.AllowOwn);
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.AllowOwnLociData, ref canApplyOwn, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultLociAccess), curAccess ^ LociAccess.AllowOwn));
        CkGui.AttachTooltip(CkLoc.Settings.MainOptions.AllowOwnLociDataTT);

        ImGui.SameLine();
        var canApplyOthers = curAccess.HasAny(LociAccess.AllowOther);
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.AllowOtherLociData, ref canApplyOthers, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultLociAccess), curAccess ^ LociAccess.AllowOther));
        CkGui.AttachTooltip(CkLoc.Settings.MainOptions.AllowOtherLociDataTT);
        ImGui.TableNextRow();

        // Removal
        ImGui.TableNextColumn();
        CkGui.TextFrameAligned("Let Others Remove:");
        ImGui.TableNextColumn();
        var canRemoveApplied = curAccess.HasAny(LociAccess.RemoveApplied);
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.RemoveAppliedLociStatuses, ref canRemoveApplied, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultLociAccess), curAccess ^ LociAccess.RemoveApplied));
        CkGui.AttachTooltip(CkLoc.Settings.MainOptions.RemoveAppliedLociStatusesTT);

        ImGui.SameLine();
        var canRemoveAny = curAccess.HasAny(LociAccess.RemoveAny);
        if (CkGui.Checkbox(CkLoc.Settings.MainOptions.RemoveAnyLociStatuses, ref canRemoveAny, UiService.DisableUI))
            UiService.SetUITask(async () => await ChangeGlobalPerm(nameof(GlobalPerms.DefaultLociAccess), curAccess ^ LociAccess.RemoveAny));
        CkGui.AttachTooltip(CkLoc.Settings.MainOptions.RemoveAnyLociStatusesTT);
    }


    private void DrawMainRadar()
    {
        //CkGui.FontText(CkLoc.Settings.MainOptions.HeaderRadar, Fonts.UidFont);
        //var enabled = _config.Current.RadarEnabled;
        //var sendPings = _config.Current.RadarSendPings;
        //var nearbyDtr = _config.Current.RadarNearbyDtr;
        //var joinChats = _config.Current.RadarJoinChats;
        //var showUnreadBubble = _config.Current.RadarShowUnreadBubble;

        //if (ImGui.Checkbox(CkLoc.Settings.MainOptions.RadarEnabledLabel, ref enabled))
        //{
        //    _config.Current.RadarEnabled = enabled;
        //    _config.Save();
        //    Mediator.Publish(new RadarConfigChanged(nameof(ConfigStorage.RadarEnabled)));
        //}

        //using var _ = ImRaii.Disabled(!enabled);

        //if (ImGui.Checkbox(CkLoc.Settings.MainOptions.RadarSendPingsLabel, ref sendPings))
        //{
        //    _config.Current.RadarSendPings = sendPings;
        //    _config.Save();
        //    Mediator.Publish(new RadarConfigChanged(nameof(ConfigStorage.RadarSendPings)));
        //}
        //ImUtf8.SameLineInner();
        //CkGui.FramedIconText(FAI.SatelliteDish);
        //CkGui.HelpText(CkLoc.Settings.MainOptions.RadarSendPingsTT, true);

        //if (ImGui.Checkbox(CkLoc.Settings.MainOptions.RadarNearbyDtrLabel, ref nearbyDtr))
        //{
        //    _config.Current.RadarNearbyDtr = nearbyDtr;
        //    _config.Save();
        //    _dtr.RefreshEntries();
        //    Mediator.Publish(new RadarConfigChanged(nameof(ConfigStorage.RadarNearbyDtr)));
        //}
        //ImUtf8.SameLineInner();
        //CkGui.FramedIconText(FAI.PersonDressBurst);
        //CkGui.HelpText(CkLoc.Settings.MainOptions.RadarNearbyDtrTT, true);

        //if (ImGui.Checkbox(CkLoc.Settings.MainOptions.RadarJoinChatsLabel, ref joinChats))
        //{
        //    _config.Current.RadarJoinChats = joinChats;
        //    _config.Save();
        //    _dtr.RefreshEntries();
        //    Mediator.Publish(new RadarConfigChanged(nameof(ConfigStorage.RadarJoinChats)));
        //}
        //ImUtf8.SameLineInner();
        //CkGui.FramedIconText(FAI.CommentDots);
        //CkGui.HelpText(CkLoc.Settings.MainOptions.RadarJoinChatsTT, true);

        //if (ImGui.Checkbox(CkLoc.Settings.MainOptions.RadarShowUnreadBubbleLabel, ref showUnreadBubble))
        //{
        //    _config.Current.RadarShowUnreadBubble = showUnreadBubble;
        //    _config.Save();
        //    Mediator.Publish(new RadarConfigChanged(nameof(ConfigStorage.RadarShowUnreadBubble)));
        //}
        //ImUtf8.SameLineInner();
        //CkGui.FramedIconText(FAI.Bell);
        //CkGui.HelpText(CkLoc.Settings.MainOptions.RadarShowUnreadBubbleTT, true);
    }

    private void DrawPrefsDownloads()
    {
        CkGui.FontText(CkLoc.Settings.Preferences.HeaderDownloads, Fonts.UidFont);
        var maxParallelDLs = _config.Current.MaxParallelDownloads;
        var dlLimit = _config.Current.DownloadLimitBytes;
        var dlSpeedType = _config.Current.DownloadSpeedType;

        CkGui.FramedIconText(FAI.Gauge);
        CkGui.TextFrameAlignedInline(CkLoc.Settings.Preferences.DownloadLimitLabel);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("###DownloadSpeedLimit", ref dlLimit))
        {
            _config.Current.DownloadLimitBytes = dlLimit;
            _config.Save();
            Mediator.Publish(new DownloadLimitChangedMessage());
        }
        CkGui.AttachTooltip(CkLoc.Settings.Preferences.DownloadLimitTT);

        ImUtf8.SameLineInner();
        if (CkGuiUtils.EnumCombo("###SpeedType", 50 * ImGuiHelpers.GlobalScale, _config.Current.DownloadSpeedType, out var newType, s => s.ToName()))
        {
            _config.Current.DownloadSpeedType = newType;
            _config.Save();
            Mediator.Publish(new DownloadLimitChangedMessage());
        }
        CkGui.AttachTooltip(CkLoc.Settings.Preferences.DownloadSpeedTypeTT);

        CkGui.FramedIconText(FAI.Download);
        CkGui.TextFrameAlignedInline(CkLoc.Settings.Preferences.MaxParallelDLsLabel);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("###Maximum Parallel Downloads", ref maxParallelDLs, 1, 10))
        {
            _config.Current.MaxParallelDownloads = maxParallelDLs;
            _config.Save();
        }
        CkGui.HelpText(CkLoc.Settings.Preferences.MaxParallelDLsTT);

        var showUploadText = _config.Current.ShowUploadingText;
        var transferDebug = _config.Current.TransferWindow;
        var progressBars = _config.Current.TransferBars;
        var showBarText = _config.Current.TransferBarText;
        var barHeight = _config.Current.TransferBarHeight;
        var barWidth = _config.Current.TransferBarWidth;

        if (ImGui.Checkbox(CkLoc.Settings.Preferences.ShowUploadingTextLabel, ref showUploadText))
        {
            _config.Current.ShowUploadingText = showUploadText;
            _config.Save();
        }
        CkGui.HelpText(CkLoc.Settings.Preferences.ShowUploadingTextTT);

        if (ImGui.Checkbox(CkLoc.Settings.Preferences.TransferWindowLabel, ref transferDebug))
        {
            _config.Current.TransferWindow = transferDebug;
            _config.Save();
            Mediator.Publish(new UiToggleMessage(typeof(TransferBarUI), transferDebug ? ToggleType.Show : ToggleType.Hide));
        }
        CkGui.HelpText(CkLoc.Settings.Preferences.TransferWindowTT);

        if (ImGui.Checkbox(CkLoc.Settings.Preferences.TransferBarsLabel, ref progressBars))
        {
            _config.Current.TransferBars = progressBars;
            _config.Save();
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
                    _config.Current.TransferBarWidth = barWidth;
                    _config.Save();
                }
            }
            CkGui.AttachTooltip(CkLoc.Settings.Preferences.TransferBarWidthTT);

            CkGui.FrameSeparatorV();

            using (ImRaii.Group())
            {
                CkGui.FramedIconText(FAI.TextHeight);
                ImUtf8.SameLineInner();
                ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
                if (ImGui.SliderInt("##transfer-height", ref barHeight, 10, 500))
                {
                    _config.Current.TransferBarHeight = barHeight;
                    _config.Save();
                }
            }
            CkGui.AttachTooltip(CkLoc.Settings.Preferences.TransferBarHeightTT);

            CkGui.FrameSeparatorV();

            if (ImGui.Checkbox(CkLoc.Settings.Preferences.TransferBarTextLabel, ref showBarText))
            {
                _config.Current.TransferBarText = showBarText;
                _config.Save();
            }
            CkGui.HelpText(CkLoc.Settings.Preferences.TransferBarTextTT);
        }
    }

    private void DrawPrefsRequests()
    {
        CkGui.FontText("Requests", Fonts.UidFont);
        var showBubbles = _config.Current.RequestNotifiers.HasAny(AlertKind.Bubble);
        var showDtr = _config.Current.RequestNotifiers.HasAny(AlertKind.DtrBar);
        var sounds = _config.Current.RequestNotifiers.HasAny(AlertKind.Audio);
        
        if (ImGui.Checkbox("Show Total Incoming", ref showBubbles))
        {
            _config.Current.RequestNotifiers ^= AlertKind.Bubble;
            _config.Save();
        }
        CkGui.HelpText("Displays the number of incoming requests along the MainUI TabBar.");

        if (ImGui.Checkbox("Show DTR Entry", ref showDtr))
        {
            _config.Current.RequestNotifiers ^= AlertKind.DtrBar;
            _config.Save();
            _dtr.RefreshEntries();
        }
        CkGui.HelpText("Displays the number of incoming and outoing requests to the DTR bar.");

        if (ImGui.Checkbox("Play Audio Alerts", ref sounds))
        {
            _config.Current.RequestNotifiers ^= AlertKind.Audio;
            _config.Save();
        }
        CkGui.HelpText("Use a In-Game or custom sound trigger after recieving a request");

        using var dis = ImRaii.Disabled(!sounds);
        using var ident = ImRaii.PushIndent();
        
        using (ImRaii.Group())
        {
            CkGui.FramedIconText(FAI.FileAudio);
            ImUtf8.SameLineInner();
            ImGui.SetNextItemWidth(125 * ImGuiHelpers.GlobalScale);
            int soundType = _config.Current.AlertIsCustomSound ? 1 : 0;
            if (ImGui.Combo("##SoundType", ref soundType, "Game Sound\0Custom Sound\0"))
            {
                _config.Current.AlertIsCustomSound = soundType == 1;
                _config.UpdateAudio();
            }
        }
        CkGui.AttachTooltip("The type of audio to be played.");
                
        CkGui.FrameSeparatorV();

        // Sample sound.
        if (CkGui.IconButton(FAI.Play, disabled: !sounds))
            _config.StartSound();

        ImUtf8.SameLineInner();
        if (_config.Current.AlertIsCustomSound)
            DrawCustomSound();
        else
            DrawGameSound();
    }

    private void DrawGameSound()
    {
        var curGamesound = _config.Current.AlertGameSoundbyte;
        if (CkGuiUtils.EnumCombo("##alert-gamesounds", 150f, curGamesound, out var newSound, _ => _.ToName()))
        {
            _config.Current.AlertGameSoundbyte = newSound;
            UIGlobals.PlaySoundEffect((uint)newSound);
            _config.UpdateAudio();
        }
        CkGui.AttachTooltip("The In-Game Audio to play when recieving a new request");
    }

    private void DrawCustomSound()
    {
        var volume = _config.Current.AlertVolume;
        ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("##alertVolume", ref volume, 0, 1, $"Volume: {volume * 100:F1}%%"))
        {
            _config.Current.AlertVolume = volume;
            _config.UpdateAudio();
        }
        CkGui.AttachTooltip("How loud the custom sound is in playback");

        ImGui.SameLine();
        var path = _config.Current.AlertSoundPath;

        var soundInvalid = _config.Current.AlertIsCustomSound && !_config.IsSoundReady();
        using var col = ImRaii.PushColor(ImGuiCol.Border, 0xFF0000FF, soundInvalid);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 2, soundInvalid);

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputTextWithHint($"##custom-path-input", "Sound File Path..", ref path, 256))
        {
            _config.Current.AlertSoundPath = path;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _config.UpdateAudio();
            _config.Save();
        }
        CkGui.AttachTooltip(soundInvalid ? "--COL--Sound Path Invalid!--COL--" : "The filepath to the custom audio file.", ImGuiColors.DalamudRed);
    }

    private void DrawPrefsNotify()
    {
        /* --------------- Separator for moving onto the Notifications Section ----------- */
        CkGui.FontText(CkLoc.Settings.Preferences.HeaderNotifications, Fonts.UidFont);
        var pairDtr = _config.Current.EnablePairDtr;
        var onlineNotifs = _config.Current.OnlineNotifications;
        var onlineNotifsNickLimited = _config.Current.NotifyLimitToNickedPairs;

        if (ImGui.Checkbox(CkLoc.Settings.Preferences.PairDtrEntry, ref pairDtr))
        {
            _config.Current.EnablePairDtr = pairDtr;
            _config.Save();
        }
        CkGui.HelpText(CkLoc.Settings.Preferences.PairDtrEntryTT);

        if (ImGui.Checkbox(CkLoc.Settings.Preferences.OnlineNotifLabel, ref onlineNotifs))
        {
            _config.Current.OnlineNotifications = onlineNotifs;
            if (!onlineNotifs) _config.Current.NotifyLimitToNickedPairs = false;
            _config.Save();
        }
        CkGui.HelpText(CkLoc.Settings.Preferences.OnlineNotifTT);

        using (ImRaii.Disabled(!onlineNotifs))
        {
            if (ImGui.Checkbox(CkLoc.Settings.Preferences.LimitForNicksLabel, ref onlineNotifsNickLimited))
            {
                _config.Current.NotifyLimitToNickedPairs = onlineNotifsNickLimited;
                _config.Save();
            }
            CkGui.HelpText(CkLoc.Settings.Preferences.LimitForNicksTT);
        }

        if(ImGuiUtil.GenericEnumCombo("Info Location##notifInfo", 125f, _config.Current.InfoNotification, out var newInfo, i => i.ToString()))
        {
            _config.Current.InfoNotification = newInfo;
            _config.Save();
        }
        CkGui.HelpText("The location where \"Info\" notifications will display." +
            "--NL----COL--Nowhere--COL-- will not show any Info notifications." +
            "--NL----COL--Chat--COL-- prints Info notifications in chat" +
            "--NL----COL--Toast--COL-- shows Info toast notifications in the bottom right corner" +
            "--NL----COL--Both--COL-- shows chat as well as the toast notification", ImGuiColors.ParsedGold);

        if (ImGuiUtil.GenericEnumCombo("Warning Location##notifWarn", 125f, _config.Current.WarningNotification, out var newWarn, i => i.ToString()))
        {
            _config.Current.WarningNotification = newWarn;
            _config.Save();
        }
        CkGui.HelpText("The location where \"Warning\" notifications will display." +
            "--NL----COL--Nowhere--COL-- will not show any Warning notifications." +
            "--NL----COL--Chat--COL-- prints Warning notifications in chat" +
            "--NL----COL--Toast--COL-- shows Warning toast notifications in the bottom right corner" +
            "--NL----COL--Both--COL-- shows chat as well as the toast notification", ImGuiColors.ParsedGold);

        if (ImGuiUtil.GenericEnumCombo("Error Location##notifError", 125f, _config.Current.ErrorNotification, out var newError, i => i.ToString()))
        {
            _config.Current.ErrorNotification = newError;
            _config.Save();
        }
        CkGui.HelpText("The location where \"Error\" notifications will display." +
            "--NL----COL--Nowhere--COL-- will not show any Error notifications." +
            "--NL----COL--Chat--COL-- prints Error notifications in chat" +
            "--NL----COL--Toast--COL-- shows Error toast notifications in the bottom right corner" +
            "--NL----COL--Both--COL-- shows chat as well as the toast notification", ImGuiColors.ParsedGold);
    }

    private string _customServerName = string.Empty;
    private string _customServerUri = string.Empty;
    private void DrawHubServiceSelector()
    {
        CkGui.FontText("Current ServiceHub Service Selector", Fonts.UidFont);
        var hubs = ServerHubConfig.ServerHubs;
        var current = ServerHubConfig.CurrentHub;

        CkGui.FramedIconText(FAI.GlobeAsia);
        CkGui.TextFrameAlignedInline($"{current.HubName}, URI:");
        ImGui.SameLine();
        CkGui.TagLabelTextFrameAligned(current.HubUri, ImGuiColors.ParsedGold.Darken(.5f), 3 * ImGuiHelpers.GlobalScale);

        CkGui.FramedIconText(FAI.Hdd);
        ImUtf8.SameLineInner();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.5f);
        using (var c = ImRaii.Combo("Select Service", current.HubName))
        {
            if (c)
            {
                for (int i = 0; i < hubs.Count; i++)
                {
                    var selected = hubs[i] == current;
                    if (ImGui.Selectable($"{hubs[i].HubName} ({hubs[i].HubUri})", selected) && !selected)
                    {
                        _configDirector.SetHubIndex(i);
                        UiService.SetUITask(async () => await _hub.Reconnect(DisconnectIntent.Reload).ConfigureAwait(false));
                    }
                }
            }
        }
        if (ServerHubConfig.ChosenHubIndex >= 2)
        {
            // Change this later to let you remove the service while not connected to it and stuff.
            ImGui.SameLine();
            if (CkGui.IconTextButton(FAI.Trash, "Remove Service"))
            {
                if (!_configDirector.RemoveServerHub(ServerHubConfig.CurrentHub))
                    return;
                // Successful removal, reconnect.
                UiService.SetUITask(async () => await _hub.Reconnect(DisconnectIntent.Reload).ConfigureAwait(false));
            }
        }

        ImGui.Separator();
        ImGui.SetNextItemWidth(250);
        ImGui.InputText("Custom Service Name", ref _customServerName, 255);

        ImGui.SetNextItemWidth(250);
        ImGui.InputText("Custom Service URI", ref _customServerUri, 255);
        if (CkGui.IconTextButton(FAI.Plus, "Add Service"))
        {
            if (string.IsNullOrWhiteSpace(_customServerUri) || !Uri.IsWellFormedUriString(_customServerUri, UriKind.Absolute))
                return;
            // It is valid, so add the service.
            var newService = new ServerHubInfo() { HubName = _customServerName, HubUri = _customServerUri };
            if (!_configDirector.AddServerHub(newService))
                return;

            // Reset the input fields, and save the config.
            _customServerName = string.Empty;
            _customServerUri = string.Empty;
        }
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
