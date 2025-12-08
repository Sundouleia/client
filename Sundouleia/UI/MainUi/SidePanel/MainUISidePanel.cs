using CkCommons;
using CkCommons.Gui;
using CkCommons.Helpers;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui;
using OtterGui.Text;
using Sundouleia.DrawSystem;
using Sundouleia.DrawSystem.Selector;
using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Util;
using System.Collections.Immutable;

namespace Sundouleia.Gui.MainWindow;

// We could ideally have this continuously running but never drawing much
// if anything at all while not expected.
// It would allow us to process the logic in the draw-loop like we want.
public class MainUISidePanel : WindowMediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly MainMenuTabs _mainUiTabs;
    private readonly FolderConfig _config;
    private readonly GroupsFolderDrawer _folderDrawer;
    private readonly SundesmoManager _sundesmos;
    private readonly StickyUIService _service;

    public MainUISidePanel(ILogger<MainUISidePanel> logger, SundouleiaMediator mediator,
        MainHub hub, MainMenuTabs tabs, FolderConfig config, GroupsFolderDrawer drawer,
        SundesmoManager sundesmos, StickyUIService service)
        : base(logger, mediator, "##SundouleiaInteractionsUI")
    {
        _mainUiTabs = tabs;
        _config = config;
        _hub = hub;
        _sundesmos = sundesmos;
        _folderDrawer = drawer;
        _service = service;

        Flags = WFlags.NoCollapse | WFlags.NoTitleBar | WFlags.NoScrollbar;
    }

    /// <summary>
    ///     Internal logic performed every draw frame regardless of if the window is open or not. <para />
    ///     Lets us Open/Close the window based on logic in the service using minimal computation.
    /// </summary>
    public override void PreOpenCheck()
    {
        IsOpen = _service.CanDraw;
        if (_service.DisplayMode is not SidePanelMode.GroupEditor)
            Flags |= WFlags.NoResize;
        else
            Flags &= ~WFlags.NoResize;
    }
    protected override void PreDrawInternal()
    {
        // Magic that makes the sticky pair window move with the main UI.
        var position = MainUI.LastPos;
        position.X += MainUI.LastSize.X;
        position.Y += ImGui.GetFrameHeightWithSpacing();
        ImGui.SetNextWindowPos(position);
        Flags |= WFlags.NoMove;

        float fixedWidth = _service.DisplayWidth;
        float fixedHeight = MainUI.LastSize.Y - ImGui.GetFrameHeightWithSpacing() * 2;

        if (_service.DisplayMode is SidePanelMode.GroupEditor)
            this.SetBoundaries(new(fixedWidth, fixedHeight), new(1000, fixedHeight));
        else
            this.SetBoundaries(new(fixedWidth, fixedHeight), new(fixedWidth, fixedHeight));
    }

    protected override void PostDrawInternal()
    { }

    // If this runs, it is assumed that for this frame the data is valid for drawing.
    protected override void DrawInternal()
    {
        // If there is no mode to draw, do not draw.
        if (_service.DisplayMode is SidePanelMode.None)
            return;

        // Display the correct mode.
        switch (_service.DisplayCache)
        {
            case GroupOrganizerCache goc:
                DrawGroupOrganizer(goc);
                return;
            case InteractionsCache ic:
                DrawInteractions(ic);
                return;
            case ResponseCache irc when irc.Mode is SidePanelMode.IncomingRequests:
                DrawIncomingRequests(irc);
                return;
            case ResponseCache prc when prc.Mode is SidePanelMode.PendingRequests:
                DrawPendingRequests(prc);
                return;
        }
    }

    private void DrawGroupOrganizer(GroupOrganizerCache cache)
    {
        // Should be relatively simple to display this outside of some headers and stylizations.
        using var _ = CkRaii.Child("GroupOrganizer", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        var width = _.InnerRegion.X;
        CkGui.FontTextCentered("Group Organizer", UiFontService.Default150Percent);

        _folderDrawer.DrawButtonHeader(width);
        ImGui.Separator();


        _folderDrawer.DrawContents<GroupFolder>(width, DynamicFlags.Organizer);
    }

    #region Interactions
    private void DrawInteractions(InteractionsCache cache)
    { 
        using var _ = CkRaii.Child("InteractionsDisplay", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        var width = _.InnerRegion.X;
        var dispName = cache.DisplayName;
        var sundesmo = cache.Sundesmo!;

        DrawHeader(sundesmo, dispName);

        DrawCommon(sundesmo, dispName, width);
        ImGui.Separator();

        ImGui.Text("Permissions");
        DrawDistinctPermRow(sundesmo, dispName, width, nameof(PairPerms.AllowAnimations), sundesmo.OwnPerms.AllowAnimations);
        DrawDistinctPermRow(sundesmo, dispName, width, nameof(PairPerms.AllowSounds), sundesmo.OwnPerms.AllowSounds);
        DrawDistinctPermRow(sundesmo, dispName, width, nameof(PairPerms.AllowVfx), sundesmo.OwnPerms.AllowVfx);

        ImGui.Separator();

        ImGui.Text("Pair Options");
        DrawPairOptions(sundesmo, dispName, _.InnerRegion.X);
    }

    private void DrawHeader(Sundesmo s, string dispName)
    {
        CkGui.CenterText($"{dispName}'s Interactions");
        var width = CkGui.IconSize(FAI.VolumeUp).X + CkGui.IconSize(FAI.Running).X + CkGui.IconSize(FAI.PersonBurst).X + ImUtf8.ItemInnerSpacing.X * 2;
        CkGui.SetCursorXtoCenter(width);

        var sounds = s.PairPerms.AllowSounds;
        var anims = s.PairPerms.AllowAnimations;
        var vfx = s.PairPerms.AllowVfx;

        CkGui.IconText(FAI.VolumeUp, sounds ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip($"{dispName} {(sounds ? "can hear your modded SFX/Music." : "disabled your modded SFX/Music.")}");
        ImUtf8.SameLineInner();
        CkGui.IconText(FAI.Running, anims ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip($"{dispName} {(anims ? "can see your modded animations." : "disabled your modded animations.")}");
        ImUtf8.SameLineInner();
        CkGui.IconText(FAI.PersonBurst, vfx ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip($"{dispName} {(vfx ? "can see your modded VFX." : "disabled your modded VFX.")}");

        ImGui.Separator(); 
    }

    private void DrawCommon(Sundesmo s, string dispName, float width)
    {
        var isPaused = s.IsPaused;
        if (!isPaused)
        {
            if (CkGui.IconTextButton(FAI.User, "Open Profile", width, true, UiService.DisableUI))
                Mediator.Publish(new ProfileOpenMessage(s.UserData));
            CkGui.AttachToolTip($"Opens {dispName}'s profile!");

            if (CkGui.IconTextButton(FAI.ExclamationTriangle, $"Report {dispName}'s Profile", width, true, UiService.DisableUI))
                Mediator.Publish(new OpenReportUIMessage(s.UserData, ReportKind.Profile));
            CkGui.AttachToolTip($"Snapshot {dispName}'s Profile and make a report with its state.");
        }

        DrawDistinctPermRow(s, dispName, width, nameof(PairPerms.PauseVisuals), isPaused, false, true);
        CkGui.AttachToolTip($"{(!isPaused ? "Pause" : "Resume")} the rendering of {dispName}'s modded appearance.");
    }

    private void DrawPairOptions(Sundesmo s, string dispName, float width)
    {
        if (s.IsTemporary)
        {
            var blockButton = s.UserPair.TempAccepterUID != MainHub.UID;
            if (CkGui.IconTextButton(FAI.Link, "Convert to Permanent Pair", width, true, blockButton))
                UiService.SetUITask(async () =>
                {
                    var res = await _hub.UserPersistPair(new(s.UserData));
                    if (res.ErrorCode is not SundouleiaApiEc.Success)
                        _logger.LogWarning($"Failed to convert temporary pair for {dispName}. Reason: {res.ErrorCode}");
                    else
                    {
                        _logger.LogInformation($"Successfully converted temporary pair for {dispName}.");
                        s.MarkAsPermanent();
                    }
                });

            var timeLeft = TimeSpan.FromDays(1) - (DateTime.UtcNow - s.UserPair.CreatedAt);
            var autoDeleteText = $"Temp. Pairing Expires in --COL--{timeLeft.Days}d {timeLeft.Hours}h {timeLeft.Minutes}m--COL--";
            var ttStr = $"Makes a temporary pair permanent. --NL--{autoDeleteText}" +
                $"{(blockButton ? "--SEP----COL--Only the user who accepted the request can use this.--COL--" : string.Empty)}";
            CkGui.AttachToolTip(ttStr, color: ImGuiColors.DalamudYellow);
        }

        if (CkGui.IconTextButton(FAI.Trash, $"Remove {dispName} from your Pairs", width, true, !KeyMonitor.CtrlPressed() || !KeyMonitor.ShiftPressed()))
            UiService.SetUITask(async () =>
            {
                var res = await _hub.UserRemovePair(new(s.UserData));
                if (res.ErrorCode is not SundouleiaApiEc.Success)
                    _logger.LogWarning($"Failed to remove pair {dispName}. Reason: {res.ErrorCode}");
                else
                {
                    _logger.LogInformation($"Successfully removed pair {dispName}.");
                    ImGui.CloseCurrentPopup();
                }
            });
        CkGui.AttachToolTip($"Must hold --COL--CTRL & SHIFT to remove.", color: ImGuiColors.DalamudRed);
    }

    // Modular framework similar to GSpeak but vastly simplified.
    private void DrawDistinctPermRow(Sundesmo sundesmo, string dispName, float width, string permName, bool current, bool defaultTT = true, bool invertColors = false)
    {
        using var col = ImRaii.PushColor(ImGuiCol.Button, 0);
        var txtData = PermissionData[permName];
        var pos = ImGui.GetCursorScreenPos();
        var trueCol = invertColors ? CkColor.TriStateCross.Uint() : CkColor.TriStateCheck.Uint();
        var falseCol = invertColors ? CkColor.TriStateCheck.Uint() : CkColor.TriStateCross.Uint();

        if (ImGuiUtil.DrawDisabledButton("##pair" + permName, new Vector2(width, ImGui.GetFrameHeight()), string.Empty, UiService.DisableUI))
        {
            if (string.IsNullOrEmpty(permName))
                return;

            UiService.SetUITask(async () =>
            {
                if (await ChangeOwnUnique(sundesmo, permName, !current).ConfigureAwait(false))
                    _logger.LogInformation($"Successfully changed own permission {permName} to {!current} for {sundesmo.GetNickAliasOrUid()}.");
            });
        }

        ImGui.SetCursorScreenPos(pos);
        PrintButtonRichText(txtData, dispName, current, trueCol, falseCol);
        if (defaultTT)
            CkGui.AttachToolTip($"Toggle this preference for {dispName}.");

    }

    private void PrintButtonRichText(PermInfo pdp, string dispName, bool current, uint trueCol, uint falseCol)
    {
        using var _ = ImRaii.Group();
        CkGui.FramedIconText(current ? pdp.TrueFAI : pdp.FalseFAI);
        ImGui.SameLine(0, 0);
        if (pdp.CondAfterLabel)
        {
            CkGui.TextFrameAligned($" {dispName}");
            ImGui.SameLine(0, 0);
            ImGui.Text($" {pdp.Suffix} ");
            ImGui.SameLine(0, 0);
            CkGui.ColorTextFrameAligned(current ? pdp.CondTrue : pdp.CondFalse, current ? trueCol : falseCol);
            ImGui.SameLine(0, 0);
            ImGui.Text(".");
        }
        else
        {
            CkGui.ColorTextFrameAligned($" {(current ? pdp.CondTrue : pdp.CondFalse)} ", current ? trueCol : falseCol);
            ImGui.SameLine(0, 0);
            ImGui.Text(pdp.Label);
            ImGui.SameLine(0, 0);
            ImGui.Text($" {dispName}.");
        }
    }

    private record PermInfo(FAI TrueFAI, FAI FalseFAI, string CondTrue, string CondFalse, string Label, bool CondAfterLabel, string Suffix = "");
    private readonly ImmutableDictionary<string, PermInfo> PermissionData = ImmutableDictionary<string, PermInfo>.Empty
        .Add(nameof(PairPerms.PauseVisuals), new PermInfo(FAI.Eye, FAI.EyeSlash, "Paused", "Unpaused", string.Empty, true, "is"))
        .Add(nameof(PairPerms.AllowAnimations), new PermInfo(FAI.Running, FAI.Ban, "Allowing", "Preventing", "animations from", false))
        .Add(nameof(PairPerms.AllowSounds), new PermInfo(FAI.VolumeUp, FAI.VolumeMute, "Allowing", "Preventing", "sounds from", false))
        .Add(nameof(PairPerms.AllowVfx), new PermInfo(FAI.PersonBurst, FAI.Ban, "Allowing", "Preventing", "VFX from", false));


    /// <summary>
    ///     Updates a client's own PairPermission for a defined Sundesmo client-side.
    ///     After the client-side change is made, it requests the change server side.
    ///     If any error occurs from the server-call, the value is reverted to its state before the change.
    /// </summary>
    public async Task<bool> ChangeOwnUnique(Sundesmo sundesmo, string propertyName, object newValue)
    {
        if (sundesmo is null) return false;

        var type = sundesmo.OwnPerms.GetType();
        var property = type.GetProperty(propertyName);
        if (property is null || !property.CanRead || !property.CanWrite)
            return false;

        // Initially, Before sending it off, store the current value.
        var currentValue = property.GetValue(sundesmo.OwnPerms);

        try
        {
            // Update it before we send off for validation.
            if (!PropertyChanger.TrySetProperty(sundesmo.OwnPerms, propertyName, newValue, out object? finalVal))
                throw new InvalidOperationException($"Failed to set property {propertyName} for self in PairPerms with value {newValue}.");

            if (finalVal is null)
                throw new InvalidOperationException($"Property {propertyName} in PairPerms, has the finalValue was null, which is not allowed.");

            // Now that it is updated client-side, attempt to make the change on the server, and get the hub response.
            HubResponse response = await _hub.ChangeUniquePerm(sundesmo.UserData, propertyName, (bool)newValue);

            if (response.ErrorCode is not SundouleiaApiEc.Success)
                throw new InvalidOperationException($"Failed to change {propertyName} to {finalVal} for self. Reason: {response.ErrorCode}");
        }
        catch (InvalidOperationException ex)
        {
            Svc.Logger.Warning(ex.Message + "(Resetting to Previous Value)");
            property.SetValue(sundesmo.OwnPerms, currentValue);
            return false;
        }

        return true;
    }
    #endregion Interactions

    #region RequestResponder
    private void DrawIncomingRequests(ResponseCache irc)
    {
        using var _ = CkRaii.Child("RequestResponder", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        var width = _.InnerRegion.X;

        CkGui.FontTextCentered("Request Responder", UiFontService.Default150Percent);

        CkGui.FramedIconText(FAI.ObjectGroup);
        CkGui.TextFrameAlignedInline("Bulk Selector Area");
        CkGui.TextFrameAligned($"There are currently: {irc.Selected.Count} selected requests.");
    }

    private void DrawPendingRequests(ResponseCache prc)
    {
        using var _ = CkRaii.Child("PendingRequests", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        var width = _.InnerRegion.X;

        CkGui.FontTextCentered("Pending Requests", UiFontService.Default150Percent);

        CkGui.FramedIconText(FAI.ObjectGroup);
        CkGui.TextFrameAlignedInline("Bulk Selector Area");
        CkGui.TextFrameAligned($"There are currently: {prc.Selected.Count} selected requests.");
    }
    #endregion RequestResponder

}
