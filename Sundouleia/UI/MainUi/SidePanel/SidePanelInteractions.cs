using CkCommons;
using CkCommons.DrawSystem;
using CkCommons.Gui;
using CkCommons.Helpers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Util;
using System.Collections.Immutable;

namespace Sundouleia.Gui.MainWindow;

// This is temporary until organized better.
public enum OpenedInteraction
{
    None,
    ApplyOwnStatus, 
    ApplyOwnPreset,
    ApplyOtherStatus,
    ApplyOtherPreset,
    RemoveStatus,
}

public class SidePanelInteractions
{
    private readonly ILogger<SidePanelInteractions> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly MainHub _hub;
    private readonly SundesmoManager _sundesmos;
    private readonly RadarManager _radar;
    private readonly SidePanelService _service;

    // internal storage.
    private Dictionary<SIID, string> _timespanCache = new();

    public SidePanelInteractions(ILogger<SidePanelInteractions> logger, SundouleiaMediator mediator,
        MainHub hub, SundesmoManager sundesmos, RadarManager radar, SidePanelService service)
    {
        _logger = logger;
        _mediator = mediator;
        _hub = hub;
        _sundesmos = sundesmos;
        _radar = radar;
        _service = service;
    }

    public void DrawInteractions(InteractionsCache cache, Sundesmo sundesmo, string dispName, float width)
    {
        ImGuiUtil.Center($"Interactions with {dispName}");
        ImGui.Separator();

        ImGui.Text("Common");
        DrawCommon(sundesmo, dispName, width);
        ImGui.Separator();

        ImGui.Text("Moodles");
        DrawApplyMoodleOwn(cache, sundesmo, dispName, width);
        DrawApplyMoodleOther(cache, sundesmo, dispName, width);
    }

    public void DrawPermissions(InteractionsCache cache, Sundesmo s, string dispName, float width)
    {
        ImGuiUtil.Center($"Permissions for {dispName}");
        ImGui.Separator();
        DrawHeader(s, dispName);
        ImGui.Separator();

        ImGui.Text("Data Syncronization");
        DrawPermRow(s, dispName, width, SIID.DataSyncAnimations, nameof(PairPerms.AllowAnimations), s.OwnPerms.AllowAnimations);
        DrawPermRow(s, dispName, width, SIID.DataSyncSounds, nameof(PairPerms.AllowSounds), s.OwnPerms.AllowSounds);
        DrawPermRow(s, dispName, width, SIID.DataSyncVfx, nameof(PairPerms.AllowVfx), s.OwnPerms.AllowVfx);
        ImGui.Separator();

        ImGui.Text("Moodles Permissions");
        DrawPermRow(s, dispName, width, SIID.ShareMoodles, nameof(PairPerms.ShareOwnMoodles), s.OwnPerms.ShareOwnMoodles, () => _mediator.Publish(new MoodleSharePermChanged(s)));
        DrawPermRow(s, dispName, width, SIID.AllowPositve, s.OwnPerms.MoodleAccess, MoodleAccess.Positive, () => _mediator.Publish(new MoodlePermsChanged(s)));
        DrawPermRow(s, dispName, width, SIID.AllowNegative, s.OwnPerms.MoodleAccess, MoodleAccess.Negative, () => _mediator.Publish(new MoodlePermsChanged(s)));
        DrawPermRow(s, dispName, width, SIID.AllowSpecial, s.OwnPerms.MoodleAccess, MoodleAccess.Special, () => _mediator.Publish(new MoodlePermsChanged(s)));
        DrawPermRow(s, dispName, width, SIID.AllowOwnMoodles, s.OwnPerms.MoodleAccess, MoodleAccess.AllowOwn, () => _mediator.Publish(new MoodlePermsChanged(s)));
        DrawPermRow(s, dispName, width, SIID.AllowOtherMoodles, s.OwnPerms.MoodleAccess, MoodleAccess.AllowOther, () => _mediator.Publish(new MoodlePermsChanged(s)));
        DrawPermRow(s, dispName, width, SIID.MaxMoodleTime, nameof(PairPerms.MaxMoodleTime), s.OwnPerms.MaxMoodleTime, () => _mediator.Publish(new MoodlePermsChanged(s)));
        DrawPermRow(s, dispName, width, SIID.AllowPermanent, s.OwnPerms.MoodleAccess, MoodleAccess.Permanent, () => _mediator.Publish(new MoodlePermsChanged(s)));
        DrawPermRow(s, dispName, width, SIID.RemoveApplied, s.OwnPerms.MoodleAccess, MoodleAccess.RemoveApplied, () => _mediator.Publish(new MoodlePermsChanged(s)));
        DrawPermRow(s, dispName, width, SIID.RemoveAny, s.OwnPerms.MoodleAccess, MoodleAccess.RemoveAny, () => _mediator.Publish(new MoodlePermsChanged(s)));
    }

    private void DrawHeader(Sundesmo s, string dispName)
    {
        // Data Sync Row
        var width = CkGui.IconsSize([FAI.VolumeUp, FAI.Running, FAI.PersonBurst]).X + ImUtf8.ItemInnerSpacing.X * 2;
        CkGui.SetCursorXtoCenter(width);
        var sounds = s.PairPerms.AllowSounds;
        var anims = s.PairPerms.AllowAnimations;
        var vfx = s.PairPerms.AllowVfx;
        CkGui.IconTextAligned(FAI.VolumeUp, sounds ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip($"{dispName} {(sounds ? "can hear your modded SFX/Music." : "disabled your modded SFX/Music.")}");
        ImUtf8.SameLineInner();
        CkGui.IconTextAligned(FAI.Running, anims ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip($"{dispName} {(anims ? "can see your modded animations." : "disabled your modded animations.")}");
        ImUtf8.SameLineInner();
        CkGui.IconTextAligned(FAI.PersonBurst, vfx ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip($"{dispName} {(vfx ? "can see your modded VFX." : "disabled your modded VFX.")}");

        // Moodles Row
        var iconsW = CkGui.IconsSize([FAI.TheaterMasks, FAI.SmileBeam, FAI.FrownOpen, FAI.WandMagicSparkles,
            FAI.PersonArrowUpFromLine, FAI.PersonArrowDownToLine, FAI.Stopwatch, FAI.Infinity, FAI.Eraser]).X;
        var moodlesW = iconsW + ImUtf8.ItemInnerSpacing.X * 8;

        CkGui.SetCursorXtoCenter(moodlesW);
        var sharing = s.PairPerms.ShareOwnMoodles;
        var pos = s.PairPerms.MoodleAccess.HasAny(MoodleAccess.Positive);
        var neg = s.PairPerms.MoodleAccess.HasAny(MoodleAccess.Negative);
        var spec = s.PairPerms.MoodleAccess.HasAny(MoodleAccess.Special);
        var own = s.PairPerms.MoodleAccess.HasAny(MoodleAccess.AllowOwn);
        var other = s.PairPerms.MoodleAccess.HasAny(MoodleAccess.AllowOther);
        var perm = s.PairPerms.MoodleAccess.HasAny(MoodleAccess.Permanent);
        var remApp = s.PairPerms.MoodleAccess.HasAny(MoodleAccess.RemoveApplied);
        var remAny = s.PairPerms.MoodleAccess.HasAny(MoodleAccess.RemoveAny);
        CkGui.IconTextAligned(FAI.TheaterMasks, sharing ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip($"{dispName} {(sharing ? "is sharing their Moodles with you." : "is not sharing their Moodles with you.")}");

        ImUtf8.SameLineInner();
        CkGui.IconTextAligned(FAI.SmileBeam, pos ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip($"{dispName} {(pos ? "allows Positive Moodles." : "prevents Positive Moodles.")}");

        ImUtf8.SameLineInner();
        CkGui.IconTextAligned(FAI.FrownOpen, neg ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip($"{dispName} {(pos ? "allows Negative Moodles." : "prevents Negative Moodles.")}");

        ImUtf8.SameLineInner();
        CkGui.IconTextAligned(FAI.WandMagicSparkles, spec ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip($"{dispName} {(pos ? "allows Special Moodles." : "prevents Special Moodles.")}");

        ImUtf8.SameLineInner();
        CkGui.IconTextAligned(FAI.PersonArrowUpFromLine, own ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip(pos ? $"You can apply {dispName}'s own Moodles." : $"Applying {dispName}'s Moodles is forbidden.");

        ImUtf8.SameLineInner();
        CkGui.IconTextAligned(FAI.PersonArrowDownToLine, other ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip(other ? $"You can apply your Moodles" : "Applying of your Moodles is forbidden.");

        ImUtf8.SameLineInner();
        CkGui.IconTextAligned(FAI.Stopwatch, s.PairPerms.MaxMoodleTime != TimeSpan.Zero ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip(s.PairPerms.MaxMoodleTime != TimeSpan.Zero ? $"{dispName}'s Maximum Moodle Duration is: {s.PairPerms.MaxMoodleTime.ToTimeSpanStr()}." : "You can not apply timed Moodles.");

        ImUtf8.SameLineInner();
        CkGui.IconTextAligned(FAI.Infinity, perm ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip(perm ? $"{dispName} allows Permanent Moodles." : $"{dispName} prevents Permanent Moodles.");

        if (remAny)
        {
            ImUtf8.SameLineInner();
            CkGui.IconTextAligned(FAI.Eraser, remAny ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
            CkGui.AttachToolTip(remAny ? $"You can remove {dispName}'s Moodles." : $"You cannot remove {dispName}'s Moodles.");
        }
        else
        {
            ImUtf8.SameLineInner();
            CkGui.IconTextAligned(FAI.Eraser, remApp ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
            CkGui.AttachToolTip(remApp ? $"You can remove Moodles applied by you." : $"You cannot remove {dispName}'s Moodles.");
        }

    }

    private void DrawCommon(Sundesmo s, string dispName, float width)
    {
        var isPaused = s.IsPaused;
        if (!isPaused)
        {
            if (CkGui.IconTextButton(FAI.User, "Open Profile", width, true, UiService.DisableUI))
                _mediator.Publish(new ProfileOpenMessage(s.UserData));
            CkGui.AttachToolTip($"Opens {dispName}'s profile!");

            if (CkGui.IconTextButton(FAI.ExclamationTriangle, $"Report {dispName}'s Profile", width, true, UiService.DisableUI))
                _mediator.Publish(new OpenReportUIMessage(s.UserData, ReportKind.Profile));
            CkGui.AttachToolTip($"Snapshot {dispName}'s Profile and make a report with its state.");
        }

        // Pausing (Special due to inverted nature. If more occur in the future, add an invert logic bool to the draw perm row method.
        DrawPauseRow(s, dispName, width, isPaused);

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
                    _sundesmos.RemoveSundesmo(new(s.UserData));
                    _radar.RefreshUser(s.UserData);
                    _service.ClearDisplay();
                }
            });
        CkGui.AttachToolTip($"Must hold --COL--CTRL & SHIFT to remove.", color: ImGuiColors.DalamudRed);
    }

    private void DrawPauseRow(Sundesmo s, string dispName, float width, bool isPaused)
    {
        using var dis = ImRaii.PushStyle(ImGuiStyleVar.Alpha, 0.5f, UiService.DisableUI);
        using var col = ImRaii.PushColor(ImGuiCol.Button, 0);

        var pos = ImGui.GetCursorScreenPos();

        if (ImGui.Button("##pauseStatePerm", new Vector2(width, ImUtf8.FrameHeight)) && !UiService.DisableUI)
        {
            UiService.SetUITask(async () =>
            {
                // Perform the change over the server. When it completes, determine the pause state.
                if (await ChangeOwnUnique(s, nameof(PairPerms.PauseVisuals), !isPaused).ConfigureAwait(false))
                    _logger.LogInformation($"[OwnPermChange] Permission 'IsPaused' is now '{!isPaused}' for {s.GetNickAliasOrUid()}.");
            });
        }
        CkGui.AttachToolTip(isPaused ? $"Unpause {dispName}, restoring their Modded Appearance" : $"Pause {dispName}, reverting them to game state.");
        // Show the text overlay.
        ImGui.SetCursorScreenPos(pos);

        using var _ = ImRaii.Group();
        CkGui.FramedIconText(isPaused ? FAI.EyeSlash : FAI.Eye);
        ImGui.SameLine(0, 0);
        CkGui.TextFrameAligned($" {dispName} is ");
        ImGui.SameLine(0, 0);
        CkGui.ColorTextFrameAligned(isPaused ? "Paused" : "Unpaused", isPaused ? CkCol.TriStateCross.Uint() : CkCol.TriStateCheck.Uint());
        ImGui.SameLine(0, 0);
        ImGui.Text(".");
    }

    private void DrawApplyMoodleOwn(InteractionsCache cache, Sundesmo s, string dispName, float width)
    {
        var hasStatuses = ClientMoodles.Data.Statuses.Count > 0;
        var hasPresets = ClientMoodles.Data.Presets.Count > 0;
        var isAllowed = s.PairPerms.MoodleAccess.HasAny(MoodleAccess.AllowOther);

        var statusTxt = hasStatuses ? $"Apply a status to {dispName}" : $"No statuses to apply";
        var statusTT = isAllowed ? $"Applies a status to {dispName}." : $"Cannot apply your own moodles to {dispName}. --COL--(Permission Denied)--COL--";
        var presetTxt = hasStatuses ? $"Apply a preset to {dispName}" : $"No presets to apply";
        var presetTT = isAllowed ? $"Applies a preset to {dispName}." : $"Cannot apply your own moodles to {dispName}. --COL--(Permission Denied)--COL--";

        // Applying own moodles
        if (CkGui.IconTextButton(FAI.UserPlus, statusTxt, width, true, !isAllowed || !hasStatuses))
            cache.ToggleInteraction(OpenedInteraction.ApplyOwnStatus);
        CkGui.AttachToolTip(statusTT);

        if (cache.OpenedInteraction is OpenedInteraction.ApplyOwnStatus)
        {
            using (ImRaii.Child("applyownstatus", new Vector2(width, ImGui.GetFrameHeight())))
                cache.OwnStatuses.DrawApplyStatuses($"##ownstatus-{s.UserData.UID}", width, $"Applies this Status to {dispName}");
            ImGui.Separator();
        }

        // Applying own presets.
        if (CkGui.IconTextButton(FAI.FileCirclePlus, presetTxt, width, true, !isAllowed || !hasPresets))
            cache.ToggleInteraction(OpenedInteraction.ApplyOwnPreset);
        CkGui.AttachToolTip(presetTT);

        if (cache.OpenedInteraction is OpenedInteraction.ApplyOwnPreset)
        {
            using (ImRaii.Child("applyownpresets", new Vector2(width, ImGui.GetFrameHeight())))
                cache.OwnPresets.DrawApplyPresets($"##ownpreset-{s.UserData.UID}", width, $"Applies this Preset to {dispName}");
            ImGui.Separator();
        }
    }

    private void DrawApplyMoodleOther(InteractionsCache cache, Sundesmo s, string dispName, float width)
    {
        var hasStatuses = s.SharedData.Statuses.Count > 0;
        var hasPresets = s.SharedData.Presets.Count > 0;
        var isAllowed = s.PairPerms.MoodleAccess.HasAny(MoodleAccess.AllowOwn);

        var statusTxt = hasStatuses ? $"Apply a status from {dispName}'s list" : "No statuses to apply.";
        var statusTT = isAllowed ? $"Applies a chosen status to {dispName}." : $"Cannot apply {dispName}'s statuses. --COL--(Permission Denied)--COL--";
        var presetTxt = hasPresets ? $"Apply a preset from {dispName}'s list" : "No presets to apply.";
        var presetTT = isAllowed ? $"Applies a chosen preset to {dispName}." : $"Cannot apply {dispName}'s presets. --COL--(Permission Denied)--COL--";

        // Applying sundesmo's moodles
        if (CkGui.IconTextButton(FAI.UserPlus, statusTxt, width, true, !isAllowed || !hasStatuses))
            cache.ToggleInteraction(OpenedInteraction.ApplyOtherStatus);
        CkGui.AttachToolTip(statusTT);

        if (cache.OpenedInteraction is OpenedInteraction.ApplyOtherStatus)
        {
            using (ImRaii.Child("applyotherstatus", new Vector2(width, ImGui.GetFrameHeight())))
                cache.Statuses.DrawStatuses($"##otherstatus-{s.UserData.UID}", width, true, $"Applies this Status to {dispName}");
            ImGui.Separator();
        }

        // Applying sundesmo's presets.
        if (CkGui.IconTextButton(FAI.FileCirclePlus, presetTxt, width, true, !isAllowed || !hasPresets))
            cache.ToggleInteraction(OpenedInteraction.ApplyOtherPreset);
        CkGui.AttachToolTip(presetTT);

        if (cache.OpenedInteraction is OpenedInteraction.ApplyOtherPreset)
        {
            using (ImRaii.Child("applyotherpresets", new Vector2(width, ImGui.GetFrameHeight())))
                cache.Presets.DrawPresets($"##otherpreset-{s.UserData.UID}", width, $"Applies this Preset to {dispName}");
            ImGui.Separator();
        }

        // For removing. (Of note, we will need to make a seperate combo for removals if we want to distinguish between applied vs any.)
        var canRemApplied = s.PairPerms.MoodleAccess.HasAny(MoodleAccess.RemoveApplied);
        var canRemAny = s.PairPerms.MoodleAccess.HasAny(MoodleAccess.RemoveAny);
        var canRemove = canRemApplied || canRemAny;
        var remText = canRemove ? $"Remove a status from {dispName}." : "Cannot remove statuses.";
        var remTT = canRemove ? $"Removes a status from {dispName}." : $"Cannot remove statuses from {dispName}. --COL--(Permission Denied)--COL--";

        if (CkGui.IconTextButton(FAI.UserMinus, remText, width, true, !canRemove))
            cache.ToggleInteraction(OpenedInteraction.RemoveStatus);
        CkGui.AttachToolTip(remTT);

        if (cache.OpenedInteraction is OpenedInteraction.RemoveStatus)
        {
            using (ImRaii.Child("removestatus", new Vector2(width, ImGui.GetFrameHeight())))
                cache.Remover.DrawStatuses($"##statusremover-{s.UserData.UID}", width, false, $"Removes Selected Status from {dispName}");
        }
    }

    private void DrawPermRow(Sundesmo sundesmo, string dispName, float width, SIID id, MoodleAccess curState, MoodleAccess option, Action? onChange = null)
        => DrawPermInternal(sundesmo, dispName, width, id, nameof(PairPerms.MoodleAccess), curState.HasAny(option), () => curState ^ option, onChange);

    private void DrawPermRow(Sundesmo sundesmo, string dispName, float width, SIID id, string permName, bool current, Action? onChange = null)
        => DrawPermInternal(sundesmo, dispName, width, id, permName, current, () => !current, onChange);

    private void DrawPermRow(Sundesmo sundesmo, string dispName, float width, SIID id, string permname, TimeSpan current, Action? onChange = null)
    {
        var inputTxtWidth = width * .4f;
        var str = _timespanCache.TryGetValue(id, out var value) ? value : current.ToTimeSpanStr();
        var txtData = PermissionData[id];

        if (CkGui.IconInputText(txtData.TrueFAI, txtData.Label, "0d0h0m0s", ref str, 32, inputTxtWidth, true))
        {
            if (str != current.ToTimeSpanStr() && CkTimers.TryParseTimeSpan(str, out var newTime))
            {
                var ticks = (ulong)newTime.Ticks;
                _logger.LogInformation($"Attempting to change {dispName}'s {permname} to {ticks} ticks.", LoggerType.PairDataTransfer);
                UiService.SetUITask(async () => await ChangeOwnUnique(sundesmo, permname, ticks, onChange));
            }
            _timespanCache.Remove(id);
        }
        CkGui.AttachToolTip($"The Maximum Time {dispName} can apply a moodle on you for.");
    }

    private void DrawPermInternal<T>(Sundesmo sundesmo, string dispName, float width, SIID id, string permName, bool current, Func<T> toNewState, Action? onChange = null)
    {
        using var col = ImRaii.PushColor(ImGuiCol.Button, 0);
        var txtData = PermissionData[id];
        var pos = ImGui.GetCursorScreenPos();
        if (ImGuiUtil.DrawDisabledButton($"##pairperm{id}", new Vector2(width, ImGui.GetFrameHeight()), string.Empty, UiService.DisableUI))
        {
            if (string.IsNullOrEmpty(permName))
                return;

            UiService.SetUITask(async () =>
            {
                var newState = toNewState();
                if (newState is null)
                    return;

                if (await ChangeOwnUnique(sundesmo, permName, newState, onChange).ConfigureAwait(false))
                    _logger.LogInformation($"Successfully changed own permission {permName} to {newState} for {sundesmo.GetNickAliasOrUid()}.");
            });
        }

        ImGui.SetCursorScreenPos(pos);
        PrintButtonRichText(txtData, dispName, current);
        CkGui.AttachToolTip($"Toggle this preference for {dispName}.");
    }

    private void PrintButtonRichText(PermInfo pdp, string dispName, bool current)
    {
        using var _ = ImRaii.Group();
        CkGui.FramedIconText(current ? pdp.TrueFAI : pdp.FalseFAI);
        ImGui.SameLine(0, 0);
        CkGui.ColorTextFrameAligned($" {(current ? pdp.CondTrue : pdp.CondFalse)} ", current ? CkCol.TriStateCheck.Uint() : CkCol.TriStateCross.Uint());
        ImGui.SameLine(0, 0);
        ImGui.Text(pdp.Label);
        if (!pdp.SkipDisplayName)
        {
            ImGui.SameLine(0, 0);
            ImGui.Text(dispName);
        }
        ImGui.SameLine(0, 0);
        ImGui.Text($"{pdp.Suffix}.");
    }

    private enum SIID : byte
    {
        DataSyncAnimations,
        DataSyncSounds,
        DataSyncVfx,
        
        ShareMoodles,
        AllowPositve,
        AllowNegative,
        AllowSpecial,
        AllowOwnMoodles,
        AllowOtherMoodles,
        MaxMoodleTime,
        AllowPermanent,
        RemoveApplied,
        RemoveAny,
        Clearing,
    }

    private record PermInfo(FAI TrueFAI, FAI FalseFAI, string CondTrue, string CondFalse, string Label, bool SkipDisplayName, string Suffix = "");

    private readonly ImmutableDictionary<SIID, PermInfo> PermissionData = ImmutableDictionary<SIID, PermInfo>.Empty
        .Add(SIID.DataSyncAnimations,   new PermInfo(FAI.Running,               FAI.Ban,        "Allowing", "Preventing",   "animations from ",      false))
        .Add(SIID.DataSyncSounds,       new PermInfo(FAI.VolumeUp,              FAI.VolumeMute, "Allowing", "Preventing",   "sounds from ",          false))
        .Add(SIID.DataSyncVfx,          new PermInfo(FAI.PersonBurst,           FAI.Ban,        "Allowing", "Preventing",   "VFX from ",             false))
        .Add(SIID.ShareMoodles,         new PermInfo(FAI.PeopleArrows,          FAI.Ban,        "Sharing",  "Not sharing",  "moodles with ",         false))
        .Add(SIID.AllowOwnMoodles,      new PermInfo(FAI.PersonArrowUpFromLine, FAI.Ban,        "Allowing", "Preventing",   "use of your moodles",   true))
        .Add(SIID.AllowOtherMoodles,    new PermInfo(FAI.PersonArrowDownToLine, FAI.Ban,        "Allowing", "Preventing",   "use of their moodles",  true))
        .Add(SIID.AllowPositve,         new PermInfo(FAI.SmileBeam,             FAI.Ban,        "Allowing", "Preventing",   "positive moodles",      true))
        .Add(SIID.AllowNegative,        new PermInfo(FAI.FrownOpen,             FAI.Ban,        "Allowing", "Preventing",   "negative moodles",      true))
        .Add(SIID.AllowSpecial,         new PermInfo(FAI.WandMagicSparkles,     FAI.Ban,        "Allowing", "Preventing",   "special moodles",       true))
        .Add(SIID.AllowPermanent,       new PermInfo(FAI.Infinity,              FAI.Ban,        "Allowing", "Preventing",   "permanent moodles",     true))
        .Add(SIID.RemoveApplied,        new PermInfo(FAI.Eraser,                FAI.Ban,        "Allowing", "Preventing",   "removing moodles by them",true))
        .Add(SIID.RemoveAny,            new PermInfo(FAI.Eraser,                FAI.Ban,        "Allowing", "Preventing",   "removing moodles by anyone",true))
        .Add(SIID.MaxMoodleTime,        new PermInfo(FAI.HourglassHalf,         FAI.None,       "",         "",             "Max moodle duration",   true));



    /// <summary>
    ///     Updates a client's own PairPermission for a defined Sundesmo client-side.
    ///     After the client-side change is made, it requests the change server side.
    ///     If any error occurs from the server-call, the value is reverted to its state before the change.
    /// </summary>
    public async Task<bool> ChangeOwnUnique(Sundesmo sundesmo, string propertyName, object newValue, Action? onChange = null)
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
            var response = await _hub.ChangeUniquePerm(sundesmo.UserData, propertyName, newValue);

            if (response.ErrorCode is not SundouleiaApiEc.Success)
                throw new InvalidOperationException($"Failed to change {propertyName} to {finalVal} for self. Reason: {response.ErrorCode}");

            // Trigger what to do after the change occurs.
            onChange?.Invoke();
        }
        catch (InvalidOperationException ex)
        {
            Svc.Logger.Warning(ex.Message + "(Resetting to Previous Value)");
            property.SetValue(sundesmo.OwnPerms, currentValue);
            return false;
        }

        return true;
    }
}
