using CkCommons;
using CkCommons.Gui;
using CkCommons.Helpers;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Util;
using System.Collections.Immutable;

namespace Sundouleia.Gui.MainWindow;
public class InteractionsHandler : DisposableMediatorSubscriberBase
{
    private readonly MainHub _hub;
    public InteractionsHandler(ILogger<InteractionsHandler> logger, SundouleiaMediator mediator, MainHub hub)
        : base(logger, mediator)
    {
        _hub = hub;

        Mediator.Subscribe<DisconnectedMessage>(this, (msg) =>
        {
            if (ImGui.IsPopupOpen(PopupLabel))
                ImGui.CloseCurrentPopup();
        });
    }

    private Sundesmo? _sundesmo;
    private string _dispName => _sundesmo?.GetNickAliasOrUid() ?? "Anon. User";

    public string PopupLabel { get; private set; } = string.Empty;

    // Dynamically adjustable window based on interactable.
    private float _windowWidth => (ImGui.CalcTextSize($"Preventing animations from {_dispName}zz").X + ImGui.GetFrameHeightWithSpacing()).AddWinPadX();

    // Assign and define the popup window for the sundesmo interactions.
    public void OpenSundesmoInteractions(Sundesmo sundesmo)
    {
        Logger.LogInformation($"Attempting to open interactions for {sundesmo.GetNickAliasOrUid()}.");
        // close any others if they are open.
        if (ImGui.IsPopupOpen(PopupLabel))
            ImGui.CloseCurrentPopup();

        // Set the new values.
        PopupLabel = $"interactions_{sundesmo.UserData.UID}";
        _sundesmo = sundesmo;

        Logger.LogInformation($"Opened interactions for {sundesmo.GetNickAliasOrUid()} with popup label {PopupLabel}.");
        // Open the new popup.
        ImGui.OpenPopup(PopupLabel);
    }

    public void DrawIfOpen(Sundesmo s)
    {
        // If the sundesmo's do not match, fail.
        if (_sundesmo is null || s.UserData.UID != _sundesmo.UserData.UID)
            return;

        var position = MainUI.LastPos + new Vector2(MainUI.LastSize.X, ImGui.GetFrameHeightWithSpacing());
        var size = new Vector2(_windowWidth, MainUI.LastSize.Y - ImGui.GetFrameHeightWithSpacing() * 2);
        var flags = WFlags.NoMove | WFlags.NoResize | WFlags.NoCollapse | WFlags.NoScrollbar;
        ImGui.SetNextWindowPos(position);
        ImGui.SetNextWindowSize(size);
        using var popup = ImRaii.Popup(PopupLabel, flags);
        var id = ImGui.GetID(PopupLabel);
        if (!popup)
            return;

        Logger.LogInformation($"Drawing interactions popup {PopupLabel} for {_sundesmo.GetNickAliasOrUid()}.");


        // Otherwise draw out the contents and stuff.
        DrawContentsInternal();
    }

    private void DrawContentsInternal()
    {
        using var _ = CkRaii.Child("InteractionsUI", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        var width = _.InnerRegion.X;
        DrawHeader();

        DrawCommon(width);
        ImGui.Separator();

        ImGui.Text("Permissions");
        DrawDistinctPermRow(_sundesmo!, _dispName, width, nameof(PairPerms.AllowAnimations), _sundesmo!.OwnPerms.AllowAnimations);
        DrawDistinctPermRow(_sundesmo, _dispName, width, nameof(PairPerms.AllowSounds), _sundesmo.OwnPerms.AllowSounds);
        DrawDistinctPermRow(_sundesmo, _dispName, width, nameof(PairPerms.AllowVfx), _sundesmo.OwnPerms.AllowVfx);

        ImGui.Separator();

        ImGui.Text("Pair Options");
        DrawPairOptions(_.InnerRegion.X);
    }

    private void DrawHeader()
    {
        CkGui.CenterText($"{_dispName}'s Interactions");
        var width = CkGui.IconSize(FAI.VolumeUp).X + CkGui.IconSize(FAI.Running).X + CkGui.IconSize(FAI.PersonBurst).X + ImUtf8.ItemInnerSpacing.X * 2;
        CkGui.SetCursorXtoCenter(width);

        var sounds = _sundesmo!.PairPerms.AllowSounds;
        var anims = _sundesmo.PairPerms.AllowAnimations;
        var vfx = _sundesmo.PairPerms.AllowVfx;

        CkGui.IconText(FAI.VolumeUp, sounds ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip($"{_dispName} {(sounds ? "can hear your modded SFX/Music." : "disabled your modded SFX/Music.")}");
        ImUtf8.SameLineInner();
        CkGui.IconText(FAI.Running, anims ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip($"{_dispName} {(anims ? "can see your modded animations." : "disabled your modded animations.")}");
        ImUtf8.SameLineInner();
        CkGui.IconText(FAI.PersonBurst, vfx ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip($"{_dispName} {(vfx ? "can see your modded VFX." : "disabled your modded VFX.")}");

        ImGui.Separator();    
    }

    private void DrawCommon(float width)
    {
        var isPaused = _sundesmo!.IsPaused;
        if (!isPaused)
        {
            if (CkGui.IconTextButton(FAI.User, "Open Profile", width, true, UiService.DisableUI))
                Mediator.Publish(new ProfileOpenMessage(_sundesmo.UserData));
            CkGui.AttachToolTip($"Opens {_dispName}'s profile!");

            if (CkGui.IconTextButton(FAI.ExclamationTriangle, $"Report {_dispName}'s Profile", width, true, UiService.DisableUI))
                Mediator.Publish(new OpenReportUIMessage(_sundesmo.UserData, ReportKind.Profile));
            CkGui.AttachToolTip($"Snapshot {_dispName}'s Profile and make a report with its state.");
        }

        DrawDistinctPermRow(_sundesmo, _dispName, width, nameof(PairPerms.PauseVisuals), isPaused, false, true);
        CkGui.AttachToolTip($"{(!isPaused ? "Pause" : "Resume")} the rendering of {_dispName}'s modded appearance.");
    }

    private void DrawPairOptions(float width)
    {
        if (_sundesmo!.IsTemporary)
        {
            var blockButton = _sundesmo.UserPair.TempAccepterUID != MainHub.UID;
            if (CkGui.IconTextButton(FAI.Link, "Convert to Permanent Pair", width, true, blockButton))
                UiService.SetUITask(async () =>
                {
                    var res = await _hub.UserPersistPair(new(_sundesmo.UserData));
                    if (res.ErrorCode is not SundouleiaApiEc.Success)
                        Logger.LogWarning($"Failed to convert temporary pair for {_sundesmo.GetNickAliasOrUid()}. Reason: {res.ErrorCode}");
                    else
                    {
                        Logger.LogInformation($"Successfully converted temporary pair for {_sundesmo.GetNickAliasOrUid()}.");
                        _sundesmo.MarkAsPermanent();
                    }
                });

            var timeLeft = TimeSpan.FromDays(1) - (DateTime.UtcNow - _sundesmo.UserPair.CreatedAt);
            var autoDeleteText = $"Temp. Pairing Expires in --COL--{timeLeft.Days}d {timeLeft.Hours}h {timeLeft.Minutes}m--COL--";
            var ttStr = $"Makes a temporary pair permanent. --NL--{autoDeleteText}" +
                $"{(blockButton ? "--SEP----COL--Only the user who accepted the request can use this.--COL--" : string.Empty)}";
            CkGui.AttachToolTip(ttStr, color: ImGuiColors.DalamudYellow);
        }

        if (CkGui.IconTextButton(FAI.Trash, $"Remove {_dispName} from your Pairs", width, true, !KeyMonitor.CtrlPressed() || !KeyMonitor.ShiftPressed()))
            UiService.SetUITask(async () =>
            {
                var res = await _hub.UserRemovePair(new(_sundesmo.UserData));
                if (res.ErrorCode is not SundouleiaApiEc.Success)
                    Logger.LogWarning($"Failed to remove pair {_sundesmo.GetNickAliasOrUid()}. Reason: {res.ErrorCode}");
                else
                {
                    Logger.LogInformation($"Successfully removed pair {_sundesmo.GetNickAliasOrUid()}.");
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
                if (await ChangeOwnUnique(permName, !current).ConfigureAwait(false))
                    Logger.LogInformation($"Successfully changed own permission {permName} to {!current} for {sundesmo.GetNickAliasOrUid()}.");
            });
        }

        ImGui.SetCursorScreenPos(pos);
        PrintButtonRichText(txtData, dispName, current, trueCol, falseCol);
        if (defaultTT)
            CkGui.AttachToolTip($"Toggle this preference for {_dispName}.");

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
    public async Task<bool> ChangeOwnUnique(string propertyName, object newValue)
    {
        if (_sundesmo is null) return false;

        var type = _sundesmo.OwnPerms.GetType();
        var property = type.GetProperty(propertyName);
        if (property is null || !property.CanRead || !property.CanWrite)
            return false;

        // Initially, Before sending it off, store the current value.
        var currentValue = property.GetValue(_sundesmo.OwnPerms);

        try
        {
            // Update it before we send off for validation.
            if (!PropertyChanger.TrySetProperty(_sundesmo.OwnPerms, propertyName, newValue, out object? finalVal))
                throw new InvalidOperationException($"Failed to set property {propertyName} for self in PairPerms with value {newValue}.");

            if (finalVal is null)
                throw new InvalidOperationException($"Property {propertyName} in PairPerms, has the finalValue was null, which is not allowed.");

            // Now that it is updated client-side, attempt to make the change on the server, and get the hub response.
            HubResponse response = await _hub.ChangeUniquePerm(_sundesmo.UserData, propertyName, (bool)newValue);

            if (response.ErrorCode is not SundouleiaApiEc.Success)
                throw new InvalidOperationException($"Failed to change {propertyName} to {finalVal} for self. Reason: {response.ErrorCode}");
        }
        catch (InvalidOperationException ex)
        {
            Svc.Logger.Warning(ex.Message + "(Resetting to Previous Value)");
            property.SetValue(_sundesmo.OwnPerms, currentValue);
            return false;
        }

        return true;
    }
}
